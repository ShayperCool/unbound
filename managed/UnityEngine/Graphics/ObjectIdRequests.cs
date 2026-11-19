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
                if (GraphicsSettings.currentRenderPipeline == null || !RenderPipelineManager.currentPipeline.IsRenderRequestSupported(this, objectIdRequest))
                {
                    objectIdRequest.result = new ObjectIdResult(ObjectIds());
                    return;
                }
            }
            if (GraphicsSettings.currentRenderPipeline == null)
                Debug.LogWarning("Trying to invoke 'SubmitRenderRequest' when no SRP is set. A scriptable render pipeline is needed for this function call");
            else
                RenderPipeline.ProcessRenderRequest(this, renderRequest);
        }

        static readonly List<(float Depth, int Order, Renderer Renderer)> s_ObjectIds = new List<(float, int, Renderer)>();

        /// <summary>
        /// The objects of the Editor's object id pass for the camera: none (id 0), then the
        /// renderers the camera sees (enabled, on a layer of its culling mask, bounds in its
        /// frustum), farthest first by depth along the view (Unity 6000.0.71f1 Editor,
        /// tests/projects/graphicsgaps objectId.*.mapping). The destination is left as it is: the
        /// Editor's reads back zero in Play mode (objectId.*.ids).
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
