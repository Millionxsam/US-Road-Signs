using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Colossal.IO.AssetDatabase;
using Colossal.PSI.Environment;
using Colossal.UI;
using Game.Prefabs;
using Unity.Entities;
using UnityEngine;
using USRoadSigns.Catalog;
using USRoadSigns.Geometry;
using Texture = Colossal.AssetPipeline.Importers.TextureImporter.Texture;

namespace USRoadSigns.Runtime
{
    /// <summary>
    /// Turns the data catalog shipped with the mod into game prefabs:
    ///   Prepare()          – loads the catalog and decodes/compresses textures on worker threads (called from OnLoad).
    ///   RegisterPrefabs()  – on the main thread, once game prefabs exist: creates surfaces, meshes, the toolbar menu,
    ///                        categories and one StaticObjectPrefab per sign.
    /// </summary>
    internal sealed class SignLibraryLoader : IDisposable
    {
        public const string IconHost = "usroadsigns";

        private const string CatalogFolder = "Catalog";
        private const string FaceTextureFolder = "Textures/Faces";
        private const string MaterialTextureFolder = "Textures/Materials";

        private static readonly string[] s_ToolbarGroupCandidates = { "Services Toolbar Group", "Landscaping Toolbar Group" };

        private readonly string m_ModFolder;
        private readonly ModSettings m_Settings;
        private readonly World m_World;

        private Task<Dictionary<string, Texture>> m_TextureJob;
        private bool m_Registered;
        private bool m_HostAdded;
        private AssetFactory m_Factory;

        public SignCatalog Catalog { get; private set; }

        public SignLibraryLoader(string modFolder, ModSettings settings, World world)
        {
            m_ModFolder = modFolder;
            m_Settings = settings;
            m_World = world;
        }

        // ------------------------------------------------------------------------------------------
        // Preparation (mod load)
        // ------------------------------------------------------------------------------------------

        public void Prepare()
        {
            string catalogRoot = Path.Combine(m_ModFolder, CatalogFolder);
            Catalog = SignCatalog.Load(catalogRoot, loadSymbols: false);
            foreach (string warning in Catalog.Warnings)
                Mod.Log.Warn(warning);
            Mod.Log.Info($"Catalog loaded: {Catalog.Signs.Count} signs, {Catalog.Faces.Count} faces, {Catalog.Categories.Count} categories.");

            try
            {
                UIManager.defaultUISystem.AddHostLocation(IconHost, m_ModFolder, shouldWatch: false);
                m_HostAdded = true;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Could not register the icon location (" + e.Message + "); menu icons will be missing.");
            }

            var requests = new List<TextureRequest>();
            foreach (FaceDef face in Catalog.UsedFaces())
                requests.Add(new TextureRequest(FaceTextureName(face.Id), Path.Combine(m_ModFolder, FaceTextureFolder, face.Id + ".png"), srgb: true, TextureWrapMode.Clamp, 8));
            foreach (MaterialSpec spec in s_Materials)
            {
                requests.Add(new TextureRequest(spec.Name + "_BaseColor", MaterialTexturePath(spec.Name + "_BaseColor"), srgb: true, TextureWrapMode.Repeat, 4));
                requests.Add(new TextureRequest(spec.Name + "_MaskMap", MaterialTexturePath(spec.Name + "_MaskMap"), srgb: false, TextureWrapMode.Repeat, 4));
            }
            requests.Add(new TextureRequest("Face_MaskMap", MaterialTexturePath("Face_MaskMap"), srgb: false, TextureWrapMode.Repeat, 0));
            requests.Add(new TextureRequest("ControlMask", MaterialTexturePath("ControlMask"), srgb: false, TextureWrapMode.Repeat, 0));

            m_TextureJob = Task.Run(() => BuildTextures(requests));
        }

        private string MaterialTexturePath(string name) => Path.Combine(m_ModFolder, MaterialTextureFolder, name + ".png");

        private static string FaceTextureName(string faceId) => "Face_" + SafeName(faceId);

