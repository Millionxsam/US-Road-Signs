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

            catalog.AddFlashingVariants(signIds);

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

        /// <summary>
        /// For every category with "flashing": true, adds a "&lt;Name&gt; (Flashing)" category right after it and a
        /// flashing copy of each of its signs (id + "_Flashing").
        /// </summary>
        private void AddFlashingVariants(HashSet<string> signIds)
        {
            var categories = new List<CategoryDef>();
            var variants = new List<SignDef>();
            foreach (CategoryDef source in Root.Categories)
            {
                categories.Add(source);
                if (!source.Flashing)
                    continue;

                var flashing = new CategoryDef
                {
                    Id = source.Id + "Flashing",
                    Name = source.Name + " (Flashing)",
                    Description = source.Name + " signs with flashing LED lights built into the border.",
                    Icon = "Icons/Categories/" + source.Id + "Flashing.png",
                    IconFace = source.IconFace,
                    Priority = source.Priority + 5,
                    FlashingOf = source.Id,
                };
                if (m_Categories.ContainsKey(flashing.Id))
                {
                    m_Warnings.Add("catalog.json: category '" + flashing.Id + "' already exists; flashing variants of '" + source.Id + "' skipped");
                    continue;
                }

                int added = 0;
                foreach (SignDef sign in m_Signs)
                {
                    if (sign.Category != source.Id)
                        continue;
                    SignDef variant = sign.CloneAsFlashing(flashing.Id, sign.FlashingColor ?? source.FlashingColor ?? AutomaticLedColor(sign));
                    UsePaddedFace(variant);
                    if (!signIds.Add(variant.Id))
                    {
                        m_Warnings.Add("sign '" + variant.Id + "' already exists; flashing variant skipped");
                        continue;
                    }
                    variants.Add(variant);
                    added++;
                }
                if (added == 0)
                    continue;
                categories.Add(flashing);
                m_Categories[flashing.Id] = flashing;
            }
            Root.Categories = categories;
            m_Signs.AddRange(variants);
        }

        /// <summary>Points the variant's lit panel at the enlarged "-LED" face when its face asks for padding.</summary>
        private void UsePaddedFace(SignDef variant)
        {
            int lit = Geometry.BorderLights.LitPanelIndex(this, variant);
            FaceDef face = m_Faces[variant.Panels[lit].Face];
            if (face.FlashingPadding <= 0f)
                return;
            string paddedId = face.Id + "-LED";
            if (!m_Faces.ContainsKey(paddedId))
            {
                m_Faces[paddedId] = face.Padded(face.FlashingPadding);
            }
            var panels = new List<PanelDef>(variant.Panels.Count);
            foreach (PanelDef panel in variant.Panels)
                panels.Add(new PanelDef { Face = panel.Face, OffsetX = panel.OffsetX, GapAbove = panel.GapAbove });
            panels[lit].Face = paddedId;
            variant.Panels = panels;
            variant.FlashingPanel = lit;
        }

        /// <summary>LED color matching the sign's color scheme, judged by the panel that carries the LEDs:
        /// orange work-zone signs get orange LEDs, yellow and fluorescent yellow-green warning signs amber,
        /// green, blue and brown guide/information signs white, and regulatory signs (red, white or black) red.</summary>
        private string AutomaticLedColor(SignDef sign)
        {
            FaceDef face = m_Faces[sign.Panels[Geometry.BorderLights.LitPanelIndex(this, sign)].Face];
            string background = (face.Background ?? "").Trim().ToLowerInvariant();
            switch (background)
            {
                case "orange":
                    return "orange";
                case "yellow":
                case "fyg":
                    return "amber";
                case "green":
                case "blue":
                case "brown":
                    return "white";
                default:
                    return "red";
            }
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
