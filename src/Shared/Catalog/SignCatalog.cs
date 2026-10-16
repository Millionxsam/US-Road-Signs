using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace USRoadSigns.Catalog
{
    /// <summary>
    /// The loaded, validated sign library. Layout on disk (same in the repository "data" folder
    /// and in the installed mod's "Catalog" folder):
    ///   catalog.json          global settings, categories, colors
    ///   faces/*.json          sign face artwork definitions
    ///   signs/*.json          placeable sign assemblies
    ///   symbols/*.json        vector pictograms (build tool only, optional at runtime)
    /// Adding a sign = adding entries to faces/*.json and signs/*.json, then running the build.
    /// </summary>
    public sealed class SignCatalog
    {
        public CatalogRoot Root { get; private set; }
        public IReadOnlyDictionary<string, FaceDef> Faces => m_Faces;
        public IReadOnlyList<SignDef> Signs => m_Signs;
        public IReadOnlyDictionary<string, SymbolDef> Symbols => m_Symbols;
        public IReadOnlyDictionary<string, CategoryDef> Categories => m_Categories;
        public IReadOnlyList<string> Warnings => m_Warnings;

        private readonly Dictionary<string, FaceDef> m_Faces = new Dictionary<string, FaceDef>(StringComparer.Ordinal);
        private readonly List<SignDef> m_Signs = new List<SignDef>();
        private readonly Dictionary<string, SymbolDef> m_Symbols = new Dictionary<string, SymbolDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, CategoryDef> m_Categories = new Dictionary<string, CategoryDef>(StringComparer.Ordinal);
        private readonly List<string> m_Warnings = new List<string>();

        private static readonly JsonSerializerSettings s_JsonSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Culture = CultureInfo.InvariantCulture,
        };

        public static SignCatalog Load(string rootFolder, bool loadSymbols)
        {
            var catalog = new SignCatalog();
            string catalogPath = Path.Combine(rootFolder, "catalog.json");
            if (!File.Exists(catalogPath))
                throw new FileNotFoundException("Sign catalog not found", catalogPath);

            catalog.Root = Deserialize<CatalogRoot>(catalogPath) ?? new CatalogRoot();
            foreach (CategoryDef category in catalog.Root.Categories)
            {
                if (string.IsNullOrEmpty(category.Id))
                    throw new InvalidDataException("catalog.json: category without id");
                catalog.m_Categories[category.Id] = category;
            }

            foreach (string file in EnumerateJson(Path.Combine(rootFolder, "faces")))
            {
                FaceFile faceFile = Deserialize<FaceFile>(file);
                if (faceFile?.Faces == null)
                    continue;
                foreach (FaceDef face in faceFile.Faces)
                {
                    if (string.IsNullOrEmpty(face.Id))
                        throw new InvalidDataException(file + ": face without id");
                    if (catalog.m_Faces.ContainsKey(face.Id))
                        throw new InvalidDataException(file + ": duplicate face id '" + face.Id + "'");
                    if (face.Width <= 0f || face.Height <= 0f)
                        throw new InvalidDataException(file + ": face '" + face.Id + "' has no size");
                    catalog.m_Faces.Add(face.Id, face);
                }
            }

            var signIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in EnumerateJson(Path.Combine(rootFolder, "signs")))
            {
                SignFile signFile = Deserialize<SignFile>(file);
                if (signFile?.Signs == null)
                    continue;
                int order = 0;
                foreach (SignDef sign in signFile.Signs)
                {
                    order++;
                    string where = file + ": sign '" + sign.Id + "'";
                    if (string.IsNullOrEmpty(sign.Id))
                        throw new InvalidDataException(file + ": sign without id");
                    if (!signIds.Add(sign.Id))
                        throw new InvalidDataException(where + " is a duplicate id");
                    if (sign.Panels == null || sign.Panels.Count == 0)
                    {
                        catalog.m_Warnings.Add(where + " has no panels; skipped");
                        continue;
                    }
                    if (!catalog.m_Categories.ContainsKey(sign.Category ?? ""))
                    {
                        catalog.m_Warnings.Add(where + " uses unknown category '" + sign.Category + "'; moved to Other");
                        sign.Category = "Other";
                    }
                    bool missingFace = false;
                    foreach (PanelDef panel in sign.Panels)
                    {
                        if (panel.Face == null || !catalog.m_Faces.ContainsKey(panel.Face))
                        {
                            catalog.m_Warnings.Add(where + " references unknown face '" + panel.Face + "'; skipped");
                            missingFace = true;
                        }
                    }
                    if (missingFace)
                        continue;
                    if (sign.Support == null)
                        sign.Support = new SupportDef();
                    if (string.IsNullOrEmpty(sign.Name))
                        sign.Name = sign.Id;
                    if (sign.Priority == 0)
                        sign.Priority = order * 10;
                    catalog.m_Signs.Add(sign);
                }
            }

            if (loadSymbols)
            {
                foreach (string file in EnumerateJson(Path.Combine(rootFolder, "symbols")))
                {
                    SymbolFile symbolFile = Deserialize<SymbolFile>(file);
                    if (symbolFile?.Symbols == null)
                        continue;
                    foreach (SymbolDef symbol in symbolFile.Symbols)
                    {
                        if (string.IsNullOrEmpty(symbol.Id))
                            throw new InvalidDataException(file + ": symbol without id");
                        if (catalog.m_Symbols.ContainsKey(symbol.Id))
                            throw new InvalidDataException(file + ": duplicate symbol id '" + symbol.Id + "'");
                        catalog.m_Symbols.Add(symbol.Id, symbol);
                    }
                }
            }

            return catalog;
        }

        public string GetPrefabName(SignDef sign) => Root.PrefabPrefix + sign.Id;

        /// <summary>Faces actually used by at least one sign (only these need textures at runtime).</summary>
        public IEnumerable<FaceDef> UsedFaces()
        {
            return m_Signs.SelectMany(s => s.Panels).Select(p => p.Face).Distinct().Select(id => m_Faces[id]);
        }

        public bool TryGetColor(string nameOrHex, out RgbaColor color)
        {
            color = default;
            if (string.IsNullOrEmpty(nameOrHex))
                return false;
            if (Root.Colors.TryGetValue(nameOrHex, out string hex))
                return RgbaColor.TryParseHex(hex, out color);
            return RgbaColor.TryParseHex(nameOrHex, out color);
        }

        public RgbaColor GetColor(string nameOrHex)
        {
            if (TryGetColor(nameOrHex, out RgbaColor color))
                return color;
            throw new InvalidDataException("Unknown color '" + nameOrHex + "'");
        }

        private static IEnumerable<string> EnumerateJson(string folder)
        {
            if (!Directory.Exists(folder))
                return Enumerable.Empty<string>();
            return Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }

        private static T Deserialize<T>(string path)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path), s_JsonSettings);
            }
            catch (JsonException e)
            {
                throw new InvalidDataException("Invalid JSON in " + path + ": " + e.Message, e);
            }
        }
    }

    /// <summary>8-bit sRGB color.</summary>
    public struct RgbaColor
    {
        public byte R, G, B, A;

        public RgbaColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r; G = g; B = b; A = a;
        }

        public static bool TryParseHex(string hex, out RgbaColor color)
        {
            color = default;
            if (string.IsNullOrEmpty(hex) || hex[0] != '#')
                return false;
            string s = hex.Substring(1);
            if (s.Length != 6 && s.Length != 8)
                return false;
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
                return false;
            if (s.Length == 6)
                color = new RgbaColor((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
            else
                color = new RgbaColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }
    }

    public static class Units
    {
        public const float InchesToMeters = 0.0254f;
    }
}
