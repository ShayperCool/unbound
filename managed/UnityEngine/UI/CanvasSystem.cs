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
    internal sealed partial class CanvasSystem : FrameBuilder.IDrawSource
    {
        static readonly CanvasSystem s_Instance = new CanvasSystem();
        internal static CanvasSystem Instance => s_Instance;
        static readonly List<Canvas> s_Canvases = new List<Canvas>();
        static Material s_DefaultMaterial;
        static readonly Vector3[] s_Corners = new Vector3[4];
        static long s_RegistrationOrder;
        static readonly Dictionary<Canvas, long> s_Order = new Dictionary<Canvas, long>();
        static readonly int s_AlphaTexProperty = Shader.PropertyToID("_AlphaTex");

        static CanvasSystem()
        {
            FrameBuilder.Sources.Add(s_Instance);
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.EarlyUpdate.UpdateCanvasRectTransform), DriveRootRects);
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.PlayerUpdateCanvases), UpdateCanvases);
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
            Canvas.InvokePreWillRender();
            Canvas.InvokeWillRender();
        }

        /// <summary>The frame's canvas update: willRenderCanvases, then the renderers' depths
        /// (<c>Canvas.ForceUpdateCanvases</c> sends the events only and numbers nothing, as
        /// measured: tests/projects/uidepth <c>forced.now</c>).</summary>
        static void UpdateCanvases()
        {
            SendWillRenderCanvases();
            UpdateDepths();
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
            if (canvas == null || canvas.m_Destroyed || !canvas.isActiveAndEnabled) return;
            if (!(canvas.transform is RectTransform drivenRect)) return;
            if (!canvas.isRootCanvas) return;
            if (canvas.m_RenderMode == RenderMode.WorldSpace)
            {
                if (drivenRect.m_DrivenBy == canvas) { drivenRect.m_DrivenBy = null; drivenRect.m_DrivenProperties = DrivenTransformProperties.None; }
                return;
            }
            if (!(canvas.transform is RectTransform rect)) return;
            float scale = canvas.m_ScaleFactor;
            var camera = canvas.m_RenderMode == RenderMode.ScreenSpaceCamera ? canvas.m_Camera : null;
            var pixels = camera != null ? camera.pixelRect : new Rect(0, 0, Screen.width, Screen.height);
            var size = new Vector2(pixels.width / scale, pixels.height / scale);
            rect.m_DrivenBy = canvas;
            rect.m_DrivenProperties = DrivenTransformProperties.All;
            // The hierarchy below is marked changed only when a driven value changes: marking it
            // every frame re-ran every child rect's change test (most of an idle UI frame).
            var before = (rect.m_AnchorMin, rect.m_AnchorMax, rect.m_Pivot, rect.m_SizeDelta, rect.m_LocalRotation, rect.m_LocalScale, rect.m_LocalPosition, rect.m_AnchoredPosition);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            if (camera == null)
            {
                SetWorldPose(rect, new Vector3(pixels.x + pixels.width / 2f, pixels.y + pixels.height / 2f, 0f),
                    Quaternion.identity, scale);
            }
            else
            {
                var ct = camera.transform;
                float distance = canvas.m_PlaneDistance;
                float height = camera.orthographic
                    ? camera.orthographicSize * 2f
                    : 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float unit = height / size.y;
                SetWorldPose(rect, ct.position + ct.forward * distance, ct.rotation, unit);
            }
            var after = (rect.m_AnchorMin, rect.m_AnchorMax, rect.m_Pivot, rect.m_SizeDelta, rect.m_LocalRotation, rect.m_LocalScale, rect.m_LocalPosition, rect.m_AnchoredPosition);
            if (!before.Equals(after)) rect.MarkChanged();
            // A root update also releases the driver of canvases which have become nested.
            foreach (var nested in s_Canvases.ToArray())
                if (nested != canvas && nested.rootCanvas == canvas) ReleaseNestedDriver(nested);
        }

        static void ReleaseNestedDriver(Canvas canvas)
        {
            if (canvas.transform is RectTransform rect && rect.m_DrivenBy == canvas)
            {
                rect.m_DrivenBy = null;
                rect.m_DrivenProperties = DrivenTransformProperties.None;
            }
        }

        static void SetWorldPose(RectTransform rect, Vector3 world, Quaternion rotation, float scale)
        {
            var parent = rect.m_Parent;
            var local = parent == null ? world : parent.worldToLocalMatrix.MultiplyPoint3x4(world);
            var reference = rect.AnchorReference();
            rect.m_AnchoredPosition = new Vector2(local.x - reference.x, local.y - reference.y);
            rect.m_LocalPosition = local;
            if (parent == null)
            {
                rect.m_LocalRotation = rotation;
                rect.m_LocalScale = Vector3.one * scale;
            }
            else
            {
                var parentScale = parent.lossyScale;
                rect.m_LocalRotation = QuaternionMath.Normalize(Transform.ReflectRotation(Quaternion.Inverse(parent.rotation) * rotation, parentScale));
                // Unity projects the parent's basis onto the requested world rotation before
                // solving the scale. Dividing by parent.lossyScale fails under rotated,
                // non-uniform scale because that basis contains shear.
                var basis = Matrix4x4.Rotate(Quaternion.Inverse(rotation)) * parent.localToWorldMatrix * Matrix4x4.Rotate(rect.m_LocalRotation);
                rect.m_LocalScale = new Vector3(scale / basis.m00, scale / basis.m11, scale / basis.m22);
            }
        }

        /// <summary>Canvases drawn on their own: roots and nested canvases that override sorting.</summary>
        static List<Canvas> DrawnCanvases(Func<Canvas, bool> mode)
        {
            var list = new List<Canvas>();
            foreach (var canvas in s_Canvases)
            {
                if (!canvas || !canvas.isActiveAndEnabled || UnderDisabledCanvas(canvas)) continue;
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

        /// <summary>The overlay canvases of Display 1, the one the Editor's Game view shows (measured:
        /// tests/projects/display, an overlay canvas on Display 2 is not drawn).</summary>
        public void CollectOverlayDraws(List<FrameBuilder.DrawItem> into)
        {
            foreach (var canvas in OverlayCanvases())
            {
                var root = canvas.rootCanvas;
                if ((root != null ? root : canvas).m_TargetDisplay != 0) continue;
                CollectCanvas(canvas, null, into);
            }
        }

        public void CollectCameraDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            // A Preview camera's scriptable culling draws no canvases unless UI geometry was
            // emitted for it; each emission draws them once more (Unbound.Runtime.Culling.EmitUI).
            if (camera.CanvasCopies <= 0) return;
            int first = into.Count;
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
                CollectCanvas(canvas, camera, into);
            }
            // The further copies draw the same geometry again, each copy after the one before where
            // the sort leaves them tied (the order between copies is unmeasured: one blended draw
            // over another of the same colour gives the same result either way).
            int count = into.Count - first;
            for (int copy = 1; copy < camera.CanvasCopies; copy++)
                for (int i = 0; i < count; i++)
                {
                    var item = into[first + i];
                    item.Sequence = FrameBuilder.NextSequence();
                    into.Add(item);
                }
        }

        void CollectCanvas(Canvas canvas, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            int before = into.Count;
            CollectCanvasDraws(canvas, camera, into);
            if (Trace.RenderDetailOn && Time.frameCount % 60 == 1)
                Trace.Render($"  canvas '{canvas.name}' ({canvas.m_RenderMode}, camera '{camera?.Camera?.name ?? "overlay"}', layer {canvas.m_GameObject.m_Layer}, order {canvas.sortingOrder}): {into.Count - before} draws");
        }

        void CollectCanvasDraws(Canvas canvas, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var root = canvas.rootCanvas;
            int layer = canvas.cachedSortingLayerValue;
            int order = canvas.sortingOrder;
            int first = into.Count;
            s_HasDrawnBounds = false;
            // Nothing the walk does moves the root: its matrices once for the whole canvas.
            var placement = new RootPlacement(root.transform);
            m_Run = default;
            Walk(canvas.transform, canvas, placement, camera, into, layer, order, isCanvasRoot: true);
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

        /// <summary>Whether a canvas lies under a disabled one: nothing there is drawn, enabled
        /// canvases included (measured, 6000.0, tests/projects/sorting).</summary>
        static bool UnderDisabledCanvas(Canvas canvas)
        {
            for (var t = canvas.transform.m_Parent; t != null; t = t.m_Parent)
                foreach (var c in t.m_GameObject.m_Components)
                    if (c is Canvas parent && !parent.m_Destroyed && !parent.m_Enabled) return true;
            return false;
        }

        /// <summary>The root canvas's placement: the world matrix its draws carry and its inverse.</summary>
        readonly struct RootPlacement
        {
            internal readonly Matrix4x4 ToWorld, FromWorld;

            internal RootPlacement(Transform root)
            {
                ToWorld = root.localToWorldMatrix;
                FromWorld = root.worldToLocalMatrix;
            }
        }

        void Walk(Transform t, Canvas canvas, in RootPlacement root, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into,
            int layer, int order, bool isCanvasRoot)
        {
            var go = t.m_GameObject;
            if (!go.m_IsActive) return;
            if (!isCanvasRoot)
            {
                foreach (var c in go.m_Components)
                {
                    if (!(c is Canvas nested) || nested.m_Destroyed) continue;
                    // A disabled nested canvas hides everything under it (measured, 6000.0).
                    if (!nested.m_Enabled) return;
                    // A nested canvas that sorts on its own is drawn separately.
                    if (nested.m_OverrideSorting) return;
                    // Color batching follows the nearest Canvas, independently of sorting.
                    canvas = nested;
                }
            }
            foreach (var c in go.m_Components)
            {
                if (!(c is CanvasRenderer cr) || cr.m_Destroyed) continue;
                if (cr.m_Cull || cr.m_Mesh == null || cr.m_MaterialCount == 0) continue;
                // A renderer whose alpha (its own times its groups') is zero or less is never drawn,
                // cullTransparentMesh or not; the flag adds the test for all-transparent vertices
                // (6000.0.71f1, tests/projects/uitransparentcull).
                float alpha = cr.m_Color.a * cr.GetInheritedAlpha();
                if (alpha <= 0f) continue;
                if (cr.m_CullTransparentMesh && Transparent(cr.m_Mesh)) continue;
                var color = new Color(cr.m_Color.r, cr.m_Color.g, cr.m_Color.b, alpha);
                bool alwaysGamma = canvas.m_VertexColorAlwaysGammaSpace;
                var model = root.ToWorld;
                ulong mesh = cr.PrepareCanvasMesh(color, QualitySettings.activeColorSpace == ColorSpace.Linear && !alwaysGamma,
                    root.FromWorld * t.localToWorldMatrix).Sync();
                ulong texture = cr.m_Texture != null ? TextureHandles.Of(cr.m_Texture) : 0;
                int submeshes = Math.Max(1, cr.m_Mesh.subMeshCount);
                if (camera != null) AddDrawnBounds(cr.m_Mesh.bounds, t.localToWorldMatrix);
                // Each material slot of each renderer is a draw of its own. A renderer drawn right
                // after another of the same canvas with the same single material and texture is
                // batched with it and takes its alpha texture (see AlphaRun).
                for (int i = 0; i < cr.m_MaterialCount; i++)
                {
                    // An unset slot draws the default UI material; a destroyed material, which a
                    // renderer keeps until its Graphic gives it another, draws nothing, as its pop
                    // materials do (6000.0.71f1, tests/projects/uitransparentcull destroyed.*,
                    // modifier.*: a StencilMaterial entry removed by its only user).
                    var held = i < cr.m_Materials.Length ? cr.m_Materials[i] : null;
                    if (!ReferenceEquals(held, null) && held == null) continue;
                    var material = held ?? DefaultMaterial;
                    var alphaTexture = m_Run.AlphaFor(canvas, material, cr, cr.m_MaterialCount);
                    into.Add(new FrameBuilder.DrawItem
                    {
                        Mesh = mesh,
                        CanvasDraw = true,
                        Submesh = Math.Min(i, submeshes - 1),
                        Material = cr.CanvasMaterial(material),
                        Model = model,
                        Color = Color.white,
                        Properties = DrawProperties(cr, alwaysGamma, alphaTexture),
                        MainTexture = texture,
                        Queue = material.renderQueue,
                        SortingLayer = layer,
                        SortingOrder = order,
                        Sequence = FrameBuilder.NextSequence(),
                    });
                }
            }
            foreach (var child in t.m_Children)
                Walk(child, canvas, root, camera, into, layer, order, isCanvasRoot: false);
            // A mask's pop instructions after its children: its mesh again with the materials that
            // undo its stencil (uGUI's Mask sets them; they write no colour). Without them a mask's
            // stencil stays for everything drawn after it.
            foreach (var c in go.m_Components)
            {
                if (!(c is CanvasRenderer cr) || cr.m_Destroyed || cr.m_PopMaterialCount == 0) continue;
                if (cr.m_Cull || cr.m_Mesh == null || cr.m_MaterialCount == 0) continue;
                bool alwaysGamma = canvas.m_VertexColorAlwaysGammaSpace;
                var color = new Color(cr.m_Color.r, cr.m_Color.g, cr.m_Color.b, cr.m_Color.a * cr.GetInheritedAlpha());
                var model = root.ToWorld;
                ulong mesh = cr.PrepareCanvasMesh(color, QualitySettings.activeColorSpace == ColorSpace.Linear && !alwaysGamma,
                    root.FromWorld * t.localToWorldMatrix).Sync();
                ulong texture = cr.m_Texture != null ? TextureHandles.Of(cr.m_Texture) : 0;
                m_Run = default;
                for (int i = 0; i < cr.m_PopMaterialCount && i < cr.m_PopMaterials.Length; i++)
                {
                    var material = cr.m_PopMaterials[i];
                    if (material == null) continue;
                    into.Add(new FrameBuilder.DrawItem
                    {
                        Mesh = mesh,
                        CanvasDraw = true,
                        Submesh = 0,
                        Material = cr.CanvasMaterial(material),
                        Model = model,
                        Color = Color.white,
                        Properties = DrawProperties(cr, alwaysGamma, cr.m_AlphaTexture),
                        MainTexture = texture,
                        Queue = material.renderQueue,
                        SortingLayer = layer,
                        SortingOrder = order,
                        Sequence = FrameBuilder.NextSequence(),
                    });
                }
            }
        }

        /// <summary>A renderer's per-draw properties, with an alpha texture as <c>_AlphaTex</c>
        /// (CanvasRenderer.SetAlphaTexture, 6000.0 Scripting API: "passed to the Shader under the
        /// _AlphaTex property"), over the material's as its texture is over <c>_MainTex</c>.
        /// Without one nothing is passed and the material's own value, else the shader's default,
        /// is sampled (matches the Unity 6000.0.71f1 Editor, tests/projects/uialphaborders canvas:
        /// UI/DefaultETC1 with and without a material _AlphaTex).</summary>
        static Dictionary<int, object> DrawProperties(CanvasRenderer cr, bool alwaysGamma, Texture alphaTexture)
        {
            var properties = cr.CanvasProperties(alwaysGamma);
            if (alphaTexture != null) properties[s_AlphaTexProperty] = alphaTexture;
            else properties.Remove(s_AlphaTexProperty);
            return properties;
        }

        /// <summary>The draw run the canvas walk is in. Renderers drawn one after another under one
        /// canvas with the same single material, texture and rect clipping are one batch, and the
        /// batch draws with its first renderer's alpha texture, whatever the others set (matches
        /// the Unity 6000.0.71f1 Editor, tests/projects/uialphaborders canvas `shared*`: adjacent
        /// unclipped sibling renderers). Batches across other draws, which Unity's canvas may also
        /// form, and clipped batches are not measured: renderers with another clip start a run.</summary>
        struct AlphaRun
        {
            Canvas m_Canvas;
            Material m_Material;
            Texture m_Texture, m_Alpha;
            bool m_Clipped;
            Rect m_ClipRect;
            Vector2 m_Softness;

            internal Texture AlphaFor(Canvas canvas, Material material, CanvasRenderer cr, int materials)
            {
                if (materials == 1 && m_Material != null && ReferenceEquals(canvas, m_Canvas)
                    && ReferenceEquals(material, m_Material) && ReferenceEquals(cr.m_Texture, m_Texture)
                    && cr.m_HasRectClipping == m_Clipped
                    && (!m_Clipped || (cr.m_ClipRect == m_ClipRect && cr.m_ClippingSoftness == m_Softness)))
                    return m_Alpha;
                if (materials == 1)
                {
                    m_Canvas = canvas;
                    m_Material = material;
                    m_Texture = cr.m_Texture;
                    m_Alpha = cr.m_AlphaTexture;
                    m_Clipped = cr.m_HasRectClipping;
                    m_ClipRect = cr.m_ClipRect;
                    m_Softness = cr.m_ClippingSoftness;
                }
                else this = default;
                return cr.m_AlphaTexture;
            }
        }

        AlphaRun m_Run;

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

        internal static void EnsureRegistered() { }
    }
}
