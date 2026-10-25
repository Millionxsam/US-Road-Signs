using System;
using System.Collections.Generic;
using System.Text;

namespace USRoadSigns.Route
{
    public enum ShieldKind
    {
        /// <summary>M1-1: blue shield with a red crown.</summary>
        Interstate = 0,
        /// <summary>M1-2: green Off-Interstate Business Loop shield.</summary>
        BusinessLoop = 1,
        /// <summary>M1-3: green Off-Interstate Business Spur shield.</summary>
        BusinessSpur = 2,
        /// <summary>M1-4: white US shield on a black square.</summary>
        US = 3,
        /// <summary>M1-5: white circle on a black square (generic state route).</summary>
        State = 4,
        /// <summary>M1-6: blue and yellow county pentagon.</summary>
        County = 5,
        /// <summary>M1-7: brown National Forest route marker.</summary>
        Forest = 6,
    }

    /// <summary>Cardinal direction plate (M3 series) above the route marker.</summary>
    public enum CardinalKind
    {
        None = 0,
        North = 1,
        South = 2,
        East = 3,
        West = 4,
    }

    /// <summary>Auxiliary plate (M4 series) above the cardinal direction.</summary>
    public enum AuxKind
    {
        None = 0,
        To = 1,
        Jct = 2,
        Begin = 3,
        End = 4,
        Alt = 5,
        Bypass = 6,
        Business = 7,
        Truck = 8,
    }

    /// <summary>Color of the cardinal, auxiliary and arrow plates.</summary>
    public enum PlateStyle
    {
        /// <summary>Plates match the route marker (blue for Interstates, green for Business routes, ...).</summary>
        Matching = 0,
        /// <summary>Standard black-on-white plates for every route.</summary>
        White = 1,
    }

    /// <summary>Background of destination signs.</summary>
    public enum GuideColor
    {
        Green = 0,
        Blue = 1,
        Brown = 2,
    }

    public enum RouteSignKind
    {
        /// <summary>Route marker assembly: columns of plates (auxiliary, direction, route shield, arrow).</summary>
        Marker,
        /// <summary>Green destination sign: lines of place names with arrows.</summary>
        Destination,
    }

    public enum RouteStructure
    {
        /// <summary>One square post behind the column.</summary>
        Post,
        /// <summary>Two columns side by side on one post between them, held by horizontal channel braces.</summary>
        CenterPost,
        /// <summary>Two posts with horizontal channel braces carrying the columns.</summary>
        TwinPostBraced,
        /// <summary>Two posts directly behind one large column (freeway markers).</summary>
        TwinPost,
        /// <summary>Destination sign on two square posts.</summary>
        GuideTwinPost,
        /// <summary>Destination sign on two steel I-beam posts.</summary>
        GuideIBeam,
        /// <summary>Destination sign hung from a mast arm over the road.</summary>
        GuideMastArm,
    }

    public sealed class RouteSignType
    {
        public string Id;
        public string Name;
        public string Description;
        public RouteSignKind Kind;
        public RouteStructure Structure;

        // Route markers
        public int Columns;
        public bool HasAux;
        public bool HasArrow;
        /// <summary>Nominal route marker size, inches (24 conventional, 36 freeway).</summary>
        public float ShieldSize = 24f;

        // Destination signs
        public int Lines;
        public float FaceWidth;
        public float FaceHeight;
        /// <summary>Capital letter height of destination names, inches.</summary>
        public float LetterHeight;

        public int Priority;
        public uint Cost;
        public Func<RouteSignState> Default;

        public string Icon => "Icons/Route/" + Id + ".png";
    }

    public sealed class RouteColumn
    {
        public AuxKind Aux;
        public CardinalKind Cardinal;
        public ShieldKind Shield;
        public string Number = "";
        /// <summary>County name on County route markers.</summary>
        public string Name = "";
        public ArrowKind Arrow;

        public RouteColumn Clone() => (RouteColumn)MemberwiseClone();
    }

    public sealed class DestinationLine
    {
        public string Text = "";
        public ArrowKind Arrow;
        /// <summary>Optional mileage shown at the right end of the line.</summary>
        public string Distance = "";

        public DestinationLine Clone() => (DestinationLine)MemberwiseClone();
    }

    /// <summary>
    /// What one placed route sign shows. Stored per sign as a compact string (<see cref="Encode"/>), which also
    /// serves as the cache key of its rendered faces.
    /// </summary>
    public sealed class RouteSignState
    {
        public const int MaxNumberLength = 3;
        public const int MaxNameLength = 12;
        public const int MaxTextLength = 26;
        public const int MaxDistanceLength = 5;

