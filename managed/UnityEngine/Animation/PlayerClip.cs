using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// An AnimationClip in player form (built into an AssetBundle or a player): no editor curves,
    /// a muscle clip instead — streamed curves (cubic segments between keys), dense curves (samples
    /// at a rate) and constant curves, in that order of curve index — and generic bindings naming
    /// what each curve animates by CRC32 hashes of the path and of the attribute (Transforms by code:
    /// 1 position, 2 rotation, 3 scale, 4 Euler angles; a material's property by its name's CRC32 in
    /// the low 28 bits and the channel above). The hashes are resolved against the animated
    /// hierarchy when the clip is bound (<see cref="HashedBindings"/>).
    /// </summary>
    internal static class PlayerClip
    {
        static readonly string[] s_Xyz = { ".x", ".y", ".z" };
        static readonly string[] s_Xyzw = { ".x", ".y", ".z", ".w" };

        internal static void Read(AnimationClip clip, YNode body, YNode muscle, IReferenceResolver resolver)
        {
            clip.m_HasRange = true;
            clip.m_StartTime = muscle.Float("m_StartTime");
            clip.m_StopTime = Math.Max(clip.m_StartTime, muscle.Float("m_StopTime"));
            clip.m_LoopTime = muscle.Bool("m_LoopTime");
            clip.m_CycleOffset = muscle.Float("m_CycleOffset");
            clip.m_LoopBlend = muscle.Bool("m_LoopBlend");

            var curves = new List<SampledCurve>();
            var data = muscle.Get("m_Clip")?.Get("data");
            if (data != null)
            {
                ReadStreamed(data.Get("m_StreamedClip"), curves);
                ReadDense(data.Get("m_DenseClip"), curves);
                foreach (var value in data.Get("m_ConstantClip")?.Get("data")?.Seq() ?? Array.Empty<YNode>())
                    curves.Add(new SampledCurve { Constant = Float(value) });
            }

            var bindings = body.Get("m_ClipBindingConstant");
            var objects = new List<Object>();
            foreach (var item in bindings?.Get("pptrCurveMapping")?.Seq() ?? Array.Empty<YNode>())
            {
                var reference = item.AsRef();
                objects.Add(reference.IsNull ? null : resolver.Resolve(reference, typeof(Object)));
            }
            int index = 0;
            foreach (var b in bindings?.Get("genericBindings")?.Seq() ?? Array.Empty<YNode>())
            {
                uint path = unchecked((uint)(b.Get("path")?.AsLong(0) ?? 0));
                uint attribute = unchecked((uint)(b.Get("attribute")?.AsLong(0) ?? 0));
                int typeId = b.Int("typeID");
                bool pptr = b.Int("isPPtrCurve") != 0;
                var binding = new CurveBinding
                {
                    Hashed = true,
                    PathHash = path,
                    Path = path == 0 ? "" : "#" + path.ToString(CultureInfo.InvariantCulture),
                    ClassId = typeId,
                    CustomType = b.Int("customType"),
                    AttributeHash = attribute,
                    Script = b.Ref("script"),
                };
                if (typeId == EngineTypes.TransformId && attribute >= 1 && attribute <= 4)
                {
                    string name = attribute switch { 1 => "m_LocalPosition", 2 => "m_LocalRotation", 3 => "m_LocalScale", _ => "localEulerAnglesRaw" };
                    var components = attribute == 2 ? s_Xyzw : s_Xyz;
                    for (int k = 0; k < components.Length; k++, index++)
                    {
                        if (index >= curves.Count) break;
                        clip.m_Curves.Add(new ClipCurve
                        {
                            Binding = new CurveBinding
                            {
                                Hashed = true,
                                PathHash = path,
                                Path = binding.Path,
                                ClassId = typeId,
                                Attribute = name + components[k],
                            },
                            Sampled = curves[index],
                            QuaternionStart = attribute == 2 && k == 0,
                            EulerStart = attribute == 4 && k == 0,
                        });
                    }
                    continue;
                }
                if (index >= curves.Count) break;
                var curve = curves[index++];
                if (pptr)
                {
                    // Object references by their index in the mapping, stepped.
                    var times = new List<float>();
                    var values = new List<Object>();
                    foreach (var (time, value) in curve.Keys())
                    {
                        int i = (int)Math.Round(value);
                        times.Add(time);
                        values.Add(i >= 0 && i < objects.Count ? objects[i] : null);
                    }
                    clip.m_ObjectCurves.Add(new ClipObjectCurve { Binding = binding, Times = times.ToArray(), Values = values.ToArray() });
                    continue;
                }
                clip.m_Curves.Add(new ClipCurve { Binding = binding, Sampled = curve });
            }
        }

        static float Float(YNode node) => float.Parse(node.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>Streamed curves: frames of a time, a key count and per key the curve's index and
        /// its segment's four coefficients (the first frame at -FLT_MAX, the last at +inf).</summary>
        static void ReadStreamed(YNode streamed, List<SampledCurve> curves)
        {
            if (streamed == null) return;
            int count = streamed.Int("curveCount");
            var words = new List<uint>();
            foreach (var item in streamed.Get("data")?.Seq() ?? Array.Empty<YNode>())
                words.Add((uint)item.AsLong(0));
            var times = new List<float>[count];
            var coefficients = new List<float>[count];
            for (int c = 0; c < count; c++)
            {
                times[c] = new List<float>();
                coefficients[c] = new List<float>();
            }
            float F(int i) => BitConverter.Int32BitsToSingle(unchecked((int)words[i]));
            int p = 0;
            while (p + 1 < words.Count)
            {
                float time = F(p);
                int keys = (int)words[p + 1];
                p += 2;
                for (int k = 0; k < keys && p + 4 < words.Count; k++, p += 5)
                {
                    int curve = (int)words[p];
                    if (curve < 0 || curve >= count) continue;
                    times[curve].Add(time);
                    coefficients[curve].Add(F(p + 1));
                    coefficients[curve].Add(F(p + 2));
                    coefficients[curve].Add(F(p + 3));
                    coefficients[curve].Add(F(p + 4));
                }
            }
            for (int c = 0; c < count; c++)
                curves.Add(new SampledCurve { Times = times[c].ToArray(), Coefficients = coefficients[c].ToArray() });
        }

        /// <summary>Dense curves: frame by frame the values of every curve.</summary>
        static void ReadDense(YNode dense, List<SampledCurve> curves)
        {
            if (dense == null) return;
            int count = dense.Int("m_CurveCount");
            int frames = dense.Int("m_FrameCount");
            if (count <= 0 || frames <= 0) return;
            var samples = new List<float>();
            foreach (var item in dense.Get("m_SampleArray")?.Seq() ?? Array.Empty<YNode>()) samples.Add(Float(item));
            float rate = dense.Float("m_SampleRate", 30f);
            float begin = dense.Float("m_BeginTime");
            for (int c = 0; c < count; c++)
            {
                var values = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    int i = f * count + c;
                    values[f] = i < samples.Count ? samples[i] : 0f;
                }
                curves.Add(new SampledCurve { Samples = values, BeginTime = begin, SampleRate = rate });
            }
        }
    }

    /// <summary>Resolves a player clip's hashed bindings against a hierarchy.</summary>
    internal static class HashedBindings
    {
        static readonly Dictionary<(Type, uint), string> s_Attributes = new Dictionary<(Type, uint), string>();

        internal static uint Crc32(string text)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(text))
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }

        /// <summary>The transform below the root whose path (relative, '/'-separated) hashes so.</summary>
        internal static Transform Find(Transform root, uint hash)
        {
            if (hash == 0) return root;
            return Search(root, "", hash);
        }

        static Transform Search(Transform parent, string prefix, uint hash)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                string path = prefix.Length == 0 ? child.name : prefix + "/" + child.name;
                if (Crc32(path) == hash) return child;
                var found = Search(child, path, hash);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>The component a hashed binding animates and its attribute's name, or null.</summary>
        internal static string Attribute(GameObject go, CurveBinding binding, out Component component)
        {
            component = null;
            if (binding.ClassId == 1) return Crc32("m_IsActive") == binding.AttributeHash ? "m_IsActive" : null;
            var type = ClassIds.TypeOf(binding.ClassId);
            if (type == null) return null;
            var components = go.m_Components;
            for (int i = 0; i < components.Count; i++)
            {
                var c = components[i];
                if (c == null || !type.IsInstanceOfType(c)) continue;
                var name = binding.CustomType == 22 && c is Renderer renderer
                    ? MaterialAttribute(renderer, binding.AttributeHash)
                    : NameOf(c, binding.AttributeHash);
                if (name == null) continue;
                component = c;
                return name;
            }
            return null;
        }

        static string NameOf(Component c, uint hash)
        {
            var type = c.GetType();
            if (s_Attributes.TryGetValue((type, hash), out var cached) && cached != null) return cached;
            foreach (var name in Candidates(c))
            {
                if (Crc32(name) != hash) continue;
                // Names that depend on the instance (blend shapes, parameters) are not cached.
                if (!(c is SkinnedMeshRenderer) && !(c is Animator)) s_Attributes[(type, hash)] = name;
                return name;
            }
            return null;
        }

        // A material property's channel: x, y, z, w, r, g, b, a, or the float itself (measured).
        static readonly string[] s_Channels = { ".x", ".y", ".z", ".w", ".r", ".g", ".b", ".a", "" };

        static string MaterialAttribute(Renderer renderer, uint hash)
        {
            uint channel = hash >> 28;
            if (channel >= s_Channels.Length) return null;
            uint name = hash & 0x0FFFFFFFu;
            foreach (var material in renderer.sharedMaterials)
            {
                var shader = material != null ? material.shader : null;
                if (shader == null) continue;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    var property = shader.GetPropertyName(i);
                    if ((Crc32(property) & 0x0FFFFFFFu) == name) return "material." + property + s_Channels[channel];
                    // A texture's scale and offset.
                    if ((Crc32(property + "_ST") & 0x0FFFFFFFu) == name) return "material." + property + "_ST" + s_Channels[channel];
                }
            }
            return null;
        }

        /// <summary>The attribute names a component's curves can have: its serialized fields (and
        /// their vector and color components, nested structs), the properties the binder knows.</summary>
        static IEnumerable<string> Candidates(Component c)
        {
            foreach (var known in s_Known) yield return known;
            if (c is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++) yield return "blendShape." + smr.sharedMesh.GetBlendShapeName(i);
            if (c is Animator animator)
                foreach (var p in animator.parameters) yield return p.name;
            foreach (var name in Fields(c.GetType(), "", 0)) yield return name;
        }

        static readonly string[] s_Known =
        {
            "m_Enabled", "m_IsActive", "m_SortingOrder", "m_Color.r", "m_Color.g", "m_Color.b", "m_Color.a", "m_Size.x", "m_Size.y",
            "m_FlipX", "m_FlipY", "m_Alpha", "m_Interactable", "m_BlocksRaycasts", "m_Sprite",
            "m_Materials.Array.data[0]", "m_Materials.Array.data[1]", "m_Materials.Array.data[2]", "m_Materials.Array.data[3]",
        };

        static IEnumerable<string> Fields(Type type, string prefix, int depth)
        {
            if (depth > 3) yield break;
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour) && t != typeof(Component) && t != typeof(Object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                                  System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    var name = prefix + field.Name;
                    var ft = field.FieldType;
                    if (ft == typeof(Vector2) || ft == typeof(Vector2Int))
                    {
                        yield return name + ".x"; yield return name + ".y";
                    }
                    else if (ft == typeof(Vector3) || ft == typeof(Vector3Int))
                    {
                        yield return name + ".x"; yield return name + ".y"; yield return name + ".z";
                    }
                    else if (ft == typeof(Vector4) || ft == typeof(Quaternion))
                    {
                        yield return name + ".x"; yield return name + ".y"; yield return name + ".z"; yield return name + ".w";
                    }
                    else if (ft == typeof(Color) || ft == typeof(Color32))
                    {
                        yield return name + ".r"; yield return name + ".g"; yield return name + ".b"; yield return name + ".a";
                    }
                    else if (ft.IsPrimitive || ft.IsEnum || typeof(Object).IsAssignableFrom(ft))
                    {
                        yield return name;
                    }
                    else if (ft.IsValueType && !ft.IsGenericType)
                    {
                        foreach (var nested in Fields(ft, name + ".", depth + 1)) yield return nested;
                    }
                }
            }
        }
    }
}
