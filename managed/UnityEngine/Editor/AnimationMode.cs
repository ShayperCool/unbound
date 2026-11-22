using System;
using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>What starts and stops animation mode.</summary>
    public partial class AnimationModeDriver : ScriptableObject
    {
        public AnimationModeDriver() { }
    }

    /// <summary>
    /// The Editor's animation preview mode (Scripting API 6000.0 AnimationMode: "Stops Animation
    /// mode, reverts all properties that were animated in animation mode"). While a driver keeps
    /// it on, sampling a clip records each property it animates before writing it, as does
    /// AddEditorCurveBinding (with the current value); stopping the last driver writes the recorded values back. Measured in
    /// Unity 6000.0.71f1, tests/projects/animatorapi mode.*: a driver that did not start it stops
    /// nothing; sampling outside the mode throws. A property counts as animated when it was
    /// recorded, and a Transform's vector when one of its components was (a script's or a Light's
    /// struct does not: <c>typed.mode.*.animated</c>, <c>modify.plain.animated</c>,
    /// <c>modify.text.animated</c>). A sampled clip's object curves are recorded but not applied,
    /// whatever their property (<c>typed.mode.*</c>).
    /// </summary>
    public partial class AnimationMode
    {
        public AnimationMode() { }

        static readonly List<Object> s_Drivers = new List<Object>();
        static AnimationModeDriver s_DummyDriver;

        /// <summary>A recorded property and the value stopping writes back.</summary>
        struct Recorded
        {
            internal Object Target;
            internal string Path;
            internal float Value;
            internal bool IsObject;
            internal Object ObjectValue;
            /// <summary>A property modification's value as given: what a text property takes back.</summary>
            internal bool IsText;
            internal string Text;
        }

        static readonly List<Recorded> s_Recorded = new List<Recorded>();

        // The colors the inspector tints properties with (light skin, dark skin), as the Editor's
        // preferences define them.
        static readonly PrefColor s_AnimatedPropertyColor = new PrefColor("Animation/Property Animated", 0.82f, 0.97f, 1f, 1f, 0.54f, 0.85f, 1f, 1f);
        static readonly PrefColor s_RecordedPropertyColor = new PrefColor("Animation/Property Recorded", 1f, 0.6f, 0.6f, 1f, 1f, 0.5f, 0.5f, 1f);
        static readonly PrefColor s_CandidatePropertyColor = new PrefColor("Animation/Property Candidate", 1f, 0.7f, 0.6f, 1f, 1f, 0.67f, 0.43f, 1f);

        public static Color animatedPropertyColor => s_AnimatedPropertyColor.Color;
        public static Color recordedPropertyColor => s_RecordedPropertyColor.Color;
        public static Color candidatePropertyColor => s_CandidatePropertyColor.Color;

        static AnimationModeDriver DummyDriver()
        {
            if (s_DummyDriver == null)
            {
                s_DummyDriver = ScriptableObject.CreateInstance<AnimationModeDriver>();
                s_DummyDriver.name = "DummyDriver";
            }
            return s_DummyDriver;
        }

        public static bool InAnimationMode() => s_Drivers.Count > 0;

        public static bool InAnimationMode(AnimationModeDriver driver) => driver != null && s_Drivers.Contains(driver);

        public static void StartAnimationMode() => StartAnimationMode(DummyDriver());

        public static void StartAnimationMode(AnimationModeDriver driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (!s_Drivers.Contains(driver)) s_Drivers.Add(driver);
        }

        public static void StopAnimationMode() => StopAnimationMode(DummyDriver());

        public static void StopAnimationMode(AnimationModeDriver driver)
        {
            if (driver == null || !s_Drivers.Remove(driver) || s_Drivers.Count > 0) return;
            var transforms = new List<Transform>();
            foreach (var r in s_Recorded)
            {
                if (r.Target == null) continue;
                if (r.IsObject)
                {
                    WriteObject(r.Target, r.Path, r.ObjectValue);
                }
                else if (r.Target is Transform t)
                {
                    if (!transforms.Contains(t)) transforms.Add(t);
                }
                else Write(r.Target, r.Path, r.Value, r.IsText, r.Text);
            }
            foreach (var t in transforms) RestoreTransform(t);
            s_Recorded.Clear();
        }

        [Unbound.EditorFaithful("groups samples for the Editor's own batching: each SampleAnimationClip writes its values at once (tests/projects/animatorapi mode.plainSampled samples without it)")]
        public static void BeginSampling() { }

        [Unbound.EditorFaithful("ends the Editor's sample batch: the values are already written")]
        public static void EndSampling() { }

        public static bool IsPropertyAnimated(Object target, string propertyPath)
        {
            if (target == null || propertyPath == null) return false;
            bool vector = target is Transform;
            foreach (var r in s_Recorded)
                if (ReferenceEquals(r.Target, target) && (r.Path == propertyPath || vector && r.Path.Length > propertyPath.Length
                    && r.Path.StartsWith(propertyPath, StringComparison.Ordinal) && r.Path[propertyPath.Length] == '.'))
                    return true;
            return false;
        }

        public static void SampleAnimationClip(GameObject gameObject, AnimationClip clip, float time)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (!InAnimationMode())
                throw new InvalidOperationException("AnimationMode.SampleAnimationClip may only be called in animation mode.  See AnimationMode.StartAnimationMode.");
            foreach (var curve in clip.CurvesAsImported())
            {
                var binding = EditorCurveBinding.FloatCurve(curve.Binding.Path, curve.Binding.ResolveType(), curve.Binding.Attribute);
                Record(AnimationUtility.GetAnimatedObject(gameObject, binding), binding.propertyName);
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var target = AnimationUtility.GetAnimatedObject(gameObject, binding);
                if (target == null || IsRecorded(target, binding.propertyName)) continue;
                AnimationUtility.GetObjectReferenceValue(gameObject, binding, out var current);
                s_Recorded.Add(new Recorded { Target = target, Path = binding.propertyName, IsObject = true, ObjectValue = current });
            }
            clip.SampleAnimation(gameObject, time);
        }

        public static void AddEditorCurveBinding(GameObject gameObject, EditorCurveBinding binding)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            Record(AnimationUtility.GetAnimatedObject(gameObject, binding), binding.propertyName);
        }

        /// <summary>Registers a property modification to revert when animation mode stops: the
        /// property then takes the modification's value, not the one it had (first registration
        /// for a target/property wins; throws outside animation mode or on null
        /// modification/target). A number, bool or enum takes the value read as a number, 0 when
        /// it is none ("", null, "x", "true", an enum's name); a text property the value itself
        /// ("" for null); an object property the object. A property the target does not have is
        /// recorded all the same. <paramref name="keepPrefabOverride"/> changed nothing on prefab
        /// instances with and without overrides. Matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/animatorapi <c>mode.*</c>, <c>modify.*</c>, tests/projects/animrest
        /// <c>propmod.add.*</c>. Unmeasured: number text with spaces, exponents or a suffix.</summary>
        public static void AddPropertyModification(EditorCurveBinding binding, PropertyModification modification, bool keepPrefabOverride)
        {
            if (!InAnimationMode())
                throw new InvalidOperationException("AnimationMode.AddPropertyModification may only be called in animation mode.  See AnimationMode.StartAnimationMode.");
            if (modification == null || modification.target == null)
                throw new NullReferenceException("(null)");
            var path = modification.propertyPath ?? "";
            if (IsRecorded(modification.target, path)) return;
            if (binding.isPPtrCurve || modification.objectReference != null || IsObjectReferenceProperty(modification.target, path))
            {
                s_Recorded.Add(new Recorded { Target = modification.target, Path = path, IsObject = true, ObjectValue = modification.objectReference });
                return;
            }
            float.TryParse(modification.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number);
            s_Recorded.Add(new Recorded { Target = modification.target, Path = path, Value = number, IsText = true, Text = modification.value });
        }

        static bool IsObjectReferenceProperty(Object target, string path)
        {
            if (target is Renderer && AnimationUtility.TryParseMaterialSlot(path, out _)) return true;
            if (target is SpriteRenderer && path == "m_Sprite") return true;
            if (target is GameObject || target is Transform) return false;
            using var so = new SerializedObject(target);
            var p = so.FindProperty(path);
            return p != null && p.propertyType == SerializedPropertyType.ObjectReference;
        }

        /// <summary>The bindings animation mode records for a clip: non-Transform float curves,
        /// object curves, then expanded Transform rotation (+ Euler hint when authored as Euler),
        /// position, and scale bindings per unique animated path in ordinal order (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/animrest <c>modebindings.mode.*</c>).</summary>
        internal static EditorCurveBinding[] GetCurveBindings(AnimationClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            var result = new List<EditorCurveBinding>();
            var rotPaths = new SortedSet<string>(StringComparer.Ordinal);
            var eulerPaths = new HashSet<string>(StringComparer.Ordinal);
            var posPaths = new SortedSet<string>(StringComparer.Ordinal);
            var scalePaths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type == typeof(Transform) && b.propertyName != null)
                {
                    var p = b.path ?? "";
                    if (b.propertyName.StartsWith("localEulerAngles", StringComparison.Ordinal))
                    {
                        rotPaths.Add(p);
                        eulerPaths.Add(p);
                        continue;
                    }
                    if (b.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal))
                    {
                        rotPaths.Add(p);
                        continue;
                    }
                    if (b.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal))
                    {
                        posPaths.Add(p);
                        continue;
                    }
                    if (b.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal))
                    {
                        scalePaths.Add(p);
                        continue;
                    }
                }
                result.Add(b);
            }
            result.AddRange(AnimationUtility.GetObjectReferenceCurveBindings(clip));
            foreach (var p in rotPaths)
            {
                result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalRotation.x"));
                result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalRotation.y"));
                result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalRotation.z"));
                result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalRotation.w"));
                if (eulerPaths.Contains(p))
                {
                    result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalEulerAnglesHint.x"));
                    result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalEulerAnglesHint.y"));
                    result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalEulerAnglesHint.z"));
                }
            }
            foreach (var p in posPaths)
            {
                result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalPosition.x"));
                result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalPosition.y"));
                result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalPosition.z"));
            }
            foreach (var p in scalePaths)
            {
                result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalScale.x"));
                result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalScale.y"));
                result.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalScale.z"));
            }
            return result.ToArray();
        }

        static void Record(Object target, string path)
        {
            if (target == null || path == null || IsRecorded(target, path)) return;
            if (Read(target, path) is float value) Record(target, path, value);
        }

        static void Record(Object target, string path, float value)
        {
            if (!InAnimationMode() || IsRecorded(target, path)) return;
            s_Recorded.Add(new Recorded { Target = target, Path = path, Value = value });
        }

        static bool IsRecorded(Object target, string path)
        {
            foreach (var r in s_Recorded)
                if (ReferenceEquals(r.Target, target) && r.Path == path) return true;
            return false;
        }

        static float? Read(Object target, string path) =>
            target is GameObject go ? path == "m_IsActive" ? go.activeSelf ? 1f : 0f : null : AnimationUtility.SerializedValue(target, path);

        /// <summary>Writes a recorded value back: a GameObject's active state or (from a
        /// modification's text) its name, else the serialized property.</summary>
        static void Write(Object target, string path, float value, bool isText, string text)
        {
            if (target is GameObject go)
            {
                if (path == "m_IsActive") go.SetActive(value > 0.5f);
                // A GameObject's name takes the modification's text back like any text property
                // (tests/projects/animrest propmod.extra.str.*).
                else if (path == "m_Name" && isText) go.name = text ?? "";
                return;
            }
            if (target is Renderer renderer && path == "m_Enabled")
            {
                renderer.enabled = value != 0f;
                return;
            }
            using var so = new SerializedObject(target);
            var p = so.FindProperty(path);
            if (p == null)
            {
                AnimationUtility.SetAnimatedValue(target, path, value);
                return;
            }
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: p.floatValue = value; break;
                case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: p.intValue = (int)value; break;
                case SerializedPropertyType.Boolean: p.boolValue = value != 0f; break;
                case SerializedPropertyType.String when isText: p.stringValue = text ?? ""; break;
                default: return;
            }
            // The mode's own write, not an edit: a prefab instance gets no override from it
            // (tests/projects/animatorapi modify.*.after; whether it dirties is unmeasured).
            so.ApplyQuietly();
        }

        /// <summary>Writes a recorded object reference back to the target.</summary>
        static void WriteObject(Object target, string path, Object value)
        {
            if (target is Renderer renderer && AnimationUtility.TryParseMaterialSlot(path, out int index))
            {
                var mats = renderer.sharedMaterials;
                if (index >= 0 && index < mats.Length)
                {
                    mats[index] = value as Material;
                    renderer.sharedMaterials = mats;
                }
                return;
            }
            if (target is SpriteRenderer sprite && path == "m_Sprite")
            {
                sprite.sprite = value as Sprite;
                return;
            }
            using var so = new SerializedObject(target);
            var p = so.FindProperty(path);
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference)
            {
                p.objectReferenceValue = value;
                so.ApplyQuietly();
            }
        }

        /// <summary>A transform's recorded components written back together: its position, scale
        /// and rotation (from the quaternion's components when they were recorded, else from the
        /// Euler angles').</summary>
        static void RestoreTransform(Transform t)
        {
            Vector3 position = t.localPosition, scale = t.localScale, euler = t.localEulerAngles;
            var rotation = t.localRotation;
            bool hasPosition = false, hasScale = false, hasRotation = false, hasEuler = false;
            foreach (var r in s_Recorded)
            {
                if (!ReferenceEquals(r.Target, t)) continue;
                int dot = r.Path.LastIndexOf('.');
                if (dot < 0 || dot + 1 >= r.Path.Length) continue;
                int axis = r.Path[dot + 1] switch { 'x' => 0, 'y' => 1, 'z' => 2, 'w' => 3, _ => -1 };
                if (axis < 0) continue;
                switch (r.Path.Substring(0, dot))
                {
                    case "m_LocalPosition" when axis < 3: position[axis] = r.Value; hasPosition = true; break;
                    case "m_LocalScale" when axis < 3: scale[axis] = r.Value; hasScale = true; break;
                    case "localEulerAnglesRaw" when axis < 3: euler[axis] = r.Value; hasEuler = true; break;
                    case "m_LocalRotation": rotation[axis] = r.Value; hasRotation = true; break;
                }
            }
            if (hasPosition) t.localPosition = position;
            if (hasScale) t.localScale = scale;
            if (hasRotation) t.localRotation = rotation;
            else if (hasEuler) t.localEulerAngles = euler;
        }
    }

    public partial class AnimationUtility
    {
        [Obsolete("Use AnimationMode.InAnimationMode instead.")]
        public static bool InAnimationMode() => AnimationMode.InAnimationMode();

        [Obsolete("Use AnimationMode.StartAnimationmode instead.")]
        public static void StartAnimationMode(Object[] objects)
        {
            Debug.LogWarning("AnimationUtility.StartAnimationMode is deprecated. Use AnimationMode.StartAnimationMode with the new APIs. The objects passed to this function will no longer be reverted automatically. See AnimationMode.AddPropertyModification");
            AnimationMode.StartAnimationMode();
        }

        [Obsolete("Use AnimationMode.StopAnimationMode instead.")]
        public static void StopAnimationMode() => AnimationMode.StopAnimationMode();
    }
}
