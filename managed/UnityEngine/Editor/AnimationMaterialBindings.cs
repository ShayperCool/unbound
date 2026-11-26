using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor
{
    public partial class AnimationUtility
    {
        /// <summary>The order Unity numbers a name in: its shader property id (Shader.PropertyToID: the
        /// engine's names first, in the Editor's order, then each name as it is first given one, a
        /// shader's in its declaration order as it loads with a texture's _ST, _TexelSize and _HDR
        /// after it; tests/projects/propertyids, tests/projects/animmaterialbindings).</summary>
        static int NameOrder(string name) => Shader.PropertyToID(name);

        static readonly Dictionary<string, string> s_FirstShaderOf = new Dictionary<string, string>();

        static void SeeShaderNames(Shader shader)
        {
            foreach (var p in shader.m_Properties)
            {
                Shader.RegisterPropertyName(p);
                SeeShaderName(shader, p.Name);
                if (p.Type != ShaderPropertyType.Texture) continue;
                SeeShaderName(shader, p.Name + "_ST");
                SeeShaderName(shader, p.Name + "_TexelSize");
                SeeShaderName(shader, p.Name + "_HDR");
            }
        }

        static void SeeShaderName(Shader shader, string name)
        {
            if (NameOrder(name) < Shader.EngineNameCount) return;
            var shaderName = shader.m_Name ?? "";
            if (!s_FirstShaderOf.TryGetValue(name, out var first)) s_FirstShaderOf[name] = shaderName;
            else if (first != shaderName)
                Unbound.Runtime.Stubs.Ignored("AnimationUtility.GetAnimatableBindings names shaders share",
                    "orders a name two shaders declare as the first shader seen declares it (the Editor numbers it in its own shader load order; not measured)");
        }

        static readonly string[] s_Rgba = { "r", "g", "b", "a" };
        static readonly string[] s_Xyzw = { "x", "y", "z", "w" };

        /// <summary>A renderer's material property bindings, ahead of its own, as the Editor lists
        /// them (tests/projects/animmaterialbindings, animbindings): for each material in slot order,
        /// its float properties, then its vector properties (colours, vectors, and each texture's
        /// _ST, _TexelSize and _HDR), each kind by Unity's name ids. Values a script set for names
        /// its shader does not declare are listed too; Integer properties are not. A colour has
        /// r, g, b, a components, an HDR colour and every vector x, y, z, w.</summary>
        internal static void AddMaterialBindings(Renderer renderer, string path, Type type, List<EditorCurveBinding> list)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                var floats = new List<string>();
                var vectors = new List<(string Name, string[] Axes)>();
                var shader = material.shader;
                if (shader != null)
                {
                    SeeShaderNames(shader);
                    foreach (var p in shader.m_Properties)
                    {
                        switch (p.Type)
                        {
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range:
                                floats.Add(p.Name);
                                break;
                            case ShaderPropertyType.Color:
                                vectors.Add((p.Name, (p.Flags & ShaderPropertyFlags.HDR) != 0 ? s_Xyzw : s_Rgba));
                                break;
                            case ShaderPropertyType.Vector:
                                vectors.Add((p.Name, s_Xyzw));
                                break;
                            case ShaderPropertyType.Texture:
                                vectors.Add((p.Name + "_ST", s_Xyzw));
                                vectors.Add((p.Name + "_TexelSize", s_Xyzw));
                                vectors.Add((p.Name + "_HDR", s_Xyzw));
                                break;
                        }
                    }
                }
                // Values a script set for names the shader does not declare; not the values a
                // loaded material's data or an earlier shader's defaults hold (the built-in
                // Sprites-Mask material's stale entries; a replaced shader's properties).
                foreach (var id in material.m_ScriptSet)
                {
                    var name = Shader.IdToName(id);
                    if (name == null) continue;
                    if (material.m_Floats.ContainsKey(id) && !floats.Contains(name)) floats.Add(name);
                    else if (material.m_Vectors.ContainsKey(id) && !vectors.Exists(v => v.Name == name)) vectors.Add((name, s_Xyzw));
                }
                // Stable: equal orders (an unmeasured id meeting a measured one) keep the listing's order.
                floats = floats.Select((n, i) => (n, i)).OrderBy(x => NameOrder(x.n)).ThenBy(x => x.i).Select(x => x.n).ToList();
                vectors = vectors.Select((v, i) => (v, i)).OrderBy(x => NameOrder(x.v.Name)).ThenBy(x => x.i).Select(x => x.v).ToList();
                // A binding names a property, not a slot: a name two materials share is listed once
                // (two Sprites-Default materials: tests/projects/animbindings SpriteShapeRenderer).
                foreach (var name in floats) AddOnce(list, EditorCurveBinding.FloatCurve(path, type, "material." + name));
                foreach (var (name, axes) in vectors)
                    foreach (var axis in axes)
                        AddOnce(list, EditorCurveBinding.FloatCurve(path, type, "material." + name + "." + axis));
            }
        }

        static void AddOnce(List<EditorCurveBinding> list, EditorCurveBinding binding)
        {
            if (!list.Contains(binding)) list.Add(binding);
        }
    }
}
