using System.Collections.Generic;
using USRoadSigns.Catalog;

namespace USRoadSigns.Digital
{
    public enum DisplayKind
    {
        /// <summary>Full-matrix changeable message sign showing text.</summary>
        Text = 0,
        /// <summary>Flashing arrow board.</summary>
        Arrow = 1,
    }

    public enum StructureKind
    {
        /// <summary>Full-size portable changeable message sign on a single-axle trailer.</summary>
        Trailer,
        /// <summary>Small portable sign on a compact trailer.</summary>
        CompactTrailer,
        /// <summary>Arrow board on a single-axle trailer.</summary>
        ArrowTrailer,
        /// <summary>Permanent roadside sign on one round pole.</summary>
        Pole,
        /// <summary>Permanent sign hung from a cantilever arm over the road.</summary>
        Cantilever,
        /// <summary>Permanent sign on a truss gantry spanning the road.</summary>
        Gantry,
    }

    public enum LedColor
    {
        Amber = 0,
        Yellow = 1,
        White = 2,
        Red = 3,
        Green = 4,
        Blue = 5,
    }

    public sealed class DigitalSignType
    {
        public string Id;
        public string Name;
        public string Description;
        public StructureKind Structure;
        public DisplayKind Display;
        /// <summary>Full-matrix size in LEDs (text signs) - arrow boards use <see cref="ArrowBoard"/>.</summary>
        public int Columns;
        public int Rows;
        /// <summary>Text lines per page.</summary>
        public int Lines;
        /// <summary>Distance between LED centers, meters.</summary>
        public float Pitch;
        /// <summary>Clear span between gantry legs, meters (gantries only).</summary>
        public float Span;
        public bool HasBeacons;
        /// <summary>Default message in preset notation ('|' between lines, '||' between pages).</summary>
        public string DefaultMessage;
        public ArrowMode DefaultArrow;
        public int Priority;
        public uint Cost;

        public string Icon => "Icons/Digital/" + Id + ".png";
    }

    public static class DigitalSignTypes
    {
        public const string CategoryId = "DigitalSigns";
        public const string CategoryName = "Digital Message Signs";
        public const string CategoryDescription = "Changeable message signs and arrow boards. Select a placed sign to type its own message, pick a preset, change the LED color or set the arrow pattern.";
        public const string CategoryIcon = "Icons/Digital/Category.png";
        public const int CategoryPriority = 5;

        /// <summary>Toolbar category holding every digital sign (inside the US Road Signs menu).</summary>
        public static CategoryDef Category() => new CategoryDef
        {
            Id = CategoryId,
            Name = CategoryName,
            Description = CategoryDescription,
            Icon = CategoryIcon,
            Priority = CategoryPriority,
        };