        public RouteColumn[] Columns = Array.Empty<RouteColumn>();
        public PlateStyle Plates;
        public DestinationLine[] Lines = Array.Empty<DestinationLine>();
        public GuideColor Color;
        public bool Dividers = true;

        public RouteSignState Clone()
        {
            var copy = (RouteSignState)MemberwiseClone();
            copy.Columns = Array.ConvertAll(Columns, c => c.Clone());
            copy.Lines = Array.ConvertAll(Lines, l => l.Clone());
            return copy;
        }

        public static RouteColumn Column(CardinalKind cardinal, ShieldKind shield, string number, ArrowKind arrow = ArrowKind.None, AuxKind aux = AuxKind.None, string name = "")
        {
            return new RouteColumn { Aux = aux, Cardinal = cardinal, Shield = shield, Number = number, Arrow = arrow, Name = name };
        }

        public static RouteSignState Markers(params RouteColumn[] columns) => new RouteSignState { Columns = columns };

        public static RouteSignState Destinations(params (string Text, ArrowKind Arrow, string Distance)[] lines)
        {
            var state = new RouteSignState { Lines = new DestinationLine[lines.Length] };
            for (int i = 0; i < lines.Length; i++)
                state.Lines[i] = new DestinationLine { Text = lines[i].Text, Arrow = lines[i].Arrow, Distance = lines[i].Distance };
            return state;
        }

        /// <summary>Makes the state fit a type: column and line counts, enum ranges, text lengths and characters.</summary>
        public void Normalize(RouteSignType type)
        {
            RouteSignState defaults = type.Default();
            if (type.Kind == RouteSignKind.Marker)
            {
                var columns = new RouteColumn[type.Columns];
                for (int i = 0; i < columns.Length; i++)
                {
                    RouteColumn c = i < Columns.Length && Columns[i] != null ? Columns[i] : defaults.Columns[Math.Min(i, defaults.Columns.Length - 1)].Clone();
                    c.Aux = type.HasAux ? (AuxKind)Clamp((int)c.Aux, 0, (int)AuxKind.Truck) : AuxKind.None;
                    c.Cardinal = (CardinalKind)Clamp((int)c.Cardinal, 0, (int)CardinalKind.West);
                    c.Shield = (ShieldKind)Clamp((int)c.Shield, 0, (int)ShieldKind.Forest);
                    c.Arrow = type.HasArrow ? (ArrowKind)Clamp((int)c.Arrow, 0, (int)ArrowKind.UpAndRight) : ArrowKind.None;
                    c.Number = CleanNumber(c.Number);
                    c.Name = CleanText(c.Name, MaxNameLength).ToUpperInvariant();
                    columns[i] = c;
                }
                Columns = columns;
                Lines = Array.Empty<DestinationLine>();
                Plates = (PlateStyle)Clamp((int)Plates, 0, (int)PlateStyle.White);
            }
            else
            {
                var lines = new DestinationLine[type.Lines];
                for (int i = 0; i < lines.Length; i++)
                {
                    DestinationLine l = i < Lines.Length && Lines[i] != null ? Lines[i] : new DestinationLine();
                    l.Text = CleanText(l.Text, MaxTextLength);
                    l.Distance = CleanDistance(l.Distance);
                    l.Arrow = IsDestinationArrow(l.Arrow) ? l.Arrow : ArrowKind.None;
                    lines[i] = l;
                }
                Lines = lines;
                Columns = Array.Empty<RouteColumn>();
                Color = (GuideColor)Clamp((int)Color, 0, (int)GuideColor.Brown);
            }
        }

        /// <summary>Arrows used on destination signs (MUTCD D1 series).</summary>
        public static bool IsDestinationArrow(ArrowKind arrow) =>
            arrow == ArrowKind.None || arrow == ArrowKind.Left || arrow == ArrowKind.Right || arrow == ArrowKind.Up
            || arrow == ArrowKind.UpLeft || arrow == ArrowKind.UpRight;

        public static readonly ArrowKind[] DestinationArrows = { ArrowKind.None, ArrowKind.Left, ArrowKind.UpLeft, ArrowKind.Up, ArrowKind.UpRight, ArrowKind.Right };

        // ------------------------------------------------------------------------------------------
        // Text cleaning: only characters the sign font has, and never the encoding separators.
        // ------------------------------------------------------------------------------------------

