using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Scripting;
using USRoadSigns.Catalog;
using USRoadSigns.Digital;
using USRoadSigns.Geometry;
using ObjectTransform = Game.Objects.Transform;

namespace USRoadSigns.Route
{
    /// <summary>
    /// Draws what changes per route sign: the plates of route marker assemblies (their outline depends on the
    /// chosen marker), their printed artwork, braces and flashing LEDs, and the face of destination signs.
    /// Plate meshes are shared by every sign with the same plate; artwork textures by every plate showing the
    /// same thing. Drawing uses the game's default lit shader through Graphics.DrawMesh, like the digital signs.
    /// </summary>
    public partial class RouteSignRenderSystem : GameSystemBase
    {
        private const string ShaderName = "BH/SG_DefaultShader";
        private const int SweepInterval = 300;          // frames
        private const int UnusedLifetime = 900;         // frames a mesh or texture may go unused before it is destroyed
        private const double FaceBudgetMs = 6.0;        // spreads texture generation over frames after loading a city
        private const float MaxDrawDistance = 1500f;    // meters

        private static readonly int s_BaseColorMap = Shader.PropertyToID("_BaseColorMap");
        private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_EmissiveColorMap = Shader.PropertyToID("_EmissiveColorMap");
        private static readonly int s_EmissiveColor = Shader.PropertyToID("_EmissiveColor");
        private static readonly int s_EmissiveIntensity = Shader.PropertyToID("_EmissiveIntensity");
        private static readonly int s_EmissiveExposureWeight = Shader.PropertyToID("_EmissiveExposureWeight");
        private static readonly int s_MaskMap = Shader.PropertyToID("_MaskMap");
        private static readonly int s_ControlMask = Shader.PropertyToID("_ControlMask");
        private static readonly int s_Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int s_Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int s_DecalLayerMask = Shader.PropertyToID("colossal_DecalLayerMask");
        private static readonly int s_ColorMask0 = Shader.PropertyToID("colossal_ColorMask0");
        private static readonly int s_ColorMask1 = Shader.PropertyToID("colossal_ColorMask1");
        private static readonly int s_ColorMask2 = Shader.PropertyToID("colossal_ColorMask2");

        /// <summary>A plate (or brace) mesh: one sub-mesh per non-empty material slot.</summary>
        private sealed class CachedMesh
        {
            public Mesh Mesh;
            public MaterialSlot[] Slots;
            public int LastUsedFrame;
        }

        private sealed class CachedFace
        {
            public Material Material;
            public int LastUsedFrame;
        }

        private struct Part
        {
            public CachedMesh Mesh;
            public string FaceKey;
            public Matrix4x4 Local;
        }

        /// <summary>Resolved drawing list of one sign entity.</summary>
        private sealed class CachedSign
        {
            public string State;
            public int TypeIndex;
            public bool Flashing;
            public List<RouteDrawItem> Items;
            public Part[] Parts;
            public int LastFrame;
        }

        private struct DrawItem
        {
            public Mesh Mesh;
            public int SubMesh;
            public Matrix4x4 Matrix;
            public Material Material;
            public bool Shadows;
        }

        private readonly Dictionary<Entity, CachedSign> m_Signs = new Dictionary<Entity, CachedSign>();
        private readonly Dictionary<string, CachedMesh> m_Meshes = new Dictionary<string, CachedMesh>(StringComparer.Ordinal);
        private readonly Dictionary<string, CachedFace> m_Faces = new Dictionary<string, CachedFace>(StringComparer.Ordinal);
        private readonly List<DrawItem> m_Draws = new List<DrawItem>();
        private readonly List<string> m_Expired = new List<string>();
        private readonly List<Entity> m_StaleSigns = new List<Entity>();

