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
        static EditorCurveBinding BindingOf(ClipCurve c) =>
            EditorCurveBinding.FloatCurve(c.Binding.Path, c.Binding.ResolveType(), c.Binding.Attribute);

        /// <summary>The float curves' bindings, by path (the clip's order within one).</summary>
        public static EditorCurveBinding[] GetCurveBindings(AnimationClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            return clip.m_Curves.Select((c, i) => (c, i)).OrderBy(p => p.c.Binding.Path, StringComparer.Ordinal).ThenBy(p => p.i)
                .Select(p => BindingOf(p.c)).ToArray();
        }

        public static EditorCurveBinding[] GetObjectReferenceCurveBindings(AnimationClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            return clip.m_ObjectCurves.Select(c => EditorCurveBinding.PPtrCurve(c.Binding.Path, c.Binding.ResolveType(), c.Binding.Attribute)).ToArray();
        }

        static ClipCurve Find(AnimationClip clip, EditorCurveBinding binding) =>
            clip.m_Curves.FirstOrDefault(c => c.Binding.Path == (binding.path ?? "") && c.Binding.Attribute == binding.propertyName && c.Binding.ResolveType() == binding.type);

        /// <summary>A copy of the curve a binding names (by its serialized property name), else null.</summary>
        public static AnimationCurve GetEditorCurve(AnimationClip clip, EditorCurveBinding binding)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            var c = Find(clip, binding);
            if (c == null) return null;
            if (c.Curve != null) return new AnimationCurve(c.Curve.keys) { preWrapMode = c.Curve.preWrapMode, postWrapMode = c.Curve.postWrapMode };
            return null;
        }

        /// <summary>Sets a binding's curve (a null curve removes it).</summary>
        public static void SetEditorCurve(AnimationClip clip, EditorCurveBinding binding, AnimationCurve curve)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            clip.m_Curves.RemoveAll(c => c.Binding.Path == (binding.path ?? "") && c.Binding.Attribute == binding.propertyName && c.Binding.ResolveType() == binding.type);
            if (curve != null)
            {
                var copy = new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
                clip.m_Curves.Add(new ClipCurve { Binding = CurveBinding.ForType(binding.path ?? "", binding.propertyName, binding.type), Curve = copy });
            }
            clip.m_Version++;
        }

        public static void SetEditorCurves(AnimationClip clip, EditorCurveBinding[] bindings, AnimationCurve[] curves)
        {
            if (bindings == null || curves == null || bindings.Length != curves.Length) throw new ArgumentException("bindings and curves must have the same length");
            for (int i = 0; i < bindings.Length; i++) SetEditorCurve(clip, bindings[i], curves[i]);
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
            return binding.type == null ? null : t.GetComponent(binding.type);
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

        static float? SerializedValue(Object target, string propertyName)
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
            using var so = new SerializedObject(target);
            var p = so.FindProperty(propertyName);
            if (p == null) return null;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: return p.floatValue;
                case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: return p.intValue;
                case SerializedPropertyType.Boolean: return p.boolValue ? 1f : 0f;
                default: return null;
            }
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
                startTime = clip.StartTime,
                stopTime = clip.m_HasRange ? clip.m_StopTime : clip.length,
                loopTime = clip.m_LoopTime,
                hasAdditiveReferencePose = clip.m_HasAdditiveReferencePose,
                additiveReferencePoseClip = clip.m_AdditiveReferencePoseClip,
                additiveReferencePoseTime = clip.m_AdditiveReferencePoseTime,
            };
        }

        public static void SetAnimationClipSettings(AnimationClip clip, AnimationClipSettings srcClipInfo)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (srcClipInfo == null) throw new ArgumentNullException(nameof(srcClipInfo));
            clip.m_HasRange = true;
            clip.m_StartTime = srcClipInfo.startTime;
            clip.m_StopTime = srcClipInfo.stopTime;
            clip.m_LoopTime = srcClipInfo.loopTime;
            clip.m_HasAdditiveReferencePose = srcClipInfo.hasAdditiveReferencePose;
            clip.m_AdditiveReferencePoseClip = srcClipInfo.additiveReferencePoseClip;
            clip.m_AdditiveReferencePoseTime = srcClipInfo.additiveReferencePoseTime;
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
        }
    }
}
