using System;
using System.Collections.Generic;

namespace USRoadSigns.Digital
{
    /// <summary>What a sign shows: its saved settings, or its type's defaults while it was never edited.</summary>
    internal struct DigitalSignState
    {
        public const int DefaultPageMs = 2500;
        public const int MinPageMs = 500;
        public const int MaxPageMs = 10000;

        public string Message;
        public LedColor Color;
        public TextAlign Align;
        public ArrowMode Arrow;
        public bool Flash;
        public bool Beacons;
        public int PageMs;

        private static readonly Dictionary<string, string> s_DefaultMessages = new Dictionary<string, string>(StringComparer.Ordinal);

        public static DigitalSignState Defaults(DigitalSignType type)
        {
            if (!s_DefaultMessages.TryGetValue(type.Id, out string message))
            {
                message = type.Display == DisplayKind.Text ? SignMessage.FromPreset(type.DefaultMessage, type.Lines) : "";
                s_DefaultMessages[type.Id] = message;
            }
            return new DigitalSignState
            {
                Message = message,
                Color = LedColor.Amber,
                Align = TextAlign.Center,
                Arrow = type.DefaultArrow,
                Flash = false,
                Beacons = false,
                PageMs = DefaultPageMs,
            };
        }

        public static DigitalSignState Resolve(DigitalSignType type, DigitalSign sign)
        {
            if ((sign.m_Flags & DigitalSignFlags.Customized) == 0)
                return Defaults(type);
            return new DigitalSignState
            {
                Message = sign.m_Message.ToString(),
                Color = (LedColor)Math.Min((int)sign.m_Color, LedColors.Count - 1),
                Align = (TextAlign)Math.Min((int)sign.m_Align, 2),
                Arrow = (ArrowMode)Math.Min((int)sign.m_Arrow, ArrowBoard.ModeCount - 1),
                Flash = (sign.m_Flags & DigitalSignFlags.Flash) != 0,
                Beacons = (sign.m_Flags & DigitalSignFlags.Beacons) != 0,
                PageMs = sign.m_PageMs == 0 ? DefaultPageMs : Math.Max(MinPageMs, Math.Min(MaxPageMs, (int)sign.m_PageMs)),
            };
        }

        public DigitalSign ToComponent()
        {
            var sign = new DigitalSign
            {
                m_Flags = DigitalSignFlags.Customized
                    | (Flash ? DigitalSignFlags.Flash : DigitalSignFlags.None)
                    | (Beacons ? DigitalSignFlags.Beacons : DigitalSignFlags.None),
                m_Color = (byte)Color,
                m_Align = (byte)Align,
                m_Arrow = (byte)Arrow,
                m_PageMs = (ushort)Math.Max(MinPageMs, Math.Min(MaxPageMs, PageMs)),
            };
            sign.SetMessage(Message);
            return sign;
        }
    }
}