        private static Dictionary<string, Texture> BuildTextures(List<TextureRequest> requests)
        {
            var watch = Stopwatch.StartNew();
            var results = new Texture[requests.Count];
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };
            Parallel.For(0, requests.Count, options, i =>
            {
                TextureRequest request = requests[i];
                try
                {
                    byte[] file = File.ReadAllBytes(request.Path);
                    byte[] pixels = PngDecoder.DecodeRgbaBottomUp(file, out int width, out int height);
                    results[i] = AssetFactory.BuildTexture(request.Name, pixels, width, height, request.Srgb, request.Wrap, request.Aniso, request.Wrap == TextureWrapMode.Clamp ? 1 : 3);
                }
                catch (Exception e)
                {
                    Mod.Log.Error(e, "Could not prepare texture " + request.Path);
                }
            });

            var map = new Dictionary<string, Texture>(StringComparer.Ordinal);
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] != null)
                    map[requests[i].Name] = results[i];
            }
            Mod.Log.Info($"Prepared {map.Count}/{requests.Count} textures in {watch.ElapsedMilliseconds} ms.");
            return map;
        }

        // ------------------------------------------------------------------------------------------
        // Registration (main thread, after the game's prefabs are loaded)
        // ------------------------------------------------------------------------------------------

        public void RegisterPrefabs()
        {
            if (m_Registered || Catalog == null)
                return;
            m_Registered = true;

            var watch = Stopwatch.StartNew();
            PrefabSystem prefabSystem = m_World.GetOrCreateSystemManaged<PrefabSystem>();

            if (!AssetFactory.TemplateExists(AssetFactory.DefaultMaterialTemplate))
            {
                Mod.Log.Error("Material template '" + AssetFactory.DefaultMaterialTemplate + "' was not found; signs cannot be created.");
                return;
            }

            Dictionary<string, Texture> textures;
            try
            {
                textures = m_TextureJob != null ? m_TextureJob.Result : new Dictionary<string, Texture>();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Texture preparation failed");
                textures = new Dictionary<string, Texture>();
            }

            string cacheRoot = Path.Combine(EnvPath.kUserDataPath, "ModsData", Mod.ModId, "GeneratedAssets");
            m_Factory = AssetFactory.Create(cacheRoot);

            // Textures -> assets (the CPU copies are released as they are saved).
            var textureAssets = new Dictionary<string, TextureAsset>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Texture> entry in textures)
            {
                try
                {
                    textureAssets[entry.Key] = m_Factory.SaveTexture("Textures", entry.Value);
                }
                catch (Exception e)
                {
                    Mod.Log.Error(e, "Could not store texture " + entry.Key);
                }
            }
            textures.Clear();

            // Shared surfaces.
            var slotSurfaces = new Dictionary<MaterialSlot, SurfaceAsset>();
            foreach (MaterialSpec spec in s_Materials)
            {
                slotSurfaces[spec.Slot] = m_Factory.CreateSurface("USRS_" + spec.Name, new Dictionary<string, TextureAsset>
                {
                    { AssetFactory.BaseColorMap, Get(textureAssets, spec.Name + "_BaseColor") },
                    { AssetFactory.MaskMap, Get(textureAssets, spec.Name + "_MaskMap") },
                    { AssetFactory.ControlMask, Get(textureAssets, "ControlMask") },
                });
            }

            // One surface per sign face, created on first use.
            var faceSurfaces = new Dictionary<string, SurfaceAsset>(StringComparer.Ordinal);
            SurfaceAsset FaceSurface(string faceId)
            {
                if (!faceSurfaces.TryGetValue(faceId, out SurfaceAsset surface))
                {
                    string textureName = FaceTextureName(faceId);
                    surface = m_Factory.CreateSurface("USRS_" + textureName, new Dictionary<string, TextureAsset>
                    {
                        { AssetFactory.BaseColorMap, Get(textureAssets, textureName) },
                        { AssetFactory.MaskMap, Get(textureAssets, "Face_MaskMap") },
                        { AssetFactory.ControlMask, Get(textureAssets, "ControlMask") },
                    });
                    faceSurfaces[faceId] = surface;
                }
                return surface;
            }

            // Menu and categories.
            UIAssetMenuPrefab menu = CreateMenu(prefabSystem);
            var categories = new Dictionary<string, UIAssetCategoryPrefab>(StringComparer.OrdinalIgnoreCase);
            if (menu != null)
            {
                foreach (CategoryDef category in Catalog.Root.Categories)
                    categories[category.Id] = CreateCategory(prefabSystem, menu, category);
            }

            // Signs.
            AssemblyOptions assemblyOptions = m_Settings.ToAssemblyOptions();
            int created = 0;
            foreach (SignDef sign in Catalog.Signs)
            {
                try
                {
                    categories.TryGetValue(sign.Category ?? "", out UIAssetCategoryPrefab category);
                    CreateSign(prefabSystem, sign, assemblyOptions, category, slotSurfaces, FaceSurface);
                    created++;
                }
                catch (Exception e)
                {
                    Mod.Log.Error(e, "Could not create sign " + sign.Id);
                }
            }

            Mod.Log.Info($"Registered {created}/{Catalog.Signs.Count} signs in {watch.ElapsedMilliseconds} ms.");
        }

        private static TextureAsset Get(Dictionary<string, TextureAsset> textures, string name)
        {
            if (textures.TryGetValue(name, out TextureAsset asset))
                return asset;
            Mod.Log.Warn("Missing texture " + name);
            return null;
        }

        private UIAssetMenuPrefab CreateMenu(PrefabSystem prefabSystem)
        {
            MenuDef def = Catalog.Root.Menu;
            if (prefabSystem.TryGetPrefab(new PrefabID(nameof(UIAssetMenuPrefab), def.Id), out PrefabBase existing) && existing is UIAssetMenuPrefab existingMenu)
                return existingMenu;

            UIToolbarGroupPrefab group = null;
            foreach (string candidate in s_ToolbarGroupCandidates)
            {
                if (prefabSystem.TryGetPrefab(new PrefabID(nameof(UIToolbarGroupPrefab), candidate), out PrefabBase found) && found is UIToolbarGroupPrefab g)
                {
                    group = g;
                    break;
                }
            }
            if (group == null)
            {
                Mod.Log.Error("No toolbar group found for the US Road Signs menu; signs will not appear in the toolbar.");
                return null;
            }

            var menu = ScriptableObject.CreateInstance<UIAssetMenuPrefab>();
            menu.name = def.Id;
            UIObject ui = menu.AddComponent<UIObject>();
            ui.m_Group = group;
            ui.m_Priority = def.Priority;
            ui.m_Icon = IconUrl(def.Icon);
            ui.m_IsDebugObject = false;
            ui.active = true;
            prefabSystem.AddPrefab(menu);
            return menu;
        }

        private UIAssetCategoryPrefab CreateCategory(PrefabSystem prefabSystem, UIAssetMenuPrefab menu, CategoryDef def)
        {
            string name = LocaleEN_CategoryName(def);
            if (prefabSystem.TryGetPrefab(new PrefabID(nameof(UIAssetCategoryPrefab), name), out PrefabBase existing) && existing is UIAssetCategoryPrefab existingCategory)
                return existingCategory;

            var category = ScriptableObject.CreateInstance<UIAssetCategoryPrefab>();
            category.name = name;
            category.m_Menu = menu;
            UIObject ui = category.AddComponent<UIObject>();
            ui.m_Priority = def.Priority;
            ui.m_Icon = IconUrl(def.Icon);
            ui.m_IsDebugObject = false;
            ui.active = true;
            prefabSystem.AddPrefab(category);
            return category;
        }

        private string LocaleEN_CategoryName(CategoryDef def) => Localization.LocaleEN.CategoryPrefabName(Catalog, def);

        private void CreateSign(
            PrefabSystem prefabSystem,
            SignDef sign,
            AssemblyOptions options,
            UIAssetCategoryPrefab category,
            Dictionary<MaterialSlot, SurfaceAsset> slotSurfaces,
            Func<string, SurfaceAsset> faceSurface)
        {
            string prefabName = Catalog.GetPrefabName(sign);
            if (prefabSystem.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), prefabName), out PrefabBase _))
                return;

            AssemblyMesh assembly = AssemblyBuilder.Build(Catalog, sign, options);

            var surfaces = new List<SurfaceAsset>(assembly.Parts.Count);
            foreach (MeshPart part in assembly.Parts)
                surfaces.Add(part.Slot == MaterialSlot.Face ? faceSurface(part.FaceId) : slotSurfaces[part.Slot]);

            RenderPrefab render = m_Factory.CreateRenderPrefab(prefabName + "_Mesh", assembly, surfaces);
            prefabSystem.AddPrefab(render);

            var prefab = ScriptableObject.CreateInstance<StaticObjectPrefab>();
            prefab.name = prefabName;
            prefab.m_Meshes = new[] { new ObjectMeshInfo { m_Mesh = render } };
            prefab.m_Circular = false;

            // Free placement: no road-side or owner flags, so the object tool only keeps it upright.
            PlaceableObject placeable = prefab.AddComponent<PlaceableObject>();
            placeable.m_ConstructionCost = sign.ConstructionCost >= 0 ? (uint)sign.ConstructionCost : DefaultCost(assembly);
            placeable.m_XPReward = 0;

            // Only the posts collide, so signs can overhang sidewalks and other props.
            StandingObject standing = prefab.AddComponent<StandingObject>();
            standing.m_LegSize = new Unity.Mathematics.float3(assembly.LegSize.X, assembly.LegSize.Y, assembly.LegSize.Z);
            standing.m_LegGap = new Unity.Mathematics.float2(assembly.LegGapX, 0f);
            standing.m_CircularLeg = assembly.CircularLeg;

            UIObject ui = prefab.AddComponent<UIObject>();
            ui.m_Group = category;
            ui.m_Priority = sign.Priority;
            ui.m_Icon = IconUrl("Icons/Signs/" + sign.Id + ".png");
            ui.m_IsDebugObject = false;
            ui.active = true;

            prefabSystem.AddPrefab(prefab);
        }

        private static uint DefaultCost(AssemblyMesh assembly)
        {
            // Roughly $1 per 10 in² of sign plus $150 per post, rounded to $10.
            float cost = assembly.PanelArea / 10f + 150f * Math.Max(1, assembly.PostCount);
            return (uint)(Math.Round(cost / 10f) * 10f);
        }

        private string IconUrl(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || !m_HostAdded)
                return null;
            return "coui://" + IconHost + "/" + relativePath.Replace('\\', '/');
        }

        private static string SafeName(string id)
        {
            var chars = id.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray();
            return new string(chars);
        }

        public void Dispose()
        {
            if (m_TextureJob != null && m_TextureJob.Status == TaskStatus.RanToCompletion && !m_Registered)
            {
                foreach (Texture texture in m_TextureJob.Result.Values)
                    texture.Dispose();
            }
            m_Factory?.Dispose();
            if (m_HostAdded)
            {
                try { UIManager.defaultUISystem.RemoveHostLocation(IconHost); }
                catch (Exception) { /* UI already torn down */ }
                m_HostAdded = false;
            }
        }

        // ------------------------------------------------------------------------------------------

        private readonly struct TextureRequest
        {
            public readonly string Name;
            public readonly string Path;
            public readonly bool Srgb;
            public readonly TextureWrapMode Wrap;
            public readonly int Aniso;

            public TextureRequest(string name, string path, bool srgb, TextureWrapMode wrap, int aniso)
            {
                Name = name;
                Path = path;
                Srgb = srgb;
                Wrap = wrap;
                Aniso = aniso;
            }
        }

        private readonly struct MaterialSpec
        {
            public readonly string Name;
            public readonly MaterialSlot Slot;

            public MaterialSpec(string name, MaterialSlot slot)
            {
                Name = name;
                Slot = slot;
            }
        }

        private static readonly MaterialSpec[] s_Materials =
        {
            new MaterialSpec("Galvanized", MaterialSlot.Galvanized),
            new MaterialSpec("Perforated", MaterialSlot.Perforated),
            new MaterialSpec("Aluminum", MaterialSlot.Aluminum),
            new MaterialSpec("DarkPlastic", MaterialSlot.DarkPlastic),
        };
    }
}
