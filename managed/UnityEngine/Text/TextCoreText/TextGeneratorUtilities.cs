using System;
using UnityEngine.TextCore.LowLevel;

namespace UnityEngine.TextCore.Text
{
    internal static partial class TextGeneratorUtilities
    {
        public static readonly Vector2 largePositiveVector2 = new Vector2(2147483647f, 2147483647f);
        public static readonly Vector2 largeNegativeVector2 = new Vector2(-214748370f, -214748370f);
        public const float largePositiveFloat = 32767f;
        public const float largeNegativeFloat = -32767f;

        public static bool Approximately(float a, float b) => b - 0.0001f < a && a < b + 0.0001f;

        /// <summary>A color tag's value: #RGB, #RGBA, #RRGGBB or #RRGGBBAA (<paramref name="tagCount"/>
        /// characters from the '#'); white otherwise.</summary>
        public static Color32 HexCharsToColor(char[] hexChars, int startIndex, int tagCount)
        {
            byte Single(int i) => (byte)(HexToInt(hexChars[startIndex + i]) * 16 + HexToInt(hexChars[startIndex + i]));
            byte Pair(int i) => (byte)(HexToInt(hexChars[startIndex + i]) * 16 + HexToInt(hexChars[startIndex + i + 1]));
            return tagCount switch
            {
                4 => new Color32(Single(1), Single(2), Single(3), 255),
                5 => new Color32(Single(1), Single(2), Single(3), Single(4)),
                7 => new Color32(Pair(1), Pair(3), Pair(5), 255),
                9 => new Color32(Pair(1), Pair(3), Pair(5), Pair(7)),
                _ => new Color32(255, 255, 255, 255),
            };
        }

        /// <summary>A hex digit's value; anything else counts as 15.</summary>
        public static uint HexToInt(char hex)
        {
            if (hex >= '0' && hex <= '9') return (uint)(hex - '0');
            if (hex >= 'a' && hex <= 'f') return (uint)(hex - 'a' + 10);
            if (hex >= 'A' && hex <= 'F') return (uint)(hex - 'A' + 10);
            return 15u;
        }

        public static float ConvertToFloat(char[] chars, int startIndex, int length) => ConvertToFloat(chars, startIndex, length, out _);

        /// <summary>A tag's numeric value, accumulated digit by digit in single precision as Unity
        /// does (so 1.1 is 1 + 0.1f); stops at a comma. -32767 when there is no value.</summary>
        public static float ConvertToFloat(char[] chars, int startIndex, int length, out int lastIndex)
        {
            if (startIndex == 0)
            {
                lastIndex = 0;
                return -32767f;
            }
            int end = startIndex + length;
            int sign = 1;
            if (chars[startIndex] == '+') startIndex++;
            else if (chars[startIndex] == '-')
            {
                sign = -1;
                startIndex++;
            }
            bool integral = true;
            float fraction = 0f;
            float value = 0f;
            for (int i = startIndex; i < end; i++)
            {
                uint c = chars[i];
                if ((c >= '0' && c <= '9') || c == '.')
                {
                    if (c == '.')
                    {
                        integral = false;
                        fraction = 0.1f;
                    }
                    else if (integral)
                    {
                        value = (float)((double)value * 10.0 + (float)((c - '0') * sign));
                    }
                    else
                    {
                        value = (float)((double)value + (double)(float)(c - '0') * fraction * (float)sign);
                        fraction *= 0.1f;
                    }
                }
                else if (c == ',')
                {
                    lastIndex = i + 1 < end && chars[i + 1] == ' ' ? i + 1 : i;
                    return value;
                }
            }
            lastIndex = end;
            return value;
        }

        public static void ResizeInternalArray<T>(ref T[] array) => Array.Resize(ref array, Mathf.NextPowerOfTwo(array.Length + 1));

        public static void ResizeInternalArray<T>(ref T[] array, int size) => Array.Resize(ref array, Mathf.NextPowerOfTwo(size + 1));

