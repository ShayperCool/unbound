using System;
using System.Collections.Generic;
using System.Globalization;

namespace UnityEngine
{
    /// <summary>HTML colour strings, as Unity reads and writes them.</summary>
    public partial class ColorUtility
    {
        /// <summary>The names Unity reads without a '#' (its rich text colour names).</summary>
        static readonly Dictionary<string, uint> s_Named = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["red"] = 0xFF0000FF, ["cyan"] = 0x00FFFFFF, ["blue"] = 0x0000FFFF, ["darkblue"] = 0x0000A0FF,
            ["lightblue"] = 0xADD8E6FF, ["purple"] = 0x800080FF, ["yellow"] = 0xFFFF00FF, ["lime"] = 0x00FF00FF,
            ["fuchsia"] = 0xFF00FFFF, ["white"] = 0xFFFFFFFF, ["silver"] = 0xC0C0C0FF, ["grey"] = 0x808080FF,
            ["black"] = 0x000000FF, ["orange"] = 0xFFA500FF, ["brown"] = 0xA52A2AFF, ["maroon"] = 0x800000FF,
            ["green"] = 0x008000FF, ["olive"] = 0x808000FF, ["navy"] = 0x000080FF, ["teal"] = 0x008080FF,
            ["aqua"] = 0x00FFFFFF, ["magenta"] = 0xFF00FFFF,
        };

        /// <summary>`#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA` or a colour name.</summary>
        internal static bool DoTryParseHtmlColor(string htmlString, out Color32 color)
        {
            color = new Color32(0, 0, 0, 255);
            if (string.IsNullOrEmpty(htmlString)) return false;
            uint rgba;
            if (htmlString[0] != '#')
            {
                if (!s_Named.TryGetValue(htmlString, out rgba)) return false;
            }
            else
            {
                var hex = htmlString.Substring(1);
                if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return false;
                switch (hex.Length)
                {
                    case 3: rgba = Expand((value >> 8) & 0xF, (value >> 4) & 0xF, value & 0xF, 0xF); break;
                    case 4: rgba = Expand((value >> 12) & 0xF, (value >> 8) & 0xF, (value >> 4) & 0xF, value & 0xF); break;
                    case 6: rgba = (value << 8) | 0xFF; break;
                    case 8: rgba = value; break;
                    default: return false;
                }
            }
            color = new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
            return true;
        }

        static uint Expand(uint r, uint g, uint b, uint a) => (r * 17 << 24) | (g * 17 << 16) | (b * 17 << 8) | (a * 17);

        public static bool TryParseHtmlString(string htmlString, out Color color)
        {
            bool parsed = DoTryParseHtmlColor(htmlString, out var c);
            color = c;
            return parsed;
        }

        public static string ToHtmlStringRGB(Color color)
        {
            var c = Bytes(color);
            return string.Format(CultureInfo.InvariantCulture, "{0:X2}{1:X2}{2:X2}", c.r, c.g, c.b);
        }

        public static string ToHtmlStringRGBA(Color color)
        {
            var c = Bytes(color);
            return string.Format(CultureInfo.InvariantCulture, "{0:X2}{1:X2}{2:X2}{3:X2}", c.r, c.g, c.b, c.a);
        }

        static Color32 Bytes(Color color) => new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255));
    }
}
