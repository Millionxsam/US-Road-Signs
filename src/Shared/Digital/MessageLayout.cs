using System;
using System.Collections.Generic;
using System.Text;

namespace USRoadSigns.Digital
{
    public enum TextAlign
    {
        Center = 0,
        Left = 1,
        Right = 2,
    }

    /// <summary>LED on/off states of one display frame. Row 0 is the top row.</summary>
    public sealed class LedFrame
    {
        public readonly int Columns;
        public readonly int Rows;
        public readonly bool[] Lit;

        public LedFrame(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
            Lit = new bool[columns * rows];
        }

        public bool this[int x, int y]
        {
            get => Lit[y * Columns + x];
            set => Lit[y * Columns + x] = value;
        }

        public bool IsBlank
        {
            get
            {
                foreach (bool b in Lit)
                {
                    if (b)
                        return false;
                }
                return true;
            }
        }
    }

    /// <summary>
    /// A sign message: one or more pages ("phases") shown in turn, each with a fixed number of lines.
    /// Stored as a single string: lines separated by '\n', pages separated by '\f'.
    /// </summary>
    public static class SignMessage
    {
        public const char LineSeparator = '\n';
        public const char PageSeparator = '\f';
        public const int MaxPages = 4;
        public const int MaxLineLength = 32;

        public static List<string[]> Parse(string message, int lineCount)
        {
            var pages = new List<string[]>();
            string[] rawPages = (message ?? "").Split(PageSeparator);
            foreach (string rawPage in rawPages)
            {
                if (pages.Count == MaxPages)
                    break;
                string[] rawLines = rawPage.Split(LineSeparator);
                var lines = new string[lineCount];
                for (int i = 0; i < lineCount; i++)
                    lines[i] = i < rawLines.Length ? Clean(rawLines[i]) : "";
                pages.Add(lines);
            }
            return pages;
        }

        public static string Format(IReadOnlyList<string[]> pages)
        {
            var sb = new StringBuilder();
            for (int p = 0; p < pages.Count; p++)
            {
                if (p > 0)
                    sb.Append(PageSeparator);
                for (int l = 0; l < pages[p].Length; l++)
                {
                    if (l > 0)
                        sb.Append(LineSeparator);
                    sb.Append(Clean(pages[p][l]));
                }
            }
            return sb.ToString();
        }

        /// <summary>Builds a message from preset notation: lines separated by '|', pages by '||'.</summary>
        public static string FromPreset(string preset, int lineCount)
        {
            var pages = new List<string[]>();
            foreach (string page in preset.Split(new[] { "||" }, StringSplitOptions.None))
            {
                string[] lines = page.Split('|');
                var padded = new string[lineCount];
                for (int i = 0; i < lineCount; i++)
                    padded[i] = i < lines.Length ? lines[i] : "";
                pages.Add(padded);
            }
            return Format(pages);
        }