        private EntityQuery m_Query;
        private Shader m_Shader;
        private Material m_Aluminum, m_Galvanized, m_Dark, m_LensOn, m_LensOff;
        private readonly List<Texture2D> m_SharedTextures = new List<Texture2D>();
        private Texture2D m_FaceMask, m_FaceControl;
        private bool m_ResourcesFailed;
        private int m_PreparedFrame = -1;
        private readonly System.Diagnostics.Stopwatch m_FaceTimer = new System.Diagnostics.Stopwatch();
        private float m_AppliedIntensity = -1f;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RouteSign>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<ObjectTransform>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Hidden>(),
                },
            });
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            foreach (CachedMesh mesh in m_Meshes.Values)
                DestroyObject(mesh.Mesh);
            m_Meshes.Clear();
            foreach (CachedFace face in m_Faces.Values)
                DestroyFace(face);
            m_Faces.Clear();
            foreach (Material material in new[] { m_Aluminum, m_Galvanized, m_Dark, m_LensOn, m_LensOff })
                DestroyObject(material);
            foreach (Texture2D texture in m_SharedTextures)
                DestroyObject(texture);
            base.OnDestroy();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            m_Draws.Clear();
            m_Signs.Clear();
            m_PreparedFrame = -1;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            m_Draws.Clear();
            if (m_Query.IsEmptyIgnoreFilter || !RouteSignLibrary.IsRegistered || !EnsureResources())
                return;

            UpdateIntensity();
            float time = UnityEngine.Time.realtimeSinceStartup;
            int frame = UnityEngine.Time.frameCount;
            m_FaceTimer.Restart();
            FlashingDefaults flash = RouteSignLibrary.Flashing;
            float period = Math.Max(0.2f, flash.Period);
            bool ledsOn = (time % period) / period < MathUtil.Clamp(flash.OnFraction, 0.1f, 0.9f);

            Camera camera = Camera.main;
            bool haveCamera = camera != null;
            Vector3 cameraPosition = haveCamera ? camera.transform.position : Vector3.zero;

            NativeArray<Entity> entities = m_Query.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity entity in entities)
                {
                    try
                    {
                        Collect(entity, frame, ledsOn, haveCamera, cameraPosition);
                    }
                    catch (Exception e)
                    {
                        Mod.Log.Error(e, "Could not draw route sign " + entity);
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }
            m_PreparedFrame = frame;

            if (frame % SweepInterval == 0)
                Sweep(frame);
        }

        private void Collect(Entity entity, int frame, bool ledsOn, bool haveCamera, Vector3 cameraPosition)
        {
            PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
            if (!EntityManager.TryGetComponent(prefabRef.m_Prefab, out RouteSignData data))
                return;
            RouteSignInfo info = RouteSignLibrary.Get(data.m_TypeIndex, data.m_Flashing);
            if (info == null)
                return;

            ObjectTransform transform = EntityManager.GetComponentData<ObjectTransform>(entity);
            if (haveCamera && math.distancesq(transform.m_Position, (float3)cameraPosition) > MaxDrawDistance * MaxDrawDistance)
                return;

            // While a sign is being moved or previewed, the tool's temporary copy shows the original's settings.
            RouteSign sign = EntityManager.GetComponentData<RouteSign>(entity);
            if (EntityManager.TryGetComponent(entity, out Temp temp) && temp.m_Original != Entity.Null
                && EntityManager.TryGetComponent(temp.m_Original, out RouteSign original))
            {
                sign = original;
            }
            string state = (sign.m_Flags & RouteSignFlags.Customized) != 0 ? sign.m_State.ToString() : info.DefaultState;

            if (!m_Signs.TryGetValue(entity, out CachedSign cached) || cached.TypeIndex != info.TypeIndex || cached.Flashing != info.Flashing
                || !string.Equals(cached.State, state, StringComparison.Ordinal))
            {
                RouteSignState resolved = RouteSignState.Decode(info.Type, state);
                cached = new CachedSign
                {
                    State = state,
                    TypeIndex = info.TypeIndex,
                    Flashing = info.Flashing,
                    Items = RouteSignBuilder.DrawItems(info.Type, resolved, info.Flashing, RouteSignLibrary.Flashing, info.Structure),
                };
                m_Signs[entity] = cached;
            }
            cached.LastFrame = frame;

            if (cached.Parts == null && !Resolve(cached, frame))
                return;

            Matrix4x4 objectMatrix = Matrix4x4.TRS(transform.m_Position, transform.m_Rotation, Vector3.one);
            foreach (Part part in cached.Parts)
            {
                part.Mesh.LastUsedFrame = frame;
                Material face = null;
                if (part.FaceKey != null && m_Faces.TryGetValue(part.FaceKey, out CachedFace cachedFace))
                {
                    cachedFace.LastUsedFrame = frame;
                    face = cachedFace.Material;
                }
                Matrix4x4 matrix = objectMatrix * part.Local;
                for (int i = 0; i < part.Mesh.Slots.Length; i++)
                {
                    Material material = MaterialFor(part.Mesh.Slots[i], face, ledsOn);
                    if (material == null)
                        continue;
                    m_Draws.Add(new DrawItem
                    {
                        Mesh = part.Mesh.Mesh,
                        SubMesh = i,
                        Matrix = matrix,
                        Material = material,
                        Shadows = part.Mesh.Slots[i] != MaterialSlot.Lamp,
                    });
                }
            }
        }

        /// <summary>Builds the meshes and artwork a sign needs. Returns false while artwork is still queued.</summary>
        private bool Resolve(CachedSign cached, int frame)
        {
            foreach (RouteDrawItem item in cached.Items)
            {
                if (item.Face == null || m_Faces.ContainsKey(item.Face.Key))
                    continue;
                if (m_FaceTimer.Elapsed.TotalMilliseconds > FaceBudgetMs)
                    return false;
                m_Faces[item.Face.Key] = new CachedFace { Material = CreateFaceMaterial(item.Face.Render()), LastUsedFrame = frame };
            }

            var parts = new Part[cached.Items.Count];
            for (int i = 0; i < parts.Length; i++)
            {
                RouteDrawItem item = cached.Items[i];
                if (!m_Meshes.TryGetValue(item.MeshKey, out CachedMesh mesh))
                {
                    mesh = BuildMesh(item.MeshKey, item.BuildMesh());
                    m_Meshes[item.MeshKey] = mesh;
                }
                mesh.LastUsedFrame = frame;
                parts[i] = new Part
                {
                    Mesh = mesh,
                    FaceKey = item.Face?.Key,
                    Local = Matrix4x4.TRS(new Vector3(item.Position.X, item.Position.Y, item.Position.Z), Quaternion.identity,
                        new Vector3(item.Scale.X, item.Scale.Y, item.Scale.Z)),
                };
            }
            cached.Parts = parts;
            return true;
        }

        private Material MaterialFor(MaterialSlot slot, Material face, bool ledsOn)
        {
            switch (slot)
            {
                case MaterialSlot.Face: return face;
                case MaterialSlot.Aluminum: return m_Aluminum;
                case MaterialSlot.Galvanized: return m_Galvanized;
                case MaterialSlot.DarkPlastic: return m_Dark;
                case MaterialSlot.Lamp: return ledsOn ? m_LensOn : m_LensOff;
                default: return m_Galvanized;
            }
        }

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            // Only draw what was prepared this frame (nothing stale after leaving a city).
            if (m_PreparedFrame != UnityEngine.Time.frameCount)
                return;
            foreach (DrawItem item in m_Draws)
            {
                Graphics.DrawMesh(item.Mesh, item.Matrix, item.Material, 0, null, item.SubMesh, null,
                    item.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, true, null, LightProbeUsage.BlendProbes);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Caches
        // ------------------------------------------------------------------------------------------

        private static CachedMesh BuildMesh(string name, List<MeshPart> parts)
        {
            var used = new List<MeshPart>();
            foreach (MeshPart part in parts)
            {
                if (!part.IsEmpty)
                    used.Add(part);
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            foreach (MeshPart part in used)
            {
                for (int i = 0; i < part.VertexCount; i++)
                {
                    Vec3 p = part.Positions[i], n = part.Normals[i];
                    Vec2 uv = part.UVs[i];
                    vertices.Add(new Vector3(p.X, p.Y, p.Z));
                    normals.Add(new Vector3(n.X, n.Y, n.Z));
                    uvs.Add(new Vector2(uv.X, uv.Y));
                }
            }

            var mesh = new Mesh { name = "USRS_Route_" + name };
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = used.Count;
            var slots = new MaterialSlot[used.Count];
            int baseVertex = 0;
            for (int s = 0; s < used.Count; s++)
            {
                MeshPart part = used[s];
                var indices = new int[part.Indices.Count];
                for (int i = 0; i < indices.Length; i++)
                    indices[i] = part.Indices[i] + baseVertex;
                mesh.SetTriangles(indices, s, calculateBounds: false);
                slots[s] = part.Slot;
                baseVertex += part.VertexCount;
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(markNoLongerReadable: true);
            return new CachedMesh { Mesh = mesh, Slots = slots };
        }

        private void Sweep(int frame)
        {
            m_StaleSigns.Clear();
            foreach (KeyValuePair<Entity, CachedSign> entry in m_Signs)
            {
                if (frame - entry.Value.LastFrame > SweepInterval)
                    m_StaleSigns.Add(entry.Key);
            }
            foreach (Entity entity in m_StaleSigns)
                m_Signs.Remove(entity);

            // Signs not drawn for a while were dropped above; the ones left keep everything they use alive.
            m_Expired.Clear();
            foreach (KeyValuePair<string, CachedFace> entry in m_Faces)
            {
                if (frame - entry.Value.LastUsedFrame > UnusedLifetime)
                    m_Expired.Add(entry.Key);
            }
            foreach (string key in m_Expired)
            {
                DestroyFace(m_Faces[key]);
                m_Faces.Remove(key);
            }
            m_Expired.Clear();
            foreach (KeyValuePair<string, CachedMesh> entry in m_Meshes)
            {
                if (frame - entry.Value.LastUsedFrame > UnusedLifetime)
                    m_Expired.Add(entry.Key);
            }
            foreach (string key in m_Expired)
            {
                DestroyObject(m_Meshes[key].Mesh);
                m_Meshes.Remove(key);
            }
        }

        private static void DestroyFace(CachedFace face)
        {
            if (face.Material == null)
                return;
            Texture texture = face.Material.GetTexture(s_BaseColorMap);
            DestroyObject(face.Material);
            DestroyObject(texture);
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj != null)
                UnityEngine.Object.Destroy(obj);
        }

        // ------------------------------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------------------------------

        private bool EnsureResources()
        {
            if (m_ResourcesFailed)
                return false;
            if (m_Shader != null)
                return true;
            m_Shader = Shader.Find(ShaderName);
            if (m_Shader == null)
            {
                m_ResourcesFailed = true;
                Mod.Log.Error("Shader " + ShaderName + " was not found; route sign plates cannot be drawn.");
                return false;
            }

            // Mask maps: R = metallic, A = glossiness (linear). Control mask: no color masks, no snow.
            Texture2D control = Solid(0, 0, 0, 255, linear: true);
            m_FaceControl = control;
            m_FaceMask = Solid(0, 0, 0, 105, linear: true);
            m_Aluminum = CreateMaterial("USRS_RouteAluminum", Solid(186, 189, 192, 255, false), Solid(200, 0, 0, 120, true), control, 0.8f, 0.47f, null);
            m_Galvanized = CreateMaterial("USRS_RouteGalvanized", Solid(160, 163, 161, 255, false), Solid(190, 0, 0, 90, true), control, 0.75f, 0.35f, null);
            m_Dark = CreateMaterial("USRS_RouteDark", Solid(18, 18, 18, 255, false), Solid(0, 0, 0, 110, true), control, 0f, 0.43f, null);
            m_LensOff = CreateMaterial("USRS_RouteLensOff", Solid(196, 196, 188, 255, false), Solid(0, 0, 0, 220, true), control, 0f, 0.86f, null);
            RgbaColor led = RouteSignLibrary.Flashing.GetColor(RouteSignLibrary.LedColor);
            m_LensOn = CreateMaterial("USRS_RouteLensOn", Solid(led.R, led.G, led.B, 255, false), Solid(0, 0, 0, 220, true), control, 0f, 0.86f,
                Solid(led.R, led.G, led.B, 255, false));
            return true;
        }

        private Material CreateFaceMaterial(RgbaImage image)
        {
            var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "USRS_RouteFace",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            texture.SetPixelData(image.Pixels, 0);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            // Retroreflective sheeting: not metallic, a little sheen.
            return CreateMaterial("USRS_RouteFace", texture, m_FaceMask, m_FaceControl, 0f, 0.42f, null);
        }

        private Material CreateMaterial(string name, Texture2D baseColor, Texture2D mask, Texture2D control, float metallic, float smoothness, Texture2D emissive)
        {
            var material = new Material(m_Shader) { name = name };
            material.SetTexture(s_BaseColorMap, baseColor);
            material.SetColor(s_BaseColor, Color.white);
            material.SetTexture(s_MaskMap, mask);
            material.SetTexture(s_ControlMask, control);
            material.SetFloat(s_Metallic, metallic);
            material.SetFloat(s_Smoothness, smoothness);
            material.SetColor(s_ColorMask0, Color.white);
            material.SetColor(s_ColorMask1, Color.white);
            material.SetColor(s_ColorMask2, Color.white);
            material.SetFloat(s_DecalLayerMask, math.asfloat(8));
            if (emissive != null)
            {
                material.SetTexture(s_EmissiveColorMap, emissive);
                material.SetColor(s_EmissiveColor, Color.white);
                material.SetFloat(s_EmissiveIntensity, CurrentIntensity());
                material.SetFloat(s_EmissiveExposureWeight, 0f);
            }
            else
            {
                material.SetColor(s_EmissiveColor, Color.black);
            }
            material.enableInstancing = true;
            try
            {
                HDMaterial.ValidateMaterial(material);
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Could not validate the route sign material (" + e.Message + ")");
            }
            return material;
        }

        private void UpdateIntensity()
        {
            float intensity = CurrentIntensity();
            if (Math.Abs(intensity - m_AppliedIntensity) < 1e-4f)
                return;
            m_AppliedIntensity = intensity;
            m_LensOn?.SetFloat(s_EmissiveIntensity, intensity);
        }

        /// <summary>LED glow, following the LED brightness option of the digital signs.</summary>
        private static float CurrentIntensity()
        {
            ModSettings settings = Mod.Instance?.Settings;
            float percent = settings != null ? settings.LedBrightness : 100f;
            return 3f * Math.Max(0f, percent) / 100f;
        }

        private Texture2D Solid(byte r, byte g, byte b, byte a, bool linear)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false, linear: linear) { name = "USRS_RouteSolid" };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(r, g, b, a);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            m_SharedTextures.Add(texture);
            return texture;
        }
    }
}
