using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SignForge.Preview;
using SignForge.Rendering;
using USRoadSigns.Catalog;
using USRoadSigns.Geometry;

namespace SignForge
{
    /// <summary>
    /// Commands:
    ///   build   --data &lt;dataFolder&gt; --out &lt;modContentFolder&gt;
    ///           Copies the catalog and renders every texture and icon the mod needs.
    ///   preview --data &lt;dataFolder&gt; --content &lt;modContentFolder&gt; --out &lt;folder&gt; --signs id[,id...] [--post square-tube|u-channel|round-pipe]
    ///           Exports OBJ/MTL assemblies (for Blender preview renders).
    ///   sheet   --data &lt;dataFolder&gt; --content &lt;modContentFolder&gt; --out &lt;png&gt;
    ///           Contact sheet of all sign icons for review.
    ///   stats   --data &lt;dataFolder&gt;
    ///           Prints triangle / vertex counts for every assembly.
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    PrintUsage();
                    return 1;
                }
                Dictionary<string, string> options = ParseOptions(args.Skip(1));
                switch (args[0].ToLowerInvariant())
                {
                    case "build":
                        return Build(Require(options, "data"), Require(options, "out"));
                    case "preview":
                        return PreviewCommand(options);
                    case "sheet":
                        return Sheet(Require(options, "data"), Require(options, "content"), Require(options, "out"));
                    case "stats":
                        return Stats(Require(options, "data"));
                    default:
                        PrintUsage();
                        return 1;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERROR: " + e);
                return 2;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("SignForge build --data <data> --out <modContent>");
            Console.WriteLine("SignForge preview --data <data> --content <modContent> --out <folder> --signs id[,id] [--post style]");
            Console.WriteLine("SignForge sheet --data <data> --content <modContent> --out <file.png>");
            Console.WriteLine("SignForge stats --data <data>");
        }

        private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string key = null;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    key = arg.Substring(2);
                    result[key] = "true";
                }
                else if (key != null)
                {
                    result[key] = arg;
                    key = null;
                }
            }
            return result;
        }

        private static string Require(Dictionary<string, string> options, string name)
        {
            if (!options.TryGetValue(name, out string value) || value == "true")
                throw new ArgumentException("Missing required option --" + name);
            return value;
        }

        private static SignCatalog LoadCatalog(string data)
        {
            SignCatalog catalog = SignCatalog.Load(data, loadSymbols: true);
            foreach (string warning in catalog.Warnings)
                Console.WriteLine("WARNING: " + warning);
            Console.WriteLine($"Catalog: {catalog.Signs.Count} signs, {catalog.Faces.Count} faces, {catalog.Symbols.Count} symbols, {catalog.Categories.Count} categories");
            return catalog;
        }

        // ----------------------------------------------------------------------------------------

        private static int Build(string data, string outFolder)
        {
            var timer = Stopwatch.StartNew();
            SignCatalog catalog = LoadCatalog(data);
            var faces = new FaceRenderer(catalog);
            var icons = new IconRenderer(catalog, faces);

            // 1. Catalog (runtime needs catalog.json, faces and signs; symbols are build-time only).
            string catalogOut = Path.Combine(outFolder, "Catalog");
            if (Directory.Exists(catalogOut))
                Directory.Delete(catalogOut, true);
            CopyFile(Path.Combine(data, "catalog.json"), Path.Combine(catalogOut, "catalog.json"));
            foreach (string sub in new[] { "faces", "signs" })
            {
                foreach (string file in Directory.GetFiles(Path.Combine(data, sub), "*.json"))
                    CopyFile(file, Path.Combine(catalogOut, sub, Path.GetFileName(file)));
            }

            // 2. Face textures (only faces used by signs).
            string facesOut = Path.Combine(outFolder, "Textures", "Faces");
            if (Directory.Exists(facesOut))
                Directory.Delete(facesOut, true);
            Directory.CreateDirectory(facesOut);
            int faceCount = 0;
            foreach (FaceDef face in catalog.UsedFaces())
            {
                PixelBuffer texture = faces.RenderTexture(face);
                texture.SavePng(Path.Combine(facesOut, face.Id + ".png"));
                faceCount++;
            }
            Console.WriteLine($"Rendered {faceCount} face textures");

            // 3. Shared material textures.
            MaterialTextures.GenerateAll(Path.Combine(outFolder, "Textures", "Materials"));
            Console.WriteLine("Generated material textures");

            // 4. Icons.
            string signIconsOut = Path.Combine(outFolder, "Icons", "Signs");
            if (Directory.Exists(signIconsOut))
                Directory.Delete(signIconsOut, true);
            Directory.CreateDirectory(signIconsOut);
            foreach (SignDef sign in catalog.Signs)
                icons.RenderSign(sign, 128).SavePng(Path.Combine(signIconsOut, sign.Id + ".png"));
            Console.WriteLine($"Rendered {catalog.Signs.Count} sign icons");

            foreach (CategoryDef category in catalog.Root.Categories)
            {
                string faceId = category.IconFace;
                string iconCategory = category.FlashingOf ?? category.Id;
                if (string.IsNullOrEmpty(faceId) || !catalog.Faces.ContainsKey(faceId))
                    faceId = catalog.Signs.Where(s => s.Category == iconCategory).Select(s => s.Panels[0].Face).FirstOrDefault();
                if (faceId == null)
                    continue;
                string ledColor = category.FlashingOf == null ? null
                    : catalog.Signs.Where(s => s.Category == category.Id).Select(s => s.LedColor).FirstOrDefault() ?? "amber";
                icons.RenderFace(catalog.Faces[faceId], 96, postStub: false, ledColor: ledColor).SavePng(Path.Combine(outFolder, category.Icon));
            }
            MenuDef menu = catalog.Root.Menu;
            string menuFace = !string.IsNullOrEmpty(menu.IconFace) && catalog.Faces.ContainsKey(menu.IconFace) ? menu.IconFace : catalog.Signs[0].Panels[0].Face;
            icons.RenderFace(catalog.Faces[menuFace], 128, postStub: true).SavePng(Path.Combine(outFolder, menu.Icon));
            Console.WriteLine("Rendered menu and category icons");

            Console.WriteLine($"Build finished in {timer.Elapsed.TotalSeconds:F1}s");
            return 0;
        }

        private static void CopyFile(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, true);
        }

        // ----------------------------------------------------------------------------------------

        private static int PreviewCommand(Dictionary<string, string> options)
        {
            SignCatalog catalog = LoadCatalog(Require(options, "data"));
            string content = Require(options, "content");
            string outFolder = Require(options, "out");
            var assemblyOptions = new AssemblyOptions();
            if (options.TryGetValue("post", out string post))
                assemblyOptions.DefaultPostStyle = post;

            IEnumerable<SignDef> signs = catalog.Signs;
            if (options.TryGetValue("signs", out string ids) && ids != "all")
            {
                var set = new HashSet<string>(ids.Split(','), StringComparer.Ordinal);
                signs = catalog.Signs.Where(s => set.Contains(s.Id));
            }

            // Also write one combined scene with all requested signs in a row.
            var combinedMtl = new StringBuilder();
            var written = new HashSet<string>();
            var combined = new AssemblyMesh();
            float cursor = 0f;
            foreach (SignDef sign in signs)
            {
                AssemblyMesh mesh = AssemblyBuilder.Build(catalog, sign, assemblyOptions);
                ObjExporter.Export(mesh, Path.Combine(outFolder, sign.Id + ".obj"), content);
                Console.WriteLine($"{sign.Id}: {mesh.IndexCount / 3} triangles, {mesh.VertexCount} vertices, {mesh.Parts.Count} materials, bounds {mesh.BoundsMin} - {mesh.BoundsMax}");

                float width = mesh.BoundsMax.X - mesh.BoundsMin.X;
                float shift = cursor + width * 0.5f;
                foreach (MeshPart part in mesh.Parts)
                {
                    var moved = new MeshPart(part.Slot, part.FaceId);
                    for (int i = 0; i < part.VertexCount; i++)
                        moved.AddVertex(part.Positions[i] + new Vec3(-shift, 0f, 0f), part.Normals[i], part.UVs[i]);
                    moved.Indices.AddRange(part.Indices);
                    combined.Parts.Add(moved);
                }
                cursor += width + 0.6f;
            }
            if (combined.Parts.Count > 0)
            {
                // Center the row.
                foreach (MeshPart part in combined.Parts)
                {
                    for (int i = 0; i < part.VertexCount; i++)
                        part.Positions[i] = part.Positions[i] + new Vec3(cursor * 0.5f, 0f, 0f);
                }
                ObjExporter.Export(combined, Path.Combine(outFolder, "_scene.obj"), content, 0f, false, combinedMtl, written);
            }
            return 0;
        }

        private static int Sheet(string data, string content, string outFile)
        {
            SignCatalog catalog = LoadCatalog(data);
            var faces = new FaceRenderer(catalog);
            var icons = new IconRenderer(catalog, faces);
            const int cell = 196;
            int columns = 8;
            int rows = (catalog.Signs.Count + columns - 1) / columns;
            var sheet = new PixelBuffer(columns * cell, Math.Max(1, rows) * cell);
            sheet.Fill(0x2A, 0x33, 0x3D, 255);
            int index = 0;
            foreach (SignDef sign in catalog.Signs)
            {
                PixelBuffer icon = icons.RenderSign(sign, cell - 20);
                int ox = (index % columns) * cell + 10, oy = (index / columns) * cell + 10;
                for (int y = 0; y < icon.Height; y++)
                {
                    for (int x = 0; x < icon.Width; x++)
                    {
                        int s = (y * icon.Width + x) * 4;
                        int d = ((oy + y) * sheet.Width + ox + x) * 4;
                        int a = icon.Bgra[s + 3];
                        for (int c = 0; c < 3; c++)
                            sheet.Bgra[d + c] = (byte)((icon.Bgra[s + c] * a + sheet.Bgra[d + c] * (255 - a)) / 255);
                    }
                }
                index++;
            }
            sheet.SavePng(outFile);
            Console.WriteLine("Wrote " + outFile);
            return 0;
        }

        private static int Stats(string data)
        {
            SignCatalog catalog = LoadCatalog(data);
            foreach (string style in new[] { SupportTypes.SquareTube, SupportTypes.UChannel, SupportTypes.RoundPipe })
            {
                int total = 0, max = 0;
                string maxId = null;
                foreach (SignDef sign in catalog.Signs)
                {
                    AssemblyMesh mesh = AssemblyBuilder.Build(catalog, sign, new AssemblyOptions { DefaultPostStyle = style });
                    int tris = mesh.IndexCount / 3;
                    total += tris;
                    if (tris > max)
                    {
                        max = tris;
                        maxId = sign.Id;
                    }
                }
                Console.WriteLine($"Post style {style}: average {(catalog.Signs.Count > 0 ? total / catalog.Signs.Count : 0)} triangles, max {max} ({maxId})");
            }
            return 0;
        }
    }
}
