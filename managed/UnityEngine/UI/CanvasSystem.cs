using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Canvases: drives root canvas rects from the screen or their camera, sends willRenderCanvases
    /// (when uGUI rebuilds layouts and meshes), and turns CanvasRenderers into draws: overlay
    /// canvases on top of everything by sorting order, camera and world canvases inside their
    /// cameras' transparent queue; within a canvas, hierarchy order.
    /// </summary>
    internal sealed class CanvasSystem : FrameBuilder.IDrawSource
    {
        static readonly CanvasSystem s_Instance = new CanvasSystem();
        static readonly List<Canvas> s_Canvases = new List<Canvas>();
        static Material s_DefaultMaterial;
        static readonly Vector3[] s_Corners = new Vector3[4];
        static long s_RegistrationOrder;
        static readonly Dictionary<Canvas, long> s_Order = new Dictionary<Canvas, long>();

        static CanvasSystem()
        {
            FrameBuilder.Sources.Add(s_Instance);
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.EarlyUpdate.UpdateCanvasRectTransform), DriveRootRects);
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.PlayerUpdateCanvases), SendWillRenderCanvases);
        }

        internal static Material DefaultMaterial
        {
            get
            {
                if (s_DefaultMaterial == null)
                {
                    using (ObjectRegistry.AsAssets())
                        s_DefaultMaterial = new Material(Shader.Find("UI/Default")) { name = "Default UI Material" };
                    s_DefaultMaterial.IsPersistentAsset = true;
                }
                return s_DefaultMaterial;
            }
        }

        internal static void Register(Canvas canvas)
        {
            if (!s_Canvases.Contains(canvas))
            {
                s_Canvases.Add(canvas);
                s_Order[canvas] = s_RegistrationOrder++;
            }
            DriveRootRect(canvas);
        }

        internal static void Unregister(Canvas canvas)
        {
            s_Canvases.Remove(canvas);
            s_Order.Remove(canvas);
        }

        internal static void MarkLayoutDirty(Canvas canvas) => DriveRootRect(canvas);

        internal static int RenderOrderOf(Canvas canvas) => s_Canvases.IndexOf(canvas);

        internal static void SendWillRenderCanvases()
        {
            DriveRootRects();
            Canvas.InvokePreWillRender();
            Canvas.InvokeWillRender();
        }

        static void DriveRootRects()
        {
            foreach (var canvas in s_Canvases.ToArray())
                if (canvas && canvas.isActiveAndEnabled) DriveRootRect(canvas);
        }

        /// <summary>Overlay and camera canvases cover the screen: their rect is the screen size divided
        /// by the scale factor, placed in front of the camera (or in pixel space).</summary>
        static void DriveRootRect(Canvas canvas)
        {
            if (canvas == null || canvas.m_Destroyed || !canvas.isRootCanvas) return;
            if (canvas.m_RenderMode == RenderMode.WorldSpace) return;
            if (!(canvas.transform is RectTransform rect)) return;
            float scale = canvas.m_ScaleFactor;
            var camera = canvas.m_RenderMode == RenderMode.ScreenSpaceCamera ? canvas.m_Camera : null;
            var pixels = camera != null ? camera.pixelRect : new Rect(0, 0, Screen.width, Screen.height);
            var size = new Vector2(pixels.width / scale, pixels.height / scale);
            rect.m_AnchorMin = Vector2.zero;
            rect.m_AnchorMax = Vector2.zero;
            rect.m_Pivot = new Vector2(0.5f, 0.5f);
            bool sizeChanged = rect.m_SizeDelta != size;
            rect.m_SizeDelta = size;
            if (camera == null)
            {
                rect.m_LocalRotation = Quaternion.identity;
                rect.m_LocalScale = new Vector3(scale, scale, scale);
                SetWorldPosition(rect, new Vector3(pixels.x + pixels.width / 2f, pixels.y + pixels.height / 2f, 0f));
            }
            else
            {
                var ct = camera.transform;
                float distance = canvas.m_PlaneDistance;
                float height = camera.orthographic
                    ? camera.orthographicSize * 2f
                    : 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float unit = height / size.y;
                rect.m_LocalScale = new Vector3(unit, unit, unit);
                rect.m_LocalRotation = ct.rotation;
                SetWorldPosition(rect, ct.position + ct.forward * distance);
            }
            rect.MarkChanged();
            if (sizeChanged) UiMessages.DimensionsChanged(rect);
        }

        static void SetWorldPosition(RectTransform rect, Vector3 world)
        {
            var parent = rect.m_Parent;
            var local = parent == null ? world : parent.worldToLocalMatrix.MultiplyPoint3x4(world);
            rect.m_AnchoredPosition = new Vector2(local.x, local.y);
            rect.m_LocalPosition = local;
        }

        /// <summary>Canvases drawn on their own: roots and nested canvases that override sorting.</summary>
        static List<Canvas> DrawnCanvases(Func<Canvas, bool> mode)
        {
            var list = new List<Canvas>();
            foreach (var canvas in s_Canvases)
            {
                if (!canvas || !canvas.isActiveAndEnabled) continue;
                var root = canvas.rootCanvas;
                if (canvas != root && !canvas.m_OverrideSorting) continue;
                if (!mode(root)) continue;
                list.Add(canvas);
            }
            list.Sort((a, b) =>
            {
                int c = a.cachedSortingLayerValue.CompareTo(b.cachedSortingLayerValue);
                if (c != 0) return c;
                c = a.sortingOrder.CompareTo(b.sortingOrder);
                if (c != 0) return c;
                // Nested canvases draw after their root at equal order.
                c = Depth(a).CompareTo(Depth(b));
                return c != 0 ? c : s_Order[a].CompareTo(s_Order[b]);
            });
            return list;
        }

        static int Depth(Canvas c)
        {
            int depth = 0;
            for (var t = c.transform.m_Parent; t != null; t = t.m_Parent) depth++;
            return depth;
        }

        public void CollectOverlayDraws(List<FrameBuilder.DrawItem> into)
        {
            int depth = 0;
            foreach (var canvas in DrawnCanvases(root => root.m_RenderMode == RenderMode.ScreenSpaceOverlay ||
                                                         (root.m_RenderMode == RenderMode.ScreenSpaceCamera && root.m_Camera == null)))
                CollectCanvas(canvas, null, into, ref depth);
        }

        public void CollectCameraDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            int depth = 0;
            foreach (var canvas in DrawnCanvases(root =>
                         (root.m_RenderMode == RenderMode.ScreenSpaceCamera && root.m_Camera == camera.Camera) ||
                         root.m_RenderMode == RenderMode.WorldSpace))
            {
                if ((camera.Camera.cullingMask & (1 << canvas.m_GameObject.m_Layer)) == 0)
                {
                    if (Trace.RenderDetailOn && Time.frameCount % 60 == 1)
                        Trace.Render($"  canvas '{canvas.name}' culled by camera '{camera.Camera.name}' (layer {canvas.m_GameObject.m_Layer})");
                    continue;
                }
                CollectCanvas(canvas, camera, into, ref depth);
            }
        }

        void CollectCanvas(Canvas canvas, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into, ref int depth)
        {
            int before = into.Count;
            CollectCanvasDraws(canvas, camera, into, ref depth);
            if (Trace.RenderDetailOn && Time.frameCount % 60 == 1)
                Trace.Render($"  canvas '{canvas.name}' ({canvas.m_RenderMode}, camera '{camera?.Camera?.name ?? "overlay"}', layer {canvas.m_GameObject.m_Layer}, order {canvas.sortingOrder}): {into.Count - before} draws");
        }

        void CollectCanvasDraws(Canvas canvas, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into, ref int depth)
        {
            var root = canvas.rootCanvas;
            float distance = camera != null ? FrameBuilder.Distance(camera, canvas.transform.position) : 0f;
            int layer = canvas.cachedSortingLayerValue;
            int order = canvas.sortingOrder;
            Walk(canvas.transform, canvas, root, camera, into, layer, order, distance, ref depth, isCanvasRoot: true);
        }

        void Walk(Transform t, Canvas canvas, Canvas root, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into,
            int layer, int order, float distance, ref int depth, bool isCanvasRoot)
        {
            var go = t.m_GameObject;
            if (!go.m_IsActive) return;
            if (!isCanvasRoot)
            {
                // A nested canvas that sorts on its own is drawn separately.
                foreach (var c in go.m_Components)
                    if (c is Canvas nested && nested.m_Enabled && !nested.m_Destroyed && nested.m_OverrideSorting) return;
            }
            foreach (var c in go.m_Components)
            {
                if (!(c is CanvasRenderer cr) || cr.m_Destroyed) continue;
                cr.m_AbsoluteDepth = -1;
                if (cr.m_Cull || cr.m_Mesh == null || cr.m_MaterialCount == 0) continue;
                float alpha = cr.m_Color.a * cr.m_Alpha * cr.GetInheritedAlpha();
                if (cr.m_CullTransparentMesh && alpha <= 0f) continue;
                var color = new Color(cr.m_Color.r, cr.m_Color.g, cr.m_Color.b, alpha);
                ulong mesh = cr.m_Mesh.Sync();
                var model = t.localToWorldMatrix;
                ulong texture = cr.m_Texture != null ? TextureHandles.Of(cr.m_Texture) : 0;
                bool clip = cr.m_HasRectClipping;
                Rect clipRect = clip ? ClipToScreen(root, cr.m_ClipRect, camera) : default;
                int submeshes = Math.Max(1, cr.m_Mesh.subMeshCount);
                cr.m_AbsoluteDepth = depth++;
                for (int i = 0; i < cr.m_MaterialCount; i++)
                {
                    var material = cr.GetMaterial(i) ?? DefaultMaterial;
                    into.Add(new FrameBuilder.DrawItem
                    {
                        Mesh = mesh,
                        Submesh = Math.Min(i, submeshes - 1),
                        Material = material,
                        Model = model,
                        Color = color,
                        MainTexture = texture,
                        Clip = clipRect,
                        ClipEnabled = clip,
                        Queue = material.renderQueue,
                        SortingLayer = layer,
                        SortingOrder = order,
                        Distance = distance,
                        Sequence = FrameBuilder.NextSequence(),
                    });
                }
            }
            foreach (var child in t.m_Children)
                Walk(child, canvas, root, camera, into, layer, order, distance, ref depth, isCanvasRoot: false);
        }

        /// <summary>A clip rect in the root canvas's space, as pixels on the screen.</summary>
        static Rect ClipToScreen(Canvas root, Rect clip, FrameBuilder.CameraContext camera)
        {
            var m = root.transform.localToWorldMatrix;
            s_Corners[0] = new Vector3(clip.xMin, clip.yMin, 0f);
            s_Corners[1] = new Vector3(clip.xMin, clip.yMax, 0f);
            s_Corners[2] = new Vector3(clip.xMax, clip.yMax, 0f);
            s_Corners[3] = new Vector3(clip.xMax, clip.yMin, 0f);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var world = m.MultiplyPoint3x4(s_Corners[i]);
                var screen = camera != null ? camera.Camera.WorldToScreenPoint(world) : world;
                xMin = Math.Min(xMin, screen.x);
                yMin = Math.Min(yMin, screen.y);
                xMax = Math.Max(xMax, screen.x);
                yMax = Math.Max(yMax, screen.y);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        internal static void EnsureRegistered() { }
    }
}
