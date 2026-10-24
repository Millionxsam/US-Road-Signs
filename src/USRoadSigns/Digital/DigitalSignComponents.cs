using System;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;

namespace USRoadSigns.Digital
{
    [Flags]
    public enum DigitalSignFlags : byte
    {
        None = 0,
        /// <summary>The player changed the sign; otherwise the prefab defaults are shown.</summary>
        Customized = 1,
        /// <summary>Message blinks on and off.</summary>
        Flash = 2,
        /// <summary>Warning beacons flash (signs that have beacons).</summary>
        Beacons = 4,
    }

    /// <summary>
    /// Per-sign settings, on every placed digital sign (added through the prefab archetype) and saved with the city.
    /// A sign that was never edited keeps <see cref="DigitalSignFlags.Customized"/> clear and shows its type's defaults.
    /// </summary>
    public struct DigitalSign : IComponentData, IQueryTypeParameter, ISerializable
    {
        private const int kVersion = 1;

        /// <summary>Lines separated by '\n', pages by '\f' (see <see cref="SignMessage"/>).</summary>
        public FixedString512Bytes m_Message;
        public DigitalSignFlags m_Flags;
        public byte m_Color;
        public byte m_Align;
        public byte m_Arrow;
        /// <summary>Time each page is shown, milliseconds.</summary>
        public ushort m_PageMs;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(kVersion);
            writer.Write((int)m_Flags);
            writer.Write((int)m_Color);
            writer.Write((int)m_Align);
            writer.Write((int)m_Arrow);
            writer.Write((int)m_PageMs);
            writer.Write(m_Message.ToString());
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out int flags);
            reader.Read(out int color);
            reader.Read(out int align);
            reader.Read(out int arrow);
            reader.Read(out int pageMs);
            reader.Read(out string message);
            m_Flags = (DigitalSignFlags)flags;
            m_Color = (byte)color;
            m_Align = (byte)align;
            m_Arrow = (byte)arrow;
            m_PageMs = (ushort)pageMs;
            SetMessage(message);
        }

        /// <summary>Largest message stored (FixedString512Bytes holds 509 UTF-8 bytes).</summary>
        public const int MaxMessageBytes = 500;

        public void SetMessage(string message)
        {
            message = message ?? "";
            // Messages are ASCII (SignMessage.Clean), so characters are bytes.
            m_Message = message.Length > MaxMessageBytes ? message.Substring(0, MaxMessageBytes) : message;
        }
    }

    /// <summary>On digital sign prefabs: index into <see cref="DigitalSignTypes.All"/>.</summary>
    public struct DigitalSignData : IComponentData, IQueryTypeParameter
    {
        public int m_TypeIndex;
    }
}
