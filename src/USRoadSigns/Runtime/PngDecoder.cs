using System;
using System.IO;
using System.IO.Compression;

namespace USRoadSigns.Runtime
{
    /// <summary>
    /// Minimal PNG decoder (8-bit grayscale, gray+alpha, RGB, RGBA and palette images, non-interlaced).
    /// Decoding in managed code keeps the row order under our control (Unity convention: first row = bottom
    /// of the image = UV v 0) and lets textures decode on worker threads.
    /// </summary>
    internal static class PngDecoder
    {
        private static readonly byte[] s_Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>Returns RGBA8 pixels, bottom row first.</summary>
        public static byte[] DecodeRgbaBottomUp(byte[] file, out int width, out int height)
        {
            for (int i = 0; i < s_Signature.Length; i++)
            {
                if (file.Length < 8 || file[i] != s_Signature[i])
                    throw new InvalidDataException("Not a PNG file");
            }

            width = 0;
            height = 0;
            int bitDepth = 0, colorType = 0, interlace = 0;
            byte[] palette = null;
            byte[] paletteAlpha = null;
            var idat = new MemoryStream();

            int pos = 8;
            while (pos + 12 <= file.Length)
            {
                int length = ReadInt32BE(file, pos);
                string type = System.Text.Encoding.ASCII.GetString(file, pos + 4, 4);
                int dataStart = pos + 8;
                if (length < 0 || dataStart + length > file.Length)
                    throw new InvalidDataException("Corrupt PNG chunk");

                switch (type)
                {
                    case "IHDR":
                        width = ReadInt32BE(file, dataStart);
                        height = ReadInt32BE(file, dataStart + 4);
                        bitDepth = file[dataStart + 8];
                        colorType = file[dataStart + 9];
                        interlace = file[dataStart + 12];
                        break;
                    case "PLTE":
                        palette = new byte[length];
                        Buffer.BlockCopy(file, dataStart, palette, 0, length);
                        break;
                    case "tRNS":
                        paletteAlpha = new byte[length];
                        Buffer.BlockCopy(file, dataStart, paletteAlpha, 0, length);
                        break;
                    case "IDAT":
                        idat.Write(file, dataStart, length);
                        break;
                }

                pos = dataStart + length + 4; // skip CRC
                if (type == "IEND")
                    break;
            }

            if (width <= 0 || height <= 0)
                throw new InvalidDataException("PNG without IHDR");
            if (bitDepth != 8)
                throw new NotSupportedException("Only 8-bit PNG images are supported (got " + bitDepth + "-bit)");
            if (interlace != 0)
                throw new NotSupportedException("Interlaced PNG images are not supported");

            int channels;
            switch (colorType)
            {
                case 0: channels = 1; break;   // gray
                case 2: channels = 3; break;   // RGB
                case 3: channels = 1; break;   // palette
                case 4: channels = 2; break;   // gray + alpha
                case 6: channels = 4; break;   // RGBA
                default: throw new NotSupportedException("Unsupported PNG color type " + colorType);
            }

            int stride = width * channels;
            byte[] raw = new byte[(stride + 1) * height];
            idat.Position = 2; // skip the 2-byte zlib header
            using (var inflate = new DeflateStream(idat, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length)
                {
                    int n = inflate.Read(raw, read, raw.Length - read);
                    if (n <= 0)
                        throw new InvalidDataException("Truncated PNG image data");
                    read += n;
                }
            }

            byte[] rgba = new byte[width * height * 4];
            byte[] previous = new byte[stride];
            byte[] current = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * (stride + 1);
                byte filter = raw[rowStart];
                Buffer.BlockCopy(raw, rowStart + 1, current, 0, stride);
                Unfilter(filter, current, previous, channels);

                int destRow = height - 1 - y; // bottom-up
                int d = destRow * width * 4;
                for (int x = 0; x < width; x++, d += 4)
                {
                    int s = x * channels;
                    switch (colorType)
                    {
                        case 0:
                            rgba[d] = rgba[d + 1] = rgba[d + 2] = current[s];
                            rgba[d + 3] = 255;
                            break;
                        case 2:
                            rgba[d] = current[s];
                            rgba[d + 1] = current[s + 1];
                            rgba[d + 2] = current[s + 2];
                            rgba[d + 3] = 255;
                            break;
                        case 3:
                        {
                            int index = current[s];
                            int p = index * 3;
                            rgba[d] = palette != null && p + 2 < palette.Length ? palette[p] : (byte)0;
                            rgba[d + 1] = palette != null && p + 2 < palette.Length ? palette[p + 1] : (byte)0;
                            rgba[d + 2] = palette != null && p + 2 < palette.Length ? palette[p + 2] : (byte)0;
                            rgba[d + 3] = paletteAlpha != null && index < paletteAlpha.Length ? paletteAlpha[index] : (byte)255;
                            break;
                        }
                        case 4:
                            rgba[d] = rgba[d + 1] = rgba[d + 2] = current[s];
                            rgba[d + 3] = current[s + 1];
                            break;
                        default:
                            rgba[d] = current[s];
                            rgba[d + 1] = current[s + 1];
                            rgba[d + 2] = current[s + 2];
                            rgba[d + 3] = current[s + 3];
                            break;
                    }
                }

                byte[] swap = previous;
                previous = current;
                current = swap;
            }
            return rgba;
        }

        private static void Unfilter(byte filter, byte[] row, byte[] prior, int bpp)
        {
            int length = row.Length;
            switch (filter)
            {
                case 0:
                    return;
                case 1: // Sub
                    for (int i = bpp; i < length; i++)
                        row[i] = (byte)(row[i] + row[i - bpp]);
                    return;
                case 2: // Up
                    for (int i = 0; i < length; i++)
                        row[i] = (byte)(row[i] + prior[i]);
                    return;
                case 3: // Average
                    for (int i = 0; i < length; i++)
                    {
                        int left = i >= bpp ? row[i - bpp] : 0;
                        row[i] = (byte)(row[i] + ((left + prior[i]) >> 1));
                    }
                    return;
                case 4: // Paeth
                    for (int i = 0; i < length; i++)
                    {
                        int a = i >= bpp ? row[i - bpp] : 0;
                        int b = prior[i];
                        int c = i >= bpp ? prior[i - bpp] : 0;
                        int p = a + b - c;
                        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        int predictor = pa <= pb && pa <= pc ? a : (pb <= pc ? b : c);
                        row[i] = (byte)(row[i] + predictor);
                    }
                    return;
                default:
                    throw new InvalidDataException("Invalid PNG filter type " + filter);
            }
        }

        private static int ReadInt32BE(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }
    }
}