        public static string CleanText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var sb = new StringBuilder(Math.Min(text.Length, maxLength));
            foreach (char raw in text)
            {
                char c = raw == '\t' ? ' ' : raw;
                if (c == ',' || c == ';' || c == '|' || c == '~')
                    continue;
                if (!StrokeFont.Supports(c) || c > 127)
                    continue;
                if (c == ' ' && (sb.Length == 0 || sb[sb.Length - 1] == ' '))
                    continue;
                sb.Append(c);
                if (sb.Length >= maxLength)
                    break;
            }
            return sb.ToString().TrimEnd();
        }

        public static string CleanNumber(string number)
        {
            if (string.IsNullOrEmpty(number))
                return "";
            var sb = new StringBuilder(MaxNumberLength);
            foreach (char raw in number)
            {
                char c = char.ToUpperInvariant(raw);
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z'))
                    sb.Append(c);
                if (sb.Length >= MaxNumberLength)
                    break;
            }
            return sb.ToString();
        }

        public static string CleanDistance(string distance)
        {
            if (string.IsNullOrEmpty(distance))
                return "";
            var sb = new StringBuilder(MaxDistanceLength);
            foreach (char c in distance)
            {
                if ((c >= '0' && c <= '9') || c == '.' || c == '/' || c == ' ')
                    sb.Append(c);
                if (sb.Length >= MaxDistanceLength)
                    break;
            }
            return sb.ToString().Trim();
        }

        // ------------------------------------------------------------------------------------------
        // Encoding: "M|plates|aux,cardinal,shield,number,name,arrow;..." or "D|color|dividers|text,arrow,distance;..."
        // ------------------------------------------------------------------------------------------

        public string Encode(RouteSignType type)
        {
            var sb = new StringBuilder(96);
            if (type.Kind == RouteSignKind.Marker)
            {
                sb.Append("M|").Append((int)Plates).Append('|');
                for (int i = 0; i < Columns.Length; i++)
                {
                    RouteColumn c = Columns[i];
                    if (i > 0)
                        sb.Append(';');
                    sb.Append((int)c.Aux).Append(',').Append((int)c.Cardinal).Append(',').Append((int)c.Shield).Append(',')
                        .Append(c.Number).Append(',').Append(c.Name).Append(',').Append((int)c.Arrow);
                }
            }
            else
            {
                sb.Append("D|").Append((int)Color).Append('|').Append(Dividers ? 1 : 0).Append('|');
                for (int i = 0; i < Lines.Length; i++)
                {
                    DestinationLine l = Lines[i];
                    if (i > 0)
                        sb.Append(';');
                    sb.Append(l.Text).Append(',').Append((int)l.Arrow).Append(',').Append(l.Distance);
                }
            }
            return sb.ToString();
        }

        /// <summary>Parses <see cref="Encode"/> output; anything unreadable falls back to the type's default.</summary>
        public static RouteSignState Decode(RouteSignType type, string encoded)
        {
            RouteSignState state = null;
            try
            {
                if (!string.IsNullOrEmpty(encoded))
                    state = type.Kind == RouteSignKind.Marker ? DecodeMarker(encoded) : DecodeDestination(encoded);
            }
            catch (Exception)
            {
                state = null;
            }
            state = state ?? type.Default();
            state.Normalize(type);
            return state;
        }

        private static RouteSignState DecodeMarker(string encoded)
        {
            string[] parts = encoded.Split('|');
            if (parts.Length < 3 || parts[0] != "M")
                return null;
            var state = new RouteSignState { Plates = (PlateStyle)Int(parts[1]) };
            string[] columns = parts[2].Split(';');
            state.Columns = new RouteColumn[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                string[] f = columns[i].Split(',');
                if (f.Length < 6)
                    return null;
                state.Columns[i] = new RouteColumn
                {
                    Aux = (AuxKind)Int(f[0]),
                    Cardinal = (CardinalKind)Int(f[1]),
                    Shield = (ShieldKind)Int(f[2]),
                    Number = f[3],
                    Name = f[4],
                    Arrow = (ArrowKind)Int(f[5]),
                };
            }
            return state;
        }

        private static RouteSignState DecodeDestination(string encoded)
        {
            string[] parts = encoded.Split('|');
            if (parts.Length < 4 || parts[0] != "D")
                return null;
            var state = new RouteSignState { Color = (GuideColor)Int(parts[1]), Dividers = Int(parts[2]) != 0 };
            string[] lines = parts[3].Split(';');
            state.Lines = new DestinationLine[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                string[] f = lines[i].Split(',');
                if (f.Length < 3)
                    return null;
                state.Lines[i] = new DestinationLine { Text = f[0], Arrow = (ArrowKind)Int(f[1]), Distance = f[2] };
            }
            return state;
        }

