using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.Animations
{
    public partial struct CurveFilterOptions
    {
        public float positionError;
        public float rotationError;
        public float scaleError;
        public float floatError;
        public bool keyframeReduction;
        public bool unrollRotation;
    }

    /// <summary>
    /// Records the properties of a hierarchy (the Recorder editor's animation recorder) and saves
    /// them as a clip, as the Unity 6000.0.71f1 Editor does (tests/projects/animrecorder):
    /// <list type="bullet">
    /// <item>bindings are those <see cref="AnimationUtility.GetAnimatableBindings"/> offers (a
    /// component's own for <see cref="BindComponent"/>, an object's for <see cref="BindAll"/>,
    /// nothing outside the root's hierarchy), each once, never an Animator's enabled state; number
    /// bindings come first in <see cref="GetBindings"/>, object references after;</item>
    /// <item>the first snapshot is at time zero, each later one <c>dt</c> on; a snapshot keys
    /// every number binding at that time (not again at the same time) and appends every object
    /// reference;</item>
    /// <item><see cref="SaveToClip(AnimationClip, float, CurveFilterOptions)"/> clears the clip,
    /// takes the frame rate, unrolls bound Euler angles (with <c>unrollRotation</c>), gives each
    /// number curve's keys ClampedAuto tangents (Constant and broken for integers and flags),
    /// samples every curve on the clip's frames from its first key's nearest frame to its last
    /// key's frame (object references take the nearest key, the earlier on a tie), sets the
    /// tangents again and, with <c>keyframeReduction</c>, reduces each number curve with the
    /// <c>floatError</c> percentage (zero for discrete ones; the position, rotation and scale
    /// errors play no part) and drops repeated object references. The unrolling and reduction
    /// happen only from a rate of one frame a second on (the clip's length is one second while
    /// its curves are not synced).</item>
    /// </list>
    /// </summary>
    public partial class GameObjectRecorder : Object
    {
        static readonly CurveFilterOptions k_DefaultCurveFilterOptions = new CurveFilterOptions
        {
            unrollRotation = true,
            keyframeReduction = true,
            positionError = 0.5f,
            rotationError = 0.5f,
            scaleError = 0.5f,
            floatError = 0.5f,
        };

        /// <summary>The Euler angle properties whose x binding unrolls the three angles.</summary>
        static readonly string[] k_EulerX = { "localEulerAngles.x", "localEulerAnglesRaw.x", "localEulerAnglesBaked.x" };

        readonly GameObject m_Root;
        readonly List<EditorCurveBinding> m_FloatBindings = new List<EditorCurveBinding>();
        /// <summary>Each number binding's keys in time order.</summary>
        readonly List<List<Keyframe>> m_FloatKeys = new List<List<Keyframe>>();
        /// <summary>The object each number binding reads, found at the first snapshot that needs it
        /// and again once it is destroyed.</summary>
        readonly List<Object> m_FloatTargets = new List<Object>();
        readonly List<EditorCurveBinding> m_ObjectBindings = new List<EditorCurveBinding>();
        readonly List<List<ObjectReferenceKeyframe>> m_ObjectCurves = new List<List<ObjectReferenceKeyframe>>();
        bool m_Idle = true;
        float m_Time;

        public GameObjectRecorder(GameObject root)
        {
            if (!Compat.GameObjectRecorderMeasured) Stubs.NotImplemented("UnityEditor.Animations.GameObjectRecorder.ctor");
            if (root == null) throw new ArgumentNullException(nameof(root));
            m_Root = root;
        }

        public GameObject root { get { ThrowIfUncreated(); return m_Root; } }

        public float currentTime { get { ThrowIfUncreated(); return m_Time; } }

        public bool isRecording { get { ThrowIfUncreated(); return !m_Idle; } }

        public void BindComponentsOfType<T>(GameObject target, bool recursive) where T : Component =>
            BindComponentsOfType(target, typeof(T), recursive);

        public void BindComponentsOfType(GameObject target, Type componentType, bool recursive)
        {
            Component[] components = recursive ? target.GetComponentsInChildren(componentType, true) : target.GetComponents(componentType);
            for (int i = 0; i < components.Length; ++i)
                BindComponent(components[i]);
        }

        public void Bind(EditorCurveBinding binding)
        {
            if (!binding.type.IsSubclassOf(typeof(Object)))
                throw new InvalidCastException("Binding type should derive from Unity type.");
            AddBinding(binding);
        }

        /// <summary>Binds what an object offers, then (<paramref name="recursive"/>) its
        /// children's, depth first; a null target is the root.</summary>
        public void BindAll(GameObject target, bool recursive)
        {
            if (m_Root == null) return;
            if (target == null) target = m_Root;
            var list = new List<EditorCurveBinding>();
            AnimationUtility.AddAnimatableBindings(target, m_Root, list);
            foreach (var binding in list) AddBinding(binding);
            if (!recursive) return;
            foreach (Transform child in target.transform) BindAll(child.gameObject, true);
        }

        public void BindComponent(Component component)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (m_Root == null || !AnimationUtility.IsBelow(component.transform, m_Root.transform)) return;
            var list = new List<EditorCurveBinding>();
            AnimationUtility.AddComponentBindings(component, AnimationUtility.CalculateTransformPath(component.transform, m_Root.transform), list);
            foreach (var binding in list) AddBinding(binding);
        }

        void AddBinding(EditorCurveBinding binding)
        {
            if (binding.type == typeof(Animator) && binding.propertyName == "m_Enabled") return;
            var bindings = binding.isPPtrCurve ? m_ObjectBindings : m_FloatBindings;
            if (bindings.Contains(binding)) return;
            bindings.Add(binding);
            if (binding.isPPtrCurve) m_ObjectCurves.Add(new List<ObjectReferenceKeyframe>());
            else
            {
                m_FloatKeys.Add(new List<Keyframe>());
                m_FloatTargets.Add(null);
            }
        }

        public EditorCurveBinding[] GetBindings()
        {
            var result = new EditorCurveBinding[m_FloatBindings.Count + m_ObjectBindings.Count];
            m_FloatBindings.CopyTo(result);
            m_ObjectBindings.CopyTo(result, m_FloatBindings.Count);
            return result;
        }

        public void TakeSnapshot(float dt)
        {
            if (m_Root == null || (m_FloatBindings.Count == 0 && m_ObjectBindings.Count == 0)) return;
            if (m_Idle)
            {
                m_Idle = false;
                m_Time = 0f;
            }
            else
                m_Time += dt;
            for (int i = 0; i < m_FloatBindings.Count; i++)
            {
                var binding = m_FloatBindings[i];
                var target = m_FloatTargets[i];
                if (target == null) m_FloatTargets[i] = target = AnimationUtility.GetAnimatedObject(m_Root, binding);
                float value = 0f;
                if (target == null || !AnimationUtility.ReadFloatValue(m_Root, target, binding, out value))
                    Stubs.Ignored("GameObjectRecorder.TakeSnapshot unreadable binding", "keys 0 for a bound property it cannot read (the Editor keys an undefined value)");
                AddKey(m_FloatKeys[i], new Keyframe(m_Time, value) { inWeight = 1f / 3f, outWeight = 1f / 3f });
            }
            for (int i = 0; i < m_ObjectBindings.Count; i++)
            {
                AnimationUtility.GetObjectReferenceValue(m_Root, m_ObjectBindings[i], out Object value);
                m_ObjectCurves[i].Add(new ObjectReferenceKeyframe { time = m_Time, value = value });
            }
        }

        /// <summary>Adds a key in time order, as AnimationCurve.AddKey does (none at a time already
        /// keyed); snapshots usually only move on, so the key goes at the end.</summary>
        static void AddKey(List<Keyframe> keys, Keyframe key)
        {
            int n = keys.Count;
            if (n == 0 || keys[n - 1].time < key.time)
            {
                keys.Add(key);
                return;
            }
            int low = 0, high = n;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (keys[middle].time < key.time) low = middle + 1;
                else high = middle;
            }
            if (keys[low].time == key.time) return;
            keys.Insert(low, key);
        }

        public void SaveToClip(AnimationClip clip) => SaveToClip(clip, 60.0f);

        public void SaveToClip(AnimationClip clip, float fps)
        {
            if (fps <= Mathf.Epsilon)
                throw new ArgumentException("FPS can't be 0.0 or less");
            if (!isRecording)
                throw new InvalidOperationException("Cannot save to clip as there is nothing to save. The method TakeSnapshot() has not been called.");
            SaveToClipInternal(clip, fps, k_DefaultCurveFilterOptions);
            AnimationUtility.onCurveWasModified?.Invoke(clip, new EditorCurveBinding(), AnimationUtility.CurveModifiedType.ClipModified);
        }

        public void SaveToClip(AnimationClip clip, float fps, CurveFilterOptions filterOptions)
        {
            if (fps <= Mathf.Epsilon)
                throw new ArgumentException("FPS can't be 0.0 or less");
            if (filterOptions.keyframeReduction)
            {
                if (filterOptions.positionError < 0 || filterOptions.rotationError < 0 || filterOptions.scaleError < 0 || filterOptions.floatError < 0)
                    throw new ArgumentException("Allowed errors for keyframe reduction cannot be negative.");
            }
            if (!isRecording)
                throw new InvalidOperationException("Cannot save to clip as there is nothing to save. The method TakeSnapshot() has not been called.");
            SaveToClipInternal(clip, fps, filterOptions);
            AnimationUtility.onCurveWasModified?.Invoke(clip, new EditorCurveBinding(), AnimationUtility.CurveModifiedType.ClipModified);
        }

        /// <summary>Clears the bindings and the recording; <see cref="currentTime"/> keeps its value
        /// until the next snapshot.</summary>
        public void ResetRecording()
        {
            m_FloatBindings.Clear();
            m_FloatKeys.Clear();
            m_FloatTargets.Clear();
            m_ObjectBindings.Clear();
            m_ObjectCurves.Clear();
            m_Idle = true;
        }

        /// <summary>Creates no recorder: the Editor's object has no engine side, so its properties
        /// throw NullReferenceException (tests/projects/defaultstate).</summary>
        [Obsolete("The GameObjectRecorder constructor now takes a root GameObject", true)]
        public GameObjectRecorder()
        {
            if (Compat.UncreatedObjectWrappersMeasured && !ObjectConstruction.TakeBacking(this)) MarkUncreated();
        }

        [Obsolete("BindComponent() using a System::Type is obsolete, use BindComponentsOfType() instead (UnityUpgradable) -> BindComponentsOfType(*)", true)]
        [Unbound.EditorFaithful("an obsolete member that does nothing, as in the Editor")]
        public void BindComponent(GameObject target, Type componentType, bool recursive) { }

        [Obsolete("\"BindComponent<T>() where T : Component\" is obsolete, use BindComponentsOfType<T>() instead (UnityUpgradable) -> BindComponentsOfType<T>(*)", true)]
        [Unbound.EditorFaithful("an obsolete member that does nothing, as in the Editor")]
        public void BindComponent<T>(GameObject target, bool recursive) where T : Component { }

        // ------------------------------------------------------------------ saving

        void SaveToClipInternal(AnimationClip clip, float fps, CurveFilterOptions options)
        {
            if (m_Root == null || clip == null) return;
            clip.ClearCurves();
            clip.frameRate = fps;
            float rate = clip.frameRate;
            // The cleared clip's length (one second, that of a clip without curves) against a frame;
            // measured on clips without a stored range (a clip with one keeps its range's length
            // here, unmeasured).
            bool longEnough = clip.length >= 1f / fps;
            var curves = new AnimationCurve[m_FloatKeys.Count];
            for (int i = 0; i < curves.Length; i++) curves[i] = new AnimationCurve(m_FloatKeys[i].ToArray());
            if (options.unrollRotation && longEnough) UnrollEuler(curves);
            var floats = new bool[curves.Length];
            for (int i = 0; i < curves.Length; i++)
            {
                floats[i] = AnimationUtility.GetEditorCurveValueType(m_Root, m_FloatBindings[i]) == typeof(float);
                SetTangents(curves[i], floats[i]);
                curves[i] = Sample(curves[i], rate);
            }
            var objects = new List<ObjectReferenceKeyframe>[m_ObjectCurves.Count];
            for (int i = 0; i < objects.Length; i++) objects[i] = Sample(m_ObjectCurves[i], rate);
            for (int i = 0; i < curves.Length; i++) SetTangents(curves[i], floats[i]);
            if (options.keyframeReduction && longEnough)
            {
                var kept = new List<int>();
                for (int i = 0; i < curves.Length; i++)
                {
                    var binding = m_FloatBindings[i];
                    bool discrete = AnimationUtility.IsDiscreteField(binding.type, binding.propertyName);
                    curves[i] = Reduce(curves[i], 1f / rate, discrete ? 0f : options.floatError / 100f, kept);
                }
                for (int i = 0; i < objects.Length; i++) objects[i] = WithoutRepeats(objects[i]);
            }
            for (int i = 0; i < curves.Length; i++) AnimationUtility.SetEditorCurveNoSync(clip, m_FloatBindings[i], curves[i], notify: false);
            for (int i = 0; i < objects.Length; i++) AnimationUtility.SetObjectReferenceCurveNoSync(clip, m_ObjectBindings[i], objects[i].ToArray(), notify: false);
            clip.SyncLength();
        }

        /// <summary>Each bound Euler angle triple (a Transform's, its x angle bound) made continuous:
        /// the first key's angles within a turn (the remainder's sign that of the angle), each later
        /// key's the angles of its rotation nearest the key before (rotation order ZXY).</summary>
        void UnrollEuler(AnimationCurve[] curves)
        {
            for (int i = 0; i < m_FloatBindings.Count; i++)
            {
                var binding = m_FloatBindings[i];
                if (binding.type == null || !typeof(Transform).IsAssignableFrom(binding.type) || Array.IndexOf(k_EulerX, binding.propertyName) < 0) continue;
                var head = binding.propertyName.Substring(0, binding.propertyName.Length - 2);
                int y = FindFloat(binding, head + ".y"), z = FindFloat(binding, head + ".z");
                if (y < 0 || z < 0) continue;
                AnimationCurve cx = curves[i], cy = curves[y], cz = curves[z];
                int n = Math.Min(cx.length, Math.Min(cy.length, cz.length));
                if (n == 0) continue;
                var hint = new Vector3(cx[0].value % 360f, cy[0].value % 360f, cz[0].value % 360f);
                SetValue(cx, 0, hint.x);
                SetValue(cy, 0, hint.y);
                SetValue(cz, 0, hint.z);
                for (int k = 1; k < n; k++)
                {
                    hint = ClosestEulerAngles.Nearest(new Vector3(cx[k].value, cy[k].value, cz[k].value), hint, RotationOrder.OrderZXY);
                    SetValue(cx, k, hint.x);
                    SetValue(cy, k, hint.y);
                    SetValue(cz, k, hint.z);
                }
            }

            static void SetValue(AnimationCurve curve, int index, float value)
            {
                var key = curve[index];
                key.value = value;
                curve.ReplaceKey(index, key);
            }
        }

        int FindFloat(EditorCurveBinding like, string propertyName)
        {
            for (int i = 0; i < m_FloatBindings.Count; i++)
            {
                var b = m_FloatBindings[i];
                if (b.propertyName == propertyName && (b.path ?? "") == (like.path ?? "") && b.type == like.type) return i;
            }
            return -1;
        }

        /// <summary>Tangent modes on every key, ClampedAuto for a float property, else Constant and
        /// broken, and the tangents they give.</summary>
        static void SetTangents(AnimationCurve curve, bool isFloat)
        {
            var mode = isFloat ? AnimationUtility.TangentMode.ClampedAuto : AnimationUtility.TangentMode.Constant;
            int bits = (isFloat ? 0 : 1) | ((int)mode << 1) | ((int)mode << 5);
            for (int i = 0; i < curve.length; i++)
            {
                var key = curve[i];
                key.tangentModeInternal = bits;
                curve.ReplaceKey(i, key);
            }
            AnimationUtility.UpdateTangentsFromMode(curve);
        }

        /// <summary>The frames a curve covers on a clip of <paramref name="rate"/> frames a second:
        /// from its first key's nearest frame to its last key's frame (a millionth of a second
        /// earlier counts), at least the first.</summary>
        static (float Start, int Last) Frames(float first, float last, float rate)
        {
            float step = 1f / rate;
            float start = MathF.Floor((first + step * 0.5f) * rate) / rate;
            float end = MathF.Floor((last + 1e-6f) * rate) / rate;
            return (start, (int)(MathF.Max(end - start, 0f) * rate));
        }

        /// <summary>The curve sampled on the clip's frames, unweighted keys without tangents.</summary>
        static AnimationCurve Sample(AnimationCurve curve, float rate)
        {
            if (curve.length == 0) return curve;
            var (start, last) = Frames(curve[0].time, curve[curve.length - 1].time, rate);
            float step = 1f / rate;
            var keys = new Keyframe[last + 1];
            for (int i = 0; i <= last; i++)
            {
                float time = start + (float)i * step;
                keys[i] = new Keyframe(time, curve.Evaluate(time)) { inWeight = 1f / 3f, outWeight = 1f / 3f };
            }
            return new AnimationCurve(keys);
        }

        const float k_SmallestNormal = 1.17549435e-38f;

        /// <summary>Object references on the clip's frames: each frame takes the nearest key, the
        /// earlier on a tie.</summary>
        static List<ObjectReferenceKeyframe> Sample(List<ObjectReferenceKeyframe> keys, float rate)
        {
            if (keys.Count == 0) return new List<ObjectReferenceKeyframe>();
            // The Editor starts the latest time at the smallest normal float, not below zero.
            float first = float.MaxValue, lastTime = k_SmallestNormal;
            foreach (var key in keys)
            {
                if (key.time < first) first = key.time;
                if (lastTime < key.time) lastTime = key.time;
            }
            var (start, last) = Frames(first, lastTime, rate);
            float step = 1f / rate;
            var result = new List<ObjectReferenceKeyframe>(last + 1);
            for (int i = 0; i <= last; i++)
            {
                float time = start + (float)i * step;
                int k = 0;
                while (k < keys.Count && keys[k].time < time) k++;
                int nearest = k > 0 ? k - 1 : 0;
                if (nearest + 1 < keys.Count && time - keys[nearest].time > keys[nearest + 1].time - time) nearest++;
                result.Add(new ObjectReferenceKeyframe { time = time, value = keys[nearest].value });
            }
            return result;
        }

        /// <summary>The keys <see cref="KeyframeReduction"/> keeps (error a fraction); a flat curve
        /// keeps its first and last keys with the first value and zero slopes.</summary>
        static AnimationCurve Reduce(AnimationCurve curve, float step, float error, List<int> kept)
        {
            var keys = curve.keys;
            bool flat = KeyframeReduction.Reduce(new CurveSource(curve), keys, step, error, kept);
            var reduced = new Keyframe[kept.Count];
            for (int k = 0; k < reduced.Length; k++)
            {
                var key = keys[kept[k]];
                if (flat)
                {
                    key.value = keys[0].value;
                    key.inTangent = key.outTangent = 0f;
                }
                reduced[k] = key;
            }
            return new AnimationCurve(reduced);
        }

        readonly struct CurveSource : KeyframeReduction.ISource
        {
            readonly AnimationCurve m_Curve;

            internal CurveSource(AnimationCurve curve) => m_Curve = curve;

            public float Evaluate(float time) => m_Curve.Evaluate(time);

            public float Interpolate(in Keyframe from, in Keyframe to, float time) => AnimationCurve.EvaluateSegment(from, to, time);
        }

        /// <summary>A curve of three keys or more without keys that repeat the one before; the
        /// last key stays.</summary>
        static List<ObjectReferenceKeyframe> WithoutRepeats(List<ObjectReferenceKeyframe> keys)
        {
            if (keys.Count < 3) return keys;
            var result = new List<ObjectReferenceKeyframe> { keys[0] };
            var previous = keys[0].value;
            for (int i = 1; i < keys.Count - 1; i++)
            {
                if (ReferenceEquals(keys[i].value, previous)) continue;
                result.Add(keys[i]);
                previous = keys[i].value;
            }
            result.Add(keys[keys.Count - 1]);
            return result;
        }
    }
}
