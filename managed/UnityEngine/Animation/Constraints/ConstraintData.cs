using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

namespace Unbound.Runtime
{
    internal enum ConstraintKind { Position, Rotation, Scale, Parent, Aim, LookAt }

    /// <summary>Shared serialized state and source-list behavior of the six native constraints.</summary>
    internal sealed partial class ConstraintData
    {
        internal readonly ConstraintKind Kind;
        internal Behaviour Owner;
        internal float Weight = 1f;
        internal bool Active, Locked = true;
        bool m_CaptureRest;

        internal void Enable()
        {
            if (m_CaptureRest && Kind != ConstraintKind.LookAt)
            {
                TranslationRest = Owner.transform.localPosition;
                RotationRest = Owner.transform.localEulerAngles;
                ScaleRest = Owner.transform.localScale;
            }
            m_CaptureRest = false;
            ConstraintSystem.Add(this);
        }
        internal readonly List<ConstraintSource> Sources = new List<ConstraintSource>();
        internal Vector3 TranslationRest, TranslationOffset, RotationRest, RotationOffset;
        internal Vector3 ScaleRest = Vector3.one, ScaleOffset = Vector3.one;
        internal Axis TranslationAxis = (Axis)7, RotationAxis = (Axis)7, ScaleAxis = (Axis)7;
        internal readonly List<Vector3> TranslationOffsets = new List<Vector3>(), RotationOffsets = new List<Vector3>();
        internal Vector3 Aim = Vector3.forward, Up = Vector3.up, WorldUp = Vector3.up;
        internal Transform UpObject;
        internal AimConstraint.WorldUpType UpType;
        internal float Roll;
        internal bool UseUpObject;

        internal ConstraintData(ConstraintKind kind) { Kind = kind; }

        internal void GetSources(List<ConstraintSource> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            sources.Clear(); sources.AddRange(Sources);
        }

        internal void SetSources(List<ConstraintSource> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            Sources.Clear(); Sources.AddRange(sources);
            ResizeOffsets(TranslationOffsets); ResizeOffsets(RotationOffsets);
        }

        void ResizeOffsets(List<Vector3> values)
        {
            if (values.Count > Sources.Count) values.RemoveRange(Sources.Count, values.Count - Sources.Count);
            while (values.Count < Sources.Count) values.Add(Vector3.zero);
        }

        internal int AddSource(ConstraintSource source)
        {
            Sources.Add(source);
            TranslationOffsets.Add(Vector3.zero); RotationOffsets.Add(Vector3.zero);
            return Sources.Count - 1;
        }

        void CheckSource(int index)
        {
            if (Sources.Count == 0) throw new InvalidOperationException("The constraint sources list is empty.");
            if ((uint)index >= (uint)Sources.Count) throw new ArgumentOutOfRangeException(nameof(index));
        }

        internal ConstraintSource GetSource(int index) { CheckSource(index); return Sources[index]; }
        internal void SetSource(int index, ConstraintSource source) { CheckSource(index); Sources[index] = source; }
        internal void RemoveSource(int index)
        {
            CheckSource(index); Sources.RemoveAt(index);
            if (index < TranslationOffsets.Count) TranslationOffsets.RemoveAt(index);
            if (index < RotationOffsets.Count) RotationOffsets.RemoveAt(index);
        }

        internal Vector3 GetOffset(List<Vector3> offsets, int index)
        {
            if ((uint)index >= (uint)Sources.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return index < offsets.Count ? offsets[index] : Vector3.zero;
        }

        internal void SetOffset(List<Vector3> offsets, int index, Vector3 value)
        {
            if ((uint)index >= (uint)Sources.Count) throw new ArgumentOutOfRangeException(nameof(index));
            while (offsets.Count <= index) offsets.Add(Vector3.zero);
            offsets[index] = value;
        }

        internal static void SetOffsets(List<Vector3> target, Vector3[] values)
        {
            target.Clear(); if (values != null) target.AddRange(values);
        }

        internal void Read(YNode body, IReferenceResolver resolver)
        {
            m_CaptureRest = true;
            Weight = body.Float("m_Weight", 1f); Active = body.Bool("m_Active"); Locked = body.Bool("m_IsLocked", true);
            TranslationRest = body.Vector3("m_TranslationAtRest"); TranslationOffset = body.Vector3("m_TranslationOffset");
            RotationRest = body.Vector3("m_RotationAtRest"); RotationOffset = body.Vector3("m_RotationOffset");
            ScaleRest = body.Vector3("m_ScaleAtRest", Vector3.one); ScaleOffset = body.Vector3("m_ScaleOffset", Vector3.one);
            Axis Axes(string prefix) => (body.Bool(prefix + "X", true) ? Axis.X : 0)
                | (body.Bool(prefix + "Y", true) ? Axis.Y : 0) | (body.Bool(prefix + "Z", true) ? Axis.Z : 0);
            TranslationAxis = Axes("m_AffectTranslation"); RotationAxis = Axes("m_AffectRotation"); ScaleAxis = Axes("m_AffectScaling");
            Aim = body.Vector3("m_AimVector", Vector3.forward); Up = body.Vector3("m_UpVector", Vector3.up);
            WorldUp = body.Vector3("m_WorldUpVector", Vector3.up); UpType = (AimConstraint.WorldUpType)body.Int("m_UpType");
            UpObject = resolver?.Resolve(body.Ref("m_WorldUpObject"), typeof(Transform)) as Transform;
            Roll = body.Float("m_Roll"); UseUpObject = body.Bool("m_UseUpObject");
            Sources.Clear();
            foreach (var node in body.Get("m_Sources")?.Seq() ?? Array.Empty<YNode>())
                Sources.Add(new ConstraintSource { sourceTransform = resolver?.Resolve(node.Ref("sourceTransform"), typeof(Transform)) as Transform, weight = node.Float("weight") });
            ReadOffsets("m_TranslationOffsets", TranslationOffsets); ReadOffsets("m_RotationOffsets", RotationOffsets);
            void ReadOffsets(string key, List<Vector3> values)
            {
                values.Clear();
                foreach (var n in body.Get(key)?.Seq() ?? Array.Empty<YNode>())
                    values.Add(new Vector3(n.Float("x"), n.Float("y"), n.Float("z")));
            }
        }

        internal void Copy(ConstraintData source, Func<UnityEngine.Object, UnityEngine.Object> remap)
        {
            m_CaptureRest = true;
            Weight = source.Weight; Active = source.Active; Locked = source.Locked;
            TranslationRest = source.TranslationRest; TranslationOffset = source.TranslationOffset;
            RotationRest = source.RotationRest; RotationOffset = source.RotationOffset;
            ScaleRest = source.ScaleRest; ScaleOffset = source.ScaleOffset;
            TranslationAxis = source.TranslationAxis; RotationAxis = source.RotationAxis; ScaleAxis = source.ScaleAxis;
            Aim = source.Aim; Up = source.Up; WorldUp = source.WorldUp; UpType = source.UpType;
            UpObject = remap(source.UpObject) as Transform; Roll = source.Roll; UseUpObject = source.UseUpObject;
            Sources.Clear();
            foreach (var s in source.Sources) Sources.Add(new ConstraintSource { sourceTransform = remap(s.sourceTransform) as Transform, weight = s.weight });
            TranslationOffsets.Clear(); TranslationOffsets.AddRange(source.TranslationOffsets);
            RotationOffsets.Clear(); RotationOffsets.AddRange(source.RotationOffsets);
        }
    }
}
