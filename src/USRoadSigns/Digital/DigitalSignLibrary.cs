using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using UnityEngine;
using USRoadSigns.Geometry;
using USRoadSigns.Runtime;
using Texture = Colossal.AssetPipeline.Importers.TextureImporter.Texture;

namespace USRoadSigns.Digital
{
    /// <summary>Display placement of one registered digital sign type, used by the renderer and the info panel.</summary>
    internal sealed class DigitalSignInfo
    {
        public DigitalSignType Type;
        public DisplayRect Display;
        public BeaconLens[] Beacons;
        public string PrefabName;
    }

    /// <summary>
    /// Creates the digital message sign prefabs: structure materials (procedural textures), one mesh and one
    /// StaticObjectPrefab per <see cref="DigitalSignTypes"/> entry, all in their own toolbar category. The message
    /// itself is not part of the mesh; <see cref="DigitalSignRenderSystem"/> draws it per sign.
    /// </summary>
    internal static class DigitalSignLibrary
    {
        public const string PrefabPrefix = "USRS_Digital_";

        private static readonly MaterialSlot[] s_Slots =
        {
            MaterialSlot.OrangePaint, MaterialSlot.BlackHousing, MaterialSlot.LedMatrix,
            MaterialSlot.Concrete, MaterialSlot.Solar, MaterialSlot.Rubber,
        };

        private static DigitalSignInfo[] s_Infos = Array.Empty<DigitalSignInfo>();

        public static bool IsRegistered => s_Infos.Length > 0;

        public static DigitalSignInfo Get(int typeIndex)
        {
            return typeIndex >= 0 && typeIndex < s_Infos.Length ? s_Infos[typeIndex] : null;
        }

        public static string PrefabName(DigitalSignType type) => PrefabPrefix + type.Id;

        /// <param name="sharedSurfaces">Surfaces of the classic signs (galvanized steel and aluminum are reused).</param>
        /// <param name="controlMask">The neutral control mask texture shared by every material.</param>
        public static void Register(
            PrefabSystem prefabSystem,
            AssetFactory factory,
            UIAssetCategoryPrefab category,
            IReadOnlyDictionary<MaterialSlot, SurfaceAsset> sharedSurfaces,
            TextureAsset controlMask,
            Func<string, string> iconUrl)
        {
            var surfaces = new Dictionary<MaterialSlot, SurfaceAsset>();
            foreach (KeyValuePair<MaterialSlot, SurfaceAsset> entry in sharedSurfaces)
                surfaces[entry.Key] = entry.Value;

            foreach (MaterialSlot slot in s_Slots)
            {
                try
                {
                    surfaces[slot] = CreateSurface(factory, slot, controlMask);
                }
                catch (Exception e)
                {
                    Mod.Log.Error(e, "Could not create the digital sign material " + slot);
                }
            }

            var infos = new DigitalSignInfo[DigitalSignTypes.All.Count];
            int created = 0;
            for (int i = 0; i < DigitalSignTypes.All.Count; i++)
            {
                DigitalSignType type = DigitalSignTypes.All[i];
                try
                {
                    infos[i] = CreateSign(prefabSystem, factory, type, i, category, surfaces, iconUrl);
                    created++;
                }
                catch (Exception e)
                {
                    Mod.Log.Error(e, "Could not create digital sign " + type.Id);
                }
            }
            s_Infos = infos;
            Mod.Log.Info($"Registered {created}/{DigitalSignTypes.All.Count} digital message signs.");
        }

        private static SurfaceAsset CreateSurface(AssetFactory factory, MaterialSlot slot, TextureAsset controlMask)
        {
            string name = "Digital_" + slot;
            RgbaImage color = DigitalTextures.BaseColor(slot);
            RgbaImage mask = DigitalTextures.MaskMap(slot);
            Texture colorTexture = AssetFactory.BuildTexture(name + "_BaseColor", color.Pixels, color.Width, color.Height, srgb: true, TextureWrapMode.Repeat, 4);
            Texture maskTexture = AssetFactory.BuildTexture(name + "_MaskMap", mask.Pixels, mask.Width, mask.Height, srgb: false, TextureWrapMode.Repeat, 0);
            TextureAsset colorAsset = factory.SaveTexture("Textures", colorTexture);
            TextureAsset maskAsset = factory.SaveTexture("Textures", maskTexture);
            return factory.CreateSurface("USRS_" + name, new Dictionary<string, TextureAsset>
            {
                { AssetFactory.BaseColorMap, colorAsset },
                { AssetFactory.MaskMap, maskAsset },
                { AssetFactory.ControlMask, controlMask },
            });
        }

        private static DigitalSignInfo CreateSign(
            PrefabSystem prefabSystem,
            AssetFactory factory,
            DigitalSignType type,
            int typeIndex,
            UIAssetCategoryPrefab category,
            Dictionary<MaterialSlot, SurfaceAsset> surfaces,
            Func<string, string> iconUrl)
        {
            DigitalSignMesh mesh = DigitalSignBuilder.Build(type);
            var info = new DigitalSignInfo
            {
                Type = type,
                Display = mesh.Display,
                Beacons = mesh.Beacons.ToArray(),
                PrefabName = PrefabName(type),
            };

            if (prefabSystem.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), info.PrefabName), out PrefabBase _))
                return info;

            AssemblyMesh assembly = mesh.Assembly;
            var partSurfaces = new List<SurfaceAsset>(assembly.Parts.Count);
            foreach (MeshPart part in assembly.Parts)
            {
                if (!surfaces.TryGetValue(part.Slot, out SurfaceAsset surface))
                    throw new InvalidOperationException("No material for " + part.Slot);
                partSurfaces.Add(surface);
            }

            RenderPrefab render = factory.CreateRenderPrefab(info.PrefabName + "_Mesh", assembly, partSurfaces);
            prefabSystem.AddPrefab(render);

            var prefab = ScriptableObject.CreateInstance<StaticObjectPrefab>();
            prefab.name = info.PrefabName;
            prefab.m_Meshes = new[] { new ObjectMeshInfo { m_Mesh = render } };
            prefab.m_Circular = false;

            PlaceableObject placeable = prefab.AddComponent<PlaceableObject>();
            placeable.m_ConstructionCost = type.Cost;
            placeable.m_XPReward = 0;

            // Only the legs (trailer footprint, poles, gantry towers) collide; the display can overhang the road.
            StandingObject standing = prefab.AddComponent<StandingObject>();
            standing.m_LegSize = new Unity.Mathematics.float3(assembly.LegSize.X, assembly.LegSize.Y, assembly.LegSize.Z);
            standing.m_LegGap = new Unity.Mathematics.float2(assembly.LegGapX, 0f);
            standing.m_CircularLeg = assembly.CircularLeg;

            DigitalSignPrefab digital = prefab.AddComponent<DigitalSignPrefab>();
            digital.m_TypeIndex = typeIndex;

            UIObject ui = prefab.AddComponent<UIObject>();
            ui.m_Group = category;
            ui.m_Priority = type.Priority;
            ui.m_Icon = iconUrl(type.Icon);
            ui.m_IsDebugObject = false;
            ui.active = true;

            prefabSystem.AddPrefab(prefab);
            return info;
        }
    }
}
