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
        // What the SubShader Queue scan reads: strings, comments and program blocks (skipped
        // whole), words, braces and `=`.
        static readonly Regex s_QueueScan = new Regex(
            @"""(?:\\.|[^""\\])*""|/\*[\s\S]*?\*/|//[^\r\n]*|\b(?:HLSL|CG|GLSL)(?:PROGRAM|INCLUDE)\b[\s\S]*?\bEND(?:HLSL|CG|GLSL)\b|[A-Za-z_]\w*|[{}=]");
        // A queue name with an optional signed offset right after it, and nothing but whitespace
        // after that.
        static readonly Regex s_QueueValue = new Regex(@"^(?<name>[A-Za-z]+)(?:(?<sign>[+-])(?<offset>\d+))?\s*$");
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

        /// <summary>The source with its <c>Shader "Name"</c> declaration naming <paramref name="name"/>
        /// instead; the source itself without one.</summary>
        internal static string Renamed(string source, string name)
        {
            var m = s_Name.Match(source);
            if (!m.Success) return source;
            var group = m.Groups[1];
            return source.Substring(0, group.Index) + name + source.Substring(group.Index + group.Length);
        }

        /// <summary>
        /// A SubShader `Queue` tag's value as a render queue, null when Unity does not take it: a
        /// queue name in any case, optionally followed at once by a `+`/`-` integer offset, then
        /// only whitespace (the Unity 6000.0 Manual's "[queue name]" and "[queue name] +
        /// [offset]"; the names' values from its render queues page). Matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/graphicsgaps queue.*: "transparent", "GEOMETRY",
        /// "Geometry-1", "Geometry+0", "Geometry+1 " and "Overlay+5000" are queues; a number
        /// ("2450", "-5", "2000.5"), leading whitespace, spaces around the sign, "Geometry+-3"
        /// and unknown names are not.
        /// </summary>
        internal static int? QueueValue(string value)
        {
            if (value == null) return null;
            var m = s_QueueValue.Match(value);
            if (!m.Success) return null;
            int? queue = m.Groups["name"].Value.ToLowerInvariant() switch
            {
                "background" => 1000,
                "geometry" => 2000,
                "alphatest" => 2450,
                "transparent" => 3000,
                "overlay" => 4000,
                _ => null,
            };
            if (queue == null || !m.Groups["offset"].Success) return queue;
            if (!int.TryParse(m.Groups["offset"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)) return null;
            long sum = (long)queue.Value + (m.Groups["sign"].Value == "-" ? -offset : offset);
            return sum < int.MinValue || sum > int.MaxValue ? null : (int)sum;
        }

        /// <summary>A `Queue` tag's value as a render queue ("AlphaTest+10": 2460); Geometry (2000)
        /// for a value Unity does not take, as the Unity 6000.0.71f1 Editor answers
        /// (tests/projects/graphicsgaps queue.*; the value is logged,
        /// <see cref="UndefinedQueueMessage"/>).</summary>
        internal static int QueueTag(string value) => QueueValue(value) ?? 2000;

        /// <summary>The error the Editor logs for a SubShader `Queue` value it does not take, naming
        /// the shader ("Unknown" when the selection is made again for a new pipeline tag; Unity
        /// 6000.0.71f1 Editor, tests/projects/graphicsgaps queue.errors).</summary>
        internal static string UndefinedQueueMessage(string shader, string value) =>
            "Shader " + shader + " uses undefined Queue: '" + value + "'";

        /// <summary>
        /// The first SubShader's `Queue` tag in a source (2000 without one), as a render queue: a
        /// `Tags` block directly in a SubShader, else in the `Category` around it (whose tags its
        /// SubShaders take), not one of a Pass (Queue is a SubShader tag; URP's Simple Lit tags its
        /// Universal2D pass "Transparent", and its materials are Geometry in the Unity
        /// 6000.0.71f1 Editor, tests/projects/urp ssao_lightmap). A SubShader without one reads on
        /// to the next. A fallback only: the SubShader may be another pipeline's or one a LOD
        /// limit skips. <see cref="UnityEngine.Shader.renderQueue"/> answers from the selected
        /// SubShader's declared tag whenever that is known.
        /// </summary>
        /// <summary>The block kinds the Queue scan follows, in their canonical case; other words as
        /// they are.</summary>
        static string QueueBlockKind(string word) =>
            word.Equals("SubShader", StringComparison.OrdinalIgnoreCase) ? "SubShader"
            : word.Equals("Category", StringComparison.OrdinalIgnoreCase) ? "Category"
            : word.Equals("Tags", StringComparison.OrdinalIgnoreCase) ? "Tags"
            : word;

        internal static int Queue(string source)
        {
            // The kinds of the open blocks (the word before each `{`), and the queue of each
            // open Category's tags (null: none).
            var blocks = new List<string>();
            var categories = new List<string>();
            string word = null;
            bool key = false, equals = false;
            for (var m = s_QueueScan.Match(source); m.Success; m = m.NextMatch())
            {
                var token = m.Value;
                string owner = blocks.Count >= 2 && blocks[^1] == "Tags" ? blocks[^2] : null;
                bool tags = owner is "SubShader" or "Category";
                if (token[0] == '"')
                {
                    if (tags && key && equals)
                    {
                        var value = token.Substring(1, token.Length - 2);
                        if (owner == "SubShader") return QueueTag(value);
                        categories[^1] = value;
                    }
                    key = tags && !equals && token.Equals("\"Queue\"", StringComparison.OrdinalIgnoreCase);
                    equals = false;
                    word = null;
                    continue;
                }
                if (token == "=")
                {
                    equals = key;
                    continue;
                }
                key = equals = false;
                if (token == "{")
                {
                    // ShaderLab commands are case-insensitive (built-in shaders write Subshader).
                    var kind = word == null ? "" : QueueBlockKind(word);
                    blocks.Add(kind);
                    // A nested Category starts from the enclosing one's tags.
                    if (kind == "Category") categories.Add(categories.Count > 0 ? categories[^1] : null);
                    word = null;
                }
                else if (token == "}")
                {
                    word = null;
                    if (blocks.Count == 0) continue;
                    var closed = blocks[^1];
                    blocks.RemoveAt(blocks.Count - 1);
                    // A SubShader without a Queue of its own takes its Category's.
                    if (closed == "SubShader" && categories.Count > 0 && categories[^1] is string inherited) return QueueTag(inherited);
                    if (closed == "Category") categories.RemoveAt(categories.Count - 1);
                }
                else if (token[0] != '/')
                    word = token;
            }
            return 2000;
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

        /// <summary>The body of the source's `Properties` block, comments blanked (null: the
        /// source has no `Properties` block, so it declares no properties).</summary>
        internal static string PropertiesBlock(string source) => PropertiesBlock(source, out _, out _);

        static string PropertiesBlock(string source, out int open, out bool closed)
        {
            open = -1;
            closed = false;
            source = s_SourceTrivia.Replace(source, m => m.Value[0] == '"'
                ? m.Value : Regex.Replace(m.Value, @"[^\r\n]", " "));
            foreach (Match m in s_PropertiesBlock.Matches(source))
                if (m.Value[0] != '"') { open = m.Index + m.Length - 1; break; }
            if (open < 0) return null;
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
            closed = depth == 0;
            return source.Substring(open + 1, Math.Max(0, end - open - 1));
        }

        // The existing property reader also sees Properties text inside embedded programs.
        // A profiler layout needs the actual block directly within the Shader declaration.
        static bool IsShaderPropertiesBlock(string source, int open)
        {
            var declaration = s_Name.Match(source);
            if (!declaration.Success) return false;
            var token = s_QueueScan.Match(source, declaration.Index + declaration.Length);
            if (!token.Success || token.Length != 1 || source[token.Index] != '{') return false;
            int depth = 1;
            for (token = token.NextMatch(); token.Success && token.Index <= open; token = token.NextMatch())
            {
                if (token.Index == open) return depth == 1 && token.Length == 1;
                if (token.Length != 1) continue;
                if (source[token.Index] == '{') depth++;
                else if (source[token.Index] == '}' && --depth == 0) return false;
            }
            return false;
        }

        internal static List<ShaderPropertyInfo> Properties(string source) => Properties(source, out _);

        internal static List<ShaderPropertyInfo> Properties(string source, out bool layoutKnown)
        {
            var list = new List<ShaderPropertyInfo>();
            var block = PropertiesBlock(source, out int opening, out layoutKnown);
            if (block == null) return list;
            layoutKnown &= IsShaderPropertiesBlock(source, opening);
            int parsedThrough = 0;
            foreach (Match m in s_Property.Matches(block))
            {
                layoutKnown &= block.AsSpan(parsedThrough, m.Index - parsedThrough).IsWhiteSpace();
                parsedThrough = m.Index + m.Length;
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
                        layoutKnown &= type is "Float" or "Range" or "Int" or "Integer";
                        // `Int` is the legacy type, a float (measured, 6000.0); `Integer` is an int.
                        info.Type = type == "Range" ? ShaderPropertyType.Range : type == "Integer" ? ShaderPropertyType.Int : ShaderPropertyType.Float;
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) info.DefaultValue = new Vector4(number, 0, 0, 0);
                        // A Range's limits (Shader.GetPropertyRangeLimits; measured,
                        // tests/projects/graphicsgaps materialProperty._Rng.range).
                        if (info.Type == ShaderPropertyType.Range)
                        {
                            var limits = m.Groups[4].Value.Split(',');
                            if (limits.Length == 2
                                && float.TryParse(limits[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
                                && float.TryParse(limits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
                                info.Range = new Vector2(min, max);
                        }
                        break;
                }
                // Names get their ids as the shader is read, as in Unity (ids order texture names).
                Shader.PropertyToID(info.Name);
                list.Add(info);
            }
            layoutKnown &= block.AsSpan(parsedThrough).IsWhiteSpace();
            return list;
        }
    }
}
