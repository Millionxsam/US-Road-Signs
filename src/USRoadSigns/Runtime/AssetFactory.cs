using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Colossal.AssetPipeline;
using Colossal.AssetPipeline.Importers;
using Colossal.AssetPipeline.Native;
using Colossal.IO.AssetDatabase;
using Colossal.Mathematics;
using Game.Prefabs;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using USRoadSigns.Geometry;
using Texture = Colossal.AssetPipeline.Importers.TextureImporter.Texture;
using TextureAsset = Colossal.IO.AssetDatabase.TextureAsset;

namespace USRoadSigns.Runtime
{
    /// <summary>
    /// Creates the engine assets behind every sign: textures, surfaces (materials), geometry and render prefabs.
    /// Everything is written to a mod-owned asset database under ModsData, registered with the global asset
    /// database so the game's renderer can resolve assets by id (RenderPrefab stores asset references).
    /// </summary>
    internal sealed class AssetFactory : IDisposable
    {
        /// <summary>Cities: Skylines II opaque lit material template (Colossal.AssetPipeline.Constants.Material.Shader.kDefault).</summary>
        public const string DefaultMaterialTemplate = "Default";

        public const string BaseColorMap = "_BaseColorMap";
        public const string MaskMap = "_MaskMap";
        public const string ControlMask = "_ControlMask";
        public const string NormalMap = "_NormalMap";
        public const string EmissiveColorMap = "_EmissiveColorMap";

        private readonly ILocalAssetDatabase m_Database;
        private readonly Task m_Registration;
        private readonly string m_Root;

        private AssetFactory(ILocalAssetDatabase database, Task registration, string root)
        {
            m_Database = database;
            m_Registration = registration;
            m_Root = root;
        }

        public static AssetFactory Create(string rootFolder)
        {
            try
            {
                if (Directory.Exists(rootFolder))
                    Directory.Delete(rootFolder, recursive: true);
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Could not clear the previous asset cache (" + e.Message + "); continuing.");
            }
            Directory.CreateDirectory(rootFolder);

            ILocalAssetDatabase database = AssetDatabase.GetTransient(0L, rootFolder);
            Task registration = AssetDatabase.global.RegisterDatabase(database);
            return new AssetFactory(database, registration, rootFolder);
        }

        public bool WaitUntilRegistered(TimeSpan timeout)
        {
            try
            {
                return m_Registration.Wait(timeout);
            }
            catch (AggregateException e)
            {
                Mod.Log.Error(e.InnerException ?? e, "Registering the US Road Signs asset database failed");
                return false;
            }
        }

        public static bool TemplateExists(string template)
        {
            return AssetDatabase.global.resources.materialLibrary.GetMaterialHash(template) != -1;
        }

        // ------------------------------------------------------------------------------------------
        // Textures
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds a compressed pipeline texture from bottom-up RGBA pixels. Thread-safe (no Unity API calls):
        /// mip generation and BC7 compression run in the game's native texture library.
        /// </summary>
        public static Texture BuildTexture(string name, byte[] rgbaBottomUp, int width, int height, bool srgb, TextureWrapMode wrap, int anisoLevel, int compressionEffort = 3)
        {
            var pixels = new NativeArray<byte>(rgbaBottomUp.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            pixels.CopyFrom(rgbaBottomUp);
            Texture texture = Texture.CreateUncompressed1Mip(name, width, height, srgb, pixels);
            texture.wrapMode = wrap;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = anisoLevel;
            texture.ComputeMips(wrap == TextureWrapMode.Clamp, alphaIsTransparency: false);
            texture.CompressBC(compressionEffort, NativeTextures.BlockCompressionFormat.BC7);
            return texture;
        }

        /// <summary>Stores a pipeline texture in the database and releases the CPU copy. Main thread.</summary>
        public TextureAsset SaveTexture(string folder, Texture texture)
        {
            try
            {
                TextureAsset asset = m_Database.AddAsset(AssetDataPath.Create(folder, texture.name, EscapeStrategy.None), (TextureImporter.ITexture)texture);
                asset.Save();
                asset.Unload();
                return asset;
            }
            finally
            {
                texture.Dispose();
            }
        }

        // ------------------------------------------------------------------------------------------
        // Surfaces (materials)
        // ------------------------------------------------------------------------------------------

        public SurfaceAsset CreateSurface(string name, IReadOnlyDictionary<string, TextureAsset> textures)
        {
            var surface = new Surface(name, DefaultMaterialTemplate);
            try
            {
                SurfaceAsset asset = m_Database.AddAsset(AssetDataPath.Create("Surfaces", name, EscapeStrategy.None), surface);
                foreach (KeyValuePair<string, TextureAsset> texture in textures)
                {
                    if (texture.Value != null)
                        asset.UpdateTexture(texture.Key, texture.Value);
                }
                asset.Save(0, force: true, saveTextures: false);
                return asset;
            }
            finally
            {
                surface.Dispose();
            }
        }

        // ------------------------------------------------------------------------------------------
        // Geometry and render prefabs
        // ------------------------------------------------------------------------------------------

        /// <summary>Converts engine-independent parts to Unity meshes, stores them as one GeometryAsset and
        /// creates the RenderPrefab (one sub-mesh + one surface per part). Main thread.</summary>
        public RenderPrefab CreateRenderPrefab(string name, AssemblyMesh assembly, IReadOnlyList<SurfaceAsset> surfaces)
        {
            if (surfaces.Count != assembly.Parts.Count)
                throw new ArgumentException("Surface count does not match mesh part count");

            var meshes = new Mesh[assembly.Parts.Count];
            for (int i = 0; i < meshes.Length; i++)
                meshes[i] = ToUnityMesh(name + "_" + i, assembly.Parts[i]);

            GeometryAsset geometry = m_Database.AddAsset(AssetDataPath.Create("Geometry", name, EscapeStrategy.None), meshes);
            geometry.Save();

            RenderPrefab render = PrefabBase.Create<RenderPrefab>(name);
            render.geometryAsset = geometry;
            render.surfaceAssets = surfaces;
            render.bounds = new Bounds3(
                new float3(assembly.BoundsMin.X, assembly.BoundsMin.Y, assembly.BoundsMin.Z),
                new float3(assembly.BoundsMax.X, assembly.BoundsMax.Y, assembly.BoundsMax.Z));
            render.surfaceArea = assembly.SurfaceArea;
            render.meshCount = meshes.Length;
            render.vertexCount = assembly.VertexCount;
            render.indexCount = assembly.IndexCount;
            render.manualVTRequired = false;
            render.isImpostor = false;

            // The geometry is streamed back from the database by the renderer when needed.
            geometry.Unload();
            return render;
        }

        private static Mesh ToUnityMesh(string name, MeshPart part)
        {
            int count = part.VertexCount;
            var vertices = new List<Vector3>(count);
            var normals = new List<Vector3>(count);
            var uvs = new List<Vector2>(count);
            for (int i = 0; i < count; i++)
            {
                Vec3 p = part.Positions[i];
                Vec3 n = part.Normals[i];
                Vec2 uv = part.UVs[i];
                vertices.Add(new Vector3(p.X, p.Y, p.Z));
                normals.Add(new Vector3(n.X, n.Y, n.Z));
                uvs.Add(new Vector2(uv.X, uv.Y));
            }

            var mesh = new Mesh { name = name };
            mesh.indexFormat = count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(part.Indices, 0, calculateBounds: true);
            mesh.RecalculateTangents();
            return mesh;
        }

        public void Dispose()
        {
            // The database stays registered for the lifetime of the game session; its files are replaced on the next start.
        }
    }
}
