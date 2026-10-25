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
using ObjectTransform = Game.Objects.Transform;

namespace USRoadSigns.Digital
{
    /// <summary>
    /// Draws the live display of every digital sign: the current message page (or arrow board frame) as a textured
    /// quad just in front of the sign's LED window, plus the warning beacons.
    /// Textures are generated on the CPU from the 5x7 LED font and shared between signs showing the same thing.
    /// Rendering uses the game's default lit shader through Graphics.DrawMesh, the approach Write Everywhere uses.
    /// </summary>
    public partial class DigitalSignRenderSystem : GameSystemBase
    {
        private const string ShaderName = "BH/SG_DefaultShader";
        private const float FlashPeriod = 1.0f;
        private const float FlashOnFraction = 0.7f;
        private const float BeaconPeriod = 1.0f;
        private const int SweepInterval = 300;      // frames
        private const int UnusedLifetime = 900;     // frames a visual may go unused before it is destroyed

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

        /// <summary>Everything drawn for one look (type + message + color + alignment, or arrow mode).</summary>
        private sealed class Visual
        {
            public Material[] Frames;
            public List<TimedFrame> Sequence;   // arrow boards only
            public float SequenceLength;
            public int LastUsedFrame;
        }

        private struct DrawItem
        {
            public Matrix4x4 Matrix;
            public Material Material;
        }

        /// <summary>Last resolved settings of one sign entity.</summary>
        private struct CachedSign
        {
            public DigitalSign Raw;
            public int TypeIndex;
            public DigitalSignState State;
            public string Key;
            public int LastFrame;
        }

        private readonly Dictionary<Entity, CachedSign> m_Signs = new Dictionary<Entity, CachedSign>();
        private readonly List<Entity> m_StaleSigns = new List<Entity>();
        private readonly Dictionary<string, Visual> m_Visuals = new Dictionary<string, Visual>(StringComparer.Ordinal);
        private readonly List<DrawItem> m_Draws = new List<DrawItem>();
        private readonly List<string> m_Expired = new List<string>();
        private readonly List<Texture2D> m_Textures = new List<Texture2D>();

        private EntityQuery m_Query;
        private Shader m_Shader;
        private Mesh m_Quad;
        private Texture2D m_MaskMap;
        private Texture2D m_ControlMask;
        private Material m_BeaconOn;
        private Material m_BeaconOff;
        private bool m_ResourcesFailed;
        private int m_PreparedFrame = -1;
        private float m_AppliedIntensity = -1f;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<DigitalSign>(),
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
            ClearVisuals();
            DestroyObject(m_BeaconOn);
            DestroyObject(m_BeaconOff);
            DestroyObject(m_Quad);
            DestroyObject(m_MaskMap);
            DestroyObject(m_ControlMask);
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
            if (m_Query.IsEmptyIgnoreFilter || !DigitalSignLibrary.IsRegistered || !EnsureResources())
                return;

            UpdateIntensity();