        public static readonly IReadOnlyList<DigitalSignType> All = new List<DigitalSignType>
        {
            new DigitalSignType
            {
                Id = "PortableMessageTrailer",
                Name = "Portable Message Sign (Trailer)",
                Description = "Solar-powered portable changeable message sign on a work-zone trailer. 3 lines of 8 characters, 48 x 27 full matrix.",
                Structure = StructureKind.Trailer, Display = DisplayKind.Text,
                Columns = 48, Rows = 27, Lines = 3, Pitch = 0.058f,
                DefaultMessage = "ROAD|WORK|AHEAD||USE|CAUTION|",
                Priority = 10, Cost = 2500,
            },
            new DigitalSignType
            {
                Id = "CompactMessageTrailer",
                Name = "Compact Message Sign (Trailer)",
                Description = "Small towable message board, about the size of a TV. 2 lines of 5 characters.",
                Structure = StructureKind.CompactTrailer, Display = DisplayKind.Text,
                Columns = 32, Rows = 18, Lines = 2, Pitch = 0.042f,
                DefaultMessage = "SLOW|DOWN",
                Priority = 20, Cost = 1200,
            },
            new DigitalSignType
            {
                Id = "ArrowBoardTrailer",
                Name = "Arrow Board (Trailer)",
                Description = "Type C flashing arrow board on a solar trailer. Select it to switch between arrows, sequential chevrons and caution modes.",
                Structure = StructureKind.ArrowTrailer, Display = DisplayKind.Arrow,
                DefaultArrow = ArrowMode.RightArrow,
                Priority = 30, Cost = 1800,
            },
            new DigitalSignType
            {
                Id = "RoadsideMessagePole",
                Name = "Roadside Message Sign (Pole)",
                Description = "Permanent dynamic message sign on a single round pole. 3 lines of 15 characters.",
                Structure = StructureKind.Pole, Display = DisplayKind.Text,
                Columns = 90, Rows = 27, Lines = 3, Pitch = 0.066f, HasBeacons = true,
                DefaultMessage = "TEXTING|WHILE DRIVING|OH CELL NO",
                Priority = 40, Cost = 9000,
            },
            new DigitalSignType
            {
                Id = "CantileverMessageSign",
                Name = "Overhead Message Sign (Cantilever)",
                Description = "Dynamic message sign hung over the lanes from a single-pole cantilever arm. 3 lines of 18 characters.",
                Structure = StructureKind.Cantilever, Display = DisplayKind.Text,
                Columns = 108, Rows = 27, Lines = 3, Pitch = 0.066f, HasBeacons = true,
                DefaultMessage = "2 LEFT LANES|CLOSED AHEAD|",
                Priority = 50, Cost = 14000,
            },
            new DigitalSignType
            {
                Id = "GantryMessageSign3Lane",
                Name = "Overhead Message Sign (Gantry, 3 Lanes)",
                Description = "Walk-in dynamic message sign on a truss gantry spanning about 18 m (3 lanes and shoulders). 3 lines of 21 characters.",
                Structure = StructureKind.Gantry, Display = DisplayKind.Text,
                Columns = 126, Rows = 27, Lines = 3, Pitch = 0.066f, Span = 18f, HasBeacons = true,
                DefaultMessage = "VISITING IN-LAWS?|SLOW DOWN|GET THERE LATE",
                Priority = 60, Cost = 22000,
            },
            new DigitalSignType
            {
                Id = "GantryMessageSign5Lane",
                Name = "Overhead Message Sign (Gantry, 5 Lanes)",
                Description = "Walk-in dynamic message sign on a truss gantry spanning about 26 m (5 lanes and shoulders). 3 lines of 21 characters.",
                Structure = StructureKind.Gantry, Display = DisplayKind.Text,
                Columns = 126, Rows = 27, Lines = 3, Pitch = 0.066f, Span = 26f, HasBeacons = true,
                DefaultMessage = "TRAVEL TIME TO|DOWNTOWN|12-15 MIN",
                Priority = 70, Cost = 28000,
            },
        };

        public static DigitalSignType Find(string id)
        {
            foreach (DigitalSignType type in All)
            {
                if (type.Id == id)
                    return type;
            }
            return null;
        }

