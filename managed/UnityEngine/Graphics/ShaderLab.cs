using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>The parts of ShaderLab source the engine needs: name, queue, property defaults.</summary>
    internal static class ShaderLab
    {
        static readonly Regex s_Name = new Regex("^\\s*Shader\\s+\"([^\"]+)\"", RegexOptions.Multiline);
        static readonly Regex s_Queue = new Regex("\"Queue\"\\s*=\\s*\"(\\w+)([+-]\\d+)?\"");
        static readonly Regex s_Property = new Regex(
            "^\\s*(?:\\[[^\\]]*\\]\\s*)*(\\w+)\\s*\\(\\s*\"([^\"]*)\"\\s*,\\s*(\\w+)[^)]*?(?:\\(([^)]*)\\))?\\s*\\)\\s*=\\s*(.+?)\\s*$",
            RegexOptions.Multiline);

        internal static string Name(string source)
        {
            var m = s_Name.Match(source);
            return m.Success ? m.Groups[1].Value : null;
        }

        internal static int Queue(string source)
        {
            var m = s_Queue.Match(source);
            if (!m.Success) return 2000;
            int queue = m.Groups[1].Value switch
            {
                "Background" => 1000,
                "Geometry" => 2000,
                "AlphaTest" => 2450,
                "Transparent" => 3000,
                "Overlay" => 4000,
                _ => 2000,
            };
            if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)) queue += offset;
            return queue;
        }

        internal static List<ShaderPropertyInfo> Properties(string source)
        {
            var list = new List<ShaderPropertyInfo>();
            int start = source.IndexOf("Properties", StringComparison.Ordinal);
            if (start < 0) return list;
            int open = source.IndexOf('{', start);
            if (open < 0) return list;
            int depth = 0, end = open;
            for (; end < source.Length; end++)
            {
                if (source[end] == '{') depth++;
                else if (source[end] == '}' && --depth == 0) break;
            }
            var block = source.Substring(open + 1, Math.Max(0, end - open - 1));
            foreach (Match m in s_Property.Matches(block))
            {
                var type = m.Groups[3].Value;
                var value = m.Groups[5].Value.Trim();
                var info = new ShaderPropertyInfo { Name = m.Groups[1].Value, Description = m.Groups[2].Value };
                switch (type)
                {
                    case "Color":
                    case "Vector":
                    {
                        info.Type = type == "Color" ? ShaderPropertyType.Color : ShaderPropertyType.Vector;
                        var parts = value.Trim('(', ')').Split(',');
                        var v = new Vector4();
                        for (int i = 0; i < 4 && i < parts.Length; i++)
                            if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) v[i] = f;
                        info.DefaultValue = v;
                        break;
                    }
                    case "2D":
                    case "3D":
                    case "Cube":
                    case "2DArray":
                        info.Type = ShaderPropertyType.Texture;
                        info.DefaultTexture = value.Split('"').Length > 1 ? value.Split('"')[1] : "";
                        break;
                    default:
                        info.Type = type == "Range" ? ShaderPropertyType.Range : type == "Int" || type == "Integer" ? ShaderPropertyType.Int : ShaderPropertyType.Float;
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) info.DefaultValue = new Vector4(number, 0, 0, 0);
                        break;
                }
                list.Add(info);
            }
            return list;
        }
    }
}