        public static TextStyle GetStyle(TextGenerationSettings generationSetting, int hashCode)
        {
            var sheet = generationSetting.textSettings != null ? generationSetting.textSettings.defaultStyleSheet : null;
            return sheet != null ? sheet.GetStyle(hashCode) : null;
        }

        public static uint GetUTF16(uint[] text, int i) =>
            (HexToInt((char)text[i]) << 12) + (HexToInt((char)text[i + 1]) << 8) + (HexToInt((char)text[i + 2]) << 4) + HexToInt((char)text[i + 3]);

        public static uint GetUTF32(uint[] text, int i)
        {
            uint value = 0;
            for (int k = 0; k < 8; k++) value += HexToInt((char)text[i + k]) << (28 - 4 * k);
            return value;
        }

        /// <summary>Copies an element's quad into its material's vertex buffer, flipping y into the
        /// screen rect (y down from its top).</summary>
        public static void FillCharacterVertexBuffers(int i, bool convertToLinearSpace, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            ref var element = ref textInfo.textElementInfo[i];
            ref var mesh = ref textInfo.meshInfo[element.materialReferenceIndex];
            int start = mesh.vertexCount;
            if (start >= mesh.vertexBufferSize) mesh.ResizeMeshInfo(Mathf.NextPowerOfTwo((start + 4) / 4), generationSettings.isIMGUI);
            if (mesh.vertexData.Length < start + 4) return;
            element.vertexIndex = start;
            float height = generationSettings.screenRect.height;
            WriteVertex(ref mesh.vertexData[start], element.vertexBottomLeft, height, convertToLinearSpace);
            WriteVertex(ref mesh.vertexData[start + 1], element.vertexTopLeft, height, convertToLinearSpace);
            WriteVertex(ref mesh.vertexData[start + 2], element.vertexTopRight, height, convertToLinearSpace);
            WriteVertex(ref mesh.vertexData[start + 3], element.vertexBottomRight, height, convertToLinearSpace);
            mesh.vertexCount = start + 4;
        }

        static void WriteVertex(ref TextCoreVertex target, in TextVertex source, float height, bool linear)
        {
            var position = source.position;
            position.y *= -1f;
            target.position = position + new Vector3(0f, height, 0f);
            target.uv0 = source.uv;
            target.uv2 = source.uv2;
            target.color = linear ? GammaToLinear(source.color) : source.color;
        }

        public static void AdjustLineOffset(int startIndex, int endIndex, float offset, TextInfo textInfo)
        {
            var shift = new Vector3(0f, offset, 0f);
            for (int i = startIndex; i <= endIndex; i++)
            {
                ref var element = ref textInfo.textElementInfo[i];
                element.bottomLeft -= shift;
                element.topLeft -= shift;
                element.topRight -= shift;
                element.bottomRight -= shift;
                element.ascender -= shift.y;
                element.baseLine -= shift.y;
                element.descender -= shift.y;
                if (!element.isVisible) continue;
                element.vertexBottomLeft.position -= shift;
                element.vertexTopLeft.position -= shift;
                element.vertexTopRight.position -= shift;
                element.vertexBottomRight.position -= shift;
            }
        }

        public static void ResizeLineExtents(int size, TextInfo textInfo)
        {
            size = size > 1024 ? size + 256 : Mathf.NextPowerOfTwo(size + 1);
            var lines = new LineInfo[size];
            for (int i = 0; i < size; i++)
            {
                if (i < textInfo.lineInfo.Length)
                {
                    lines[i] = textInfo.lineInfo[i];
                    continue;
                }
                lines[i].lineExtents.min = largePositiveVector2;
                lines[i].lineExtents.max = largeNegativeVector2;
                lines[i].ascender = -32767f;
                lines[i].descender = 32767f;
            }
            textInfo.lineInfo = lines;
        }

