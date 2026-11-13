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
    /// Levels of detail: each camera draws the renderers of one level, the first (from
    /// QualitySettings.maximumLODLevel on) whose reach the camera is within, none past the last.
    /// A level reaches as far as the group's world size (its size times its largest scale) over
    /// the level's transition height; the camera's distance counts times the view's height at
    /// distance 1 (2 tan(fov / 2); for an orthographic camera twice its size, at any distance)
    /// over QualitySettings.lodBias, and a level ends where the two are equal (measured,
    /// tests/projects/lod). In terms of the group's height on screen: a level is drawn while
    /// that height is more than its transition height. Levels switch at once (cross-fades are
    /// not drawn).
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
        internal LOD[] m_LODs = Array.Empty<LOD>();
        int m_Forced = -1;
        static float s_CrossFadeAnimationDuration = 0.5f;
        /// <summary>The groups alive, attached to their GameObjects (ObjectDispatcher's transform
        /// tracking reads them).</summary>
        internal static readonly List<LODGroup> s_Live = new List<LODGroup>();

        public LODGroup()
        {
            if (Compat.DefaultConstructorStateMeasured)
                m_LODs = new[] { new LOD(0.6f, Array.Empty<Renderer>()), new LOD(0.3f, Array.Empty<Renderer>()), new LOD(0.1f, Array.Empty<Renderer>()) };
        }

        public Vector3 localReferencePoint { get => m_LocalReferencePoint; set { m_LocalReferencePoint = value; ObjectDispatch.Changed(this); } }
        public float size { get => m_Size; set { m_Size = value; ObjectDispatch.Changed(this); } }
        public int lodCount => m_LODs.Length;
        public bool lastLODBillboard { get => m_LastLODIsBillboard; set { m_LastLODIsBillboard = value; ObjectDispatch.Changed(this); } }
        public LODFadeMode fadeMode { get => m_FadeMode; set { m_FadeMode = value; ObjectDispatch.Changed(this); } }
        public bool animateCrossFading { get => m_AnimateCrossFading; set { m_AnimateCrossFading = value; ObjectDispatch.Changed(this); } }

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                m_Enabled = value;
                ObjectDispatch.Changed(this);
            }
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

        /// <summary>The group's size in the world: its size times its largest absolute scale.</summary>
        internal float WorldSize
        {
            get
            {
                var scale = transform.lossyScale;
                return m_Size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            }
        }

        /// <summary>The forced level as a mask of one bit (0: no level forced).</summary>
        internal byte ForcedMask => m_Forced >= 0 && m_Forced < 8 ? (byte)(1 << m_Forced) : (byte)0;

        /// <summary>The levels that list a renderer, one bit per level (of the first eight).</summary>
        internal byte LevelMask(Renderer renderer)
        {
            int mask = 0;
            for (int level = 0; level < m_LODs.Length && level < 8; level++)
            {
                var renderers = m_LODs[level].renderers;
                if (renderers == null) continue;
                foreach (var r in renderers)
                    if (ReferenceEquals(r, renderer))
                    {
                        mask |= 1 << level;
                        break;
                    }
            }
            return (byte)mask;
        }

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
            for (int level = m_LODs.Length - 1; level >= 0; level--)
                Attach(m_LODs[level].renderers, this, level == 0);
            ObjectDispatch.Changed(this);
        }

        void Attach(Renderer[] renderers, LODGroup group, bool first = true)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
                if (r && (group != null || r.m_LODGroup == this))
                {
                    r.m_LODGroup = group;
                    r.m_FirstLOD = first;
                }
        }

        public void ForceLOD(int index)
        {
            m_Forced = index;
            ObjectDispatch.Changed(this);
        }

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
            ObjectDispatch.Changed(this);
        }

        /// <summary>The level a camera draws (-1: none).</summary>
        internal int LevelFor(Camera camera, Vector3 cameraPosition) => LevelFor(camera, cameraPosition, worldReferencePoint, WorldSize);

        /// <summary>The level a camera draws of the group placed with a world reference point and
        /// size (a terrain's trees place their prefab's group).</summary>
        internal int LevelFor(Camera camera, Vector3 cameraPosition, Vector3 reference, float worldSize)
        {
            if (m_LODs.Length == 0) return -1;
            if (m_Forced >= 0) return Math.Min(m_Forced, m_LODs.Length - 1);
            float distance = ViewDistance(camera, cameraPosition, reference);
            for (int i = Math.Max(0, QualitySettings.maximumLODLevel); i < m_LODs.Length; i++)
                if (distance < worldSize / m_LODs[i].screenRelativeTransitionHeight) return i;
            return -1;
        }

        /// <summary>
        /// The distance a level's reach is compared with: the reference point's distance times
        /// the view's height at distance 1 (twice the tangent of half the field of view) over
        /// the LOD bias; for an orthographic camera its height (twice the orthographic size)
        /// over the bias, at any distance. A level is drawn while this is less than the
        /// group's size over the level's height: at equality the next level is (Unity
        /// 6000.0.71f1, tests/projects/lod: the fov 90 sweep's last level ends at 5.0 exactly,
        /// the orthographic one's at size 5.0).
        /// </summary>
        static float ViewDistance(Camera camera, Vector3 cameraPosition, Vector3 reference)
        {
            float bias = QualitySettings.lodBias;
            if (camera.orthographic) return (camera.orthographicSize + camera.orthographicSize) / bias;
            float fieldOfView = camera.fieldOfView;
            // Half the field of view in radians (this float order is our choice; the probe's
            // sweeps give the same levels with the plain product).
            float tangent = Mathf.Tan((fieldOfView / 360f + fieldOfView / 360f) * Mathf.PI * 0.5f);
            float scale = (tangent + tangent) / bias;
            var to = reference - cameraPosition;
            return Mathf.Sqrt((to.z * to.z + to.y * to.y + to.x * to.x) * (scale * scale));
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

        internal override void OnAttached() => s_Live.Add(this);

        internal override void OnEngineEnable() => ObjectDispatch.Changed(this);

        internal override void OnEngineDisable() => ObjectDispatch.Changed(this);

        internal override void OnEngineDestroy()
        {
            s_Live.Remove(this);
            foreach (var lod in m_LODs) Attach(lod.renderers, null);
        }
    }
}
