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
        internal static CanvasSystem Instance => s_Instance;
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
            s_RenderOrder.Remove(canvas);
        }

        internal static void MarkLayoutDirty(Canvas canvas) => DriveRootRect(canvas);

        /// <summary><c>Canvas.renderOrder</c>: the canvas's place in the drawing order (overlay
        /// canvases; camera and world ones are ordered among their camera's draws).</summary>
        internal static int RenderOrderOf(Canvas canvas) =>
            s_RenderOrder.TryGetValue(canvas, out var order) ? order : s_Canvases.IndexOf(canvas);

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
            // The hierarchy below is marked changed only when a driven value changes: marking it
            // every frame re-ran every child rect's change test (most of an idle UI frame).
            var before = (rect.m_AnchorMin, rect.m_AnchorMax, rect.m_Pivot, rect.m_SizeDelta, rect.m_LocalRotation, rect.m_LocalScale, rect.m_LocalPosition, rect.m_AnchoredPosition);
            rect.m_AnchorMin = Vector2.zero;
            rect.m_AnchorMax = Vector2.zero;
            rect.m_Pivot = new Vector2(0.5f, 0.5f);
            bool sizeChanged = rect.m_SizeDelta != size;
            rect.m_SizeDelta = size;
            if (camera == null)
            {
                rect.m_LocalRotation = Quaternion.identity;
                rect.m_LocalScale = new Vector3(scale, scale, scale);
                // The canvas keeps its depth: overlay canvases of equal sorting order draw farthest
                // first (see DrawnCanvases).
                SetWorldPosition(rect, new Vector3(pixels.x + pixels.width / 2f, pixels.y + pixels.height / 2f, rect.position.z));
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
            var after = (rect.m_AnchorMin, rect.m_AnchorMax, rect.m_Pivot, rect.m_SizeDelta, rect.m_LocalRotation, rect.m_LocalScale, rect.m_LocalPosition, rect.m_AnchoredPosition);
            if (!before.Equals(after)) rect.MarkChanged();
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
                if (c != 0) return c;
                // Then the farther canvas first (games order their overlay screens by z).
                c = b.transform.position.z.CompareTo(a.transform.position.z);
                return c != 0 ? c : s_Order[a].CompareTo(s_Order[b]);
            });
            return list;
        }

        /// <summary>The overlay canvases drawn on their own, in drawing order (which is their
        /// <c>renderOrder</c>).</summary>
        internal static List<Canvas> OverlayCanvases()
        {
            var overlay = DrawnCanvases(root => root.m_RenderMode == RenderMode.ScreenSpaceOverlay ||
                                                (root.m_RenderMode == RenderMode.ScreenSpaceCamera && root.m_Camera == null));
            for (int i = 0; i < overlay.Count; i++) s_RenderOrder[overlay[i]] = i;
            return overlay;
        }

        /// <summary>Each drawn canvas's place in the last frame's drawing order.</summary>
        static readonly Dictionary<Canvas, int> s_RenderOrder = new Dictionary<Canvas, int>();

        static int Depth(Canvas c)
        {
            int depth = 0;
            for (var t = c.transform.m_Parent; t != null; t = t.m_Parent) depth++;
            return depth;
        }

        public void CollectOverlayDraws(List<FrameBuilder.DrawItem> into)
        {
            int depth = 0;
            foreach (var canvas in OverlayCanvases())
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
            int layer = canvas.cachedSortingLayerValue;
            int order = canvas.sortingOrder;
            int first = into.Count;
            s_HasDrawnBounds = false;
            Walk(canvas.transform, canvas, root, camera, into, layer, order, ref depth, isCanvasRoot: true);
            if (camera == null || first == into.Count) return;
            // Among the camera's draws a canvas sorts as one unit, at the centre of the bounds of
            // what it draws (not at its own transform): a canvas whose content lies behind its root
            // sorts behind (measured in Unity 6, tests/projects/sorting).
            float distance = FrameBuilder.Distance(camera, s_DrawnBounds.center);
            for (int i = first; i < into.Count; i++)
            {
                var item = into[i];
                item.Distance = distance;
                into[i] = item;
            }
        }

        /// <summary>The world bounds of the canvas being collected, so far.</summary>
        static Bounds s_DrawnBounds;
        static bool s_HasDrawnBounds;

        static void AddDrawnBounds(Bounds local, Matrix4x4 model)
        {
            var c = model.MultiplyPoint3x4(local.center);
            var e = local.extents;
            var x = model.MultiplyVector(new Vector3(e.x, 0f, 0f));
            var y = model.MultiplyVector(new Vector3(0f, e.y, 0f));
            var z = model.MultiplyVector(new Vector3(0f, 0f, e.z));
            var extents = new Vector3(
                Math.Abs(x.x) + Math.Abs(y.x) + Math.Abs(z.x),
                Math.Abs(x.y) + Math.Abs(y.y) + Math.Abs(z.y),
                Math.Abs(x.z) + Math.Abs(y.z) + Math.Abs(z.z));
            var world = new Bounds(c, extents * 2f);
            if (s_HasDrawnBounds) s_DrawnBounds.Encapsulate(world);
            else { s_DrawnBounds = world; s_HasDrawnBounds = true; }
        }

        void Walk(Transform t, Canvas canvas, Canvas root, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into,
            int layer, int order, ref int depth, bool isCanvasRoot)
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
                // Every renderer of an active object has its depth, in hierarchy order, drawn or
                // not: without a mesh or material, culled, transparent, its graphic disabled
                // (measured, 6000.0). An invisible raycast target (a Graphic that never makes its
                // mesh) is hit so.
                cr.m_AbsoluteDepth = depth++;
                if (cr.m_Cull || cr.m_Mesh == null || cr.m_MaterialCount == 0) continue;
                float alpha = cr.m_Color.a * cr.m_Alpha * cr.GetInheritedAlpha();
                if (cr.m_CullTransparentMesh && alpha <= 0f) continue;
                if (cr.m_CullTransparentMesh && Transparent(cr.m_Mesh)) continue;
                var color = new Color(cr.m_Color.r, cr.m_Color.g, cr.m_Color.b, alpha);
                ulong mesh = cr.m_Mesh.Sync();
                var model = t.localToWorldMatrix;
                ulong texture = cr.m_Texture != null ? TextureHandles.Of(cr.m_Texture) : 0;
                bool clip = cr.m_HasRectClipping;
                Rect clipRect = clip ? ClipToScreen(root, cr.m_ClipRect, camera) : default;
                int submeshes = Math.Max(1, cr.m_Mesh.subMeshCount);
                if (camera != null) AddDrawnBounds(cr.m_Mesh.bounds, model);
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
                        Sequence = FrameBuilder.NextSequence(),
                    });
                }
            }
            foreach (var child in t.m_Children)
                Walk(child, canvas, root, camera, into, layer, order, ref depth, isCanvasRoot: false);
            // A mask's pop instructions after its children: its mesh again with the materials that
            // undo its stencil (uGUI's Mask sets them; they write no colour). Without them a mask's
            // stencil stays for everything drawn after it.
            foreach (var c in go.m_Components)
            {
                if (!(c is CanvasRenderer cr) || cr.m_Destroyed || cr.m_PopMaterialCount == 0) continue;
                if (cr.m_Cull || cr.m_Mesh == null || cr.m_MaterialCount == 0) continue;
                ulong mesh = cr.m_Mesh.Sync();
                var model = t.localToWorldMatrix;
                ulong texture = cr.m_Texture != null ? TextureHandles.Of(cr.m_Texture) : 0;
                bool clip = cr.m_HasRectClipping;
                Rect clipRect = clip ? ClipToScreen(root, cr.m_ClipRect, camera) : default;
                for (int i = 0; i < cr.m_PopMaterialCount && i < cr.m_PopMaterials.Length; i++)
                {
                    var material = cr.m_PopMaterials[i];
                    if (material == null) continue;
                    into.Add(new FrameBuilder.DrawItem
                    {
                        Mesh = mesh,
                        Submesh = 0,
                        Material = material,
                        Model = model,
                        Color = cr.m_Color,
                        MainTexture = texture,
                        Clip = clipRect,
                        ClipEnabled = clip,
                        Queue = material.renderQueue,
                        SortingLayer = layer,
                        SortingOrder = order,
                        Sequence = FrameBuilder.NextSequence(),
                    });
                }
            }
        }

        /// <summary>Whether every vertex of a mesh is fully transparent (what
        /// <c>cullTransparentMesh</c> culls: a graphic's colour is in its vertices).</summary>
        static bool Transparent(Mesh mesh)
        {
            var colors = mesh.m_Colors;
            if (colors == null || colors.Length == 0) return false;
            foreach (var c in colors)
                if (c.a != 0) return false;
            return true;
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