        private static int Int(string s) => int.TryParse(s, out int v) ? v : 0;

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        // ------------------------------------------------------------------------------------------
        // Names for the info panel
        // ------------------------------------------------------------------------------------------

        public static string Name(ShieldKind kind)
        {
            switch (kind)
            {
                case ShieldKind.Interstate: return "Interstate";
                case ShieldKind.BusinessLoop: return "Business Loop";
                case ShieldKind.BusinessSpur: return "Business Spur";
                case ShieldKind.US: return "U.S. Route";
                case ShieldKind.State: return "State Route";
                case ShieldKind.County: return "County Route";
                default: return "Forest Route";
            }
        }

        public static string Legend(CardinalKind kind)
        {
            switch (kind)
            {
                case CardinalKind.North: return "NORTH";
                case CardinalKind.South: return "SOUTH";
                case CardinalKind.East: return "EAST";
                case CardinalKind.West: return "WEST";
                default: return "";
            }
        }

        public static string Legend(AuxKind kind)
        {
            switch (kind)
            {
                case AuxKind.To: return "TO";
                case AuxKind.Jct: return "JCT";
                case AuxKind.Begin: return "BEGIN";
                case AuxKind.End: return "END";
                case AuxKind.Alt: return "ALT";
                case AuxKind.Bypass: return "BYPASS";
                case AuxKind.Business: return "BUSINESS";
                case AuxKind.Truck: return "TRUCK";
                default: return "";
            }
        }

        public static string Name(ArrowKind kind)
        {
            switch (kind)
            {
                case ArrowKind.Left: return "Left";
                case ArrowKind.Right: return "Right";
                case ArrowKind.Up: return "Ahead";
                case ArrowKind.UpLeft: return "Ahead left";
                case ArrowKind.UpRight: return "Ahead right";
                case ArrowKind.TurnLeft: return "Turn left";
                case ArrowKind.TurnRight: return "Turn right";
                case ArrowKind.LeftRight: return "Left and right";
                case ArrowKind.UpAndLeft: return "Ahead or left";
                case ArrowKind.UpAndRight: return "Ahead or right";
                default: return "None";
            }
        }