        /// <summary>
        /// Upper case, no control characters, trimmed to <see cref="MaxLineLength"/>. Characters the sign font does not
        /// have become '?', so stored messages stay plain ASCII.
        /// </summary>
        public static string Clean(string line)
        {
            if (string.IsNullOrEmpty(line))
                return "";
            var sb = new StringBuilder(Math.Min(line.Length, MaxLineLength));
            foreach (char c in line)
            {
                if (sb.Length == MaxLineLength)
                    break;
                if (c == LineSeparator || c == PageSeparator || char.IsControl(c))
                    continue;
                sb.Append(LedFont.IsSupported(c) ? char.ToUpperInvariant(c) : '?');
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>Lays out message pages on a full-matrix display.</summary>
    public static class MessageLayout
    {
        /// <summary>Renders one page. <paramref name="overflow"/>[i] is true when line i did not fit and was clipped.</summary>
        public static LedFrame RenderPage(string[] lines, int columns, int rows, TextAlign align, out bool[] overflow)
        {
            var frame = new LedFrame(columns, rows);
            overflow = new bool[lines.Length];
            int count = lines.Length;
            int free = Math.Max(0, rows - count * LedFont.Height);

            // Even vertical spacing: margins and gaps share the free rows. The used lines are then centered as a
            // block, so a two-line message on a three-line sign sits in the middle like on real signs.
            int Top(int line) => (int)Math.Round((line + 1) * free / (double)(count + 1)) + line * LedFont.Height;
            int first = Array.FindIndex(lines, l => !string.IsNullOrEmpty(l));
            int last = Array.FindLastIndex(lines, l => !string.IsNullOrEmpty(l));
            if (first < 0)
                return frame;
            int blockTop = Top(first);
            int blockHeight = Top(last) + LedFont.Height - blockTop;
            int shift = (rows - blockHeight) / 2 - blockTop;

            for (int i = 0; i < count; i++)
            {
                string text = lines[i] ?? "";
                if (text.Length == 0)
                    continue;

                int top = Top(i) + shift;

                // Tighten letter spacing before clipping, as sign controllers do.
                int spacing = LedFont.CharSpacing;
                int width = LedFont.MeasureLine(text, spacing);
                if (width > columns)
                {
                    spacing = 0;
                    width = LedFont.MeasureLine(text, spacing);
                }
                overflow[i] = width > columns;

                int x;
                switch (align)
                {
                    case TextAlign.Left:
                        x = Math.Min(1, Math.Max(0, columns - width));
                        break;
                    case TextAlign.Right:
                        x = columns - width - Math.Min(1, Math.Max(0, columns - width));
                        break;
                    default:
                        x = (columns - width) / 2;
                        break;
                }
                if (overflow[i])
                    x = 0;

                foreach (char c in text)
                {
                    LedFont.Glyph glyph = LedFont.Get(c);
                    for (int gy = 0; gy < LedFont.Height; gy++)
                    {
                        int y = top + gy;
                        if (y < 0 || y >= rows)
                            continue;
                        for (int gx = 0; gx < glyph.Width; gx++)
                        {
                            int px = x + gx;
                            if (px >= 0 && px < columns && glyph[gx, gy])
                                frame[px, y] = true;
                        }
                    }
                    x += glyph.Width + spacing;
                    if (x >= columns)
                        break;
                }
            }
            return frame;
        }

        public static bool Fits(string[] lines, int columns)
        {
            foreach (string line in lines)
            {
                if (LedFont.MeasureLine(line) > columns)
                    return false;
            }
            return true;
        }
    }

    public enum ArrowMode
    {
        Off = 0,
        RightArrow = 1,
        LeftArrow = 2,
        DoubleArrow = 3,
        RightSequential = 4,
        LeftSequential = 5,
        RightChevrons = 6,
        LeftChevrons = 7,
        CautionBar = 8,
        FourCorners = 9,
    }

    /// <summary>One frame of an animation: a display state shown for a number of seconds.</summary>
    public struct TimedFrame
    {
        public int Index;        // into the frame list, -1 = all lamps dark
        public float Seconds;

        public TimedFrame(int index, float seconds)
        {
            Index = index;
            Seconds = seconds;
        }
    }

    /// <summary>
    /// 25-lamp style flashing arrow board (MUTCD 6F.61, Type C). Lamps sit on a 15 x 5 grid; only grid points used
    /// by at least one display mode carry a lamp.
    /// </summary>
    public static class ArrowBoard
    {
        public const int Columns = 15;
        public const int Rows = 5;
        public const int ModeCount = 10;

        // Flash timing: MUTCD 6F.61 asks for 25 to 40 flashes per minute with at least 50% on time.
        public const float FlashOn = 0.85f;
        public const float FlashOff = 0.65f;

        private const string Shaft = ".......|.......|#.#.#.#.#.#.#.#|.......|.......";

        private static readonly string[] s_Right =
        {
            "..........#....",
            "............#..",
            "#.#.#.#.#.#.#.#",
            "............#..",
            "..........#....",
        };

        private static readonly string[] s_Chevrons =
        {
            "#....#....#....",
            "..#....#....#..",
            "....#....#....#",
            "..#....#....#..",
            "#....#....#....",
        };

        private static readonly string[] s_Corners =
        {
            "#.............#",
            "...............",
            "...............",
            "...............",
            "#.............#",
        };

        public static string ModeName(ArrowMode mode)
        {
            switch (mode)
            {
                case ArrowMode.Off: return "Off";
                case ArrowMode.RightArrow: return "Right arrow";
                case ArrowMode.LeftArrow: return "Left arrow";
                case ArrowMode.DoubleArrow: return "Double arrow";
                case ArrowMode.RightSequential: return "Sequential right";
                case ArrowMode.LeftSequential: return "Sequential left";
                case ArrowMode.RightChevrons: return "Chevrons right";
                case ArrowMode.LeftChevrons: return "Chevrons left";
                case ArrowMode.CautionBar: return "Caution bar";
                case ArrowMode.FourCorners: return "Four corners";
                default: return mode.ToString();
            }
        }

        /// <summary>Grid points that carry a lamp (row 0 = top).</summary>
        public static LedFrame LampLayout()
        {
            var layout = new LedFrame(Columns, Rows);
            for (int m = 0; m < ModeCount; m++)
            {
                foreach (LedFrame frame in Frames((ArrowMode)m, out _))
                {
                    for (int i = 0; i < frame.Lit.Length; i++)
                        layout.Lit[i] |= frame.Lit[i];
                }
            }
            return layout;
        }

        /// <summary>Distinct lamp states of a mode and the timed sequence that plays them.</summary>
        public static List<LedFrame> Frames(ArrowMode mode, out List<TimedFrame> sequence)
        {
            var frames = new List<LedFrame>();
            sequence = new List<TimedFrame>();
            LedFrame right = Parse(s_Right, false);
            LedFrame left = Parse(s_Right, true);
            switch (mode)
            {
                case ArrowMode.RightArrow:
                    Flashing(frames, sequence, right);
                    break;
                case ArrowMode.LeftArrow:
                    Flashing(frames, sequence, left);
                    break;
                case ArrowMode.DoubleArrow:
                    Flashing(frames, sequence, Union(right, left));
                    break;
                case ArrowMode.RightSequential:
                case ArrowMode.LeftSequential:
                {
                    bool mirror = mode == ArrowMode.LeftSequential;
                    LedFrame shaft = Parse(Shaft.Split('|'), mirror, padTo: Columns);
                    frames.Add(Window(shaft, 0, 4, mirror));
                    frames.Add(Window(shaft, 0, 9, mirror));
                    frames.Add(mirror ? left : right);
                    sequence.Add(new TimedFrame(0, 0.35f));
                    sequence.Add(new TimedFrame(1, 0.35f));
                    sequence.Add(new TimedFrame(2, 0.8f));
                    sequence.Add(new TimedFrame(-1, 0.5f));
                    break;
                }
                case ArrowMode.RightChevrons:
                case ArrowMode.LeftChevrons:
                {
                    bool mirror = mode == ArrowMode.LeftChevrons;
                    LedFrame chevrons = Parse(s_Chevrons, mirror);
                    frames.Add(Window(chevrons, 0, 4, mirror));
                    frames.Add(Window(chevrons, 0, 9, mirror));
                    frames.Add(chevrons);
                    sequence.Add(new TimedFrame(0, 0.35f));
                    sequence.Add(new TimedFrame(1, 0.35f));
                    sequence.Add(new TimedFrame(2, 0.8f));
                    sequence.Add(new TimedFrame(-1, 0.5f));
                    break;
                }
                case ArrowMode.CautionBar:
                    Flashing(frames, sequence, Parse(Shaft.Split('|'), false, padTo: Columns));
                    break;
                case ArrowMode.FourCorners:
                    Flashing(frames, sequence, Parse(s_Corners, false));
                    break;
                default:
                    sequence.Add(new TimedFrame(-1, 1f));
                    break;
            }
            return frames;
        }

        private static void Flashing(List<LedFrame> frames, List<TimedFrame> sequence, LedFrame on)
        {
            frames.Add(on);
            sequence.Add(new TimedFrame(0, FlashOn));
            sequence.Add(new TimedFrame(-1, FlashOff));
        }

        private static LedFrame Parse(string[] rows, bool mirror, int padTo = 0)
        {
            var frame = new LedFrame(Columns, Rows);
            for (int y = 0; y < Rows; y++)
            {
                string row = padTo > 0 ? rows[y].PadRight(padTo, '.') : rows[y];
                for (int x = 0; x < Columns; x++)
                {
                    if (row[x] == '#')
                        frame[mirror ? Columns - 1 - x : x, y] = true;
                }
            }
            return frame;
        }

        /// <summary>Keeps lamps in columns [from, to], counted from the trailing end when <paramref name="mirror"/>.</summary>
        private static LedFrame Window(LedFrame source, int from, int to, bool mirror)
        {
            var frame = new LedFrame(Columns, Rows);
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    int c = mirror ? Columns - 1 - x : x;
                    if (c >= from && c <= to)
                        frame[x, y] = source[x, y];
                }
            }
            return frame;
        }

        private static LedFrame Union(LedFrame a, LedFrame b)
        {
            var frame = new LedFrame(Columns, Rows);
            for (int i = 0; i < frame.Lit.Length; i++)
                frame.Lit[i] = a.Lit[i] || b.Lit[i];
            return frame;
        }
    }
}
