using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A level of a <see cref="LODGroup"/>: the renderers it draws from a screen-relative
    /// height on.</summary>
    public partial struct LOD
    {
        public float screenRelativeTransitionHeight;
        public float fadeTransitionWidth;
        public Renderer[] renderers;

        public LOD(float screenRelativeTransitionHeight, Renderer[] renderers)
        {
            this.screenRelativeTransitionHeight = screenRelativeTransitionHeight;
            fadeTransitionWidth = 0f;
            this.renderers = renderers;
        }
    }

    /// <summary>
    /// Levels of detail: each camera draws the renderers of one level, the first whose transition
    /// height the group's screen-relative height reaches (from QualitySettings.maximumLODLevel on),
    /// none past the last. The height is the group's world size (its size times its largest scale)
    /// over the view's height at the reference point's distance (2 d tan(fov / 2); twice the
    /// orthographic size), times QualitySettings.lodBias (measured, tests/projects/lod). Levels
    /// switch at once (cross-fades are not drawn).
    /// </summary>
    [ClassId(205)]
    public partial class LODGroup : Component
    {
        Vector3 m_LocalReferencePoint;
        float m_Size = 1f;
        LODFadeMode m_FadeMode;
        bool m_AnimateCrossFading;
        bool m_LastLODIsBillboard;
        bool m_Enabled = true;
        LOD[] m_LODs = Array.Empty<LOD>();
        int m_Forced = -1;
        static float s_CrossFadeAnimationDuration = 0.5f;

        public LODGroup() { }

        public Vector3 localReferencePoint { get => m_LocalReferencePoint; set => m_LocalReferencePoint = value; }
        public float size { get => m_Size; set => m_Size = value; }
        public int lodCount => m_LODs.Length;
        public bool lastLODBillboard { get => m_LastLODIsBillboard; set => m_LastLODIsBillboard = value; }
        public LODFadeMode fadeMode { get => m_FadeMode; set => m_FadeMode = value; }
        public bool animateCrossFading { get => m_AnimateCrossFading; set => m_AnimateCrossFading = value; }

        public bool enabled
        {
            get => m_Enabled;
            set => m_Enabled = value;
        }

        public static float crossFadeAnimationDuration
        {
            get => s_CrossFadeAnimationDuration;
            set
            {
                if (value <= 0f) throw new ArgumentOutOfRangeException(nameof(value), "Cross-fade animation duration needs to be greater than zero.");
                s_CrossFadeAnimationDuration = value;
            }
        }

        internal Vector3 worldReferencePoint => transform.TransformPoint(m_LocalReferencePoint);

        public LOD[] GetLODs()
        {
            var copy = new LOD[m_LODs.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = m_LODs[i];
                copy[i].renderers = m_LODs[i].renderers != null ? (Renderer[])m_LODs[i].renderers.Clone() : Array.Empty<Renderer>();
            }
            return copy;
        }

        [Obsolete("Use SetLODs instead.")]
        public void SetLODS(LOD[] lods) => SetLODs(lods);

        public void SetLODs(LOD[] lods)
        {
            if (lods == null) throw new ArgumentNullException(nameof(lods));
            foreach (var lod in m_LODs) Attach(lod.renderers, null);
            m_LODs = new LOD[lods.Length];
            for (int i = 0; i < lods.Length; i++)
            {
                m_LODs[i] = lods[i];
                m_LODs[i].renderers = lods[i].renderers != null ? (Renderer[])lods[i].renderers.Clone() : Array.Empty<Renderer>();
            }
            foreach (var lod in m_LODs) Attach(lod.renderers, this);
        }

        void Attach(Renderer[] renderers, LODGroup group)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
                if (r) r.m_LODGroup = group;
        }

        public void ForceLOD(int index) => m_Forced = index;

        /// <summary>The size and reference point of the levels' renderers: their bounds in the
        /// group's space, the size their largest side.</summary>
        public void RecalculateBounds()
        {
            bool any = false;
            var bounds = new Bounds();
            var toLocal = transform.worldToLocalMatrix;
            foreach (var lod in m_LODs)
                foreach (var r in lod.renderers ?? Array.Empty<Renderer>())
                {
                    if (!r) continue;
                    var world = r.bounds;
                    var e = world.extents;
                    for (int i = 0; i < 8; i++)
                    {
                        var p = toLocal.MultiplyPoint3x4(world.center + new Vector3((i & 1) != 0 ? e.x : -e.x, (i & 2) != 0 ? e.y : -e.y, (i & 4) != 0 ? e.z : -e.z));
                        if (!any) bounds = new Bounds(p, Vector3.zero);
                        else bounds.Encapsulate(p);
                        any = true;
                    }
                }
            if (!any) return;
            m_LocalReferencePoint = bounds.center;
            m_Size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        }

        /// <summary>The level a camera draws (-1: none).</summary>
        internal int LevelFor(Camera camera, Vector3 cameraPosition)
        {
            var scale = transform.lossyScale;
            float worldSize = m_Size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            return LevelFor(camera, cameraPosition, worldReferencePoint, worldSize);
        }

        /// <summary>The level a camera draws of the group placed with a world reference point and
        /// size (a terrain's trees place their prefab's group).</summary>
        internal int LevelFor(Camera camera, Vector3 cameraPosition, Vector3 reference, float worldSize)
        {
            if (m_LODs.Length == 0) return -1;
            if (m_Forced >= 0) return Math.Min(m_Forced, m_LODs.Length - 1);
            float viewHeight = camera.orthographic
                ? 2f * camera.orthographicSize
                : 2f * (reference - cameraPosition).magnitude * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float height = viewHeight > 0f ? worldSize / viewHeight * QualitySettings.lodBias : float.PositiveInfinity;
            for (int i = Math.Max(0, QualitySettings.maximumLODLevel); i < m_LODs.Length; i++)
                if (height >= m_LODs[i].screenRelativeTransitionHeight) return i;
            return -1;
        }

        /// <summary>Whether a camera draws a renderer of the group: whether it is in the camera's level.</summary>
        internal bool Draws(Renderer renderer, Camera camera, Vector3 cameraPosition)
        {
            if (!m_Enabled || !gameObject.activeInHierarchy) return true;
            int level = LevelFor(camera, cameraPosition);
            if (level < 0) return false;
            var drawn = m_LODs[level].renderers;
            if (drawn == null) return false;
            foreach (var r in drawn)
                if (ReferenceEquals(r, renderer)) return true;
            return false;
        }

        /// <summary>Whether a camera draws a renderer: one of no LOD group, or in its group's level.</summary>
        internal static bool Drawn(Renderer renderer, Camera camera, Vector3 cameraPosition)
        {
            var group = renderer.m_LODGroup;
            if (group == null || camera == null) return true;
            if (!group) return true;
            return group.Draws(renderer, camera, cameraPosition);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_LocalReferencePoint = body.Vector3("m_LocalReferencePoint");
            m_Size = body.Float("m_Size", 1f);
            m_FadeMode = (LODFadeMode)body.Int("m_FadeMode");
            m_AnimateCrossFading = body.Bool("m_AnimateCrossFading");
            m_LastLODIsBillboard = body.Bool("m_LastLODIsBillboard");
            m_Enabled = body.Bool("m_Enabled", true);
            var lods = new List<LOD>();
            foreach (var l in body.Get("m_LODs")?.Seq() ?? Array.Empty<YNode>())
            {
                var renderers = new List<Renderer>();
                foreach (var r in l.Get("renderers")?.Seq() ?? Array.Empty<YNode>())
                {
                    var reference = r.Ref("renderer");
                    if (!reference.IsNull && resolver.Resolve(reference, typeof(Renderer)) is Renderer renderer) renderers.Add(renderer);
                }
                lods.Add(new LOD
                {
                    screenRelativeTransitionHeight = l.Float("screenRelativeHeight"),
                    fadeTransitionWidth = l.Float("fadeTransitionWidth"),
                    renderers = renderers.ToArray(),
                });
            }
            SetLODs(lods.ToArray());
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (LODGroup)source;
            m_LocalReferencePoint = s.m_LocalReferencePoint;
            m_Size = s.m_Size;
            m_FadeMode = s.m_FadeMode;
            m_AnimateCrossFading = s.m_AnimateCrossFading;
            m_LastLODIsBillboard = s.m_LastLODIsBillboard;
            m_Enabled = s.m_Enabled;
            var lods = s.GetLODs();
            for (int i = 0; i < lods.Length; i++)
            {
                var renderers = lods[i].renderers;
                for (int k = 0; k < renderers.Length; k++) renderers[k] = remap(renderers[k]) as Renderer;
            }
            SetLODs(lods);
        }

        internal override void OnEngineDestroy()
        {
            foreach (var lod in m_LODs) Attach(lod.renderers, null);
        }
    }
}
