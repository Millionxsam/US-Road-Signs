using System.Collections.Generic;
using Colossal;
using USRoadSigns.Catalog;
using USRoadSigns.Digital;

namespace USRoadSigns.Localization
{
    /// <summary>
    /// English strings: the options page plus every menu, category and sign name from the catalog.
    /// Game key formats (verified against Game.UI.InGame.PrefabUISystem):
    ///   menu      Services.NAME[id]      / Services.DESCRIPTION[id]
    ///   category  SubServices.NAME[id]   / Assets.SUB_SERVICE_DESCRIPTION[id]
    ///   asset     Assets.NAME[prefab]    / Assets.DESCRIPTION[prefab]
    /// </summary>
    public sealed class LocaleEN : IDictionarySource
    {
        private readonly ModSettings m_Settings;
        private readonly SignCatalog m_Catalog;

        public LocaleEN(ModSettings settings, SignCatalog catalog)
        {
            m_Settings = settings;
            m_Catalog = catalog;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var entries = new Dictionary<string, string>
            {
                { m_Settings.GetSettingsLocaleID(), "US Road Signs" },
                { m_Settings.GetOptionTabLocaleID(ModSettings.kSection), "Main" },
                { m_Settings.GetOptionGroupLocaleID(ModSettings.kAppearanceGroup), "Appearance" },
                { m_Settings.GetOptionGroupLocaleID(ModSettings.kInfoGroup), "Information" },

                { m_Settings.GetOptionLabelLocaleID(nameof(ModSettings.StandardPostStyle)), "Standard post style" },
                { m_Settings.GetOptionDescLocaleID(nameof(ModSettings.StandardPostStyle)), "Support used by standard single-post signs: perforated square steel tube, galvanized U-channel, or round galvanized pipe with clamp brackets. Signs with special supports (large guide signs, barricades, portable stands) are not affected. Takes effect after restarting the game." },
                { m_Settings.GetEnumValueLocaleID(PostStyle.SquareTube), "Square perforated tube" },
                { m_Settings.GetEnumValueLocaleID(PostStyle.UChannel), "Galvanized U-channel" },
                { m_Settings.GetEnumValueLocaleID(PostStyle.RoundPipe), "Round pipe with brackets" },

                { m_Settings.GetOptionLabelLocaleID(nameof(ModSettings.ShowMountingBolts)), "Show mounting bolts" },
                { m_Settings.GetOptionDescLocaleID(nameof(ModSettings.ShowMountingBolts)), "Adds small bolt heads and washers where signs are fastened to their supports. Takes effect after restarting the game." },

                { m_Settings.GetOptionGroupLocaleID(ModSettings.kDigitalGroup), "Digital message signs" },
                { m_Settings.GetOptionLabelLocaleID(nameof(ModSettings.LedBrightness)), "LED brightness" },
                { m_Settings.GetOptionDescLocaleID(nameof(ModSettings.LedBrightness)), "How strongly the LEDs of digital message signs, arrow boards and their beacons glow. Applies immediately." },

                { m_Settings.GetOptionLabelLocaleID(nameof(ModSettings.RestartNotice)), "Appearance changes are applied when the game starts. Restart the game after changing them; signs already placed in your cities will update automatically." },
                { m_Settings.GetOptionLabelLocaleID(nameof(ModSettings.LibraryStatus)), m_Catalog != null
                    ? $"Sign library: {m_Catalog.Signs.Count} signs in {m_Catalog.Categories.Count} categories. Find them in the \"US Road Signs\" toolbar menu."
                    : "The sign library could not be loaded. See Logs\\USRoadSigns.log." },
            };

            if (m_Catalog != null)
            {
                MenuDef menu = m_Catalog.Root.Menu;
                entries["Services.NAME[" + menu.Id + "]"] = menu.Name;
                entries["Services.DESCRIPTION[" + menu.Id + "]"] = menu.Description;

                foreach (CategoryDef category in m_Catalog.Root.Categories)
                {
                    string id = CategoryPrefabName(m_Catalog, category);
                    entries["SubServices.NAME[" + id + "]"] = category.Name;
                    entries["Assets.SUB_SERVICE_DESCRIPTION[" + id + "]"] = category.Description;
                }

                foreach (SignDef sign in m_Catalog.Signs)
                {
                    string prefab = m_Catalog.GetPrefabName(sign);
                    entries["Assets.NAME[" + prefab + "]"] = sign.Name;
                    string description = sign.Description ?? "";
                    if (!string.IsNullOrEmpty(sign.Code))
                        description = string.IsNullOrEmpty(description) ? "MUTCD " + sign.Code : description + " (MUTCD " + sign.Code + ")";
                    entries["Assets.DESCRIPTION[" + prefab + "]"] = description;
                }

                CategoryDef digital = DigitalSignTypes.Category();
                string digitalId = CategoryPrefabName(m_Catalog, digital);
                entries["SubServices.NAME[" + digitalId + "]"] = digital.Name;
                entries["Assets.SUB_SERVICE_DESCRIPTION[" + digitalId + "]"] = digital.Description;
                foreach (DigitalSignType type in DigitalSignTypes.All)
                {
                    string prefab = DigitalSignLibrary.PrefabName(type);
                    entries["Assets.NAME[" + prefab + "]"] = type.Name;
                    entries["Assets.DESCRIPTION[" + prefab + "]"] = type.Description + " Select it after placing to change what it shows.";
                }
            }

            return entries;
        }

        public void Unload()
        {
        }

        public static string CategoryPrefabName(SignCatalog catalog, CategoryDef category) => catalog.Root.Menu.Id + "." + category.Id;
    }
}