        public static FontStyles LegacyStyleToNewStyle(FontStyle fontStyle) => fontStyle switch
        {
            FontStyle.Bold => FontStyles.Bold,
            FontStyle.Italic => FontStyles.Italic,
            FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
            _ => FontStyles.Normal,
        };

        public static TextAlignment LegacyAlignmentToNewAlignment(TextAnchor anchor) => anchor switch
        {
            TextAnchor.UpperLeft => TextAlignment.TopLeft,
            TextAnchor.UpperCenter => TextAlignment.TopCenter,
            TextAnchor.UpperRight => TextAlignment.TopRight,
            TextAnchor.MiddleLeft => TextAlignment.MiddleLeft,
            TextAnchor.MiddleCenter => TextAlignment.MiddleCenter,
            TextAnchor.MiddleRight => TextAlignment.MiddleRight,
            TextAnchor.LowerLeft => TextAlignment.BottomLeft,
            TextAnchor.LowerCenter => TextAlignment.BottomCenter,
            TextAnchor.LowerRight => TextAlignment.BottomRight,
            _ => TextAlignment.TopLeft,
        };

        internal static UnityEngine.TextCore.HorizontalAlignment GetHorizontalAlignment(TextAnchor anchor) => anchor switch
        {
            TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter => UnityEngine.TextCore.HorizontalAlignment.Center,
            TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight => UnityEngine.TextCore.HorizontalAlignment.Right,
            _ => UnityEngine.TextCore.HorizontalAlignment.Left,
        };

        internal static UnityEngine.TextCore.VerticalAlignment GetVerticalAlignment(TextAnchor anchor) => anchor switch
        {
            TextAnchor.LowerLeft or TextAnchor.LowerCenter or TextAnchor.LowerRight => UnityEngine.TextCore.VerticalAlignment.Bottom,
            TextAnchor.MiddleLeft or TextAnchor.MiddleCenter or TextAnchor.MiddleRight => UnityEngine.TextCore.VerticalAlignment.Middle,
            _ => UnityEngine.TextCore.VerticalAlignment.Top,
        };

        public static uint ConvertToUTF32(uint highSurrogate, uint lowSurrogate) => (highSurrogate - 0xD800) * 1024 + (lowSurrogate - 0xDC00 + 0x10000);

        /// <summary>The hash of a tag name read from <paramref name="readIndex"/> up to '&gt;', '=' or
        /// a space (16 characters at most).</summary>
        public static int GetMarkupTagHashCode(uint[] styleDefinition, int readIndex)
        {
            int hash = 0;
            int end = Math.Min(readIndex + 16, styleDefinition.Length);
            for (; readIndex < end; readIndex++)
            {
                uint c = styleDefinition[readIndex];
                if (c == '>' || c == '=' || c == ' ') return hash;
                hash = ((hash << 5) + hash) ^ (int)ToUpperASCIIFast(c);
            }
            return hash;
        }

        public static char ToUpperASCIIFast(char c) => TextUtilities.ToUpperFast(c);

        public static uint ToUpperASCIIFast(uint c) => TextUtilities.ToUpperASCIIFast(c);

        public static char ToUpperFast(char c) => TextUtilities.ToUpperFast(c);

        /// <summary>Comma separated numbers of an attribute value.</summary>
        public static int GetAttributeParameters(char[] chars, int startIndex, int length, ref float[] parameters)
        {
            int last = startIndex;
            int count = 0;
            while (last < startIndex + length)
            {
                parameters[count] = ConvertToFloat(chars, startIndex, length, out last);
                length -= last - startIndex + 1;
                startIndex = last + 1;
                count++;
            }
            return count;
        }

        public static bool IsBitmapRendering(GlyphRenderMode glyphRenderMode) =>
            glyphRenderMode == GlyphRenderMode.RASTER || glyphRenderMode == GlyphRenderMode.RASTER_HINTED ||
            glyphRenderMode == GlyphRenderMode.SMOOTH || glyphRenderMode == GlyphRenderMode.SMOOTH_HINTED;

