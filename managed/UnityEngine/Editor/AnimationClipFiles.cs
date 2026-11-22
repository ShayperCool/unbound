using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// An animation clip made by script, written whole as the Editor writes it when it is made an
    /// asset (matches the Unity 6000.0.71f1 Editor, tests/projects/scenesave clip-assets and
    /// engine-assets Rich, Legacy): its Transform curves as vector curves (rotation, Euler angles,
    /// position, scale, each path's in the order its curves were set, the components keyed together),
    /// its other float curves and object reference curves in the order set, its settings and events,
    /// once AnimationUtility.SetEditorCurve has been used its float curves again as editor curves,
    /// and, once an AnimationUtility edit has built it, its binding table.
    /// </summary>
    internal static class AnimationClipFiles
    {
        /// <summary>The clip's serialized version in the release's files.</summary>
        internal const int SerializedVersion = 7;

        /// <summary>The Transform vector curves of a clip file, in the order its sections are
        /// written: their serialized names, sections and binding attributes.</summary>
        internal static readonly (string Head, string Section, int Attribute, string[] Axes)[] VectorKinds =
        {
            ("m_LocalRotation", "m_RotationCurves", 2, new[] { "x", "y", "z", "w" }),
            ("localEulerAnglesRaw", "m_EulerCurves", 4, new[] { "x", "y", "z" }),
            ("m_LocalPosition", "m_PositionCurves", 1, new[] { "x", "y", "z" }),
            ("m_LocalScale", "m_ScaleCurves", 3, new[] { "x", "y", "z" }),
        };

        /// <summary>The order of the vector kinds in a binding table: position, rotation, Euler
        /// angles, scale (indexes into <see cref="VectorKinds"/>).</summary>
        static readonly int[] s_BindingKindOrder = { 2, 0, 1, 3 };

        internal sealed class VectorGroup
        {
            internal int Kind;
            internal string Path;
            internal AnimationCurve[] Components;
            internal Keyframe[][] Keys;
        }

        /// <summary>The clip's Transform vector curves, grouped by kind and path; each kind's groups
        /// in the order their first component was set.</summary>
        internal static List<VectorGroup> VectorGroups(AnimationClip clip)
        {
            var groups = new List<VectorGroup>();
            foreach (var c in clip.m_Curves)
            {
                if (!IsVectorComponent(c, out int kind, out int axis)) continue;
                var group = groups.Find(g => g.Kind == kind && g.Path == c.Binding.Path);
                if (group == null)
                {
                    group = new VectorGroup { Kind = kind, Path = c.Binding.Path, Components = new AnimationCurve[VectorKinds[kind].Axes.Length] };
                    groups.Add(group);
                }
                group.Components[axis] = c.Curve;
            }
            foreach (var g in groups) g.Keys = AnimationClip.KeyedTogether(VectorKinds[g.Kind].Head, g.Components);
            return groups;
        }

        /// <summary>Whether a curve is one component of a Transform vector property.</summary>
        internal static bool IsVectorComponent(ClipCurve c, out int kind, out int axis)
        {
            kind = axis = -1;
            if (c.Binding.ResolveType() != typeof(Transform)) return false;
            var attribute = c.Binding.Attribute ?? "";
            int dot = attribute.LastIndexOf('.');
            if (dot < 0) return false;
            var head = attribute.Substring(0, dot);
            var tail = attribute.Substring(dot + 1);
            for (int k = 0; k < VectorKinds.Length; k++)
            {
                if (VectorKinds[k].Head != head) continue;
                int a = Array.IndexOf(VectorKinds[k].Axes, tail);
                if (a < 0) return false;
                kind = k;
                axis = a;
                return true;
            }
            return false;
        }

        /// <summary>Why the clip cannot be written yet (null when it can): a player clip's compiled
        /// curves, a humanoid clip, motion curves, or a built binding table naming a binding whose
        /// form in a file is not measured.</summary>
        internal static string Unwritable(AnimationClip clip)
        {
            foreach (var c in clip.m_Curves)
            {
                if (c.Curve == null) return "it holds a player clip's compiled curves";
                var type = c.Binding.ResolveType();
                if (Unnamed(type) is string unnamed) return $"its curve {c.Binding} {unnamed}";
                if (type == typeof(Animator)) return "it holds Animator curves (humanoid, motion or parameter curves), whose form in a clip file is not measured";
                if (type == typeof(Transform) && !IsVectorComponent(c, out _, out _)) return $"its Transform curve {c.Binding.Attribute} is not one of a vector property's components";
                // A RectTransform's position, rotation and scale curves: their form is not measured.
                if (type != typeof(Transform) && typeof(Transform).IsAssignableFrom(type) && VectorHead(c.Binding.Attribute))
                    return $"its curve {c.Binding} is a {type.Name}'s Transform vector curve, whose form in a clip file is not measured";
            }
            if (clip.m_HumanAvatar != null || clip.m_HumanTransformPaths != null) return "it is a humanoid clip";
            foreach (var o in clip.m_ObjectCurves)
                if (Unnamed(o.Binding.ResolveType()) is string unnamed) return $"its object reference curve {o.Binding} {unnamed}";
            if (clip.m_BindingsBuilt && clip.m_Legacy) return "it is a legacy clip an AnimationUtility edit has built, whose file is not measured";
            // Transform vectors set by SetCurve beside editor curves: how they are listed is not
            // measured.
            if (clip.m_EditorCurvesSynced)
                foreach (var c in clip.m_Curves)
                    if (c.Flags == 0 && IsVectorComponent(c, out _, out _))
                        return "it holds Transform curves set by SetCurve and curves set by AnimationUtility.SetEditorCurve, whose listing in a clip file is not measured";
            if (!clip.m_BindingsBuilt) return null;
            foreach (var c in clip.m_Curves)
            {
                if (IsVectorComponent(c, out _, out _)) continue;
                // A discrete curve has no binding in the measured case (a sprite renderer's sorting
                // order); others' are not measured.
                if (c.Flags == 1)
                {
                    if (c.Binding.ResolveType() == typeof(SpriteRenderer) && c.Binding.Attribute == "m_SortingOrder") continue;
                    return $"its discrete curve {c.Binding} has a binding whose form in a clip file is not measured";
                }
                if (CustomType(c.Binding.ResolveType(), c.Binding.Attribute) == null)
                    return $"its curve {c.Binding} has a binding whose form in a clip file is not measured";
            }
            foreach (var o in clip.m_ObjectCurves)
                if (ObjectBinding(o.Binding.ResolveType(), o.Binding.Attribute) == null)
                    return $"its object reference curve {o.Binding} has a binding whose form in a clip file is not measured";
            return null;
        }

        /// <summary>Why a curve's class cannot be named in a clip file (null when it can): unresolved,
        /// without a class id, or a script without its MonoScript.</summary>
        static string Unnamed(Type type)
        {
            if (type == null) return "names a class Unbound cannot resolve";
            if (typeof(MonoBehaviour).IsAssignableFrom(type))
                return MonoScript.FromType(type) is MonoScript script && !string.IsNullOrEmpty(script.m_Guid) ? null : "names a script without its script asset";
            return ClassIdOf(type) < 0 ? "names a class without a class id" : null;
        }

        static bool VectorHead(string attribute)
        {
            int dot = (attribute ?? "").LastIndexOf('.');
            if (dot < 0) return false;
            var head = attribute.Substring(0, dot);
            foreach (var k in VectorKinds)
                if (k.Head == head) return true;
            return false;
        }

        /// <summary>A float curve's custom binding type in a binding table, by its class and
        /// property; null where it is not measured (tests/projects/scenesave clip-assets Order,
        /// Editor, Kinds, Common, engine-assets Rich).</summary>
        internal static int? CustomType(Type type, string attribute)
        {
            if (type == null) return null;
            if (type == typeof(GameObject) || type == typeof(CanvasGroup) || type == typeof(AudioSource)) return 0;
            if (typeof(MonoBehaviour).IsAssignableFrom(type)) return 0;
            if (type == typeof(RectTransform)) return 28;
            if (type == typeof(Camera)) return 37;
            if (type == typeof(Light)) return 25;
            if (type == typeof(MeshRenderer) || type == typeof(SkinnedMeshRenderer) || type == typeof(SpriteRenderer))
            {
                if (attribute.StartsWith("material.", StringComparison.Ordinal)) return 22;
                if (attribute.StartsWith("blendShape.", StringComparison.Ordinal)) return type == typeof(SkinnedMeshRenderer) ? 20 : null;
                return 0;
            }
            return null;
        }

        /// <summary>An object reference curve's binding attribute and custom type in a binding
        /// table; null where it is not measured (a renderer's first material, a sprite renderer's
        /// sprite: tests/projects/scenesave clip-assets Order, Common).</summary>
        internal static (uint Attribute, int CustomType)? ObjectBinding(Type type, string attribute)
        {
            if (type == null) return null;
            if (typeof(Renderer).IsAssignableFrom(type) && attribute == "m_Materials.Array.data[0]") return (0, 21);
            if (type == typeof(SpriteRenderer) && attribute == "m_Sprite") return (0, 23);
            return null;
        }

        /// <summary>A curve is constant when every key has the same value and flat tangents (a
        /// vector's every component): such curves come last in a binding table (measured with
        /// AnimationCurve.Constant's keys; a curve of equal values with sloped tangents is
        /// unmeasured and counts as changing).</summary>
        static bool Constant(Keyframe[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
                if (keys[i].value != keys[0].value || keys[i].inTangent != 0f || keys[i].outTangent != 0f) return false;
            return true;
        }

        static bool Constant(VectorGroup g)
        {
            foreach (var keys in g.Keys)
                if (!Constant(keys)) return false;
            return true;
        }

        internal readonly struct Binding
        {
            internal readonly uint Path, Attribute;
            internal readonly Type Type;
            internal readonly int CustomType;
            internal readonly bool PPtr;
            internal readonly ClipObjectCurve Objects;

            internal Binding(uint path, uint attribute, Type type, int customType, bool pptr, ClipObjectCurve objects)
            {
                Path = path;
                Attribute = attribute;
                Type = type;
                CustomType = customType;
                PPtr = pptr;
                Objects = objects;
            }
        }

        /// <summary>A built clip's binding table: its changing Transform curves (position,
        /// rotation, Euler angles, scale), its changing float curves, its object reference curves,
        /// then its constant Transform and float curves, each in the order set; discrete curves
        /// have none (tests/projects/scenesave clip-assets Order, Kinds, Common, Editor).</summary>
        internal static List<Binding> Bindings(AnimationClip clip, List<VectorGroup> groups)
        {
            var changing = new List<Binding>();
            var constant = new List<Binding>();
            foreach (int kind in s_BindingKindOrder)
                foreach (var g in groups)
                {
                    if (g.Kind != kind) continue;
                    var binding = new Binding(HashedBindings.Crc32(g.Path), (uint)VectorKinds[kind].Attribute, typeof(Transform), kind == 1 ? 4 : 0, false, null);
                    (Constant(g) ? constant : changing).Add(binding);
                }
            var floats = new List<Binding>();
            var constantFloats = new List<Binding>();
            foreach (var c in clip.m_Curves)
            {
                if (IsVectorComponent(c, out _, out _) || c.Flags == 1) continue;
                var type = c.Binding.ResolveType();
                var binding = new Binding(HashedBindings.Crc32(c.Binding.Path), Attribute(type, c.Binding.Attribute), type,
                    CustomType(type, c.Binding.Attribute) ?? 0, false, null);
                (Constant(c.Curve.keys) ? constantFloats : floats).Add(binding);
            }
            changing.AddRange(floats);
            foreach (var o in clip.m_ObjectCurves)
            {
                var type = o.Binding.ResolveType();
                var (attribute, custom) = ObjectBinding(type, o.Binding.Attribute) ?? (0u, 0);
                changing.Add(new Binding(HashedBindings.Crc32(o.Binding.Path), attribute, type, custom, true, o));
            }
            changing.AddRange(constant);
            changing.AddRange(constantFloats);
            return changing;
        }

        /// <summary>Whether a built clip's root has position or rotation curves (Euler angles
        /// too): its m_HasGenericRootTransform (scale alone does not count: engine-assets Rich).</summary>
        internal static bool GenericRoot(AnimationClip clip, List<VectorGroup> groups)
        {
            if (!clip.m_BindingsBuilt) return false;
            foreach (var g in groups)
                if (g.Path.Length == 0 && VectorKinds[g.Kind].Head != "m_LocalScale") return true;
            return false;
        }

        /// <summary>The class id a curve names its class by (shared with <see cref="ClassIds.IdOf"/>,
        /// returning -1 when unknown so <see cref="Unnamed"/> can reject it).</summary>
        internal static int ClassIdOf(Type type)
        {
            int id = ClassIds.IdOf(type);
            return id > 0 ? id : -1;
        }

        /// <summary>A float curve's binding attribute: a blend shape's by its name's CRC32
        /// (tests/projects/scenesave clip-assets Editor.anim), a material property's by
        /// <see cref="MaterialAttribute"/>, others by the attribute's CRC32.</summary>
        internal static uint Attribute(Type type, string attribute)
        {
            const string blendShape = "blendShape.", material = "material.";
            if (type == typeof(SkinnedMeshRenderer) && attribute.StartsWith(blendShape, StringComparison.Ordinal))
                return HashedBindings.Crc32(attribute.Substring(blendShape.Length));
            if (typeof(Renderer).IsAssignableFrom(type) && attribute.StartsWith(material, StringComparison.Ordinal))
                return MaterialAttribute(attribute.Substring(material.Length));
            return HashedBindings.Crc32(attribute);
        }

        /// <summary>A material property curve's binding attribute: the property name's CRC32 with its
        /// top four bits replaced by the component's code, 0 to 3 for a vector's x to w, 4 to 7 for
        /// a color's r to a, 8 for a float (tests/projects/scenesave clip-assets Editor.anim,
        /// Materials.anim).</summary>
        internal static uint MaterialAttribute(string property)
        {
            int dot = property.LastIndexOf('.');
            int code = dot < 0 ? -1 : Array.IndexOf(s_MaterialComponents, property.Substring(dot + 1));
            var name = code < 0 ? property : property.Substring(0, dot);
            return (HashedBindings.Crc32(name) & 0x0FFFFFFFu) | ((uint)(code < 0 ? MaterialFloatCode : code) << 28);
        }

        static readonly string[] s_MaterialComponents = { "x", "y", "z", "w", "r", "g", "b", "a" };
        const int MaterialFloatCode = 8;
    }

    internal static partial class UnityYamlWriter
    {
        internal sealed partial class Writer
        {
            static readonly string s_Third = FloatText.Format(1f / 3f);

            /// <summary>An animation clip's whole body (see <see cref="AnimationClipFiles"/>).</summary>
            internal void AnimationClipBody(AnimationClip clip)
            {
                var groups = AnimationClipFiles.VectorGroups(clip);
                ObjectHeader(clip);
                String("m_Name", clip.name);
                Int("serializedVersion", AnimationClipFiles.SerializedVersion);
                Bool("m_Legacy", clip.m_Legacy);
                Int("m_Compressed", 0);
                Bool("m_UseHighQualityCurve", true);
                VectorSection(groups, 0);
                Line(1, "m_CompressedRotationCurves", "[]");
                VectorSection(groups, 1);
                VectorSection(groups, 2);
                VectorSection(groups, 3);
                var floats = clip.m_Curves.FindAll(c => !AnimationClipFiles.IsVectorComponent(c, out _, out _));
                FloatCurves("m_FloatCurves", floats);
                if (clip.m_ObjectCurves.Count == 0) Line(1, "m_PPtrCurves", "[]");
                else
                {
                    Key("m_PPtrCurves", 1);
                    foreach (var o in clip.m_ObjectCurves)
                    {
                        Item("serializedVersion", "2", 1);
                        if (o.Times.Length == 0) Line(2, "curve", "[]");
                        else
                        {
                            Key("curve", 2);
                            for (int i = 0; i < o.Times.Length; i++)
                            {
                                Item("time", F(o.Times[i]), 2);
                                Line(3, "value", Reference(o.Values[i]));
                            }
                        }
                        BindingFields(o.Binding, 2, 2);
                    }
                }
                Float("m_SampleRate", clip.m_SampleRate);
                Int("m_WrapMode", (int)clip.m_WrapMode);
                Key("m_Bounds", 1);
                Vector3("m_Center", clip.m_Bounds.center, 2);
                Vector3("m_Extent", clip.m_Bounds.extents, 2);
                BindingTable(clip, groups);
                ClipSettings(clip);
                FloatCurves("m_EditorCurves", clip.m_EditorCurvesSynced ? clip.m_Curves : new List<ClipCurve>());
                Line(1, "m_EulerEditorCurves", "[]");
                Bool("m_HasGenericRootTransform", AnimationClipFiles.GenericRoot(clip, groups));
                Bool("m_HasMotionFloatCurves", false);
                if (clip.m_Events.Count == 0) Line(1, "m_Events", "[]");
                else
                {
                    Key("m_Events", 1);
                    foreach (var e in clip.m_Events)
                    {
                        Item("time", F(e.m_Time), 1);
                        String("functionName", e.m_FunctionName ?? "", 2);
                        String("data", e.m_StringParameter ?? "", 2);
                        Line(2, "objectReferenceParameter", Reference(e.m_ObjectReferenceParameter));
                        Float("floatParameter", e.m_FloatParameter, 2);
                        Int("intParameter", e.m_IntParameter, 2);
                        Int("messageOptions", e.m_MessageOptions, 2);
                    }
                }
            }

            void VectorSection(List<AnimationClipFiles.VectorGroup> groups, int kind)
            {
                var section = AnimationClipFiles.VectorKinds[kind].Section;
                var mine = groups.FindAll(g => g.Kind == kind);
                if (mine.Count == 0)
                {
                    Line(1, section, "[]");
                    return;
                }
                Key(section, 1);
                foreach (var g in mine)
                {
                    Key("- curve", 1);
                    Line(3, "serializedVersion", "2");
                    int count = g.Keys[0].Length;
                    if (count == 0) Line(3, "m_Curve", "[]");
                    else
                    {
                        Key("m_Curve", 3);
                        for (int i = 0; i < count; i++)
                        {
                            Item("serializedVersion", "3", 3);
                            Line(4, "time", F(g.Keys[0][i].time));
                            Line(4, "value", VectorText(g.Keys, i, k => k.value));
                            Line(4, "inSlope", VectorText(g.Keys, i, k => k.inTangent));
                            Line(4, "outSlope", VectorText(g.Keys, i, k => k.outTangent));
#pragma warning disable CS0618
                            Line(4, "tangentMode", g.Keys[0][i].tangentMode.ToString(CultureInfo.InvariantCulture));
#pragma warning restore CS0618
                            Line(4, "weightedMode", ((int)g.Keys[0][i].weightedMode).ToString(CultureInfo.InvariantCulture));
                            Line(4, "inWeight", VectorText(g.Keys, i, k => k.inWeight));
                            Line(4, "outWeight", VectorText(g.Keys, i, k => k.outWeight));
                        }
                    }
                    var first = g.Components[Array.FindIndex(g.Components, c => c != null)];
                    Line(3, "m_PreInfinity", Infinity(first.preWrapMode).ToString(CultureInfo.InvariantCulture));
                    Line(3, "m_PostInfinity", Infinity(first.postWrapMode).ToString(CultureInfo.InvariantCulture));
                    Line(3, "m_RotationOrder", "4");
                    String("path", g.Path, 2);
                }
            }

            static readonly string[] s_Axes = { "x", "y", "z", "w" };
            static readonly char[] s_Brackets = { '[', ']' };

            static string VectorText(Keyframe[][] keys, int index, Func<Keyframe, float> part)
            {
                var text = new System.Text.StringBuilder("{");
                for (int a = 0; a < keys.Length; a++)
                {
                    if (a > 0) text.Append(", ");
                    text.Append(s_Axes[a]).Append(": ").Append(F(part(keys[a][index])));
                }
                return text.Append('}').ToString();
            }

            void FloatCurves(string section, List<ClipCurve> curves)
            {
                if (curves.Count == 0)
                {
                    Line(1, section, "[]");
                    return;
                }
                Key(section, 1);
                foreach (var c in curves)
                {
                    Item("serializedVersion", "2", 1);
                    Key("curve", 2);
                    Curve(c.Curve, 3);
                    BindingFields(c.Binding, 2, c.Flags);
                }
            }

            /// <summary>A curve's attribute, path, class and script, and its flags.</summary>
            void BindingFields(CurveBinding binding, int level, int flags)
            {
                var type = binding.ResolveType();
                // An attribute holding brackets (an array element's) is single-quoted
                // (tests/projects/scenesave clip-assets Order.anim).
                var attribute = binding.Attribute ?? "";
                if (attribute.IndexOfAny(s_Brackets) >= 0) Line(level, "attribute", "'" + attribute.Replace("'", "''") + "'");
                else String("attribute", attribute, level);
                String("path", binding.Path ?? "", level);
                Int("classID", AnimationClipFiles.ClassIdOf(type), level);
                Line(level, "script", ScriptReference(type));
                Int("flags", flags, level);
            }

            string ScriptReference(Type type) =>
                typeof(MonoBehaviour).IsAssignableFrom(type) ? Reference(MonoScript.FromType(type)) : "{fileID: 0}";

            void BindingTable(AnimationClip clip, List<AnimationClipFiles.VectorGroup> groups)
            {
                Key("m_ClipBindingConstant", 1);
                if (!clip.m_BindingsBuilt)
                {
                    Line(2, "genericBindings", "[]");
                    Line(2, "pptrCurveMapping", "[]");
                    return;
                }
                var bindings = AnimationClipFiles.Bindings(clip, groups);
                if (bindings.Count == 0) Line(2, "genericBindings", "[]");
                else
                {
                    Key("genericBindings", 2);
                    foreach (var b in bindings)
                    {
                        Item("serializedVersion", "2", 2);
                        Int("path", b.Path, 3);
                        Int("attribute", b.Attribute, 3);
                        Line(3, "script", ScriptReference(b.Type));
                        Int("typeID", AnimationClipFiles.ClassIdOf(b.Type), 3);
                        Int("customType", b.CustomType, 3);
                        Bool("isPPtrCurve", b.PPtr, 3);
                        Bool("isIntCurve", false, 3);
                        Bool("isSerializeReferenceCurve", false, 3);
                    }
                }
                var mapping = new List<Object>();
                foreach (var b in bindings)
                    if (b.Objects != null) mapping.AddRange(b.Objects.Values);
                if (mapping.Count == 0) Line(2, "pptrCurveMapping", "[]");
                else
                {
                    Key("pptrCurveMapping", 2);
                    foreach (var o in mapping) SequenceValue(2, Reference(o));
                }
            }

            void ClipSettings(AnimationClip clip)
            {
                Key("m_AnimationClipSettings", 1);
                Int("serializedVersion", 2, 2);
                Line(2, "m_AdditiveReferencePoseClip", Reference(clip.m_AdditiveReferencePoseClip));
                Float("m_AdditiveReferencePoseTime", clip.m_AdditiveReferencePoseTime, 2);
                Float("m_StartTime", clip.SettingsStartTime, 2);
                Float("m_StopTime", clip.SettingsStopTime, 2);
                Float("m_OrientationOffsetY", clip.m_OrientationOffsetY, 2);
                Float("m_Level", clip.m_Level, 2);
                Float("m_CycleOffset", clip.m_CycleOffset, 2);
                Bool("m_HasAdditiveReferencePose", clip.m_HasAdditiveReferencePose, 2);
                Bool("m_LoopTime", clip.m_LoopTime, 2);
                Bool("m_LoopBlend", clip.m_LoopBlend, 2);
                Bool("m_LoopBlendOrientation", clip.m_LoopBlendOrientation, 2);
                Bool("m_LoopBlendPositionY", clip.m_LoopBlendPositionY, 2);
                Bool("m_LoopBlendPositionXZ", clip.m_LoopBlendPositionXZ, 2);
                Bool("m_KeepOriginalOrientation", clip.m_KeepOriginalOrientation, 2);
                Bool("m_KeepOriginalPositionY", clip.m_KeepOriginalPositionY, 2);
                Bool("m_KeepOriginalPositionXZ", clip.m_KeepOriginalPositionXZ, 2);
                Bool("m_HeightFromFeet", clip.m_HeightFromFeet, 2);
                Bool("m_Mirror", clip.m_Mirror, 2);
            }
        }
    }
}
