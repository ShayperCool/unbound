using System;
using UnityEngine;
using UnityEngine.Animations;

namespace Unbound.Runtime
{
    internal sealed partial class ConstraintData
    {
        internal static Vector3 Mask(Vector3 original, Vector3 value, Axis axes) => new Vector3(
            (axes & Axis.X) != 0 ? value.x : original.x,
            (axes & Axis.Y) != 0 ? value.y : original.y,
            (axes & Axis.Z) != 0 ? value.z : original.z);

        Quaternion LocalRotation(Quaternion world)
        {
            var parent = Owner.transform.parent;
            if (parent == null) return world;
            var q = Quaternion.Inverse(parent.rotation) * world;
            var s = parent.lossyScale;
            float x = s.x < 0 ? -1 : 1, y = s.y < 0 ? -1 : 1, z = s.z < 0 ? -1 : 1;
            return new Quaternion(q.x * y * z, q.y * x * z, q.z * x * y, q.w);
        }

        Vector3 LocalPosition(Vector3 world) => Owner.transform.parent != null ? Owner.transform.parent.InverseTransformPoint(world) : world;

        Vector3 SourcePosition(bool parent)
        {
            Vector3 sum = Vector3.zero;
            float total = 0f;
            int count = 0;
            for (int i = 0; i < Sources.Count; i++)
            {
                var s = Sources[i]; if (s.sourceTransform == null) continue;
                count++;
                var p = s.sourceTransform.position;
                if (parent) p += (s.sourceTransform != null ? s.sourceTransform.rotation : Quaternion.identity) * GetOffset(TranslationOffsets, i);
                sum += p * s.weight; total += s.weight;
            }
            if (Mathf.Abs(total) < 1e-6f) return TranslationRest;
            return LocalPosition(count == 1 ? sum : sum / total) + (parent ? Vector3.zero : TranslationOffset);
        }

        Quaternion SourceRotation(bool parent)
        {
            var sum = new Quaternion(0, 0, 0, 0);
            Quaternion first = Quaternion.identity;
            bool found = false;
            for (int i = 0; i < Sources.Count; i++)
            {
                var s = Sources[i]; if (s.sourceTransform == null || s.weight <= 0) continue;
                var q = s.sourceTransform != null ? s.sourceTransform.rotation : Quaternion.identity;
                if (parent) q *= Quaternion.Euler(GetOffset(RotationOffsets, i));
                if (!found) { first = q; found = true; }
                float w = Quaternion.Dot(first, q) < 0 ? -s.weight : s.weight;
                sum.x += q.x * w; sum.y += q.y * w; sum.z += q.z * w; sum.w += q.w * w;
            }
            if (!found) return Quaternion.Euler(RotationRest);
            var value = LocalRotation(Quaternion.Normalize(sum));
            return parent ? value : value * Quaternion.Euler(RotationOffset);
        }

        Vector3 SourceScale()
        {
            Vector3 value = Vector3.one;
            float total = SourceTotal(false);
            if (Mathf.Abs(total) < 1e-6f) return ScaleRest;
            int count = 0;
            foreach (var s in Sources)
            {
                if (s.sourceTransform == null) continue;
                var scale = s.sourceTransform.lossyScale;
                float weight = s.weight / total;
                value = Vector3.Scale(value, new Vector3(
                    Mathf.Sign(scale.x * ScaleOffset.x) * ExpScale(LogScale(scale.x) * weight),
                    Mathf.Sign(scale.y * ScaleOffset.y) * ExpScale(LogScale(scale.y) * weight),
                    Mathf.Sign(scale.z * ScaleOffset.z) * ExpScale(LogScale(scale.z) * weight)));
                count++;
            }
            value = Vector3.Scale(value, new Vector3(ExpScale(LogScale(ScaleOffset.x) * count / total),
                ExpScale(LogScale(ScaleOffset.y) * count / total), ExpScale(LogScale(ScaleOffset.z) * count / total)));
            var parent = Owner.transform.parent;
            if (parent != null)
            {
                var p = parent.lossyScale;
                value = new Vector3(value.x / p.x, value.y / p.y, value.z / p.z);
            }
            return value;
        }

        // Mecanim scale blending uses a cubic mantissa logarithm and a quadratic exponential.
        // The coefficients and the per-source multiplicative offsets are measured by the
        // scale.log/scale.exp Editor probes; ordinary Pow differs by up to about 0.6 percent.
        static float LogScale(float value)
        {
            value = Mathf.Abs(value);
            if (value == 0) return -128f;
            int bits = BitConverter.SingleToInt32Bits(value);
            int exponent = ((bits >> 23) & 255) - 127;
            float m = BitConverter.Int32BitsToSingle((bits & 0x7fffff) | 0x3f800000) - 1f;
            return exponent + m * (1.437907f + m * (-0.6322878f + m * 0.1943807f));
        }

        static float ExpScale(float value)
        {
            if (value <= -126) return 0f;
            if (value >= 128) return float.PositiveInfinity;
            int exponent = (int)Mathf.Floor(value);
            float m = value - exponent;
            float mantissa = 1f + m * (0.6822699f + m * 0.3177301f);
            return mantissa * BitConverter.Int32BitsToSingle((exponent + 127) << 23);
        }