        /// <summary>Whether a character is not a combining mark (one that positions on the base
        /// glyph before it).</summary>
        public static bool IsBaseGlyph(uint c) =>
            !(In(c, 0x0300, 0x036F) || In(c, 0x1AB0, 0x1AFF) || In(c, 0x1DC0, 0x1DFF) || In(c, 0x20D0, 0x20FF) || In(c, 0xFE20, 0xFE2F) ||
              c == 0x0E31 || In(c, 0x0E34, 0x0E3A) || In(c, 0x0E47, 0x0E4E) || In(c, 0x0591, 0x05BD) || c == 0x05BF || In(c, 0x05C1, 0x05C2) ||
              In(c, 0x05C4, 0x05C5) || c == 0x05C7 || In(c, 0x0610, 0x061A) || In(c, 0x064B, 0x065F) || c == 0x0670 || In(c, 0x06D6, 0x06DC) ||
              In(c, 0x06DF, 0x06E4) || In(c, 0x06E7, 0x06E8) || In(c, 0x06EA, 0x06ED) || In(c, 0x08D3, 0x08E1) || In(c, 0x08E3, 0x08FF) ||
              In(c, 0xFBB2, 0xFBC1));

        static bool In(uint c, uint first, uint last) => c >= first && c <= last;

        public static Color MinAlpha(this Color c1, Color c2) => new Color(c1.r, c1.g, c1.b, c1.a < c2.a ? c1.a : c2.a);

        internal static Color32 GammaToLinear(Color32 c) => new Color32(GammaToLinear(c.r), GammaToLinear(c.g), GammaToLinear(c.b), c.a);

        static byte GammaToLinear(byte value)
        {
            float v = value / 255f;
            if (v <= 0.04045f) return (byte)(v / 12.92f * 255f);
            if (v < 1f) return (byte)(Mathf.Pow((v + 0.055f) / 1.055f, 2.4f) * 255f);
            if (v == 1f) return 255;
            return (byte)(Mathf.Pow(v, 2.2f) * 255f);
        }

        internal static bool IsHangul(uint c) =>
            In(c, 0x1100, 0x11FF) || In(c, 0xA960, 0xA97F) || In(c, 0xD7B0, 0xD7FF) || In(c, 0x3130, 0x318F) || In(c, 0xFFA0, 0xFFDC) || In(c, 0xAC00, 0xD7AF);

        internal static bool IsCJK(uint c) =>
            In(c, 0x3000, 0x303F) || In(c, 0x3100, 0x312F) || In(c, 0x31A0, 0x31BF) || In(c, 0x4E00, 0x9FFF) || In(c, 0x3400, 0x4DBF) ||
            In(c, 0x20000, 0x2A6DF) || In(c, 0x2A700, 0x2B73F) || In(c, 0x2B740, 0x2B81F) || In(c, 0x2B820, 0x2CEAF) || In(c, 0x2CEB0, 0x2EBE0) ||
            In(c, 0x30000, 0x3134A) || In(c, 0xF900, 0xFAFF) || In(c, 0x2F800, 0x2FA1F) || In(c, 0x2F00, 0x2FDF) || In(c, 0x2E80, 0x2EFF) ||
            In(c, 0x31C0, 0x31EF) || In(c, 0x2FF0, 0x2FFF) || In(c, 0x3040, 0x309F) || In(c, 0x1B100, 0x1B12F) || In(c, 0x1AFF0, 0x1AFFF) ||
            In(c, 0x1B000, 0x1B0FF) || In(c, 0x1B130, 0x1B16F) || In(c, 0x3190, 0x319F) || In(c, 0x30A0, 0x30FF) || In(c, 0x31F0, 0x31FF) ||
            In(c, 0xFF65, 0xFF9F);
    }
}