        /// <summary>
        /// Common messages in preset notation ('|' between lines, '||' between pages). The UI only offers presets
        /// whose lines fit the selected sign.
        /// </summary>
        public static readonly IReadOnlyList<string> Presets = new List<string>
        {
            // Work zones
            "ROAD|WORK|AHEAD",
            "ROAD|WORK|AHEAD||USE|CAUTION",
            "USE|CAUTION",
            "SLOW|TRAFFIC|AHEAD",
            "STOPPED|TRAFFIC|AHEAD",
            "PREPARE|TO STOP",
            "EXPECT|DELAYS",
            "LEFT|LANE|CLOSED",
            "RIGHT|LANE|CLOSED",
            "2 LEFT|LANES|CLOSED",
            "2 RIGHT|LANES|CLOSED",
            "LANES|SHIFT|AHEAD",
            "LANES|CHANGE|AHEAD",
            "MERGE|LEFT",
            "MERGE|RIGHT",
            "ROAD|CLOSED|AHEAD",
            "ROAD|CLOSED||DETOUR|>",
            "DETOUR|AHEAD",
            "RAMP|CLOSED",
            "EXIT|CLOSED",
            "FLAGGER|AHEAD",
            "WORKERS|PRESENT",
            "TRUCKS|ENTERING",
            "NIGHT|WORK|AHEAD",
            "ONE LANE|ROAD|AHEAD",
            "BUMP|AHEAD",
            "LOOSE|GRAVEL",
            "UNEVEN|LANES",
            "NO|SHOULDER",
            "FINES|DOUBLED",
            "SPEED|LIMIT|45",
            "SLOW|DOWN",
            // Incidents and weather
            "CRASH|AHEAD",
            "ACCIDENT|AHEAD||EXPECT|DELAYS",
            "FOG|AHEAD",
            "ICE ON|BRIDGE",
            "HIGH|WINDS",
            "FLOODED|ROAD|AHEAD",
            "CRASH AHEAD|LEFT LANE|BLOCKED",
            "DISABLED|VEHICLE|RIGHT SHOULDER",
            "ALL LANES|CLOSED|AT EXIT 12",
            "STOPPED TRAFFIC|AHEAD|BE PREPARED TO STOP",
            // Travel and events
            "EVENT|PARKING|>",
            "EVENT|TRAFFIC|USE EXIT 5",
            "TRAVEL TIME TO|DOWNTOWN|12-15 MIN",
            "I-95 NORTH|EXIT 12|8 MIN",
            "EXPRESS LANES|OPEN",
            "EXPRESS LANES|CLOSED",
            "AMBER ALERT|CALL 911|WITH INFO",
            // Safety campaigns
            "BUCKLE|UP",
            "SLOW DOWN|MOVE OVER",
            "CLICK IT|OR TICKET",
            "DONT|TEXT AND|DRIVE",
            "TEXTING|WHILE DRIVING|OH CELL NO",
            "WATCH FOR|MOTORCYCLES",
            "DRIVE SOBER|OR GET|PULLED OVER",
            "VISITING IN-LAWS?|SLOW DOWN|GET THERE LATE",
            "TEXTING AND DRIVING?|SAY IT: I'M THE|PROBLEM IT'S ME",
            "HEADLIGHTS ON|WHEN WIPERS ON",
            "LEFT LANE|FOR PASSING|ONLY",
        };

        /// <summary>Presets whose pages fit the sign (line count and width).</summary>
        public static List<string> PresetsFor(DigitalSignType type)
        {
            var list = new List<string>();
            if (type.Display != DisplayKind.Text)
                return list;
            foreach (string preset in Presets)
            {
                bool fits = true;
                foreach (string page in preset.Split(new[] { "||" }, System.StringSplitOptions.None))
                {
                    string[] lines = page.Split('|');
                    if (lines.Length > type.Lines || !MessageLayout.Fits(lines, type.Columns))
                    {
                        fits = false;
                        break;
                    }
                }
                if (fits)
                    list.Add(preset);
            }
            return list;
        }
    }

    public static class LedColors
    {
        public const int Count = 6;

        /// <summary>sRGB color of a lit LED.</summary>
        public static void Get(LedColor color, out byte r, out byte g, out byte b)
        {
            switch (color)
            {
                case LedColor.Yellow: r = 255; g = 214; b = 40; break;
                case LedColor.White: r = 255; g = 246; b = 228; break;
                case LedColor.Red: r = 255; g = 40; b = 24; break;
                case LedColor.Green: r = 40; g = 255; b = 90; break;
                case LedColor.Blue: r = 60; g = 140; b = 255; break;
                default: r = 255; g = 150; b = 20; break;   // amber, ~590 nm
            }
        }

        public static string Name(LedColor color)
        {
            switch (color)
            {
                case LedColor.Yellow: return "Yellow";
                case LedColor.White: return "White";
                case LedColor.Red: return "Red";
                case LedColor.Green: return "Green";
                case LedColor.Blue: return "Blue";
                default: return "Amber";
            }
        }
    }
}