        Quaternion SourceAim()
        {
            Vector3 sum = Vector3.zero; float total = 0f; int count = 0;
            foreach (var s in Sources)
            {
                if (s.sourceTransform == null) continue;
                sum += s.sourceTransform.position * s.weight; total += s.weight; count++;
            }
            if (Mathf.Abs(total) < 1e-6f) return Quaternion.Euler(RotationRest);
            Vector3 forward = (count == 1 ? sum : sum / total) - Owner.transform.position;
            if (forward.sqrMagnitude < 1e-12f) return Quaternion.Euler(RotationRest);
            Vector3 up = Vector3.up;
            if (Kind == ConstraintKind.LookAt)
            {
                if (UseUpObject && UpObject != null) up = UpObject.position - Owner.transform.position;
                var look = Quaternion.LookRotation(forward, up) * Quaternion.AngleAxis(Roll, Vector3.forward);
                return LocalRotation(look) * Quaternion.Euler(RotationOffset);
            }
            switch (UpType)
            {
                case AimConstraint.WorldUpType.ObjectUp: if (UpObject != null) up = UpObject.position - Owner.transform.position; break;
                case AimConstraint.WorldUpType.ObjectRotationUp: up = UpObject != null ? UpObject.rotation * WorldUp : WorldUp; break;
                case AimConstraint.WorldUpType.Vector: up = WorldUp; break;
                case AimConstraint.WorldUpType.None:
                    return LocalRotation(Quaternion.FromToRotation(Owner.transform.rotation * Aim, forward) * Owner.transform.rotation) * Quaternion.Euler(RotationOffset);
            }
            return LocalRotation(Quaternion.LookRotation(forward, up) * Quaternion.Inverse(Quaternion.LookRotation(Aim, Up))) * Quaternion.Euler(RotationOffset);
        }

        float SourceTotal(bool positive)
        {
            float total = 0f;
            foreach (var source in Sources)
                if (source.sourceTransform != null) total += positive ? Mathf.Max(0, source.weight) : source.weight;
            return total;
        }

        internal void Evaluate()
        {
            var t = Owner.transform;
            if (Kind == ConstraintKind.Position || Kind == ConstraintKind.Parent)
            {
                var candidate = Vector3.LerpUnclamped(TranslationRest, SourcePosition(Kind == ConstraintKind.Parent), Weight);
                t.localPosition = Mathf.Abs(SourceTotal(false)) < 1e-6f ? TranslationRest : Mask(t.localPosition, candidate, TranslationAxis);
            }
            if (Kind == ConstraintKind.Scale)
            {
                t.localScale = Mathf.Abs(SourceTotal(false)) < 1e-6f ? ScaleRest
                    : Mask(t.localScale, Vector3.LerpUnclamped(ScaleRest, SourceScale(), Weight), ScaleAxis);
            }
            if (Kind == ConstraintKind.Rotation || Kind == ConstraintKind.Parent || Kind == ConstraintKind.Aim || Kind == ConstraintKind.LookAt)
            {
                if (Mathf.Abs(SourceTotal(Kind == ConstraintKind.Rotation || Kind == ConstraintKind.Parent)) < 1e-6f)
                { t.localRotation = Quaternion.Euler(RotationRest); return; }
                var candidate = Kind == ConstraintKind.Aim || Kind == ConstraintKind.LookAt ? SourceAim() : SourceRotation(Kind == ConstraintKind.Parent);
                candidate = Quaternion.LerpUnclamped(Quaternion.Euler(RotationRest), candidate, Weight);
                var axes = Kind == ConstraintKind.LookAt ? (Axis)7 : RotationAxis;
                var angles = Quaternion.ToEulerRad(candidate) * Mathf.Rad2Deg;
                var current = Quaternion.ToEulerRad(t.localRotation) * Mathf.Rad2Deg;
                t.localRotation = Quaternion.Euler(Mask(current, angles, axes));
            }
        }

        internal void Activate(bool preserve)
        {
            if (preserve) UpdateOffset();
            else
            {
                TranslationOffset = RotationOffset = Vector3.zero; ScaleOffset = Vector3.one;
                for (int i = 0; i < Sources.Count; i++)
                { SetOffset(TranslationOffsets, i, Vector3.zero); SetOffset(RotationOffsets, i, Vector3.zero); }
            }
            Active = Locked = true;
        }

        internal void UpdateOffset()
        {
            // These entry points are used by the Editor's activation commands.
            var t = Owner.transform;
            TranslationRest = t.localPosition; RotationRest = t.localEulerAngles; ScaleRest = t.localScale;
            if (Kind == ConstraintKind.Parent)
            {
                for (int i = 0; i < Sources.Count; i++)
                {
                    var s = Sources[i].sourceTransform; if (s == null) continue;
                    SetOffset(TranslationOffsets, i, Quaternion.Inverse(s.rotation) * (t.position - s.position));
                    SetOffset(RotationOffsets, i, (Quaternion.Inverse(s.rotation) * t.rotation).eulerAngles);
                }
            }
            else if (Kind == ConstraintKind.Position)
            {
                TranslationOffset = Vector3.zero; TranslationOffset = t.localPosition - SourcePosition(false);
            }
            else if (Kind == ConstraintKind.Scale)
            {
                ScaleOffset = Vector3.one; var scale = SourceScale();
                ScaleOffset = new Vector3(t.localScale.x / scale.x, t.localScale.y / scale.y, t.localScale.z / scale.z);
            }
            else
            {
                RotationOffset = Vector3.zero;
                var rotation = Kind == ConstraintKind.Rotation ? SourceRotation(false) : SourceAim();
                RotationOffset = (Quaternion.Inverse(rotation) * t.localRotation).eulerAngles;
            }
        }
    }
}