        public static string Name(GuideColor color)
        {
            switch (color)
            {
                case GuideColor.Blue: return "Blue";
                case GuideColor.Brown: return "Brown";
                default: return "Green";
            }
        }
    }

    public static class RouteSignTypes
    {
        public static readonly IReadOnlyList<RouteSignType> All = new List<RouteSignType>
        {
            new RouteSignType
            {
                Id = "RouteMarkerCurrent",
                Name = "Route Marker (Current Route)",
                Description = "Confirms the route being traveled: a cardinal direction plate over a route marker on one post (MUTCD M3 + M1).",
                Kind = RouteSignKind.Marker, Structure = RouteStructure.Post,
                Columns = 1, HasAux = false, HasArrow = false,
                Default = () => RouteSignState.Markers(RouteSignState.Column(CardinalKind.North, ShieldKind.Interstate, "25")),
                Priority = 1, Cost = 400,
            },
            new RouteSignType
            {
                Id = "RouteMarkerDirectional",
                Name = "Route Marker Assembly (Directional)",
                Description = "Points the way to a route: optional TO/JCT plate, cardinal direction, route marker and arrow on one post (MUTCD M4 + M3 + M1 + M6).",
                Kind = RouteSignKind.Marker, Structure = RouteStructure.Post,
                Columns = 1, HasAux = true, HasArrow = true,
                Default = () => RouteSignState.Markers(RouteSignState.Column(CardinalKind.East, ShieldKind.Interstate, "80", ArrowKind.TurnRight)),
                Priority = 2, Cost = 550,
            },
            new RouteSignType
            {
                Id = "RouteMarkerDouble",
                Name = "Route Marker Assembly (2 Routes)",
                Description = "Two route marker columns side by side on one post, braced by steel channels: one for each direction or each route.",
                Kind = RouteSignKind.Marker, Structure = RouteStructure.CenterPost,
                Columns = 2, HasAux = true, HasArrow = true,
                Default = () => RouteSignState.Markers(
                    RouteSignState.Column(CardinalKind.West, ShieldKind.Interstate, "80", ArrowKind.Up),
                    RouteSignState.Column(CardinalKind.East, ShieldKind.Interstate, "80", ArrowKind.TurnRight)),
                Priority = 3, Cost = 900,
            },
            new RouteSignType
            {
                Id = "RouteMarkerTriple",
                Name = "Route Marker Assembly (3 Routes)",
                Description = "Junction assembly with three route marker columns on two posts and steel channel braces.",
                Kind = RouteSignKind.Marker, Structure = RouteStructure.TwinPostBraced,
                Columns = 3, HasAux = true, HasArrow = true,
                Default = () => RouteSignState.Markers(
                    RouteSignState.Column(CardinalKind.West, ShieldKind.Interstate, "8", ArrowKind.Left, AuxKind.To),
                    RouteSignState.Column(CardinalKind.West, ShieldKind.BusinessLoop, "8", ArrowKind.Left),
                    RouteSignState.Column(CardinalKind.South, ShieldKind.State, "85", ArrowKind.Right)),
                Priority = 4, Cost = 1300,
            },
            new RouteSignType
            {
                Id = "RouteMarkerFreeway",
                Name = "Route Marker (Freeway, Large)",
                Description = "Oversized 36-inch route marker with its cardinal direction on two posts, used along freeways after interchanges.",
                Kind = RouteSignKind.Marker, Structure = RouteStructure.TwinPost,
                Columns = 1, HasAux = false, HasArrow = false, ShieldSize = 36f,
                Default = () => RouteSignState.Markers(RouteSignState.Column(CardinalKind.South, ShieldKind.Interstate, "5")),
                Priority = 5, Cost = 800,
            },
            new RouteSignType
            {
                Id = "DestinationSign1",
                Name = "Destination Sign (1 Line)",
                Description = "Green destination guide sign with one place name and arrow (MUTCD D1-1).",
                Kind = RouteSignKind.Destination, Structure = RouteStructure.GuideTwinPost,
                Lines = 1, FaceWidth = 60f, FaceHeight = 18f, LetterHeight = 6f,
                Default = () => RouteSignState.Destinations(("Downtown", ArrowKind.Right, "")),
                Priority = 6, Cost = 700,
            },
            new RouteSignType
            {
                Id = "DestinationSign2",
                Name = "Destination Sign (2 Lines)",
                Description = "Green destination guide sign listing two places with arrows, separated by a divider line (MUTCD D1-2).",
                Kind = RouteSignKind.Destination, Structure = RouteStructure.GuideTwinPost,
                Lines = 2, FaceWidth = 72f, FaceHeight = 30f, LetterHeight = 6f,
                Default = () => RouteSignState.Destinations(("Airport", ArrowKind.Left, ""), ("City Center", ArrowKind.Right, "")),
                Priority = 7, Cost = 900,
            },
            new RouteSignType
            {
                Id = "DestinationSign3",
                Name = "Destination Sign (3 Lines)",
                Description = "Green destination guide sign listing three places with arrows, separated by divider lines (MUTCD D1-3).",
                Kind = RouteSignKind.Destination, Structure = RouteStructure.GuideTwinPost,
                Lines = 3, FaceWidth = 78f, FaceHeight = 42f, LetterHeight = 6f,
                Default = () => RouteSignState.Destinations(("Stadium", ArrowKind.Left, ""), ("Downtown", ArrowKind.Up, ""), ("University", ArrowKind.Right, "")),
                Priority = 8, Cost = 1100,
            },
            new RouteSignType
            {
                Id = "DestinationSignLarge",
                Name = "Destination Sign (4 Lines, Large)",
                Description = "Large destination guide sign for high-speed roads: four places with arrows or distances on steel I-beam posts.",
                Kind = RouteSignKind.Destination, Structure = RouteStructure.GuideIBeam,
                Lines = 4, FaceWidth = 120f, FaceHeight = 72f, LetterHeight = 8f,
                Default = () => RouteSignState.Destinations(("Harbor", ArrowKind.Left, ""), ("Convention Center", ArrowKind.Up, ""), ("Airport", ArrowKind.Right, ""), ("Beach", ArrowKind.Right, "")),
                Priority = 9, Cost = 2200,
            },
            new RouteSignType
            {
                Id = "DestinationSignOverhead",
                Name = "Overhead Destination Sign (Mast Arm)",
                Description = "Destination guide sign hung over the lanes from a steel mast arm, three places with arrows.",
                Kind = RouteSignKind.Destination, Structure = RouteStructure.GuideMastArm,
                Lines = 3, FaceWidth = 120f, FaceHeight = 60f, LetterHeight = 8f,
                Default = () => RouteSignState.Destinations(("Main St", ArrowKind.Left, ""), ("Old Town", ArrowKind.Up, ""), ("Riverside", ArrowKind.Right, "")),
                Priority = 10, Cost = 6000,
            },
        };

        public static RouteSignType Find(string id)
        {
            foreach (RouteSignType type in All)
            {
                if (type.Id == id)
                    return type;
            }
            return null;
        }
    }
}
