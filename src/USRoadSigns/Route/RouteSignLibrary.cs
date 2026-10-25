using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using UnityEngine;
using USRoadSigns.Catalog;
using USRoadSigns.Geometry;
using USRoadSigns.Runtime;

namespace USRoadSigns.Route
{
    /// <summary>One registered route sign prefab (a type, plain or flashing), used by the renderer and the info panel.</summary>
    internal sealed class RouteSignInfo
    {
        public RouteSignType Type;
        public int TypeIndex;
        public bool Flashing;
        public RouteStructureMesh Structure;
        public string PrefabName;
        public string DefaultState;
    }

    /// <summary>
    /// Creates the route marker assemblies and destination signs: one StaticObjectPrefab per
    /// <see cref="RouteSignTypes"/> entry in the Highway category, and a flashing copy (white LEDs, like the other
    /// green guide signs) in Highway (Flashing). The prefab holds the posts and hardware; plates and artwork are
    /// drawn per sign by <see cref="RouteSignRenderSystem"/>.
    /// </summary>
    internal static class RouteSignLibrary
    {
        public const string PrefabPrefix = "USRS_Route_";
        public const string LedColor = "white";

        private static RouteSignInfo[] s_Infos = Array.Empty<RouteSignInfo>();

        public static bool IsRegistered => s_Infos.Length > 0;

        public static FlashingDefaults Flashing { get; private set; } = new FlashingDefaults();

        /// <summary>Info by (type index, flashing).</summary>
        public static RouteSignInfo Get(int typeIndex, bool flashing)
        {
            int index = typeIndex * 2 + (flashing ? 1 : 0);
            return typeIndex >= 0 && index < s_Infos.Length ? s_Infos[index] : null;
        }

        public static string PrefabName(RouteSignType type, bool flashing) => PrefabPrefix + type.Id + (flashing ? "_Flashing" : "");

        public static string IconPath(RouteSignType type, bool flashing) => "Icons/Route/" + type.Id + (flashing ? "_Flashing" : "") + ".png";

        public static void Register(
            PrefabSystem prefabSystem,
            AssetFactory factory,
            UIAssetCategoryPrefab category,
            UIAssetCategoryPrefab flashingCategory,
            IReadOnlyDictionary<MaterialSlot, SurfaceAsset> surfaces,
            FlashingDefaults flashing,
            bool bolts,
            Func<string, string> iconUrl)
        {
            Flashing = flashing ?? new FlashingDefaults();
            var infos = new RouteSignInfo[RouteSignTypes.All.Count * 2];
            int created = 0;
            for (int i = 0; i < RouteSignTypes.All.Count; i++)
            {
                RouteSignType type = RouteSignTypes.All[i];
                for (int variant = 0; variant < 2; variant++)
                {
                    bool lit = variant == 1;
                    UIAssetCategoryPrefab target = lit ? flashingCategory : category;
                    if (lit && target == null)
                        continue;
                    try
                    {
                        infos[i * 2 + variant] = CreateSign(prefabSystem, factory, type, i, lit, target, surfaces, bolts, iconUrl);
                        created++;
                    }
                    catch (Exception e)
                    {
                        Mod.Log.Error(e, "Could not create route sign " + PrefabName(type, lit));
                    }
                }
            }
            s_Infos = infos;
            Mod.Log.Info($"Registered {created} route marker and destination signs.");
        }

        private static RouteSignInfo CreateSign(
            PrefabSystem prefabSystem,
            AssetFactory factory,
            RouteSignType type,
            int typeIndex,
            bool flashing,
            UIAssetCategoryPrefab category,
            IReadOnlyDictionary<MaterialSlot, SurfaceAsset> surfaces,
            bool bolts,
            Func<string, string> iconUrl)
        {
            RouteStructureMesh structure = RouteSignBuilder.BuildStructure(type, flashing, Flashing, bolts);
            RouteSignState defaults = type.Default();
            defaults.Normalize(type);
            var info = new RouteSignInfo
            {
                Type = type,
                TypeIndex = typeIndex,
                Flashing = flashing,
                Structure = structure,
                PrefabName = PrefabName(type, flashing),
                DefaultState = defaults.Encode(type),
            };

            if (prefabSystem.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), info.PrefabName), out PrefabBase _))
                return info;

            AssemblyMesh assembly = structure.Assembly;
            var partSurfaces = new List<SurfaceAsset>(assembly.Parts.Count);
            foreach (MeshPart part in assembly.Parts)
            {
                if (!surfaces.TryGetValue(part.Slot, out SurfaceAsset surface))
                    throw new InvalidOperationException("No material for " + part.Slot);
                partSurfaces.Add(surface);
            }

            RenderPrefab render = factory.CreateRenderPrefab(info.PrefabName + "_Mesh", assembly, partSurfaces);
            int lampIndex = assembly.Parts.FindIndex(p => p.Slot == MaterialSlot.Lamp);
            if (lampIndex >= 0)
                SignLibraryLoader.AddFlashingLights(render, lampIndex, Flashing, LedColor);
            prefabSystem.AddPrefab(render);

            var prefab = ScriptableObject.CreateInstance<StaticObjectPrefab>();
            prefab.name = info.PrefabName;
            prefab.m_Meshes = new[] { new ObjectMeshInfo { m_Mesh = render } };
            prefab.m_Circular = false;

            PlaceableObject placeable = prefab.AddComponent<PlaceableObject>();
            placeable.m_ConstructionCost = type.Cost + (flashing ? 300u : 0u);
            placeable.m_XPReward = 0;

            // Only the posts collide, so plates and mast arms can overhang the road.
            StandingObject standing = prefab.AddComponent<StandingObject>();
            standing.m_LegSize = new Unity.Mathematics.float3(assembly.LegSize.X, assembly.LegSize.Y, assembly.LegSize.Z);
            standing.m_LegGap = new Unity.Mathematics.float2(assembly.LegGapX, 0f);
            standing.m_CircularLeg = assembly.CircularLeg;

            RouteSignPrefab route = prefab.AddComponent<RouteSignPrefab>();
            route.m_TypeIndex = typeIndex;
            route.m_Flashing = flashing;

            UIObject ui = prefab.AddComponent<UIObject>();
            ui.m_Group = category;
            ui.m_Priority = type.Priority;
            ui.m_Icon = iconUrl(IconPath(type, flashing));
            ui.m_IsDebugObject = false;
            ui.active = true;

            prefabSystem.AddPrefab(prefab);
            return info;
        }
    }
}
