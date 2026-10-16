using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using USRoadSigns.Geometry;

namespace USRoadSigns
{
    /// <summary>Post style used by signs whose catalog support type is "post".</summary>
    public enum PostStyle
    {
        SquareTube = 0,
        UChannel = 1,
        RoundPipe = 2,
    }

    [FileLocation(nameof(USRoadSigns))]
    [SettingsUIGroupOrder(kAppearanceGroup, kInfoGroup)]
    [SettingsUIShowGroupName(kAppearanceGroup, kInfoGroup)]
    public sealed class ModSettings : ModSetting
    {
        public const string kSection = "Main";
        public const string kAppearanceGroup = "Appearance";
        public const string kInfoGroup = "Info";

        public ModSettings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        /// <summary>Post used by standard single-post signs. Applied when the game starts.</summary>
        [SettingsUISection(kSection, kAppearanceGroup)]
        public PostStyle StandardPostStyle { get; set; }

        /// <summary>Model bolt heads and washers on the sign faces.</summary>
        [SettingsUISection(kSection, kAppearanceGroup)]
        public bool ShowMountingBolts { get; set; }

        /// <summary>Informational text shown in the options page.</summary>
        [SettingsUIMultilineText]
        [SettingsUISection(kSection, kInfoGroup)]
        public string RestartNotice => string.Empty;

        [SettingsUIMultilineText]
        [SettingsUISection(kSection, kInfoGroup)]
        public string LibraryStatus => string.Empty;

        public override void SetDefaults()
        {
            StandardPostStyle = PostStyle.SquareTube;
            ShowMountingBolts = true;
        }

        internal AssemblyOptions ToAssemblyOptions()
        {
            string style;
            switch (StandardPostStyle)
            {
                case PostStyle.UChannel:
                    style = SupportTypes.UChannel;
                    break;
                case PostStyle.RoundPipe:
                    style = SupportTypes.RoundPipe;
                    break;
                default:
                    style = SupportTypes.SquareTube;
                    break;
            }
            return new AssemblyOptions { DefaultPostStyle = style, IncludeBolts = ShowMountingBolts };
        }
    }
}