            float time = UnityEngine.Time.realtimeSinceStartup;
            int frame = UnityEngine.Time.frameCount;
            NativeArray<Entity> entities = m_Query.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity entity in entities)
                {
                    try
                    {
                        Collect(entity, time, frame);
                    }
                    catch (Exception e)
                    {
                        Mod.Log.Error(e, "Could not draw digital sign " + entity);
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

        private void Collect(Entity entity, float time, int frame)
        {
            PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
            if (!EntityManager.TryGetComponent(prefabRef.m_Prefab, out DigitalSignData data))
                return;
            DigitalSignInfo info = DigitalSignLibrary.Get(data.m_TypeIndex);
            if (info == null)
                return;

            // While a sign is being moved or previewed, the tool's temporary copy shows the original's settings.
            DigitalSign sign = EntityManager.GetComponentData<DigitalSign>(entity);
            if (EntityManager.TryGetComponent(entity, out Temp temp) && temp.m_Original != Entity.Null
                && EntityManager.TryGetComponent(temp.m_Original, out DigitalSign original))
            {
                sign = original;
            }

            // Resolving builds strings; only do it when the sign's settings changed.
            if (!m_Signs.TryGetValue(entity, out CachedSign cached) || cached.TypeIndex != data.m_TypeIndex || !SameSettings(cached.Raw, sign))
            {
                DigitalSignState resolved = DigitalSignState.Resolve(info.Type, sign);
                cached = new CachedSign
                {
                    Raw = sign,
                    TypeIndex = data.m_TypeIndex,
                    State = resolved,
                    Key = info.Type.Display == DisplayKind.Arrow ? ArrowKey(info.Type, resolved) : TextKey(info.Type, resolved),
                };
            }
            cached.LastFrame = frame;
            m_Signs[entity] = cached;
            DigitalSignState state = cached.State;

            ObjectTransform transform = EntityManager.GetComponentData<ObjectTransform>(entity);
            Matrix4x4 objectMatrix = Matrix4x4.TRS(transform.m_Position, transform.m_Rotation, Vector3.one);

            Material material = CurrentFrame(info, state, cached.Key, time, frame);
            if (material != null)
            {
                DisplayRect d = info.Display;
                m_Draws.Add(new DrawItem
                {
                    Matrix = objectMatrix * Matrix4x4.TRS(new Vector3(d.Center.X, d.Center.Y, d.Center.Z), Quaternion.identity, new Vector3(d.Width, d.Height, 1f)),
                    Material = material,
                });
            }

            for (int i = 0; i < info.Beacons.Length; i++)
            {
                BeaconLens beacon = info.Beacons[i];
                // Alternating pairs: beacons 1 and 3 flash together, then 2 and 4, so two are always lit.
                bool firstHalf = (time / BeaconPeriod) % 1f < 0.5f;
                bool lit = state.Beacons && (firstHalf == (i % 2 == 0));
                m_Draws.Add(new DrawItem
                {
                    Matrix = objectMatrix * Matrix4x4.TRS(new Vector3(beacon.Center.X, beacon.Center.Y, beacon.Center.Z + 0.002f), Quaternion.identity, new Vector3(beacon.Diameter, beacon.Diameter, 1f)),
                    Material = lit ? m_BeaconOn : m_BeaconOff,
                });
            }
        }

        private static bool SameSettings(DigitalSign a, DigitalSign b)
        {
            return a.m_Flags == b.m_Flags && a.m_Color == b.m_Color && a.m_Align == b.m_Align && a.m_Arrow == b.m_Arrow
                && a.m_PageMs == b.m_PageMs && a.m_Message.Equals(b.m_Message);
        }

        private Material CurrentFrame(DigitalSignInfo info, DigitalSignState state, string key, float time, int frame)
        {
            DigitalSignType type = info.Type;
            if (type.Display == DisplayKind.Arrow)
            {
                Visual arrow = GetVisual(key, frame, () => BuildArrowVisual(state));
                Visual dark = GetVisual(BlankKey(type), frame, () => BuildBlankVisual(type));
                if (arrow.SequenceLength <= 0f)
                    return dark.Frames[0];
                float t = time % arrow.SequenceLength;
                foreach (TimedFrame step in arrow.Sequence)
                {
                    if (t < step.Seconds)
                        return step.Index >= 0 ? arrow.Frames[step.Index] : dark.Frames[0];
                    t -= step.Seconds;
                }
                return dark.Frames[0];
            }

            Visual text = GetVisual(key, frame, () => BuildTextVisual(type, state));
            if (state.Flash && (time % FlashPeriod) / FlashPeriod >= FlashOnFraction)
                return GetVisual(BlankKey(type), frame, () => BuildBlankVisual(type)).Frames[0];
            int pages = text.Frames.Length;
            if (pages == 1)
                return text.Frames[0];
            float pageSeconds = Math.Max(0.5f, state.PageMs / 1000f);
            int page = (int)(time / pageSeconds) % pages;
            return text.Frames[page];
        }

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            // Only draw what was prepared this frame (nothing stale after leaving a city).
            if (m_PreparedFrame != UnityEngine.Time.frameCount)
                return;
            foreach (DrawItem item in m_Draws)
            {
                if (item.Material != null)
                    Graphics.DrawMesh(m_Quad, item.Matrix, item.Material, 0, null, 0, null, ShadowCastingMode.Off, true, null, LightProbeUsage.BlendProbes);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Visual cache
        // ------------------------------------------------------------------------------------------

        private static string TextKey(DigitalSignType type, DigitalSignState state) =>
            "T|" + type.Id + "|" + (int)state.Color + "|" + (int)state.Align + "|" + state.Message;

        private static string ArrowKey(DigitalSignType type, DigitalSignState state) =>
            "A|" + type.Id + "|" + (int)state.Color + "|" + (int)state.Arrow;

        private static string BlankKey(DigitalSignType type) => "B|" + type.Id;

        private Visual GetVisual(string key, int frame, Func<Visual> build)
        {
            if (!m_Visuals.TryGetValue(key, out Visual visual))
            {
                visual = build();
                m_Visuals[key] = visual;
            }
            visual.LastUsedFrame = frame;
            return visual;
        }

        private Visual BuildTextVisual(DigitalSignType type, DigitalSignState state)
        {
            List<string[]> pages = SignMessage.Parse(state.Message, type.Lines);
            var materials = new Material[pages.Count];
            for (int i = 0; i < pages.Count; i++)
            {
                LedFrame frame = MessageLayout.RenderPage(pages[i], type.Columns, type.Rows, state.Align, out _);
                DigitalTextures.RenderMatrix(frame, state.Color, out RgbaImage baseColor, out RgbaImage emissive);
                materials[i] = CreateMaterial(baseColor, emissive);
            }
            return new Visual { Frames = materials };
        }

        private Visual BuildArrowVisual(DigitalSignState state)
        {
            List<LedFrame> frames = ArrowBoard.Frames(state.Arrow, out List<TimedFrame> sequence);
            LedFrame lamps = ArrowBoard.LampLayout();
            var materials = new Material[frames.Count];
            for (int i = 0; i < frames.Count; i++)
            {
                DigitalTextures.RenderLamps(lamps, frames[i], state.Color, out RgbaImage baseColor, out RgbaImage emissive);
                materials[i] = CreateMaterial(baseColor, emissive);
            }
            float length = 0f;
            foreach (TimedFrame step in sequence)
                length += step.Seconds;
            return new Visual { Frames = materials, Sequence = sequence, SequenceLength = length };
        }

        private Visual BuildBlankVisual(DigitalSignType type)
        {
            RgbaImage baseColor, emissive;
            if (type.Display == DisplayKind.Arrow)
                DigitalTextures.RenderLamps(ArrowBoard.LampLayout(), new LedFrame(ArrowBoard.Columns, ArrowBoard.Rows), LedColor.Amber, out baseColor, out emissive);
            else
                DigitalTextures.RenderMatrix(new LedFrame(type.Columns, type.Rows), LedColor.Amber, out baseColor, out emissive);
            return new Visual { Frames = new[] { CreateMaterial(baseColor, emissive) } };
        }

        private void Sweep(int frame)
        {
            m_Expired.Clear();
            foreach (KeyValuePair<string, Visual> entry in m_Visuals)
            {
                if (frame - entry.Value.LastUsedFrame > UnusedLifetime)
                    m_Expired.Add(entry.Key);
            }
            foreach (string key in m_Expired)
            {
                DestroyVisual(m_Visuals[key]);
                m_Visuals.Remove(key);
            }

            m_StaleSigns.Clear();
            foreach (KeyValuePair<Entity, CachedSign> entry in m_Signs)
            {
                if (frame - entry.Value.LastFrame > SweepInterval)
                    m_StaleSigns.Add(entry.Key);
            }
            foreach (Entity entity in m_StaleSigns)
                m_Signs.Remove(entity);
        }

        private void ClearVisuals()
        {
            foreach (Visual visual in m_Visuals.Values)
                DestroyVisual(visual);
            m_Visuals.Clear();
        }

        private void DestroyVisual(Visual visual)
        {
            foreach (Material material in visual.Frames)
            {
                if (material == null)
                    continue;
                Texture baseMap = material.GetTexture(s_BaseColorMap);
                Texture emissiveMap = material.GetTexture(s_EmissiveColorMap);
                DestroyObject(material);
                DestroyTexture(baseMap as Texture2D);
                DestroyTexture(emissiveMap as Texture2D);
            }
        }

        private void DestroyTexture(Texture2D texture)
        {
            if (texture != null && m_Textures.Remove(texture))
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
                Mod.Log.Error("Shader " + ShaderName + " was not found; digital sign messages cannot be drawn.");
                return false;
            }

            m_Quad = BuildQuad();

            // Glass-covered display: not metallic, fairly glossy. Control mask: no color masks, no snow (alpha 1).
            m_MaskMap = SolidTexture(0, 0, 0, 140, linear: true);
            m_ControlMask = SolidTexture(0, 0, 0, 255, linear: true);
            m_BeaconOn = CreateMaterial(DigitalTextures.BeaconLens(true), DigitalTextures.BeaconLens(true));
            m_BeaconOff = CreateMaterial(DigitalTextures.BeaconLens(false), new RgbaImage(4, 4));
            return true;
        }

        private void UpdateIntensity()
        {
            float intensity = CurrentIntensity();
            if (Math.Abs(intensity - m_AppliedIntensity) < 1e-4f)
                return;
            m_AppliedIntensity = intensity;
            foreach (Visual visual in m_Visuals.Values)
            {
                foreach (Material material in visual.Frames)
                    material?.SetFloat(s_EmissiveIntensity, intensity);
            }
            m_BeaconOn?.SetFloat(s_EmissiveIntensity, intensity);
        }

        private static float CurrentIntensity()
        {
            ModSettings settings = Mod.Instance?.Settings;
            float percent = settings != null ? settings.LedBrightness : 100f;
            return 2.5f * Math.Max(0f, percent) / 100f;
        }

        private Material CreateMaterial(RgbaImage baseColor, RgbaImage emissive)
        {
            Texture2D baseTexture = ToTexture(baseColor);
            Texture2D emissiveTexture = ToTexture(emissive);

            var material = new Material(m_Shader) { name = "USRS_DigitalDisplay" };
            material.SetTexture(s_BaseColorMap, baseTexture);
            material.SetColor(s_BaseColor, Color.white);
            material.SetTexture(s_EmissiveColorMap, emissiveTexture);
            material.SetColor(s_EmissiveColor, Color.white);
            material.SetFloat(s_EmissiveIntensity, CurrentIntensity());
            material.SetFloat(s_EmissiveExposureWeight, 0f);
            material.SetTexture(s_MaskMap, m_MaskMap);
            material.SetTexture(s_ControlMask, m_ControlMask);
            material.SetFloat(s_Metallic, 0f);
            material.SetFloat(s_Smoothness, 0.55f);
            material.SetColor(s_ColorMask0, Color.white);
            material.SetColor(s_ColorMask1, Color.white);
            material.SetColor(s_ColorMask2, Color.white);
            material.SetFloat(s_DecalLayerMask, math.asfloat(8));
            material.enableInstancing = true;
            try
            {
                HDMaterial.ValidateMaterial(material);
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Could not validate the display material (" + e.Message + ")");
            }
            return material;
        }

        private Texture2D ToTexture(RgbaImage image)
        {
            var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "USRS_DigitalDisplay",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            texture.SetPixelData(image.Pixels, 0);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            m_Textures.Add(texture);
            return texture;
        }

        private static Texture2D SolidTexture(byte r, byte g, byte b, byte a, bool linear)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false, linear: linear) { name = "USRS_DigitalSolid" };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(r, g, b, a);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// Unit quad in the XY plane facing +Z. The viewer looks toward -Z, so their right is -X: u runs from +X to -X.
        /// </summary>
        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "USRS_DigitalQuad" };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            });
            mesh.SetNormals(new List<Vector3> { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
            // Same winding rule as MeshPart: front face where cross(b - a, c - a) points along +Z.
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: true);
            return mesh;
        }
    }
}
