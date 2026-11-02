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
            @"(?<attributes>(?:\[[^\]]*\]\s*)*)(\w+)\s*\(\s*""([^""]*)""\s*,\s*(\w+)[^)]*?(?:\(([^)]*)\))?\s*\)\s*=\s*(""[^""]*""(?:\s*\{[^{}]*\})?|\([^()]*\)|[^\s{}()""]+)",
            RegexOptions.Multiline);
        // Strings are consumed first, so comment-like text inside a label/texture stays intact.
        static readonly Regex s_SourceTrivia = new Regex(@"""(?:\\.|[^""\\])*""|/\*[\s\S]*?\*/|//[^\r\n]*");
        static readonly Regex s_PropertiesBlock = new Regex(@"""(?:\\.|[^""\\])*""|\bProperties\s*\{");

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

        static readonly Regex s_CustomEditor = new Regex(
            "^\\s*CustomEditor\\s+\"([^\"]+)\"\\s*$", RegexOptions.Multiline);
        static readonly Regex s_PipelineEditor = new Regex(
            "^\\s*CustomEditorForRenderPipeline\\s+\"([^\"]+)\"\\s+\"([^\"]+)\"", RegexOptions.Multiline);

        /// <summary>The shader's `CustomEditor` and its `CustomEditorForRenderPipeline` entries
        /// (editor, pipeline asset type).</summary>
        internal static void Editors(string source, Shader shader)
        {
            var custom = s_CustomEditor.Match(source);
            shader.m_CustomEditor = custom.Success ? custom.Groups[1].Value : null;
            shader.m_PipelineEditors.Clear();
            foreach (Match m in s_PipelineEditor.Matches(source))
                shader.m_PipelineEditors.Add((m.Groups[1].Value, m.Groups[2].Value));
        }

        /// <summary>A texture property's dimension by its type keyword, in any case (measured,
        /// tests/projects/droppedparams dims.*: `2d`, `cube`, `cubearray` and `any` declare textures
        /// as their capitalised forms do); null for a type that is not a texture.</summary>
        static TextureDimension? TextureDimensionOf(string type) => type.ToLowerInvariant() switch
        {
            "2d" => TextureDimension.Tex2D,
            "3d" => TextureDimension.Tex3D,
            "cube" => TextureDimension.Cube,
            "2darray" => TextureDimension.Tex2DArray,
            "cubearray" => TextureDimension.CubeArray,
            "any" => TextureDimension.Any,
            _ => null,
        };

        internal static List<ShaderPropertyInfo> Properties(string source)
        {
            var list = new List<ShaderPropertyInfo>();
            source = s_SourceTrivia.Replace(source, m => m.Value[0] == '"'
                ? m.Value : Regex.Replace(m.Value, @"[^\r\n]", " "));
            int open = -1;
            foreach (Match m in s_PropertiesBlock.Matches(source))
                if (m.Value[0] != '"') { open = m.Index + m.Length - 1; break; }
            if (open < 0) return list;
            int depth = 1, end = open + 1;
            for (; end < source.Length; end++)
            {
                if (source[end] == '"')
                {
                    for (end++; end < source.Length; end++)
                        if (source[end] == '\\') end++;
                        else if (source[end] == '"') break;
                }
                else if (source[end] == '{') depth++;
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
                        // A colour written with three components is opaque (measured: Standard's
                        // `_EmissionColor = (0,0,0)` reads (0,0,0,1), tests/projects/modelimport).
                        var v = type == "Color" && parts.Length == 3 ? new Vector4(0, 0, 0, 1) : new Vector4();
                        for (int i = 0; i < 4 && i < parts.Length; i++)
                            if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) v[i] = f;
                        info.DefaultValue = v;
                        break;
                    }
                    default:
                        if (TextureDimensionOf(type) is TextureDimension dimension)
                        {
                            info.Type = ShaderPropertyType.Texture;
                            info.Dimension = dimension;
                            info.DefaultTexture = value.Split('"').Length > 1 ? value.Split('"')[1] : "";
                            break;
                        }
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
