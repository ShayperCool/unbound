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
            "^\\s*(?<attributes>(?:\\[[^\\]]*\\]\\s*)*)(\\w+)\\s*\\(\\s*\"([^\"]*)\"\\s*,\\s*(\\w+)[^)]*?(?:\\(([^)]*)\\))?\\s*\\)\\s*=\\s*(.+?)\\s*$",
            RegexOptions.Multiline);

        internal static string Name(string source)
        {
            var m = s_Name.Match(source);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>A `Queue` tag's value as a render queue ("AlphaTest+10": 2460).</summary>
        internal static int QueueTag(string value) => Queue("\"Queue\"=\"" + value.Trim() + "\"");

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

        static readonly Regex s_Attribute = new Regex("\\[\\s*([^\\]]*?)\\s*\\]");

        /// <summary>A property's attributes: the ones Unity knows as flags, the others (drawers and
        /// decorators) as written, in order.</summary>
        static void Attributes(string text, ShaderPropertyInfo info)
        {
            var others = new List<string>();
            foreach (Match a in s_Attribute.Matches(text))
            {
                var attribute = a.Groups[1].Value;
                switch (attribute)
                {
                    case "HideInInspector": info.Flags |= ShaderPropertyFlags.HideInInspector; break;
                    case "PerRendererData": info.Flags |= ShaderPropertyFlags.PerRendererData; break;
                    case "NoScaleOffset": info.Flags |= ShaderPropertyFlags.NoScaleOffset; break;
                    case "Normal": info.Flags |= ShaderPropertyFlags.Normal; break;
                    case "HDR": info.Flags |= ShaderPropertyFlags.HDR; break;
                    case "Gamma": info.Flags |= ShaderPropertyFlags.Gamma; break;
                    case "NonModifiableTextureData": info.Flags |= ShaderPropertyFlags.NonModifiableTextureData; break;
                    case "MainTexture": info.Flags |= ShaderPropertyFlags.MainTexture; break;
                    case "MainColor": info.Flags |= ShaderPropertyFlags.MainColor; break;
                    default: others.Add(attribute); break;
                }
            }
            info.Attributes = others.Count > 0 ? others.ToArray() : Array.Empty<string>();
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
                Attributes(m.Groups["attributes"].Value, info);
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
                        // `Int` is the legacy type, a float (measured, 6000.0); `Integer` is an int.
                        info.Type = type == "Range" ? ShaderPropertyType.Range : type == "Integer" ? ShaderPropertyType.Int : ShaderPropertyType.Float;
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) info.DefaultValue = new Vector4(number, 0, 0, 0);
                        break;
                }
                // Names get their ids as the shader is read, as in Unity (ids order texture names).
                Shader.PropertyToID(info.Name);
                list.Add(info);
            }
            return list;
        }
    }
}
