using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>What a curve of a clip animates, as the Editor's animation tools name it: the path
    /// below the animated root, the component type and the property's serialized name.</summary>
    public partial struct EditorCurveBinding : IEquatable<EditorCurveBinding>
    {
        public string path;
        public string propertyName;
        internal int m_ClassID;
        internal int m_ScriptInstanceID;
        Type m_Type;
        bool m_IsPPtr;
        bool m_IsDiscrete;
        bool m_IsSerializeReference;

        public Type type { get => m_Type; set => m_Type = value; }

        public bool isPPtrCurve => m_IsPPtr;

        public bool isDiscreteCurve => m_IsDiscrete;

        public bool isSerializeReferenceCurve => m_IsSerializeReference;

        public static EditorCurveBinding FloatCurve(string inPath, Type inType, string inPropertyName) =>
            new EditorCurveBinding { path = inPath, m_Type = inType, propertyName = inPropertyName };

        public static EditorCurveBinding PPtrCurve(string inPath, Type inType, string inPropertyName) =>
            new EditorCurveBinding { path = inPath, m_Type = inType, propertyName = inPropertyName, m_IsPPtr = true, m_IsDiscrete = true };

        public static EditorCurveBinding DiscreteCurve(string inPath, Type inType, string inPropertyName) =>
            new EditorCurveBinding { path = inPath, m_Type = inType, propertyName = inPropertyName, m_IsDiscrete = true };

        public static bool operator ==(EditorCurveBinding lhs, EditorCurveBinding rhs) => lhs.Equals(rhs);

        public static bool operator !=(EditorCurveBinding lhs, EditorCurveBinding rhs) => !lhs.Equals(rhs);

        public override int GetHashCode() => HashCode.Combine(path ?? "", propertyName ?? "", m_Type);

        public override bool Equals(object other) => other is EditorCurveBinding b && Equals(b);

        public bool Equals(EditorCurveBinding other) =>
            (path ?? "") == (other.path ?? "") && (propertyName ?? "") == (other.propertyName ?? "") && m_Type == other.m_Type
            && m_IsPPtr == other.m_IsPPtr && m_IsDiscrete == other.m_IsDiscrete;
    }

    public partial class AnimationClipSettings
    {
        public AnimationClip additiveReferencePoseClip;
        public float additiveReferencePoseTime;
        public float startTime;
        public float stopTime;
        public float orientationOffsetY;
        public float level;
        public float cycleOffset;
        public bool hasAdditiveReferencePose;
        public bool loopTime;
        public bool loopBlend;
        public bool loopBlendOrientation;
        public bool loopBlendPositionY;
        public bool loopBlendPositionXZ;
        public bool keepOriginalOrientation;
        public bool keepOriginalPositionY;
        public bool keepOriginalPositionXZ;
        public bool heightFromFeet;
        public bool mirror;

        public AnimationClipSettings() { }
    }

    /// <summary>The Editor's view of animation clips: their curves by binding, the animated objects
    /// and values a binding names (measured, tests/projects/editorui).</summary>
    public partial class AnimationUtility
    {
        static EditorCurveBinding BindingOf(ClipCurve c)
        {
            var type = c.Binding.ResolveType();
            bool discrete = (c.Flags & 1) != 0 || IsDiscreteField(type, c.Binding.Attribute);
            return discrete
                ? EditorCurveBinding.DiscreteCurve(c.Binding.Path, type, c.Binding.Attribute)
                : EditorCurveBinding.FloatCurve(c.Binding.Path, type, c.Binding.Attribute);
        }

        static List<ClipCurve> SortedRuntimeCurves(AnimationClip clip) =>
            clip.CurvesAsImported().Select((c, i) => (c, i)).OrderBy(p => p.c.Binding.Path, StringComparer.Ordinal)
                .ThenBy(p => CurveKindOrder(p.c.Binding)).ThenBy(p => p.i)
                .Select(p => p.c).ToList();

        /// <summary>The float curves' bindings: the clip's authored editor curves in their own
        /// order when present (<c>m_EditorCurves</c> or <see cref="SetEditorCurve"/>), else the
        /// imported runtime curves sorted by path and Transform kind (Unity 6000.0.71f1,
        /// tests/projects/editorui, tests/projects/animatorapi <c>clip.*.bindings</c>,
        /// tests/projects/animrest <c>modebindings.*</c>).</summary>
        public static EditorCurveBinding[] GetCurveBindings(AnimationClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (clip.m_EditorCurves != null)
                return clip.m_EditorCurves.Select(BindingOf).ToArray();
            return SortedRuntimeCurves(clip).Select(BindingOf).ToArray();
        }

        /// <summary>Where a curve's kind sorts within its path: a Transform's rotation, Euler
        /// angles, position and scale, then everything else.</summary>
        static int CurveKindOrder(CurveBinding binding)
        {
            if (binding.ClassId != k_TransformClassId) return 4;
            var attribute = binding.Attribute ?? "";
            int dot = attribute.LastIndexOf('.');
            return TransformKind(dot < 0 ? attribute : attribute.Substring(0, dot)) switch { 1 => 0, 2 => 1, 0 => 2, 3 => 3, _ => 4 };
        }

        public static EditorCurveBinding[] GetObjectReferenceCurveBindings(AnimationClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            return clip.m_ObjectCurves.Select(c => EditorCurveBinding.PPtrCurve(c.Binding.Path, c.Binding.ResolveType(), c.Binding.Attribute)).ToArray();
        }

        static ClipCurve Find(AnimationClip clip, EditorCurveBinding binding)
        {
            if (clip.m_EditorCurves != null)
            {
                var path = binding.path ?? "";
                foreach (var c in clip.m_EditorCurves)
                    if (c.Binding.Path == path && c.Binding.Attribute == binding.propertyName && c.Binding.Matches(binding.type))
                        return c;
            }
            return clip.CurveAsImported(binding.path ?? "", binding.propertyName, binding.type);
        }

        /// <summary>A copy of the curve a binding names (by its serialized property name), else null.</summary>
        public static AnimationCurve GetEditorCurve(AnimationClip clip, EditorCurveBinding binding)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            var c = Find(clip, binding);
            if (c == null) return null;
            if (c.Curve != null) return new AnimationCurve(c.Curve.keys) { preWrapMode = c.Curve.preWrapMode, postWrapMode = c.Curve.postWrapMode };
            return null;
        }

        /// <summary>Sets a binding's curve (a null curve removes it), one component at a time, also of
        /// a Transform vector property (matches the Unity 6000.0.71f1 Editor, tests/projects/editorui
        /// ClipLength.*SetEditorCurve*, tests/projects/animrest <c>modebindings.*</c>).</summary>
        public static void SetEditorCurve(AnimationClip clip, EditorCurveBinding binding, AnimationCurve curve)
        {
            SetEditorCurveNoSync(clip, binding, curve);
        }

        internal static void SetEditorCurveNoSync(AnimationClip clip, EditorCurveBinding binding, AnimationCurve curve)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            clip.RestoreImportedCurves();
            clip.m_EditorCurves ??= SortedRuntimeCurves(clip);
            var path = binding.path ?? "";
            clip.m_Curves.RemoveAll(c => c.Binding.Path == path && c.Binding.Attribute == binding.propertyName && c.Binding.Matches(binding.type));
            int existingEditor = clip.m_EditorCurves.FindIndex(c => c.Binding.Path == path && c.Binding.Attribute == binding.propertyName && c.Binding.Matches(binding.type));
            if (curve != null)
            {
                var copy = new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
                // Its file's flags: 1 for a discrete curve, else 16 (tests/projects/scenesave clip-assets
                // Editor.anim).
                var entry = new ClipCurve
                {
                    Binding = CurveBinding.ForClipCurve(path, binding.propertyName, binding.type),
                    Curve = copy,
                    Flags = binding.isDiscreteCurve ? 1 : 16,
                };
                clip.m_Curves.Add(entry);
                if (existingEditor >= 0) clip.m_EditorCurves[existingEditor] = entry;
                else clip.m_EditorCurves.Add(entry);
            }
            else if (existingEditor >= 0)
            {
                clip.m_EditorCurves.RemoveAt(existingEditor);
                if (binding.type == typeof(Transform) && !string.IsNullOrEmpty(binding.propertyName))
                {
                    int dot = binding.propertyName.LastIndexOf('.');
                    var head = dot > 0 ? binding.propertyName.Substring(0, dot) : null;
                    if (head != null && TransformKind(head) >= 0)
                    {
                        var prefix = head + ".";
                        bool anyLeft = false;
                        foreach (var c in clip.m_EditorCurves)
                            if (c.Binding.Path == path && c.Binding.Matches(typeof(Transform))
                                && c.Binding.Attribute != null && c.Binding.Attribute.StartsWith(prefix, StringComparison.Ordinal))
                            { anyLeft = true; break; }
                        if (!anyLeft)
                            clip.m_Curves.RemoveAll(c => c.Binding.Path == path && c.Binding.Matches(typeof(Transform))
                                && c.Binding.Attribute != null && c.Binding.Attribute.StartsWith(prefix, StringComparison.Ordinal));
                    }
                }
            }
            clip.m_BindingsBuilt = true;
            clip.Compile();
            clip.m_EditorCurvesSynced = true;
            clip.m_Version++;
            CurveWasModified(clip, binding, curve != null ? CurveModifiedType.CurveModified : CurveModifiedType.CurveDeleted);
        }

        /// <summary>Sets each curve (calling <see cref="onCurveWasModified"/> for each), then calls
        /// it once for the clip.</summary>
        public static void SetEditorCurves(AnimationClip clip, EditorCurveBinding[] bindings, AnimationCurve[] curves)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings), "bindings must be non-null");
            if (curves == null) throw new ArgumentNullException(nameof(curves), "curves must be non-null");
            if (bindings.Length != curves.Length) throw new InvalidOperationException("bindings and curves must be of equal length");
            for (int i = 0; i < bindings.Length; i++) SetEditorCurveNoSync(clip, bindings[i], curves[i]);
            CurveWasModified(clip, default, CurveModifiedType.ClipModified);
        }

        /// <summary>The path of a transform below a root ("" for the root itself).</summary>
        public static string CalculateTransformPath(Transform targetTransform, Transform root)
        {
            var names = new List<string>();
            for (var t = targetTransform; t != null && t != root; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        /// <summary>The object a binding animates below a root: the component, or the GameObject.</summary>
        public static Object GetAnimatedObject(GameObject root, EditorCurveBinding binding)
        {
            if (root == null) return null;
            var t = string.IsNullOrEmpty(binding.path) ? root.transform : root.transform.Find(binding.path);
            if (t == null) return null;
            if (binding.type == typeof(GameObject)) return t.gameObject;
            return binding.type == null ? null : t.gameObject.FindComponent(binding.type);
        }

        /// <summary>A binding's current value on the objects below a root.</summary>
        public static bool GetFloatValue(GameObject root, EditorCurveBinding binding, out float data)
        {
            data = 0f;
            var target = GetAnimatedObject(root, binding);
            if (target == null) return false;
            var value = SerializedValue(target, binding.propertyName);
            if (value == null) return false;
            data = value.Value;
            return true;
        }

        internal static float? SerializedValue(Object target, string propertyName)
        {
            if (target is Transform t)
            {
                Vector3? v = null;
                int dot = propertyName.LastIndexOf('.');
                var head = dot < 0 ? propertyName : propertyName.Substring(0, dot);
                var axis = dot < 0 ? "" : propertyName.Substring(dot + 1);
                switch (head)
                {
                    case "m_LocalPosition": v = t.localPosition; break;
                    case "m_LocalScale": v = t.localScale; break;
                    case "localEulerAnglesRaw": v = t.localEulerAngles; break;
                    case "m_LocalRotation":
                    {
                        var q = t.localRotation;
                        return axis switch { "x" => q.x, "y" => q.y, "z" => q.z, "w" => q.w, _ => (float?)null };
                    }
                }
                if (v == null) return null;
                return axis switch { "x" => v.Value.x, "y" => v.Value.y, "z" => v.Value.z, _ => (float?)null };
            }
            if (target is Renderer renderer && propertyName == "m_Enabled") return renderer.enabled ? 1f : 0f;
            using var so = new SerializedObject(target);
            var p = so.FindProperty(propertyName);
            if (p == null) return AnimatedGroup(target, propertyName, out int member) is PropertyGroup group ? ReadMember(group, member) : (float?)null;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: return p.floatValue;
                case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: return p.intValue;
                case SerializedPropertyType.Boolean: return p.boolValue ? 1f : 0f;
                default: return null;
            }
        }

        /// <summary>An engine component's number as animation binds it (a Light's
        /// <c>m_Color.r</c>), for the properties its SerializedObject does not hold here: the
        /// property's group and the member's place in it, or null (a script, or no such animated
        /// property, which the binder reports once).</summary>
        static PropertyGroup AnimatedGroup(Object target, string propertyName, out int member)
        {
            member = 0;
            if (target is not Component component || target is MonoBehaviour || component.m_GameObject == null) return null;
            var bindings = new AnimationBindings(component.m_GameObject, curvesOnly: true);
            int channel = bindings.BindChannel(CurveBinding.ForType("", propertyName, target.GetType()), objectValued: false);
            if (channel < 0) return null;
            var group = bindings.FloatOwner[channel];
            if (!ReferenceEquals(group.Target, target)) return null;
            member = channel - group.First;
            return group;
        }

        static float ReadMember(PropertyGroup group, int member)
        {
            var values = new float[group.First + group.Width];
            group.Read(values, group.First);
            return values[group.First + member];
        }

        /// <summary>Sets such a number (see <see cref="AnimatedGroup"/>); false when animation does
        /// not bind the property.</summary>
        internal static bool SetAnimatedValue(Object target, string propertyName, float value)
        {
            if (AnimatedGroup(target, propertyName, out int member) is not PropertyGroup group) return false;
            var values = new float[group.First + group.Width];
            group.Read(values, group.First);
            values[group.First + member] = value;
            group.Write(values, group.First);
            return true;
        }

        /// <summary>The bindings the animation window offers for an object: its Transform's
        /// position, rotation and scale, and its components' animatable fields.</summary>
        public static EditorCurveBinding[] GetAnimatableBindings(GameObject targetObject, GameObject root)
        {
            if (targetObject == null || root == null) return Array.Empty<EditorCurveBinding>();
            var path = CalculateTransformPath(targetObject.transform, root.transform);
            var list = new List<EditorCurveBinding>
            {
                EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"),
            };
            foreach (var head in new[] { "m_LocalPosition", "localEulerAnglesRaw", "m_LocalScale" })
                foreach (var axis in new[] { "x", "y", "z" })
                    list.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), head + "." + axis));
            foreach (var component in targetObject.GetComponents<Component>())
            {
                if (component == null || component is Transform) continue;
                using var so = new SerializedObject(component);
                var it = so.GetIterator();
                for (bool enter = true; it.NextVisible(enter); enter = false)
                {
                    if (it.propertyType == SerializedPropertyType.Float || it.propertyType == SerializedPropertyType.Boolean || it.propertyType == SerializedPropertyType.Integer)
                        list.Add(EditorCurveBinding.FloatCurve(path, component.GetType(), it.propertyPath));
                }
            }
            return list.ToArray();
        }

        public static Type GetEditorCurveValueType(GameObject root, EditorCurveBinding binding)
        {
            if (binding.isPPtrCurve)
            {
                var target = GetAnimatedObject(root, binding);
                return target == null ? null : typeof(Object);
            }
            return GetAnimatedObject(root, binding) != null ? typeof(float) : null;
        }

        public static AnimationClipSettings GetAnimationClipSettings(AnimationClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            return new AnimationClipSettings
            {
                startTime = clip.SettingsStartTime,
                stopTime = clip.SettingsStopTime,
                loopTime = clip.m_LoopTime,
                hasAdditiveReferencePose = clip.m_HasAdditiveReferencePose,
                additiveReferencePoseClip = clip.m_AdditiveReferencePoseClip,
                additiveReferencePoseTime = clip.m_AdditiveReferencePoseTime,
                loopBlend = clip.m_LoopBlend,
                cycleOffset = clip.m_CycleOffset,
                mirror = clip.m_Mirror,
                orientationOffsetY = clip.m_OrientationOffsetY,
                level = clip.m_Level,
                loopBlendOrientation = clip.m_LoopBlendOrientation,
                loopBlendPositionY = clip.m_LoopBlendPositionY,
                loopBlendPositionXZ = clip.m_LoopBlendPositionXZ,
                keepOriginalOrientation = clip.m_KeepOriginalOrientation,
                keepOriginalPositionY = clip.m_KeepOriginalPositionY,
                keepOriginalPositionXZ = clip.m_KeepOriginalPositionXZ,
                heightFromFeet = clip.m_HeightFromFeet,
            };
        }

        public static void SetAnimationClipSettings(AnimationClip clip, AnimationClipSettings srcClipInfo)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (srcClipInfo == null) throw new ArgumentNullException(nameof(srcClipInfo));
            clip.m_BindingsBuilt = true;
            clip.Compile();
            clip.m_HasRange = true;
            clip.m_StartTime = srcClipInfo.startTime;
            clip.m_StopTime = srcClipInfo.stopTime;
            clip.m_LoopTime = srcClipInfo.loopTime;
            clip.m_HasAdditiveReferencePose = srcClipInfo.hasAdditiveReferencePose;
            clip.m_AdditiveReferencePoseClip = srcClipInfo.additiveReferencePoseClip;
            clip.m_AdditiveReferencePoseTime = srcClipInfo.additiveReferencePoseTime;
            clip.m_LoopBlend = srcClipInfo.loopBlend;
            clip.m_CycleOffset = srcClipInfo.cycleOffset;
            clip.m_Mirror = srcClipInfo.mirror;
            clip.m_OrientationOffsetY = srcClipInfo.orientationOffsetY;
            clip.m_Level = srcClipInfo.level;
            clip.m_LoopBlendOrientation = srcClipInfo.loopBlendOrientation;
            clip.m_LoopBlendPositionY = srcClipInfo.loopBlendPositionY;
            clip.m_LoopBlendPositionXZ = srcClipInfo.loopBlendPositionXZ;
            clip.m_KeepOriginalOrientation = srcClipInfo.keepOriginalOrientation;
            clip.m_KeepOriginalPositionY = srcClipInfo.keepOriginalPositionY;
            clip.m_KeepOriginalPositionXZ = srcClipInfo.keepOriginalPositionXZ;
            clip.m_HeightFromFeet = srcClipInfo.heightFromFeet;
            clip.m_Version++;
        }

        /// <summary>A reference clip must animate every curve of the clip; without one the clip
        /// keeps only the time and subtracts its own first frame.</summary>
        public static void SetAdditiveReferencePose(AnimationClip clip, AnimationClip referenceClip, float time)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (referenceClip != null)
            {
                var animated = new HashSet<(string, string, int)>();
                foreach (var c in referenceClip.m_Curves) animated.Add((c.Binding.Path ?? "", c.Binding.Attribute, c.Binding.ClassId));
                foreach (var c in clip.m_Curves)
                {
                    if (animated.Contains((c.Binding.Path ?? "", c.Binding.Attribute, c.Binding.ClassId))) continue;
                    Debug.LogError($"AnimationClip '{referenceClip.name}' is not a valid additive reference clip");
                    return;
                }
            }
            clip.m_HasAdditiveReferencePose = referenceClip != null;
            clip.m_AdditiveReferencePoseClip = referenceClip;
            clip.m_AdditiveReferencePoseTime = time;
            clip.m_Version++;
        }

        public static AnimationEvent[] GetAnimationEvents(AnimationClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            return clip.events;
        }

        public static void SetAnimationEvents(AnimationClip clip, AnimationEvent[] events)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            clip.events = events ?? Array.Empty<AnimationEvent>();
            clip.m_BindingsBuilt = true;
            clip.Compile();
            // The clip changed as a whole; the binding names no curve but has the Object type (measured).
            CurveWasModified(clip, new EditorCurveBinding { type = typeof(Object) }, CurveModifiedType.ClipModified);
        }
    }
}
