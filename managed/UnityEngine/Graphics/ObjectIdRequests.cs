using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering
{
    /// <summary>A request for the objects a camera draws, by pixel, into a render texture
    /// (Camera.SubmitRenderRequest).</summary>
    public partial class ObjectIdRequest
    {
        public RenderTexture destination { [return: NotNull] get; set; }
        public int mipLevel { get; set; }
        public CubemapFace face { get; set; }
        public int slice { get; set; }
        public ObjectIdResult result { [return: MaybeNull] get; internal set; }

        public ObjectIdRequest([NotNull] RenderTexture destination, int mipLevel = 0, CubemapFace face = CubemapFace.Unknown, int slice = 0)
        {
            this.destination = destination;
            this.mipLevel = mipLevel;
            this.face = face;
            this.slice = slice;
        }
    }

    /// <summary>What an ObjectIdRequest found: the objects by the id their pixels encode (0: none).</summary>
    public partial class ObjectIdResult
    {
        public Object[] idToObjectMapping { [return: NotNull] get; }

        internal ObjectIdResult(Object[] idToObjectMapping) => this.idToObjectMapping = idToObjectMapping;

        public static int DecodeIdFromColor(Color color) =>
            (int)(color.r * 255f) + ((int)(color.g * 255f) << 8) + ((int)(color.b * 255f) << 16) + ((int)(color.a * 255f) << 24);
    }
}

namespace UnityEngine
{
    public sealed partial class Camera
    {
        /// <summary>
        /// Submits a render request (Scripting API 6000.0 Camera.SubmitRenderRequest; the checks and
        /// messages of Unity's C#). An ObjectIdRequest the pipeline does not take (URP takes none)
        /// is answered by the Editor's own object id pass (<see cref="ObjectIds"/>); any other
        /// request goes to the render pipeline, and warns without one.
        /// </summary>
        public void SubmitRenderRequest<RequestData>(RequestData renderRequest)
        {
            if (renderRequest == null) throw new ArgumentException("SubmitRenderRequests is invoked with invalid renderRequests");
            if (renderRequest is ObjectIdRequest objectIdRequest)
            {
                if (objectIdRequest.destination != null && objectIdRequest.destination.depthStencilFormat == GraphicsFormat.None)
                    Debug.LogWarning("ObjectId Render Request submitted without a depth stencil, which can produce results that are not depth tested correctly");
                // The pipeline is made first when nothing has rendered with it yet.
                if (GraphicsSettings.currentRenderPipeline == null
                    || !RenderPipelineManager.TryPrepareRenderPipeline(GraphicsSettings.currentRenderPipeline)
                    || RenderPipelineManager.currentPipeline?.IsRenderRequestSupported(this, objectIdRequest) != true)
                {
                    var mapping = ObjectIds();
                    DrawObjectIds(objectIdRequest, mapping);
                    objectIdRequest.result = new ObjectIdResult(mapping);
                    return;
                }
            }
            if (GraphicsSettings.currentRenderPipeline == null)
                Debug.LogWarning("Trying to invoke 'SubmitRenderRequest' when no SRP is set. A scriptable render pipeline is needed for this function call");
            else
                RenderPipeline.ProcessRenderRequest(this, renderRequest);
        }

        static readonly List<(float Depth, int Order, Renderer Renderer)> s_ObjectIds = new List<(float, int, Renderer)>();
        static readonly List<Material> s_IdMaterials = new List<Material>();

        /// <summary>
        /// The object id pass: the destination cleared to id 0, then each renderer of the mapping
        /// drawn with its id as its color (the id's bytes as red, green, blue and alpha, unblended), farthest
        /// first, with the camera's view and projection in the camera's pixel rect (Unity 6000.0.71f1
        /// Editor, tests/projects/graphicsgaps objectId.*: silhouettes with and without depth; a camera
        /// without a target renders at the Game view's size, so the destination holds a crop).
        /// </summary>
        void DrawObjectIds(ObjectIdRequest request, Object[] mapping)
        {
            var destination = request.destination;
            if (destination == null) return;
            // The Editor's scene picking shader: _SelectionID as it is (no vertex color, alpha
            // kept), opaque, depth tested and written, back faces culled.
            var shader = Shader.Find("Hidden/OpaqueSelection");
            if (shader == null)
            {
                Unbound.Runtime.Stubs.Ignored("Camera.SubmitRenderRequest", "draws no object ids (the Hidden/OpaqueSelection shader is missing)");
                return;
            }
            var cmd = new CommandBuffer { name = "ObjectId" };
            cmd.SetRenderTarget(destination, request.mipLevel, request.face, request.slice);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.SetViewport(pixelRect);
            cmd.SetViewProjectionMatrices(worldToCameraMatrix, projectionMatrix);
            for (int id = 1; id < mapping.Length; id++)
            {
                if (mapping[id] is not Renderer renderer) continue;
                if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                {
                    // Sprites, particles, lines and trails build their geometry for a camera and
                    // carry their own colors: not measured.
                    Unbound.Runtime.Stubs.Ignored("Camera.SubmitRenderRequest." + renderer.GetType().Name, "draws no object id for a " + renderer.GetType().Name + " (only mesh renderers are drawn)");
                    continue;
                }
                while (s_IdMaterials.Count < id) s_IdMaterials.Add(null);
                var material = s_IdMaterials[id - 1];
                if (material == null || material.shader != shader)
                {
                    if (material != null) DestroyImmediate(material);
                    s_IdMaterials[id - 1] = material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                // The id's bytes as they are: a vector is not converted to linear, so an sRGB
                // destination of a Linear project encodes them, as the Editor's does (id 4 reads 34;
                // tests/projects/objectidspace).
                material.SetVector("_SelectionID", new Vector4((byte)id, (byte)(id >> 8), (byte)(id >> 16), (byte)(id >> 24)) / 255f);
                int submeshes = Math.Max(1, renderer.sharedMaterials.Length);
                for (int submesh = 0; submesh < submeshes; submesh++) cmd.DrawRenderer(renderer, material, submesh, 0);
            }
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }

        /// <summary>
        /// The objects of the Editor's object id pass for the camera: none (id 0), then the
        /// renderers the camera sees (enabled, on a layer of its culling mask, bounds in its
        /// frustum), farthest first by depth along the view (Unity 6000.0.71f1 Editor,
        /// tests/projects/graphicsgaps objectId.*.mapping).
        /// </summary>
        Object[] ObjectIds()
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(this);
            var view = worldToCameraMatrix;
            s_ObjectIds.Clear();
            foreach (var renderer in Unbound.Runtime.RenderRegistry.Renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if ((cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                var bounds = renderer.bounds;
                if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                s_ObjectIds.Add((-view.MultiplyPoint3x4(bounds.center).z, s_ObjectIds.Count, renderer));
            }
            // Renderers at the same depth keep the order they were turned on (unmeasured).
            s_ObjectIds.Sort((a, b) => b.Depth != a.Depth ? b.Depth.CompareTo(a.Depth) : a.Order.CompareTo(b.Order));
            var ordered = s_ObjectIds;
            var mapping = new Object[ordered.Count + 1];
            for (int i = 0; i < ordered.Count; i++) mapping[i + 1] = ordered[i].Renderer;
            s_ObjectIds.Clear();
            return mapping;
        }
    }
}
