using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// Projects a material onto the renderers in its frustum, as the built-in pipeline does: each
    /// renderer a camera draws whose bounds touch the frustum (and whose layer is not ignored) is
    /// drawn again with the projector's material, its shader reading <c>unity_Projector</c> (object
    /// space to the projector's texture space, x and y in 0..1 over the frustum) and
    /// <c>unity_ProjectorClip</c> (x: the distance from the near plane over the depth range). The
    /// projector's scale is not part of either (measured: tests/projects/builtinfx projector.*).
    /// </summary>
    [ClassId(119)]
    public sealed partial class Projector : Behaviour
    {
        internal static readonly List<Projector> s_Enabled = new List<Projector>();

        float m_NearClipPlane = 0.1f;
        float m_FarClipPlane = 100f;
        float m_FieldOfView = 60f;
        float m_AspectRatio = 1f;
        bool m_Orthographic;
        float m_OrthographicSize = 10f;
        int m_IgnoreLayers;
        Material m_Material;

        public Projector() { }

        public float nearClipPlane { get => m_NearClipPlane; set => m_NearClipPlane = value; }
        public float farClipPlane { get => m_FarClipPlane; set => m_FarClipPlane = value; }
        public float fieldOfView { get => m_FieldOfView; set => m_FieldOfView = value; }
        public float aspectRatio { get => m_AspectRatio; set => m_AspectRatio = value; }
        public bool orthographic { get => m_Orthographic; set => m_Orthographic = value; }
        public float orthographicSize { get => m_OrthographicSize; set => m_OrthographicSize = value; }
        public int ignoreLayers { get => m_IgnoreLayers; set => m_IgnoreLayers = value; }
        public Material material { get => m_Material; set => m_Material = value; }

        [Obsolete("Property isOrthoGraphic has been deprecated. Use orthographic instead (UnityUpgradable) -> orthographic")]
        public bool isOrthoGraphic { get => m_Orthographic; set => m_Orthographic = value; }

        [Obsolete("Property orthoGraphicSize has been deprecated. Use orthographicSize instead (UnityUpgradable) -> orthographicSize")]
        public float orthoGraphicSize { get => m_OrthographicSize; set => m_OrthographicSize = value; }

        internal override void OnEngineEnable()
        {
            if (!s_Enabled.Contains(this)) s_Enabled.Add(this);
        }

        internal override void OnEngineDisable() => s_Enabled.Remove(this);

        internal override void OnEngineDestroy() => s_Enabled.Remove(this);

        /// <summary>World to the projector's clip space (OpenGL), its transform without scale.</summary>
        internal Matrix4x4 ViewProjection(out Matrix4x4 worldToProjector)
        {
            var t = transform;
            worldToProjector = Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse;
            var projection = m_Orthographic
                ? Matrix4x4.Ortho(-m_OrthographicSize * m_AspectRatio, m_OrthographicSize * m_AspectRatio, -m_OrthographicSize, m_OrthographicSize, m_NearClipPlane, m_FarClipPlane)
                : Matrix4x4.Perspective(m_FieldOfView, m_AspectRatio, m_NearClipPlane, m_FarClipPlane);
            return projection * Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * worldToProjector;
        }

        /// <summary>The world-space matrices the projector's shader reads (times the object's
        /// matrix): clip space biased into 0..1 texture space, and the depth from the near plane
        /// over the depth range (in x and z; y: the height along the projector's up).</summary>
        internal void Matrices(out Matrix4x4 projector, out Matrix4x4 clip)
        {
            var viewProjection = ViewProjection(out var worldToProjector);
            var bias = Matrix4x4.identity;
            bias.m00 = 0.5f;
            bias.m11 = 0.5f;
            bias.m03 = 0.5f;
            bias.m13 = 0.5f;
            projector = bias * viewProjection;
            float range = m_FarClipPlane - m_NearClipPlane;
            float inverse = range != 0f ? 1f / range : 0f;
            var depth = Matrix4x4.zero;
            // Local z (the projector looks down +z) to x and z, local y to y.
            depth.m02 = inverse;
            depth.m03 = -m_NearClipPlane * inverse;
            depth.m11 = inverse;
            depth.m13 = -m_NearClipPlane * inverse;
            depth.m22 = inverse;
            depth.m23 = -m_NearClipPlane * inverse;
            depth.m33 = 1f;
            clip = depth * worldToProjector;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var o = (Projector)source;
            m_NearClipPlane = o.m_NearClipPlane;
            m_FarClipPlane = o.m_FarClipPlane;
            m_FieldOfView = o.m_FieldOfView;
            m_AspectRatio = o.m_AspectRatio;
            m_Orthographic = o.m_Orthographic;
            m_OrthographicSize = o.m_OrthographicSize;
            m_IgnoreLayers = o.m_IgnoreLayers;
            m_Material = remap(o.m_Material) as Material;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_NearClipPlane = body.Float("m_NearClipPlane", 0.1f);
            m_FarClipPlane = body.Float("m_FarClipPlane", 100f);
            m_FieldOfView = body.Float("m_FieldOfView", 60f);
            m_AspectRatio = body.Float("m_AspectRatio", 1f);
            m_Orthographic = body.Bool("m_Orthographic");
            m_OrthographicSize = body.Float("m_OrthographicSize", 10f);
            m_IgnoreLayers = (int)(body.Get("m_IgnoreLayers")?.Long("m_Bits", 0) ?? 0);
            var material = body.Ref("m_Material");
            m_Material = material.IsNull ? null : resolver.Resolve(material, typeof(Material)) as Material;
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    internal static partial class FrameBuilder
    {
        static readonly int s_ProjectorId = Shader.PropertyToID("unity_Projector");
        static readonly int s_ProjectorClipId = Shader.PropertyToID("unity_ProjectorClip");
        static readonly List<DrawItem> s_ProjectorScratch = new List<DrawItem>();

        /// <summary>The enabled projectors' draws over the camera's visible renderers (meshes,
        /// skinned meshes, terrains): each of the renderer's submeshes again, with the projector's
        /// material, at its queue.</summary>
        static void CollectProjectorDraws(CameraContext camera, List<Renderer> visible, List<DrawItem> into)
        {
            if (Projector.s_Enabled.Count == 0) return;
            foreach (var projector in Projector.s_Enabled.ToArray())
            {
                if (!projector || !projector.isActiveAndEnabled || projector.material == null) continue;
                var frustum = projector.ViewProjection(out _);
                projector.Matrices(out var toTexture, out var toClip);
                var material = projector.material;
                foreach (var renderer in visible)
                {
                    if (!renderer || !IsMeshLike(renderer)) continue;
                    if ((projector.ignoreLayers & (1 << renderer.m_GameObject.m_Layer)) != 0) continue;
                    if (!InFrustum(frustum, renderer.bounds)) continue;
                    s_ProjectorScratch.Clear();
                    try { renderer.CollectDraws(camera, s_ProjectorScratch); }
                    catch (Exception e) { Debug.LogException(e, renderer); continue; }
                    var done = new HashSet<(ulong, int)>();
                    foreach (var draw in s_ProjectorScratch)
                    {
                        if (!done.Add((draw.Mesh, draw.Submesh))) continue;
                        var properties = draw.Properties != null ? new Dictionary<int, object>(draw.Properties) : new Dictionary<int, object>();
                        properties[s_ProjectorId] = toTexture * draw.Model;
                        properties[s_ProjectorClipId] = toClip * draw.Model;
                        into.Add(new DrawItem
                        {
                            Mesh = draw.Mesh,
                            Submesh = draw.Submesh,
                            Material = material,
                            Model = draw.Model,
                            Color = Color.white,
                            Queue = material.renderQueue,
                            SortingLayer = draw.SortingLayer,
                            SortingOrder = draw.SortingOrder,
                            Distance = draw.Distance,
                            Sequence = NextSequence(),
                            Properties = properties,
                            Setup = draw.Setup,
                            Bounds = renderer.bounds,
                            Layer = renderer.m_GameObject.m_Layer,
                        });
                    }
                }
            }
        }
    }
}
