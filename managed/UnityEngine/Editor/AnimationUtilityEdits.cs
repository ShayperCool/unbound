using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>The curve callbacks, object curves, tangent modes, clip lists, values and
    /// bindings of the Editor's AnimationUtility (Unity 6000.0.71f1, tests/projects/animatorapi
    /// <c>util.*</c>, <c>stream.*</c>, <c>clip.*</c>, <c>extra.*</c>).</summary>
    public partial class AnimationUtility
    {
        public enum CurveModifiedType
        {
            CurveDeleted = 0,
            CurveModified = 1,
            ClipModified = 2,
        }

        public enum TangentMode
        {
            Free = 0,
            Auto = 1,
            Linear = 2,
            Constant = 3,
            ClampedAuto = 4,
        }

        public delegate void OnCurveWasModified(AnimationClip clip, EditorCurveBinding binding, CurveModifiedType type);

        /// <summary>Called for each curve a clip edit through AnimationUtility sets or removes,
        /// once more with an empty binding after a batch edit, and with an empty binding when a
        /// clip wakes (made, loaded or instantiated). The clip's runtime API (SetCurve, ClearCurves,
        /// frameRate) and its settings do not call it.</summary>
        public static OnCurveWasModified onCurveWasModified;

        internal static void CurveWasModified(AnimationClip clip, EditorCurveBinding binding, CurveModifiedType type) =>
            onCurveWasModified?.Invoke(clip, binding, type);

        /// <summary>A clip woke: made, loaded or instantiated.</summary>
        internal static void ClipAwake(AnimationClip clip) => onCurveWasModified?.Invoke(clip, default, CurveModifiedType.ClipModified);

        public static ObjectReferenceKeyframe[] GetObjectReferenceCurve(AnimationClip clip, EditorCurveBinding binding)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            var curve = FindObjectCurve(clip, binding);
            if (curve == null) return null;
            var keys = new ObjectReferenceKeyframe[curve.Times.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = new ObjectReferenceKeyframe { time = curve.Times[i], value = curve.Values[i] };
            return keys;
        }

        static ClipObjectCurve FindObjectCurve(AnimationClip clip, EditorCurveBinding binding)
        {
            foreach (var c in clip.m_ObjectCurves)
                if (c.Binding.Path == (binding.path ?? "") && c.Binding.Attribute == binding.propertyName && c.Binding.Matches(binding.type)) return c;
            return null;
        }

        public static void SetObjectReferenceCurve(AnimationClip clip, EditorCurveBinding binding, ObjectReferenceKeyframe[] keyframes) =>
            SetObjectReferenceCurveNoSync(clip, binding, keyframes);

        public static void SetObjectReferenceCurves(AnimationClip clip, EditorCurveBinding[] bindings, ObjectReferenceKeyframe[][] keyframes)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings), "bindings must be non-null");
            if (keyframes == null) throw new ArgumentNullException(nameof(keyframes), "keyframes must be non-null");
            if (bindings.Length != keyframes.Length) throw new InvalidOperationException("bindings and keyframes must be of equal length");
            for (int i = 0; i < bindings.Length; i++) SetObjectReferenceCurveNoSync(clip, bindings[i], keyframes[i]);
            CurveWasModified(clip, default, CurveModifiedType.ClipModified);
        }

        internal static void SetObjectReferenceCurveNoSync(AnimationClip clip, EditorCurveBinding binding, ObjectReferenceKeyframe[] keyframes)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            clip.m_ObjectCurves.RemoveAll(c => c.Binding.Path == (binding.path ?? "") && c.Binding.Attribute == binding.propertyName && c.Binding.Matches(binding.type));
            clip.m_BindingsBuilt = true;
            clip.Compile();
            if (keyframes != null)
            {
                var sorted = (ObjectReferenceKeyframe[])keyframes.Clone();
                Array.Sort(sorted, (a, b) => a.time.CompareTo(b.time));
                var times = new float[sorted.Length];
                var values = new Object[sorted.Length];
                for (int i = 0; i < sorted.Length; i++)
                {
                    times[i] = sorted[i].time;
                    values[i] = sorted[i].value;
                }
                clip.m_ObjectCurves.Add(new ClipObjectCurve { Binding = CurveBinding.ForClipCurve(binding.path ?? "", binding.propertyName, binding.type), Times = times, Values = values });
            }
            clip.m_Version++;
            CurveWasModified(clip, binding, keyframes != null ? CurveModifiedType.CurveModified : CurveModifiedType.CurveDeleted);
        }

        // ------------------------------------------------------------------ tangent modes

        // A key's tangent modes as its tangentMode bits hold them: bit 0 broken, then the left
        // mode in bits 1-4 and the right mode in bits 5-8 (measured: Linear on the left of a
        // key reads 5, Constant on the right 97, tests/projects/animatorapi extra.tangents.rawModes).
        const int k_BrokenMask = 1, k_LeftShift = 1, k_RightShift = 5, k_ModeMask = 0xF;

        static int CheckKey(AnimationCurve curve, int index)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            if (index < 0 || index >= curve.length)
                throw new IndexOutOfRangeException($"index {index} must be in the range of 0 to {curve.length - 1}.");
            return index;
        }

        public static TangentMode GetKeyLeftTangentMode(AnimationCurve curve, int index) =>
            (TangentMode)((curve[CheckKey(curve, index)].tangentModeInternal >> k_LeftShift) & k_ModeMask);

        public static TangentMode GetKeyRightTangentMode(AnimationCurve curve, int index) =>
            (TangentMode)((curve[CheckKey(curve, index)].tangentModeInternal >> k_RightShift) & k_ModeMask);

        public static bool GetKeyBroken(AnimationCurve curve, int index) => (curve[CheckKey(curve, index)].tangentModeInternal & k_BrokenMask) != 0;

        /// <summary>Sets a key's left mode, marks it broken and updates the tangents of it and its neighbours.</summary>
        public static void SetKeyLeftTangentMode(AnimationCurve curve, int index, TangentMode tangentMode) =>
            SetMode(curve, index, k_LeftShift, tangentMode);

        public static void SetKeyRightTangentMode(AnimationCurve curve, int index, TangentMode tangentMode) =>
            SetMode(curve, index, k_RightShift, tangentMode);

        static void SetMode(AnimationCurve curve, int index, int shift, TangentMode mode)
        {
            var key = curve[CheckKey(curve, index)];
            key.tangentModeInternal = (key.tangentModeInternal & ~(k_ModeMask << shift)) | ((int)mode << shift) | k_BrokenMask;
            curve.ReplaceKey(index, key);
            UpdateTangentsAround(curve, index);
        }

        /// <summary>Only the broken bit changes; the tangents stay (measured).</summary>
        public static void SetKeyBroken(AnimationCurve curve, int index, bool broken)
        {
            var key = curve[CheckKey(curve, index)];
            key.tangentModeInternal = broken ? key.tangentModeInternal | k_BrokenMask : key.tangentModeInternal & ~k_BrokenMask;
            curve.ReplaceKey(index, key);
            UpdateTangentsAround(curve, index);
        }

        static void UpdateTangentsAround(AnimationCurve curve, int index)
        {
            for (int i = index - 1; i <= index + 1; i++)
                if (i >= 0 && i < curve.length) UpdateTangents(curve, i);
        }

        /// <summary>A key's tangents from its modes: Linear sides point at the neighbour; either
        /// side ClampedAuto (else Auto) sets both to the smooth tangent; Constant sides are
        /// infinite. Free sides keep theirs. Measured, tests/projects/animatorapi util.tangents.*,
        /// extra.tangents.*.</summary>
        static void UpdateTangents(AnimationCurve curve, int i)
        {
            var key = curve[i];
            var left = (TangentMode)((key.tangentModeInternal >> k_LeftShift) & k_ModeMask);
            var right = (TangentMode)((key.tangentModeInternal >> k_RightShift) & k_ModeMask);
            int n = curve.length;
            if (left == TangentMode.Linear && i >= 1) key.inTangent = Slope(curve[i - 1], key);
            if (right == TangentMode.Linear && i + 1 < n) key.outTangent = Slope(key, curve[i + 1]);
            if (left == TangentMode.ClampedAuto || right == TangentMode.ClampedAuto)
                key.inTangent = key.outTangent = ClampedAutoTangent(curve, i);
            else if (left == TangentMode.Auto || right == TangentMode.Auto)
                key.inTangent = key.outTangent = AutoTangent(curve, i);
            if (left == TangentMode.Constant) key.inTangent = float.PositiveInfinity;
            if (right == TangentMode.Constant) key.outTangent = float.PositiveInfinity;
            curve.ReplaceKey(i, key);
        }

        static float Slope(Keyframe a, Keyframe b) => (b.value - a.value) / (b.time - a.time);

        /// <summary>The mean of the slopes to the neighbours; at an end, the slope to the one neighbour.</summary>
        static float AutoTangent(AnimationCurve curve, int i)
        {
            int n = curve.length;
            if (n < 2) return 0f;
            if (i == 0) return Slope(curve[0], curve[1]);
            if (i == n - 1) return Slope(curve[n - 2], curve[n - 1]);
            return (Slope(curve[i - 1], curve[i]) + Slope(curve[i], curve[i + 1])) * 0.5f;
        }

        /// <summary>The slope between the neighbours, flat at an extremum and at the ends, and at
        /// most four times the smaller rise over the span of the two intervals.</summary>
        static float ClampedAutoTangent(AnimationCurve curve, int i)
        {
            int n = curve.length;
            if (i == 0 || i == n - 1) return 0f;
            Keyframe a = curve[i - 1], k = curve[i], b = curve[i + 1];
            float rise1 = k.value - a.value, rise2 = b.value - k.value;
            if (rise1 * rise2 <= 0f) return 0f;
            float span = b.time - a.time;
            float tangent = (b.value - a.value) / span;
            float limit = 4f * Math.Min(Math.Abs(rise1), Math.Abs(rise2)) / span;
            return Math.Abs(tangent) > limit ? Math.Sign(tangent) * limit : tangent;
        }

        // ------------------------------------------------------------------ clips of objects

        /// <summary>The clips an object's Animation component, else its Animator's controller
        /// (each once), plays, then those of its IAnimationClipSource components.</summary>
        public static AnimationClip[] GetAnimationClips(GameObject gameObject)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            var result = new List<AnimationClip>();
            var animation = gameObject.GetComponent<Animation>();
            if (animation != null)
            {
                foreach (var clip in animation.m_Animations)
                    if (clip != null) result.Add(clip);
            }
            else if (gameObject.GetComponent<Animator>() is Animator animator && animator.runtimeAnimatorController != null)
            {
                foreach (var clip in animator.runtimeAnimatorController.animationClips)
                    if (clip != null && !result.Contains(clip)) result.Add(clip);
            }
            foreach (var source in gameObject.GetComponents<IAnimationClipSource>())
            {
                var list = new List<AnimationClip>();
                source.GetAnimationClips(list);
                foreach (var clip in list)
                    if (clip != null) result.Add(clip);
            }
            return result.ToArray();
        }

        /// <summary>Replaces an Animation component's clips (its default clip stays).</summary>
        public static void SetAnimationClips(Animation animation, AnimationClip[] clips)
        {
            if (animation == null) throw new ArgumentNullException(nameof(animation));
            animation.ReplaceClips(clips ?? Array.Empty<AnimationClip>());
        }

        // ------------------------------------------------------------------ values

        /// <summary>A discrete binding's value: an enum or a <see cref="DiscreteEvaluationAttribute"/>
        /// int of a script (other script fields warn and answer false); an engine property answers
        /// true and 0 (measured for m_IsActive and m_Enabled, whatever their values).</summary>
        public static bool GetDiscreteIntValue(GameObject root, EditorCurveBinding binding, out int data)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            data = 0;
            if (!binding.isDiscreteCurve) return false;
            var target = GetAnimatedObject(root, binding);
            if (target == null) return false;
            if (!(target is MonoBehaviour)) return true;
            var field = ScriptField(target.GetType(), binding.propertyName);
            if (field == null) return false;
            if (field.FieldType.IsEnum)
            {
                data = Convert.ToInt32(field.GetValue(target));
                return true;
            }
            if (field.FieldType == typeof(int) && field.IsDefined(typeof(DiscreteEvaluationAttribute), true))
            {
                data = (int)field.GetValue(target);
                return true;
            }
            Debug.LogWarning($"Property [{binding.propertyName}] is not a supported discrete curve binding. Discrete curves only support [System.Enum] and [System.Int32 with the `DiscreteEvaluation` attribute].");
            return false;
        }

        internal static FieldInfo ScriptField(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;
            foreach (var member in SerializationRules.Members(type))
                if (member.Name == name) return member.Field;
            return null;
        }

        /// <summary>Whether <paramref name="propertyName"/> on <paramref name="type"/> is a
        /// discrete integer curve (an enum or a <see cref="DiscreteEvaluationAttribute"/> int field
        /// of a MonoBehaviour).</summary>
        internal static bool IsDiscreteField(Type type, string propertyName)
        {
            if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type) || string.IsNullOrEmpty(propertyName)) return false;
            var field = ScriptField(type, propertyName);
            if (field == null) return false;
            return FieldGroup.IsDiscrete(field);
        }

        const string k_MaterialSlotPrefix = "m_Materials.Array.data[";
        const byte k_MaterialSlotCustomType = 21;

        internal static bool TryParseMaterialSlot(string propertyName, out int index)
        {
            index = -1;
            return propertyName != null
                && propertyName.StartsWith(k_MaterialSlotPrefix, StringComparison.Ordinal)
                && propertyName.EndsWith("]", StringComparison.Ordinal)
                && int.TryParse(propertyName.AsSpan(k_MaterialSlotPrefix.Length, propertyName.Length - k_MaterialSlotPrefix.Length - 1), out index)
                && index >= 0;
        }

        /// <summary>Converts a <see cref="PropertyModification"/> relative to
        /// <paramref name="gameObject"/> into an <see cref="EditorCurveBinding"/> and returns its
        /// value type, or null if not animatable (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/animatorapi <c>util.fromModification.*</c>,
        /// tests/projects/animrest <c>propmod.conv.*</c>).</summary>
        public static Type PropertyModificationToEditorCurveBinding(PropertyModification modification, GameObject gameObject, out EditorCurveBinding binding)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            binding = new EditorCurveBinding { path = "", type = typeof(Object), propertyName = "" };
            if (modification == null || modification.target == null) return null;
            Transform targetTransform;
            if (modification.target is GameObject targetGo) targetTransform = targetGo.transform;
            else if (modification.target is Component targetComp) targetTransform = targetComp.transform;
            else return null;
            if (targetTransform == null) return null;
            bool underRoot = false;
            for (var t = targetTransform; t != null; t = t.parent)
                if (ReferenceEquals(t, gameObject.transform)) { underRoot = true; break; }
            if (!underRoot) return null;
            var path = CalculateTransformPath(targetTransform, gameObject.transform);
            var targetType = modification.target.GetType();
            binding = new EditorCurveBinding { path = path, type = targetType, propertyName = "" };
            var prop = modification.propertyPath;
            if (string.IsNullOrEmpty(prop)) return null;

            if (modification.target is GameObject)
            {
                if (prop == "m_IsActive")
                {
                    binding = EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive");
                    return typeof(bool);
                }
                return null;
            }
            if (modification.target is Transform)
            {
                int dot = prop.LastIndexOf('.');
                if (dot > 0 && dot + 2 == prop.Length)
                {
                    var head = prop.Substring(0, dot);
                    char axis = prop[dot + 1];
                    bool ok = (head == "m_LocalPosition" || head == "m_LocalScale") && (axis == 'x' || axis == 'y' || axis == 'z')
                        || head == "m_LocalRotation" && (axis == 'x' || axis == 'y' || axis == 'z' || axis == 'w');
                    if (ok)
                    {
                        binding = EditorCurveBinding.FloatCurve(path, targetType, prop);
                        return typeof(float);
                    }
                }
                return null;
            }
            if (modification.target is Renderer)
            {
                if (prop == "m_Enabled")
                {
                    binding = EditorCurveBinding.FloatCurve(path, targetType, prop);
                    return typeof(bool);
                }
                if (TryParseMaterialSlot(prop, out _))
                {
                    binding = EditorCurveBinding.PPtrCurve(path, targetType, prop);
                    return typeof(Material);
                }
            }
            if (modification.target is SpriteRenderer && prop == "m_Sprite")
            {
                binding = EditorCurveBinding.PPtrCurve(path, targetType, prop);
                return typeof(Sprite);
            }
            using var so = new SerializedObject(modification.target);
            var sp = so.FindProperty(prop);
            if (sp == null) return null;
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Float:
                    binding = EditorCurveBinding.FloatCurve(path, targetType, prop);
                    return typeof(float);
                case SerializedPropertyType.Boolean:
                    binding = EditorCurveBinding.FloatCurve(path, targetType, prop);
                    return typeof(bool);
                case SerializedPropertyType.Enum:
                    binding = EditorCurveBinding.DiscreteCurve(path, targetType, prop);
                    return typeof(int);
                case SerializedPropertyType.Integer:
                    binding = IsDiscreteField(targetType, prop)
                        ? EditorCurveBinding.DiscreteCurve(path, targetType, prop)
                        : EditorCurveBinding.FloatCurve(path, targetType, prop);
                    return typeof(int);
                case SerializedPropertyType.ObjectReference:
                    if (modification.target is MonoBehaviour) return null;
                    binding = EditorCurveBinding.PPtrCurve(path, targetType, prop);
                    return typeof(Object);
                default:
                    return null;
            }
        }

        /// <summary>Converts editor curve bindings to compiled <see cref="GenericBinding"/> entries
        /// (skipping <see cref="Transform"/> bindings; matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/animatorapi <c>util.generic</c>, tests/projects/animrest <c>generic.*</c>).</summary>
        public static GenericBinding[] EditorCurveBindingsToGenericBindings(EditorCurveBinding[] editorCurveBindings)
        {
            if (editorCurveBindings == null || editorCurveBindings.Length == 0)
                return Array.Empty<GenericBinding>();
            var list = new List<GenericBinding>(editorCurveBindings.Length);
            for (int i = 0; i < editorCurveBindings.Length; i++)
            {
                var b = editorCurveBindings[i];
                if (b.type == typeof(Transform)) continue;
                uint pathHash = HashedBindings.Crc32(b.path ?? "");
                int typeId = ClassIds.IdOf(b.type);
                int scriptId = 0;
                if (b.type != null && typeof(MonoBehaviour).IsAssignableFrom(b.type))
                {
                    var script = MonoScript.FromType(b.type);
                    if (script != null) scriptId = script.GetInstanceID();
                }
                byte customType = 0;
                uint propHash;
                var prop = b.propertyName ?? "";
                if (b.isPPtrCurve)
                {
                    if (b.type != null && typeof(Renderer).IsAssignableFrom(b.type) && TryParseMaterialSlot(prop, out int slotIndex))
                    {
                        customType = k_MaterialSlotCustomType;
                        propHash = (uint)slotIndex;
                    }
                    else if (AnimationClipFiles.ObjectBinding(b.type, prop) is { } objBinding)
                    {
                        customType = (byte)objBinding.CustomType;
                        propHash = objBinding.Attribute;
                    }
                    else
                    {
                        propHash = HashedBindings.Crc32(prop);
                    }
                }
                else
                {
                    customType = (byte)(AnimationClipFiles.CustomType(b.type, prop) ?? 0);
                    propHash = b.type != null ? AnimationClipFiles.Attribute(b.type, prop) : HashedBindings.Crc32(prop);
                }
                var flags = Flags.kNone;
                if (b.isPPtrCurve) flags |= Flags.kPPtr | Flags.kDiscrete;
                else if (b.isDiscreteCurve || IsDiscreteField(b.type, b.propertyName)) flags |= Flags.kDiscrete;
                if (b.isSerializeReferenceCurve) flags |= Flags.kSerializeReference;
                list.Add(new GenericBinding(pathHash, propHash, scriptId, typeId, customType, flags));
            }
            return list.ToArray();
        }

        /// <summary>An object curve binding's current value on an engine component: a renderer's
        /// material slot (false past its last one), a sprite renderer's sprite, else the
        /// serialized property. A script's field answers false for a binding made by
        /// <see cref="EditorCurveBinding.PPtrCurve"/> (measured).</summary>
        public static bool GetObjectReferenceValue(GameObject root, EditorCurveBinding binding, out Object data)
        {
            data = null;
            var target = root == null ? null : GetAnimatedObject(root, binding);
            if (target == null || target is MonoBehaviour) return false;
            var name = binding.propertyName ?? "";
            if (target is Renderer renderer && TryParseMaterialSlot(name, out int index))
            {
                var materials = renderer.sharedMaterials;
                data = index >= 0 && index < materials.Length ? materials[index] : null;
                return data != null;
            }
            if (target is SpriteRenderer sprite && name == "m_Sprite")
            {
                data = sprite.sprite;
                return data != null;
            }
            using var so = new SerializedObject(target);
            var property = so.FindProperty(binding.propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) return false;
            data = property.objectReferenceValue;
            return data != null;
        }

        // ------------------------------------------------------------------ stream bindings and stats

        /// <summary>The properties animation jobs bound on the root's Animator (BindStreamProperty,
        /// BindCustomStreamProperty, in binding order), then the bound transforms' rotation,
        /// position and scale (measured, tests/projects/animatorapi stream.*).</summary>
        internal static EditorCurveBinding[] GetAnimationStreamBindings(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var handles = root.GetComponent<Animator>()?.m_StreamHandles;
            if (handles == null) return Array.Empty<EditorCurveBinding>();
            var result = new List<EditorCurveBinding>();
            foreach (var p in handles.Properties)
                if (p.BoundType != null)
                    result.Add(p.IsObject ? EditorCurveBinding.PPtrCurve(p.BoundPath, p.BoundType, p.BoundProperty) : EditorCurveBinding.FloatCurve(p.BoundPath, p.BoundType, p.BoundProperty));
            foreach (var t in handles.Transforms)
            {
                var path = t.Path ?? "";
                foreach (var axis in s_Quaternion) result.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + axis));
                foreach (var axis in s_Vector) result.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition." + axis));
                foreach (var axis in s_Vector) result.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale." + axis));
            }
            return result.ToArray();
        }

        static readonly string[] s_Vector = { "x", "y", "z" }, s_Quaternion = { "x", "y", "z", "w" };

        /// <summary>The curves the Editor compiled a clip to (nothing for a clip it has not
        /// compiled, <see cref="AnimationClip.Compile"/>): a Transform vector is one curve per
        /// component, every component keyed at the union of the vector's key times, and constant
        /// only when all its components are; other float curves count each, object curves each
        /// (never constant). A curve is constant when its keys have one value and no slope.
        /// <c>size</c> is the compiled clip's memory: a fixed part, eight bytes a curve and four
        /// more a constant one, and for the others (the streamed curves) eight bytes for each
        /// distinct key time and twenty for each key, where float curves have one key and one time
        /// more than they hold (the values before their first). Matches the Unity 6000.0.71f1
        /// Editor, tests/projects/animatorapi <c>clip.*.stats</c>, <c>stats.*</c>, <c>typed.stats</c>.
        /// Unmeasured: dense curves (a model's compressed clips) and Humanoid muscle clips, whose
        /// sizes follow the same sum here, and the sizes of other release families
        /// (<see cref="Compat.CompiledClipSizeMeasured"/>: the same sum, reported once).</summary>
        internal static AnimationClipStats GetAnimationClipStats(AnimationClip clip)
        {
            var stats = new AnimationClipStats();
            if (clip == null || !clip.m_Compiled) return stats;
            var vectors = new Dictionary<(string Path, string Head), VectorStats>();
            var times = new HashSet<float>();
            int streamKeys = 0;
            bool floatStream = false;
            foreach (var c in clip.CurvesAsImported())
            {
                bool constant = IsConstant(c.Curve);
                var attribute = c.Binding.Attribute ?? "";
                int dot = attribute.LastIndexOf('.');
                var head = dot < 0 ? attribute : attribute.Substring(0, dot);
                int kind = c.Binding.ResolveType() == typeof(Transform) ? TransformKind(head) : -1;
                if (kind >= 0)
                {
                    var key = (c.Binding.Path ?? "", head);
                    if (!vectors.TryGetValue(key, out var v)) vectors[key] = v = new VectorStats { Kind = kind, Constant = true };
                    v.Constant &= constant;
                    for (int i = 0, n = c.Curve?.length ?? 0; i < n; i++) v.Times.Add(c.Curve[i].time);
                    continue;
                }
                if (c.Binding.ClassId == k_AnimatorClassId && Unbound.Runtime.Humanoid.HumanClipImport.CurveIndex(attribute) >= 0) stats.muscleCurves++;
                else stats.genericCurves++;
                stats.totalCurves++;
                if (constant)
                {
                    stats.constantCurves++;
                    continue;
                }
                floatStream = true;
                streamKeys += (c.Curve?.length ?? 0) + 1;
                for (int i = 0, n = c.Curve?.length ?? 0; i < n; i++) times.Add(c.Curve[i].time);
            }
            foreach (var v in vectors.Values)
            {
                int components = v.Kind == 1 ? 4 : 3;
                if (v.Kind == 0) stats.positionCurves++;
                else if (v.Kind == 1) stats.quaternionCurves++;
                else if (v.Kind == 2) stats.eulerCurves++;
                else stats.scaleCurves++;
                stats.totalCurves += components;
                if (v.Constant)
                {
                    stats.constantCurves += components;
                    continue;
                }
                floatStream = true;
                streamKeys += components * (v.Times.Count + 1);
                times.UnionWith(v.Times);
            }
            foreach (var c in clip.m_ObjectCurves)
            {
                streamKeys += c.Times.Length;
                foreach (float t in c.Times) times.Add(t);
            }
            stats.pptrCurves = clip.m_ObjectCurves.Count;
            stats.totalCurves += stats.pptrCurves;
            stats.streamCurves = stats.totalCurves - stats.constantCurves - stats.denseCurves;
            if (stats.totalCurves == 0) return stats;
            if (!Compat.CompiledClipSizeMeasured)
                Stubs.Ignored("AnimationClipStats.size", "is the sum measured for Unity 6000.0: this release's compiled clip sizes are unmeasured");
            stats.clips = 1;
            stats.size = k_ClipBytes + k_CurveBytes * stats.totalCurves + k_ConstantBytes * stats.constantCurves
                + k_FrameBytes * (times.Count + (floatStream ? 1 : 0)) + k_KeyBytes * streamKeys;
            return stats;
        }

        /// <summary>A Transform vector's curves in a clip's stats.</summary>
        sealed class VectorStats
        {
            internal int Kind;
            internal bool Constant;
            internal readonly HashSet<float> Times = new HashSet<float>();
        }

        // The class ids of Transform and Animator; the compiled clip's sizes in bytes (see
        // GetAnimationClipStats): its fixed part, a curve, a constant curve's value, a streamed
        // key time and a streamed key.
        const int k_TransformClassId = 4, k_AnimatorClassId = 95, k_ClipBytes = 2536, k_CurveBytes = 8, k_ConstantBytes = 4, k_FrameBytes = 8, k_KeyBytes = 20;

        /// <summary>0 position, 1 rotation, 2 Euler angles, 3 scale; -1 another Transform property.</summary>
        static int TransformKind(string head) => head switch
        {
            "m_LocalPosition" => 0,
            "m_LocalRotation" => 1,
            "localEulerAnglesRaw" or "localEulerAngles" or "localEulerAnglesBaked" => 2,
            "m_LocalScale" => 3,
            _ => -1,
        };

        static bool IsConstant(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return true;
            float first = curve[0].value;
            for (int i = 0; i < curve.length; i++)
            {
                var key = curve[i];
                if (key.value != first || key.inTangent != 0f || key.outTangent != 0f) return false;
            }
            return true;
        }
    }

    /// <summary>An object curve's key: the object a property takes from a time on.</summary>
    public partial struct ObjectReferenceKeyframe
    {
        public float time;
        public Object value;
    }

    /// <summary>Curve counts of clips (AnimationUtility.GetAnimationClipStats), summed by
    /// <see cref="Combine"/>.</summary>
    internal partial struct AnimationClipStats
    {
        public int size;
        public int clips;
        public int positionCurves;
        public int quaternionCurves;
        public int eulerCurves;
        public int scaleCurves;
        public int muscleCurves;
        public int genericCurves;
        public int pptrCurves;
        public int totalCurves;
        public int constantCurves;
        public int denseCurves;
        public int streamCurves;

        public void Reset() => this = default;

        public void Combine(AnimationClipStats other)
        {
            size += other.size;
            clips += other.clips;
            positionCurves += other.positionCurves;
            quaternionCurves += other.quaternionCurves;
            eulerCurves += other.eulerCurves;
            scaleCurves += other.scaleCurves;
            muscleCurves += other.muscleCurves;
            genericCurves += other.genericCurves;
            pptrCurves += other.pptrCurves;
            totalCurves += other.totalCurves;
            constantCurves += other.constantCurves;
            denseCurves += other.denseCurves;
            streamCurves += other.streamCurves;
        }
    }
}
