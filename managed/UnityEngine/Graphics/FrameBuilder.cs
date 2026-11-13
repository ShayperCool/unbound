using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using System.Runtime.InteropServices;
using System.Text;

namespace Unbound.Runtime
{
    /// <summary>
    /// Builds each frame's render packet with the built-in pipeline's rules: cameras by depth
    /// (render-texture cameras first); per camera the renderers its culling mask sees, opaque ones
    /// (queue up to 2500) by queue then front to back, transparent ones by sorting layer, sorting
    /// order, queue, then back to front; then screen-space overlay canvases on top.
    /// </summary>
    internal static partial class FrameBuilder
    {
        internal sealed class CameraContext
        {
            internal Camera Camera;
            internal Vector3 Position;
            internal Vector3 Forward;
            internal Matrix4x4 View;
            internal Matrix4x4 Projection;
            internal Rect Viewport;
            internal bool Orthographic;
            /// <summary>How draws sort by distance: from the camera, along its view, or along
            /// <see cref="Axis"/> (the camera's transparency sort mode, else the project's).</summary>
            internal DistanceMetric Metric;
            internal Vector3 Axis;

            /// <summary>The camera's transparency sort mode and axis, the project's where the
            /// camera's is Default (measured: tests/projects/builtinrp sort_*).</summary>
            internal void SortLike(Camera camera)
            {
                var mode = camera.transparencySortMode != TransparencySortMode.Default ? camera.transparencySortMode : GraphicsSettings.transparencySortMode;
                Axis = camera.transparencySortMode == TransparencySortMode.CustomAxis ? camera.transparencySortAxis : GraphicsSettings.transparencySortAxis;
                Metric = mode switch
                {
                    TransparencySortMode.Orthographic => DistanceMetric.Orthographic,
                    TransparencySortMode.Perspective => DistanceMetric.Perspective,
                    TransparencySortMode.CustomAxis => DistanceMetric.CustomAxis,
                    _ => camera.orthographic ? DistanceMetric.Orthographic : DistanceMetric.Perspective,
                };
            }
        }

        internal struct DrawItem
        {
            internal ulong Mesh;
            internal int Submesh;
            internal Material Material;
            internal Matrix4x4 Model;
            internal Color Color;
            internal ulong MainTexture;
            internal Rect Clip;
            internal bool ClipEnabled;
            /// <summary>Canvas supplies its clip variant independently of the global keyword.</summary>
            internal bool CanvasDraw;
            /// <summary>An IMGUI draw with alphaBlend false: blending is turned off for this draw
            /// alone, over the state override in force (<see cref="BeginOpaqueImgui"/>).</summary>
            internal bool OpaqueImgui;
            internal int Queue;
            internal int SortingLayer;
            internal int SortingOrder;
            internal float Distance;
            internal long Sequence;
            /// <summary>The enclosing sorting groups, outermost first; null when there are none.</summary>
            internal SortLevel[] Groups;
            /// <summary>Per-draw properties (a Graphics.DrawMesh property block); null when none.</summary>
            internal Dictionary<int, object> Properties;
            /// <summary>The renderer the draw is of (null for engine-made draws).</summary>
            internal Renderer Renderer;
            /// <summary>The renderer whose sprite mask interaction the draw follows (renderers drawn
            /// without <see cref="Renderer"/> set name themselves here).</summary>
            internal Renderer Masked;
            /// <summary>Instances or GPU-read counts; null for one instance.</summary>
            internal DrawSetup? Setup;
            /// <summary>A Graphics.DrawMesh draw's lighting (null for renderers and engine draws),
            /// with its world bounds and layer.</summary>
            internal ImmediateLighting Lighting;
            internal Bounds Bounds;
            /// <summary>What a Graphics.DrawMesh draw is culled by: its set world bounds, else
            /// <see cref="Bounds"/>.</summary>
            internal Bounds CullBounds;
            internal int Layer;
            /// <summary>A Graphics.RenderMesh / DrawMesh draw's rendering layer mask (a renderer's
            /// draws read their renderer's; null for engine-made draws).</summary>
            internal uint? RenderingLayerMask;
            /// <summary>A BatchRendererGroup draw command's draw; null for others.</summary>
            internal BatchCulling.DrawRef? Dots;
        }

        /// <summary>Whether a draw is lit as a mesh: a mesh-like renderer's, or a Graphics.DrawMesh
        /// one.</summary>
        internal static bool IsLit(in DrawItem item) => item.Lighting != null || IsMeshLike(item.Renderer);

        static Bounds BoundsOf(in DrawItem item) => item.Renderer != null ? item.Renderer.bounds : item.Bounds;

        static int LayerOf(in DrawItem item) => item.Renderer != null ? item.Renderer.m_GameObject.m_Layer : item.Layer;

        static bool ReceivesShadows(in DrawItem item) => item.Renderer != null ? item.Renderer.receiveShadows : item.Lighting != null && item.Lighting.Receive;

        static ShadowCastingMode CastingOf(in DrawItem item) => item.Renderer != null ? item.Renderer.shadowCastingMode : item.Lighting?.Cast ?? ShadowCastingMode.Off;

        /// <summary>A sorting group's place in a draw's sort order.</summary>
        internal struct SortLevel
        {
            internal int Id;
            internal int SortingLayer;
            internal int SortingOrder;
            internal float Distance;
        }

        static readonly System.Collections.Generic.List<SortLevel> s_GroupScratch = new System.Collections.Generic.List<SortLevel>();

        /// <summary>The active sorting groups above a renderer, outermost first (a group sorting
        /// at the root cuts off those above it).</summary>
        internal static SortLevel[] GroupsOf(Renderer renderer, CameraContext camera)
        {
            s_GroupScratch.Clear();
            for (var t = renderer.m_GameObject?.m_Transform; t != null; t = t.m_Parent)
            {
                var group = t.m_GameObject.GetComponent<SortingGroup>();
                if (group == null || !group.isActiveAndEnabled) continue;
                s_GroupScratch.Add(new SortLevel
                {
                    Id = group.GetInstanceID(),
                    SortingLayer = UnityEngine.SortingLayer.GetLayerValueFromID(group.m_SortingLayerID),
                    SortingOrder = group.m_SortingOrder,
                    Distance = Distance(camera, t.position),
                });
                if (group.m_SortAtRoot) break;
            }
            if (s_GroupScratch.Count == 0) return null;
            s_GroupScratch.Reverse();
            return s_GroupScratch.ToArray();
        }

        /// <summary>Extra draw sources: canvases. They add draws per camera and overlay draws.</summary>
        internal interface IDrawSource
        {
            void CollectCameraDraws(CameraContext camera, List<DrawItem> into);
            /// <summary>Screen-space overlay draws, drawn after every camera, in order.</summary>
            void CollectOverlayDraws(List<DrawItem> into);
        }

        internal static readonly List<IDrawSource> Sources = new List<IDrawSource>();
        static long s_Sequence;
        static readonly List<DrawItem> s_Items = new List<DrawItem>();
        /// <summary>The renderers the camera being rendered sees.</summary>
        static readonly List<Renderer> s_Visible = new List<Renderer>();
        static readonly List<string> s_DrawDescriptions = new List<string>();

        static string MeshBounds(ulong handle)
        {
            foreach (var o in Resources.FindObjectsOfTypeAll<Mesh>())
                if (o.m_Handle == handle)
                {
                    var c = o.m_Colors != null && o.m_Colors.Length > 0 ? o.m_Colors[0].ToString() : "none";
                    var uv = o.m_UVs[0] != null && o.m_UVs[0].Length > 0 ? o.m_UVs[0][0].ToString() : "none";
                    var v = o.m_Vertices.Length > 0 ? o.m_Vertices[0].ToString() : "none";
                    var idx = o.m_SubMeshes.Count > 0 ? o.m_SubMeshes[0].Indices.Length : -1;
                    return $"{o.bounds} ({o.vertexCount} verts, {idx} indices, v0 {v} c0 {c} uv0 {uv})";
                }
            return "?";
        }

        static void Describe(in DrawItem item)
        {
            if (!Trace.RenderDetailOn || Time.frameCount % 60 != 1) return;
            s_DrawDescriptions.Add($"  draw {s_DrawDescriptions.Count}: mesh {item.Mesh}/{item.Submesh} material '{item.Material?.name}' shader '{item.Material?.shader?.name}' queue {item.Queue} tex {item.MainTexture} color {item.Color} pos {item.Model.GetColumn(3)} scale {item.Model.lossyScale}{(item.ClipEnabled ? $" clip {item.Clip}" : "")} bounds {MeshBounds(item.Mesh)}");
        }

        static FrameBuilder()
        {
            PlayerLoopDriver.RegisterFrameRendering(Render);
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.FinishFrameRendering), RenderLegacyFrame);
            // The frame's drawing ends after its end-of-frame callbacks.
            PlayerLoopDriver.RegisterFrameCleanup(() => InDrawingFrame = false);
            UnityEngine.Rendering.OnDemandRendering.EnsureRegistered();
        }

        internal static void EnsureRegistered() { }

        static void RenderLegacyFrame()
        {
            if (!Compat.RenderingAfterPublicLoop) Render();
        }

        internal static long NextSequence() => s_Sequence++;

        /// <summary>Adds one draw per submesh/material pair of a mesh renderer.</summary>
        internal static void AddMeshDraws(Renderer renderer, Mesh mesh, Matrix4x4 model, Color color, CameraContext camera, List<DrawItem> into, Texture mainTexture = null,
            Dictionary<int, object> properties = null)
        {
            var materials = renderer.m_Materials;
            if (materials.Length == 0) return;
            ulong meshHandle = mesh.Sync();
            ulong texture = mainTexture != null ? TextureHandles.Of(mainTexture) : 0;
            // A sprite sorts at its bounds' centre or, by its sort point, at its pivot (measured).
            var center = renderer is SpriteRenderer sprite && sprite.m_SpriteSortPoint == SpriteSortPoint.Pivot
                ? sprite.transform.position : renderer.bounds.center;
            float distance = Distance(camera, center);
            var groups = GroupsOf(renderer, camera);
            int submeshes = Math.Max(1, mesh.subMeshCount);
            for (int i = 0; i < materials.Length; i++)
            {
                var material = renderer.DrawnMaterial(i, materials[i]);
                if (material == null) continue;
                into.Add(new DrawItem
                {
                    Mesh = meshHandle,
                    Submesh = Math.Min(i, submeshes - 1),
                    Material = material,
                    Model = model,
                    Color = color,
                    MainTexture = texture,
                    Queue = material.renderQueue,
                    SortingLayer = SortingLayer.GetLayerValueFromID(renderer.m_SortingLayerID),
                    SortingOrder = renderer.m_SortingOrder,
                    Distance = distance,
                    Sequence = s_Sequence++,
                    Groups = groups,
                    Renderer = renderer,
                    Properties = properties,
                });
            }
        }

        /// <summary>Whether world bounds are at least partly inside the frustum of a view-projection
        /// matrix (OpenGL clip space). Bounds of no size count as inside: renderers that do not
        /// know their bounds are not culled.</summary>
        internal static bool InFrustum(Matrix4x4 viewProjection, Bounds bounds)
        {
            var e = bounds.extents;
            if (e.x <= 0f && e.y <= 0f && e.z <= 0f) return true;
            var c = bounds.center;
            var m = viewProjection;
            for (int i = 0; i < 6; i++)
            {
                // Gribb-Hartmann planes: row 3 plus or minus rows 0, 1, 2.
                int row = i / 2;
                float sign = (i & 1) == 0 ? 1f : -1f;
                float a = m.m30 + sign * m[row, 0];
                float b = m.m31 + sign * m[row, 1];
                float d = m.m32 + sign * m[row, 2];
                float w = m.m33 + sign * m[row, 3];
                float distance = a * c.x + b * c.y + d * c.z + w;
                float radius = Mathf.Abs(a) * e.x + Mathf.Abs(b) * e.y + Mathf.Abs(d) * e.z;
                if (distance + radius < 0f) return false;
            }
            return true;
        }

        /// <summary>A camera's culling, as Unity's: a renderer is seen when its bounds touch the
        /// culling frustum (<c>Camera.cullingMatrix</c>) and lie within its layer's cull distance
        /// (<c>layerCullDistances</c>, along the view or, with <c>layerCullSpherical</c>, from the
        /// camera). Shadow casters are culled against the camera's frustum swept away from the light.</summary>
        internal readonly struct Culler
        {
            readonly Matrix4x4 m_Matrix;
            readonly Vector3 m_Position, m_Forward;
            readonly float[] m_Distances;
            readonly bool m_Spherical;

            Culler(Matrix4x4 matrix, Vector3 position, Vector3 forward, float[] distances, bool spherical)
            {
                m_Matrix = matrix;
                m_Position = position;
                m_Forward = forward;
                m_Distances = distances;
                m_Spherical = spherical;
            }

            /// <summary>The camera's culling: its view-projection (a culling matrix set from script
            /// does not cull the built-in pipeline's renders, measured: tests/projects/builtinfx
            /// culling_matrix).</summary>
            internal static Culler ForCamera(Camera camera, Vector3 position, Vector3 forward) =>
                new Culler(camera.projectionMatrix * camera.worldToCameraMatrix, position, forward, camera.layerCullDistances, camera.layerCullSpherical);

            /// <summary>Renderers whose bounds are those Unity culls with (mesh and sprite
            /// renderers) or cover everything they draw (particle renderers that can bound their
            /// draw); others are drawn whether culled or not.</summary>
            internal static bool TrustsBounds(Renderer renderer) =>
                renderer.GetType() == typeof(MeshRenderer) || renderer is SpriteRenderer ||
                renderer is SkinnedMeshRenderer skin && skin.HasMeasuredCullingBounds ||
                renderer is ParticleSystemRenderer particles && (particles.NothingToDraw || particles.CullingBoundsCoverDraw);

            internal bool Sees(Renderer renderer)
            {
                if (renderer.NothingToDraw) return false;
                renderer.PrepareCullingBounds();
                var bounds = renderer.CullingBounds;
                return InFrustum(m_Matrix, bounds) && WithinLayerDistance(renderer, bounds);
            }

            bool WithinLayerDistance(Renderer renderer, Bounds bounds)
            {
                int layer = renderer.m_GameObject.m_Layer;
                float limit = m_Distances != null && layer < m_Distances.Length ? m_Distances[layer] : 0f;
                if (!(limit > 0f)) return true;
                var e = bounds.extents;
                if (m_Spherical) return (bounds.center - m_Position).magnitude - e.magnitude <= limit;
                var f = m_Forward;
                float radius = Mathf.Abs(f.x) * e.x + Mathf.Abs(f.y) * e.y + Mathf.Abs(f.z) * e.z;
                return Vector3.Dot(bounds.center - m_Position, f) - radius <= limit;
            }

            /// <summary>Whether a shadow caster can shade what the camera sees under a light: for a
            /// directional light, its bounds against the frustum planes the light does not cross
            /// inwards (the frustum swept towards the light); for a point or spot light, against
            /// the light's range. The layer's cull distance applies too.</summary>
            internal bool SeesCaster(Renderer renderer, Light light)
            {
                if (renderer.NothingToDraw) return false;
                renderer.PrepareCullingBounds();
                var bounds = renderer.CullingBounds;
                if (!WithinLayerDistance(renderer, bounds)) return false;
                var e = bounds.extents;
                if (e.x <= 0f && e.y <= 0f && e.z <= 0f) return true;
                if (light.type != LightType.Directional)
                {
                    float range = light.range;
                    return bounds.SqrDistance(light.transform.position) <= range * range;
                }
                var d = light.transform.forward;
                var c = bounds.center;
                var m = m_Matrix;
                for (int i = 0; i < 6; i++)
                {
                    int row = i / 2;
                    float sign = (i & 1) == 0 ? 1f : -1f;
                    var n = new Vector3(m.m30 + sign * m[row, 0], m.m31 + sign * m[row, 1], m.m32 + sign * m[row, 2]);
                    float w = m.m33 + sign * m[row, 3];
                    // The light carries a caster's shadow inwards across this plane: it cannot cull.
                    if (Vector3.Dot(n, d) > 0f) continue;
                    float radius = Mathf.Abs(n.x) * e.x + Mathf.Abs(n.y) * e.y + Mathf.Abs(n.z) * e.z;
                    if (Vector3.Dot(n, c) + w + radius < 0f) return false;
                }
                return true;
            }
        }

        /// <summary>A point's sort distance: squared from the camera, along its view, or along the
        /// custom axis (larger: drawn earlier among transparents).</summary>
        internal static float Distance(CameraContext camera, Vector3 point) => camera.Metric switch
        {
            DistanceMetric.Orthographic => Vector3.Dot(point - camera.Position, camera.Forward),
            DistanceMetric.CustomAxis => Vector3.Dot(point - camera.Position, camera.Axis),
            _ => (point - camera.Position).sqrMagnitude,
        };

        static readonly List<(string Name, Vector4 Value)> s_Lighting = new List<(string, Vector4)>();
        static readonly List<string> s_FrameKeywords = new List<string>();
        static readonly string[] s_FogKeywords = { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" };
        static int s_CameraCount;
        internal static int s_DrawCount;
        /// <summary>The render pipeline renders the player loop's cameras: their per-object motion
        /// vectors make the renderers' matrices the previous ones at the frame's end, as the
        /// built-in renderer's do (<see cref="EndFrameMotion"/>).</summary>
        static bool s_PlayerLoopPipeline;
        static ulong[] s_MaterialHandles = new ulong[256];

        /// <summary>From the frame's rendering until its end-of-frame callbacks and screenshot
        /// are done: reads of the screen outside it read another framebuffer in the Editor.</summary>
        internal static bool InDrawingFrame;

        static void Render()
        {
            InDrawingFrame = true;
            Camera.current = null;
            s_DrawDescriptions.Clear();
            s_CameraCount = s_DrawCount = 0;
            // A frame OnDemandRendering does not render (the interval latched when the frame
            // began) draws nothing on the screen and presents nothing: no screen camera, overlay
            // canvas or OnGUI. The cameras that render into textures still render, with their
            // callbacks (measured: tests/projects/coreapis).
            bool screen = UnityEngine.Rendering.OnDemandRendering.RendersThisFrame;
            Screen.BeginRendering();
            if (screen) GfxStream.BeginFrame(Screen.width, Screen.height, QualitySettings.activeColorSpace);
            else GfxStream.BeginOffscreenFrame(QualitySettings.activeColorSpace);
            EmitLightingGlobals();
            EmitFrameGlobals();
            if (GraphicsSettings.currentRenderPipeline == null) ReflectionProbe.UpdateRealtime();

            var cameras = Camera.CollectActive();
            // The Editor's Game view shows Display 1: screen cameras of other displays do not
            // render at all (measured: tests/projects/display, no OnPreRender either).
            cameras.RemoveAll(c => !c.RendersOffscreen && c.targetDisplay != 0);
            if (!screen)
            {
                cameras.RemoveAll(c => !c.RendersOffscreen);
                if (GraphicsSettings.currentRenderPipeline != null) WarnOnDemandPipelineOnce();
            }
            cameras.Sort((a, b) =>
            {
                int rt = b.RendersOffscreen.CompareTo(a.RendersOffscreen);
                return rt != 0 ? rt : a.depth.CompareTo(b.depth);
            });
            // A scriptable pipeline renders the cameras itself; else the built-in one does.
            bool srp = RenderPipelineCameras(cameras);
            if (srp) s_CameraCount += cameras.Count;
            else
            {
                PlanScreenStacks(cameras);
                foreach (var camera in cameras) RenderCamera(camera, true);
                s_StackNeeds.Clear();
                s_SingleSampleStacks.Clear();
            }

            // Overlay canvases: a pixel-space camera over the whole screen, drawn last (unless the
            // pipeline draws them).
            EndScreenStack();
            if (screen && (!srp || !SupportedRenderingFeatures.active.rendersUIOverlay || s_CameraCount == 0))
                DrawOverlayCanvases(null);
            TraceFrame();
            EndFrameMotion();
            if (screen) GfxStream.EndFrame();
            else GfxStream.Flush();
            Immediate.Queued.Clear();
            Culling.EndFrame();
            RenderTexture.CollectTemporaries();
        }

        /// <summary>The player loop's cameras (sorted, offscreen ones first) through the active
        /// render pipeline; false when there is none. As the engine does, the cameras that render
        /// into textures go to the pipeline in a call of their own before the ones on the screen
        /// (URP draws overlay UI in the last base camera of a call: in one list with an offscreen
        /// camera after it, the UI would be lost). Their renderer lists' per-object motion
        /// vectors keep the renderers' matrices at the frame's end (<see cref="s_PlayerLoopPipeline"/>).</summary>
        internal static bool RenderPipelineCameras(List<Camera> cameras)
        {
            int offscreen = 0;
            foreach (var c in cameras) if (c.RendersOffscreen) offscreen++;
            s_PlayerLoopPipeline = true;
            try
            {
                if (offscreen > 0 && offscreen < cameras.Count)
                {
                    bool srp = RenderPipelineManager.Render(cameras.GetRange(0, offscreen));
                    if (srp) RenderPipelineManager.Render(cameras.GetRange(offscreen, cameras.Count - offscreen));
                    return srp;
                }
                return RenderPipelineManager.Render(cameras);
            }
            finally
            {
                s_PlayerLoopPipeline = false;
            }
        }

        internal static bool s_WarnedForcedMotionVectors;

        static void WarnForcedMotionVectorsOnce()
        {
            if (s_WarnedForcedMotionVectors) return;
            s_WarnedForcedMotionVectors = true;
            Debug.LogWarning("Unbound: FilteringSettings.forceAllMotionVectorObjects is not measured; such a renderer list draws every " +
                             "object it matches, without the engine's previous matrices (unity_MatrixPreviousM is the material's or global value).");
        }

        static bool s_WarnedOnDemandPipeline;

        /// <summary>The measurement covers the built-in pipeline only.</summary>
        static void WarnOnDemandPipelineOnce()
        {
            if (s_WarnedOnDemandPipeline) return;
            s_WarnedOnDemandPipeline = true;
            Debug.LogWarning("Unbound: OnDemandRendering with a scriptable render pipeline is not measured; on frames that do not render, " +
                             "only the cameras that render into textures go to the pipeline.");
        }

        /// <summary>What the frame sets once for its cameras (measured: tests/projects/builtinfx
        /// quality.globals.*, a change from script shows the next frame): unity_ShadowColor (the
        /// subtractive shadow color, linear in a linear project) and the billboard keyword of
        /// the quality level.</summary>
        static void EmitFrameGlobals()
        {
            var shadow = RenderSettings.subtractiveShadowColor;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) shadow = shadow.linear;
            Shader.SetGlobalVector("unity_ShadowColor", shadow);
            if (QualitySettings.billboardsFaceCameraPosition) Shader.EnableKeyword("BILLBOARD_FACE_CAMERA_POS");
            else Shader.DisableKeyword("BILLBOARD_FACE_CAMERA_POS");
        }

        static readonly Vector4 s_UnboundedMin = new Vector4(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, 0f);
        static readonly Vector4 s_UnboundedMax = new Vector4(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, 0f);

        /// <summary>The lighting environment's built-in variables (ambient probe, fog, main
        /// light) and fog keywords, for what renders next. <paramref name="srp"/>: for a render
        /// pipeline's draws, whose renderers without a reflection probe get the default reflection
        /// over an unbounded box in both slots (measured: tests/projects/urp
        /// per-object-constants.jsonl).</summary>
        internal static void EmitLightingGlobals(bool srp = false)
        {
            s_Lighting.Clear();
            s_FrameKeywords.Clear();
            LightingGlobals.Collect(s_Lighting, s_FrameKeywords);
            foreach (var (name, value) in s_Lighting) GfxStream.GlobalVector(name, value);
            foreach (var fog in s_FogKeywords) GfxStream.Keyword(fog, s_FrameKeywords.Contains(fog));
            // Renderers outside every reflection probe reflect the default reflection (a black
            // cube when the scene has none).
            ulong handle = TextureHandles.Of(ReflectionProbe.defaultTexture);
            // The lighting settings' reflection intensity scales what it gives.
            var decode = ReflectionProbe.DefaultDecode;
            GfxStream.GlobalTexture("unity_SpecCube0", handle);
            GfxStream.GlobalTexture("unity_SpecCube1", handle);
            GfxStream.GlobalVector("unity_SpecCube0_HDR", decode);
            GfxStream.GlobalVector("unity_SpecCube1_HDR", decode);
            if (srp)
            {
                GfxStream.GlobalVector("unity_SpecCube0_BoxMin", s_UnboundedMin);
                GfxStream.GlobalVector("unity_SpecCube0_BoxMax", s_UnboundedMax);
                GfxStream.GlobalVector("unity_SpecCube0_ProbePosition", Vector4.zero);
                GfxStream.GlobalVector("unity_SpecCube1_BoxMin", s_UnboundedMin);
                GfxStream.GlobalVector("unity_SpecCube1_BoxMax", s_UnboundedMax);
                GfxStream.GlobalVector("unity_SpecCube1_ProbePosition", Vector4.zero);
            }
            // Weight 1: the built-in shaders' probe blending keeps the first cubemap alone.
            else GfxStream.GlobalVector("unity_SpecCube0_BoxMin", new Vector4(0f, 0f, 0f, 1f));
            // A renderer without a lightmap keeps its lightmapScaleOffset's default.
            GfxStream.GlobalVector("unity_LightmapST", s_IdentityScaleOffset);
        }

        static readonly Vector4 s_IdentityScaleOffset = new Vector4(1f, 1f, 0f, 0f);

        /// <summary>Screen-space overlay canvases over the screen (with no camera at all, black:
        /// "No cameras rendering"). A pipeline draws them into its own target
        /// (<paramref name="camera"/> set).</summary>
        internal static void DrawOverlayCanvases(Camera camera)
        {
            s_Items.Clear();
            int canvasStart = 0, canvasEnd = 0;
            foreach (var source in Sources)
            {
                int before = s_Items.Count;
                try { source.CollectOverlayDraws(s_Items); }
                catch (Exception e) { Debug.LogException(e); }
                if (source is CanvasSystem)
                {
                    canvasStart = before;
                    canvasEnd = s_Items.Count;
                }
            }
            var panels = OverlayPanels;
            if (Unbound.Runtime.Trace.RenderOn && Time.frameCount % 60 == 1)
                Unbound.Runtime.Trace.Render($"overlay canvases for {(camera != null ? camera.name : "the screen")}: {s_Items.Count} draws");
            if (camera != null)
            {
                if (s_Items.Count == 0 && (panels == null || !panels.Any)) return;
                var saved = GfxStream.Current;
                GfxStream.Viewport(new Rect(0, 0, Screen.width, Screen.height));
                GfxStream.Camera(Matrix4x4.identity, Matrix4x4.Ortho(0, Screen.width, 0, Screen.height, -100000f, 100000f), GfxStream.CameraOverlay);
                EmitOverlayItems(panels, canvasStart, canvasEnd, true);
                GfxStream.Restore(saved);
                return;
            }
            if (s_Items.Count > 0 || s_CameraCount == 0)
            {
                SetupCamera(0, new Rect(0, 0, Screen.width, Screen.height),
                    s_CameraCount == 0 ? CameraClearFlags.SolidColor : CameraClearFlags.Nothing, Color.black,
                    Matrix4x4.identity, Matrix4x4.Ortho(0, Screen.width, 0, Screen.height, -100000f, 100000f), GfxStream.CameraOverlay);
            }
            EmitOverlayItems(panels, canvasStart, canvasEnd, false);
        }

        /// <summary>UI Toolkit's screen panels, drawn among the overlay canvases by sorting order (a
        /// panel before the first canvas of a higher order, after canvases of its order).</summary>
        internal interface IOverlayPanels
        {
            bool Any { get; }
            void Begin();
            /// <summary>Draws the panels not drawn yet whose sorting order is below
            /// <paramref name="maxPriority"/>, into the current target or the screen.</summary>
            void RenderBefore(float maxPriority, bool intoCurrentTarget);
        }

        internal static IOverlayPanels OverlayPanels;

        static void EmitOverlayItems(IOverlayPanels panels, int canvasStart, int canvasEnd, bool intoCurrentTarget)
        {
            if (panels == null)
            {
                EmitItems(0, s_Items.Count);
                return;
            }
            panels.Begin();
            int from = 0;
            for (int i = canvasStart; i < canvasEnd; i++)
            {
                if (i > canvasStart && s_Items[i].SortingOrder == s_Items[i - 1].SortingOrder) continue;
                EmitItems(from, i);
                from = i;
                panels.RenderBefore(s_Items[i].SortingOrder, intoCurrentTarget);
            }
            EmitItems(from, canvasEnd);
            panels.RenderBefore(float.MaxValue, intoCurrentTarget);
            EmitItems(canvasEnd, s_Items.Count);
        }

        /// <summary>OnRenderObject on every script with it.</summary>
        internal static void InvokeOnRenderObject() => SendToAll(m => m.OnRenderObject);

        /// <summary>What a camera shows on its far plane (after the sky: argument false) and on its
        /// near plane (after the transparent objects: true), VideoPlayers' camera modes (measured:
        /// tests/projects/video).</summary>
        internal static Action<Camera, bool> CameraPlanes;

        /// <summary>The skybox (not drawn yet: the camera's clear color stands in).</summary>
        static Mesh s_SkyboxMesh;

        /// <summary>The sky the camera sees (its Skybox's, else RenderSettings.skybox) drawn on a sphere around the
        /// camera inside its far plane (skybox shaders read directions from the vertices; the
        /// procedural sky computes its colors per vertex, so the sphere is fine).</summary>
        internal static void DrawSkybox(Camera camera)
        {
            var material = Skybox.Of(camera);
            if (material == null || camera == null) return;
            var mesh = s_SkyboxMesh ??= SkyboxMesh();
            float radius = camera.farClipPlane * 0.9f;
            var model = Matrix4x4.TRS(camera.transform.position, Quaternion.identity, new Vector3(radius, radius, radius));
            GfxStream.Draw(mesh.Sync(), 0, material.Sync(), 0, model, Color.white,
                properties: SkyboxLightProperties(camera, material));
        }

        internal static Dictionary<int, object> SkyboxLightProperties(Camera camera, Material material)
        {
            Dictionary<int, object> properties = null;
            // The environment's capture is drawn as the built-in pipeline draws a sky, whatever
            // the pipeline.
            bool builtin = GraphicsSettings.currentRenderPipeline == null || camera.m_EnvironmentCapture;
            if (builtin && LightingGlobals.MainLightOf() == null)
                properties = new Dictionary<int, object> { [Shader.PropertyToID("_WorldSpaceLightPos0")] = (Vector4)LightingGlobals.SkyWithoutSun };
            // The ambient projection leaves out the main light's colour, whatever the sky's
            // shader (Compat.SkyAmbientWithoutLightColor).
            if (camera.m_EnvironmentCapture && Compat.SkyAmbientWithoutLightColor)
            {
                properties ??= new Dictionary<int, object>();
                properties[Shader.PropertyToID("_LightColor0")] = Vector4.zero;
            }
            return properties;
        }

        /// <summary>The sky mesh: an octahedron whose triangles are split in four five times,
        /// edge midpoints pushed onto the unit sphere (the vertices the Editor's sky draws with,
        /// measured: tests/projects/urp SkyMeshProbe).</summary>
        static Mesh SkyboxMesh()
        {
            var vertices = new List<Vector3>();
            var index = new Dictionary<Vector3, int>();
            var triangles = new List<int>();
            int Vertex(Vector3 v)
            {
                if (!index.TryGetValue(v, out var i))
                {
                    index[v] = i = vertices.Count;
                    vertices.Add(v);
                }
                return i;
            }
            void Split(Vector3 a, Vector3 b, Vector3 c, int depth)
            {
                if (depth == 0)
                {
                    triangles.Add(Vertex(a));
                    triangles.Add(Vertex(b));
                    triangles.Add(Vertex(c));
                    return;
                }
                Vector3 ab = ((a + b) * 0.5f).normalized, bc = ((b + c) * 0.5f).normalized, ca = ((c + a) * 0.5f).normalized;
                Split(a, ab, ca, depth - 1);
                Split(ab, b, bc, depth - 1);
                Split(ca, bc, c, depth - 1);
                Split(ab, bc, ca, depth - 1);
            }
            foreach (var x in new[] { 1f, -1f })
                foreach (var y in new[] { 1f, -1f })
                    foreach (var z in new[] { 1f, -1f })
                        Split(new Vector3(x, 0, 0), new Vector3(0, y, 0), new Vector3(0, 0, z), 5);
            Mesh mesh;
            using (ObjectRegistry.AsAssets()) mesh = new Mesh { name = "Unbound Skybox", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        internal static CameraContext ContextOf(Camera camera)
        {
            var t = camera.transform;
            var context = new CameraContext
            {
                Camera = camera,
                Position = t.position,
                Forward = t.forward,
                View = camera.worldToCameraMatrix,
                Projection = camera.projectionMatrix,
                Viewport = camera.pixelRect,
                Orthographic = camera.orthographic,
            };
            context.SortLike(camera);
            return context;
        }

        static readonly List<string> s_PassTagScratch = new List<string>();

        /// <summary>The queue range of the renderer list collecting draws (with the LightModes in
        /// <see cref="s_PassTagScratch"/>); null outside one.</summary>
        static RenderQueueRange? s_CollectingList;

        /// <summary>Whether a renderer list leaves out a draw of this material and queue: the queue
        /// is outside its range, the shader is hidden by its LOD, or none of the shader's passes has
        /// one of the list's LightModes.</summary>
        static bool OutsideList(Material material, int queue, RenderQueueRange range, List<string> lightModes)
        {
            if (queue < range.lowerBound || queue > range.upperBound) return true;
            var shader = material.shader;
            if (shader != null && shader.IsLodHidden) return true;
            var info = ShaderPassInfo.Of(shader);
            return info != null && !info.HasPassFor(lightModes);
        }

        /// <summary>While a renderer list collects draws: whether it leaves out a draw of this
        /// material at the material's queue, so a renderer can skip building its geometry for a
        /// draw the list discards.</summary>
        internal static bool CollectingListDrops(Material material) =>
            s_CollectingList is RenderQueueRange range && material != null
            && OutsideList(material, material.renderQueue, range, s_PassTagScratch);

        /// <summary>
        /// A renderer list: the culled renderers' draws whose queue, layer, rendering layer and
        /// sorting layer pass the filter, sorted by the criteria, each drawing its passes whose
        /// LightMode is one of the list's (or the override material's pass).
        /// </summary>
        /// <param name="shadows">A shadow caster list: renderers that cast shadows (with
        /// ShadowsOnly ones), else renderers drawn normally (without ShadowsOnly ones).</param>
        internal static void DrawRendererList(CullingData data, DrawingSettings drawing, FilteringSettings filtering, List<ShaderTagId> passNames, ShadowObjectsFilter? shadows = null,
            RendererLists.TaggedStates tagged = null, int shadowLight = -1)
        {
            // The draws' merged properties live until the list is written.
            using var pooled = DrawPropertyPool.Begin();
            var context = ContextOf(data.Camera);
            SpriteMasking.Begin(data.Camera, context);
            var sorting = drawing.sortingSettings;
            context.Position = sorting.cameraPosition;
            var view = sorting.worldToCameraMatrix;
            context.Forward = -new Vector3(view.m20, view.m21, view.m22);
            // The list's own metric (a 2D renderer passes its transparency sort settings).
            context.Metric = sorting.distanceMetric;
            context.Axis = sorting.customAxis;
            s_Items.Clear();
            var range = filtering.renderQueueRange;
            var layers = filtering.sortingLayerRange;
            bool allLayers = layers == SortingLayerRange.all;
            // Only draws whose material has a pass with one of the list's LightModes (the override
            // material then draws them instead).
            s_PassTagScratch.Clear();
            foreach (var tag in passNames) s_PassTagScratch.Add(tag.name);
            s_CollectingList = range;
            try
            {
                foreach (var renderer in shadows.HasValue ? data.Renderers : data.Visible)
                {
                    if (!renderer) continue;
                    var casting = renderer.shadowCastingMode;
                    if (shadows.HasValue ? casting == ShadowCastingMode.Off : casting == ShadowCastingMode.ShadowsOnly) continue;
                    if ((filtering.layerMask & (1 << renderer.m_GameObject.m_Layer)) == 0) continue;
                    if ((filtering.renderingLayerMask & renderer.renderingLayerMask) == 0) continue;
                    if (filtering.excludeMotionVectorObjects && renderer.motionVectorGenerationMode == MotionVectorGenerationMode.Object) continue;
                    if (!allLayers)
                    {
                        int value = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID);
                        if (value < layers.lowerBound || value > layers.upperBound) continue;
                    }
                    if (shadows.HasValue) renderer.MarkRendered();
                    try { renderer.CollectDraws(context, s_Items); }
                    catch (Exception e) { Debug.LogException(e, renderer); }
                }
            }
            finally { s_CollectingList = null; }
            // Graphics.DrawMesh draws (default sorting layer), filtered as renderers are.
            bool defaultSortingLayer = allLayers || (layers.lowerBound <= 0 && layers.upperBound >= 0);
            foreach (var q in shadows.HasValue ? data.Queued : data.QueuedVisible)
            {
                var casting = q.Lighting.Cast;
                if (shadows.HasValue ? casting == ShadowCastingMode.Off : casting == ShadowCastingMode.ShadowsOnly) continue;
                if ((filtering.layerMask & (1 << q.Layer)) == 0) continue;
                if ((filtering.renderingLayerMask & q.RenderingLayerMask) == 0) continue;
                if (!defaultSortingLayer) continue;
                s_Items.Add(QueuedItem(context, q));
            }
            // BatchRendererGroup draw commands (default sorting layer): a shadow caster list takes
            // its light culling's.
            if (shadows.HasValue) BatchCulling.CollectShadows(data, filtering, shadowLight, s_Items, ref s_Sequence);
            else if (defaultSortingLayer) BatchCulling.Collect(data, filtering, drawing.mainLightIndex, s_Items, ref s_Sequence);
            foreach (var source in Sources)
            {
                try { source.CollectCameraDraws(context, s_Items); }
                catch (Exception e) { Debug.LogException(e); }
            }
            // Per-object motion vectors: the objects with object motion alone. A list that forces
            // every object's motion vectors is unmeasured: it draws what it matches, with a warning.
            bool motion = !shadows.HasValue && (drawing.perObjectData & PerObjectData.MotionVectors) != 0;
            if (motion && filtering.forceAllMotionVectorObjects)
            {
                WarnForcedMotionVectorsOnce();
                motion = false;
            }
            if (motion)
            {
                KeepObjectMotion(s_Items);
                if (s_PlayerLoopPipeline) s_MotionRendered = true;
            }
            // The list's draws by their places in s_Items (the draws stay where they were
            // collected): those it keeps, in collection order, then sorted.
            var items = CollectionsMarshal.AsSpan(s_Items);
            if (s_Order.Length < items.Length) s_Order = new int[Math.Max(items.Length, s_Order.Length * 2)];
            int count = 0;
            for (int i = 0; i < items.Length; i++)
                if (!LeftOut(items[i], range, drawing, motion)) s_Order[count++] = i;
            var order = s_Order.AsSpan(0, count);
            SortRendererList(s_Items, order, sorting.criteria);

            var overrideMaterial = drawing.overrideMaterial;
            if (overrideMaterial != null && drawing.overrideShader != null)
                Debug.LogError("overrideMaterial and overrideShader can't both be used in the same DrawRenderers call.");
            var overrideShader = overrideMaterial == null ? drawing.overrideShader : null;
            int pass = GfxStream.TaggedPasses;
            if (overrideMaterial != null) pass = drawing.overrideMaterialPassIndex;
            // Unlike an immediate DrawMesh, the Editor treats -1 as the first override pass.
            else if (overrideShader != null) pass = Math.Max(0, drawing.overrideShaderPassIndex);
            else GfxStream.PassTags(s_PassTagScratch.ToArray());
            // Per-object lights: the additional lights reaching each renderer, as the pipeline's
            // light index map numbers them.
            if ((drawing.perObjectData & (PerObjectData.LightData | PerObjectData.LightIndices)) != 0)
                ApplyPerObjectLights(data, s_Items, order, drawing.mainLightIndex, (drawing.perObjectData & PerObjectData.Lightmaps) != 0);
            if (s_MaterialHandles.Length < count) Array.Resize(ref s_MaterialHandles, Math.Max(count, s_MaterialHandles.Length * 2));
            ulong overrideHandle = overrideMaterial != null ? overrideMaterial.Sync() : 0;
            for (int i = 0; i < count; i++)
                s_MaterialHandles[i] = overrideMaterial != null ? overrideHandle
                    : overrideShader != null ? ReplacementMaterial(s_Items[order[i]].Material, overrideShader).Sync()
                    : s_Items[order[i]].Material.Sync();
            bool lightmaps = (drawing.perObjectData & PerObjectData.Lightmaps) != 0;
            bool probes = (drawing.perObjectData & PerObjectData.LightProbe) != 0;
            // Per-tag state blocks: switched between draws as their materials' tags change.
            int block = -1;
            for (int i = 0; i < count; i++)
            {
                // By reference: the draws emitted below may add to s_Items (nothing does), so the
                // place is looked up for each draw.
                ref readonly var item = ref CollectionsMarshal.AsSpan(s_Items)[order[i]];
                // The drawn material: an override's, else the renderer's.
                var drawn = overrideMaterial ?? (overrideShader != null ? null : item.Material);
                if (overrideMaterial == null && overrideShader == null && item.Material.HasDisabledPasses && ShaderPassInfo.Of(item.Material.shader) is ShaderPassInfo info)
                {
                    bool moving = motion && MovingObject(item);
                    foreach (var p in TaggedPasses(item.Material, info, s_PassTagScratch, includeDisabled: moving))
                    {
                        if (tagged != null) SelectTaggedState(tagged, item.Material, p, ref block);
                        if (motion) NextDrawMotion(item);
                        EmitDraw(item, s_MaterialHandles[i], p, lightmaps, probes, srp: true);
                    }
                    continue;
                }
                if (tagged != null)
                {
                    // A pass tag is the drawn pass's: the list's first LightMode the material has
                    // where the renderer picks it (several drawn passes share the first's block).
                    int drawnPass = pass;
                    if (tagged.PassTag && pass < 0)
                    {
                        // An override material's pass is its own (a negative index: no pass tag).
                        drawnPass = -1;
                        if (overrideMaterial == null && ShaderPassInfo.Of(item.Material.shader) is ShaderPassInfo tags)
                        {
                            var passes = TaggedPasses(item.Material, tags, s_PassTagScratch, includeDisabled: true);
                            if (passes.Count > 0) drawnPass = passes[0];
                            if (passes.Count > 1) WarnSharedPassTagOnce();
                        }
                    }
                    SelectTaggedState(tagged, drawn ?? ReplacementMaterial(item.Material, overrideShader), drawnPass, ref block);
                }
                if (motion) NextDrawMotion(item);
                EmitDraw(item, s_MaterialHandles[i], pass, lightmaps, probes, srp: true);
            }
            if (block >= 0) GfxStream.StateOverride(null);
        }

        static readonly List<int> s_TaggedScratch = new List<int>();

        static bool s_WarnedSharedPassTag;

        /// <summary>Several passes of one material drawn by a pass-tag list take its first pass's
        /// state block (they are drawn together).</summary>
        static void WarnSharedPassTagOnce()
        {
            if (s_WarnedSharedPassTag) return;
            s_WarnedSharedPassTag = true;
            Debug.LogWarning("Unbound: a renderer list with per-pass-tag state blocks draws several passes of a material: they all take the first pass's block.");
        }

        /// <summary>Switches a renderer list's per-tag state block when the next draw's differs.</summary>
        static void SelectTaggedState(RendererLists.TaggedStates tagged, Material material, int pass, ref int block)
        {
            int next = tagged.IndexFor(material, pass);
            if (next == block) return;
            GfxStream.StateOverride(tagged.Block(next));
            block = next;
        }

        /// <summary>The places in <see cref="s_Items"/> of a renderer list's draws, in order.</summary>
        static int[] s_Order = new int[64];

        /// <summary>Whether a renderer list leaves a collected draw out: outside its queues or
        /// LightModes, or with every matching pass disabled by the material (an override draws the
        /// eligible object even so, and so does an object that moved its motion vector pass).</summary>
        static bool LeftOut(in DrawItem item, RenderQueueRange range, in DrawingSettings drawing, bool motion)
        {
            if (OutsideList(item.Material, item.Queue, range, s_PassTagScratch)) return true;
            var info = ShaderPassInfo.Of(item.Material.shader);
            if (info == null) return false;
            return drawing.overrideMaterial == null && drawing.overrideShader == null && item.Material.HasDisabledPasses
                && TaggedPasses(item.Material, info, s_PassTagScratch).Count == 0 && !(motion && MovingObject(item));
        }

        /// <summary>The passes a renderer list draws of a material: per LightMode of the list in
        /// order, the first pass with it, unless the material disables that LightMode (and
        /// <paramref name="includeDisabled"/> is false).</summary>
        static List<int> TaggedPasses(Material material, ShaderPassInfo info, List<string> lightModes, bool includeDisabled = false)
        {
            s_TaggedScratch.Clear();
            foreach (var mode in lightModes)
            {
                if (!includeDisabled && material.PassDisabled(mode)) continue;
                for (int p = 0; p < info.Passes.Count; p++)
                {
                    if (info.LightMode(p) != mode) continue;
                    if (!s_TaggedScratch.Contains(p)) s_TaggedScratch.Add(p);
                    break;
                }
            }
            return s_TaggedScratch;
        }

        /// <summary>A renderer's built-in forward passes: all of them at once, or one by one
        /// leaving out the LightModes its material disables.</summary>
        static void EmitForward(in DrawItem item, ulong material, bool lightmaps = true, bool probes = true,
            Dictionary<int, object> extra = null, string extraKeyword = null, string extraKeyword2 = null,
            RenderDispatchContext dispatch = RenderDispatchContext.Unknown)
        {
            var info = item.Material.HasDisabledPasses ? ShaderPassInfo.Of(item.Material.shader) : null;
            if (info == null)
            {
                EmitDraw(item, material, GfxStream.ForwardPasses, lightmaps, probes, extra, extraKeyword, extraKeyword2, dispatch: dispatch);
                return;
            }
            foreach (var p in info.ForwardPasses)
                if (!item.Material.PassDisabled(info.BuiltinLightMode(p)))
                    EmitDraw(item, material, p, lightmaps, probes, extra, extraKeyword, extraKeyword2, dispatch: dispatch);
        }

        /// <summary>Renderers drawn as meshes are: lit by the forward lights, with baked lighting and
        /// shadows (mesh, skinned mesh and terrain renderers).</summary>
        internal static bool IsMeshLike(Renderer renderer) => renderer is MeshRenderer || renderer is SkinnedMeshRenderer || renderer is TerrainRenderer || renderer is BillboardRenderer;

        static readonly List<string> s_DrawKeywords = new List<string>();
        static readonly int s_RenderingLayerId = Shader.PropertyToID("unity_RenderingLayer");
        /// <summary>The renderingLayerMask the renderer's draws get without one of their own: the
        /// native draw's unity_RenderingLayer default.</summary>
        const uint DefaultRenderingLayerMask = 1u;
        /// <summary>The boxed unity_RenderingLayer of each mask drawn (a box is never changed).</summary>
        static readonly Dictionary<uint, object> s_RenderingLayerValues = new Dictionary<uint, object>();

        /// <summary>A draw's properties with its rendering layer mask (a renderer's, or a
        /// Graphics.RenderMesh draw's RenderParams.renderingLayerMask) as unity_RenderingLayer.x,
        /// the mask's bits as a float, as URP's shaders read it with asuint: URP's light layers
        /// light a renderer by it (Unity 6000.0.71f1, tests/projects/urp paths-opengl
        /// light_layers). A mask equal to the native default, or no mask (an engine-made draw),
        /// keeps the properties as they are. The draw's own value of the constant wins (Unbound's
        /// choice, as for unity_LightData in a block; unmeasured for this constant).</summary>
        internal static Dictionary<int, object> WithRenderingLayer(uint? renderingLayerMask, Dictionary<int, object> properties)
        {
            if (renderingLayerMask is not uint mask) return properties;
            if (mask == DefaultRenderingLayerMask || (properties != null && properties.ContainsKey(s_RenderingLayerId))) return properties;
            if (!s_RenderingLayerValues.TryGetValue(mask, out var value))
                s_RenderingLayerValues[mask] = value = new Vector4(BitConverter.Int32BitsToSingle((int)mask), 0f, 0f, 0f);
            var merged = DrawPropertyPool.Copy(properties);
            merged[s_RenderingLayerId] = value;
            return merged;
        }

        /// <summary>A draw, with what the baked lighting gives its renderer (lightmap or light
        /// probes, <see cref="BakedLighting.Apply"/>): its keywords are on for the draw alone.</summary>
        static void EmitDraw(in DrawItem item, ulong material, int pass, bool lightmaps = true, bool probes = true,
            Dictionary<int, object> extra = null, string extraKeyword = null, string extraKeyword2 = null, string extraKeyword3 = null,
            RenderDispatchContext dispatch = RenderDispatchContext.Unknown, bool srp = false)
        {
            using var pooled = DrawPropertyPool.Begin();
            Describe(item);
            s_DrawCount++;
            s_DrawKeywords.Clear();
            var properties = item.Properties;
            if (IsMeshLike(item.Renderer))
            {
                properties = BakedLighting.Apply(item.Renderer, properties, s_DrawKeywords, lightmaps, probes);
                properties = ReflectionProbe.Apply(item.Renderer, properties, srp);
            }
            else if (item.Lighting != null)
            {
                var lighting = item.Lighting;
                if (probes && lighting.Probes == LightProbeUsage.BlendProbes)
                    properties = BakedLighting.ApplyProbes(lighting.Anchor != null ? lighting.Anchor.position : lighting.ProbePosition ?? item.Bounds.center, properties);
                properties = ReflectionProbe.Apply(item.Bounds, lighting.Reflections, properties, srp);
            }
            properties = WithRenderingLayer(item.Renderer != null ? item.Renderer.renderingLayerMask : item.RenderingLayerMask, properties);
            if (extra != null)
            {
                var merged = DrawPropertyPool.Copy(properties);
                foreach (var pair in extra) merged[pair.Key] = pair.Value;
                properties = merged;
            }
            if (extraKeyword != null) s_DrawKeywords.Add(extraKeyword);
            if (extraKeyword2 != null) s_DrawKeywords.Add(extraKeyword2);
            if (extraKeyword3 != null) s_DrawKeywords.Add(extraKeyword3);
            foreach (var k in s_DrawKeywords) if (!Shader.s_GlobalKeywords.Contains(k)) GfxStream.Keyword(k, true);
            var masked = item.Masked ?? item.Renderer;
            bool masking = masked != null && masked.MaskInteraction != SpriteMaskInteraction.None;
            var previousState = masking ? SpriteMasking.Before(item, masked, dispatch) : null;
            if (item.Setup is DrawSetup setup) GfxStream.DrawSetup(setup);
            bool restoreCanvasClip = item.CanvasDraw && Shader.s_GlobalKeywords.Contains("UNITY_UI_CLIP_RECT");
            if (restoreCanvasClip) GfxStream.Keyword("UNITY_UI_CLIP_RECT", false);
            // Inside the mask's override, so the opaque draw keeps its stencil test and the mask's
            // After puts back what was there before both.
            var beforeOpaque = item.OpaqueImgui ? BeginOpaqueImgui() : null;
            try
            {
                if (item.Dots is BatchCulling.DrawRef dots) BatchCulling.Draw(item, dots, material, pass, properties);
                else GfxStream.Draw(item.Mesh, item.Submesh, material, pass, item.Model, item.Color, item.MainTexture,
                    item.ClipEnabled ? item.Clip : (Rect?)null, properties);
            }
            finally
            {
                if (item.OpaqueImgui) EndOpaqueImgui(beforeOpaque);
            }
            if (restoreCanvasClip) GfxStream.Keyword("UNITY_UI_CLIP_RECT", true);
            if (masking) SpriteMasking.After(previousState);
            foreach (var k in s_DrawKeywords) if (!Shader.s_GlobalKeywords.Contains(k)) GfxStream.Keyword(k, false);
        }

        static readonly int s_LightDataId = Shader.PropertyToID("unity_LightData");
        static readonly int s_LightIndicesId = Shader.PropertyToID("unity_LightIndices");
        /// <summary>The constants <see cref="PerObjectLights"/> sets.</summary>
        static readonly int[] s_PerObjectLightIds = { s_LightDataId, s_LightIndicesId };
        const int MaxPerObjectLights = 8;

        static readonly Dictionary<(Renderer Renderer, Dictionary<int, object> Own, MaterialPropertyBlock Block), Dictionary<int, object>> s_RendererLights =
            new Dictionary<(Renderer, Dictionary<int, object>, MaterialPropertyBlock), Dictionary<int, object>>();
        static readonly Dictionary<(Dictionary<int, object> Own, Bounds Bounds), Dictionary<int, object>> s_QueuedLights =
            new Dictionary<(Dictionary<int, object>, Bounds), Dictionary<int, object>>();

        /// <summary>
        /// The per-object lights of a renderer list's draws, added to their properties: a
        /// renderer's at its bounds, a Graphics.DrawMesh draw's at the bounds it is culled by,
        /// never lightmapped. A draw's own block wins over them where it sets the same constants
        /// (unity_LightData zero in a block leaves a renderer and a Graphics.RenderMesh draw
        /// unlit: Unity 6000.0.71f1, tests/projects/urp light_data_block; URP's decals rely on it;
        /// unity_LightIndices, set with it, is taken to follow the same rule, unmeasured).
        /// The draws of one renderer or one call that share their block (and, queued, their
        /// bounds) share one answer.
        /// </summary>
        internal static void ApplyPerObjectLights(CullingData data, List<DrawItem> items, int mainLight, bool lightmaps)
        {
            var all = new int[items.Count];
            for (int i = 0; i < all.Length; i++) all[i] = i;
            ApplyPerObjectLights(data, items, all, mainLight, lightmaps);
        }

        /// <summary>The same for the draws at <paramref name="order"/>'s places, in that order.</summary>
        internal static void ApplyPerObjectLights(CullingData data, List<DrawItem> items, ReadOnlySpan<int> order, int mainLight, bool lightmaps)
        {
            s_RendererLights.Clear();
            s_QueuedLights.Clear();
            foreach (int i in order)
            {
                ref var item = ref CollectionsMarshal.AsSpan(items)[i];
                if (item.Renderer != null)
                {
                    var r = item.Renderer;
                    // The renderer's block is in the material it draws with (Renderer.DrawnMaterial).
                    var block = r.BlockOfDrawn(item.Material);
                    var key = (r, item.Properties, block);
                    if (!s_RendererLights.TryGetValue(key, out var merged))
                    {
                        bool lightmapped = lightmaps && r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE;
                        merged = OverOwnValues(PerObjectLights(data, r.bounds, r.m_GameObject.m_Layer, lightmapped, mainLight), item.Properties);
                        if (block != null)
                            foreach (int id in s_PerObjectLightIds)
                                if (block.m_Values.TryGetValue(id, out var value)) merged[id] = value;
                        s_RendererLights[key] = merged;
                    }
                    item.Properties = merged;
                }
                else if (item.Lighting != null && item.Dots == null)
                {
                    // (A BatchRendererGroup draw's lights are in its header.)
                    var key = (item.Properties, item.CullBounds);
                    if (!s_QueuedLights.TryGetValue(key, out var merged))
                    {
                        merged = OverOwnValues(PerObjectLights(data, item.CullBounds, item.Layer, false, mainLight), item.Properties);
                        s_QueuedLights[key] = merged;
                    }
                    item.Properties = merged;
                }
            }
            s_RendererLights.Clear();
            s_QueuedLights.Clear();
        }

        /// <summary>A draw's own (block) values written over its per-object lights.</summary>
        static Dictionary<int, object> OverOwnValues(Dictionary<int, object> perObject, Dictionary<int, object> own)
        {
            if (own != null)
                foreach (var p in own) perObject[p.Key] = p.Value;
            return perObject;
        }

        /// <summary>The per-object lights (unity_LightData, unity_LightIndices) of a renderer or a
        /// Graphics.DrawMesh draw with these world bounds on <paramref name="layer"/>: the visible
        /// lights whose culling mask has the layer, that reach the bounds and that the index map
        /// keeps, the most important first (<see cref="LightImportance"/>). unity_LightData.y
        /// counts them all, unity_LightIndices holds the first 8 (measured: tests/projects/urp
        /// light-order.jsonl, ten lights give 10); unity_LightData.z: whether the main light
        /// lights it: there is one and its culling mask has the layer. A <paramref name="lightmapped"/> renderer (the list binds lightmaps) leaves
        /// out the lights <see cref="InLightmap"/> (the main light too).</summary>
        static readonly List<LightImportance.Candidate> s_LightCandidates = new List<LightImportance.Candidate>();

        internal static Dictionary<int, object> PerObjectLights(CullingData data, Bounds bounds, int layer, bool lightmapped, int mainLight)
        {
            var candidates = s_LightCandidates;
            candidates.Clear();
            var map = data.LightIndexMap;
            // The main light lights the object unless there is none, its culling mask leaves out
            // the layer (tests/projects/urp light-order.jsonl) or, the object lightmapped, the
            // lightmap holds its light.
            float mainLit = 0f;
            if (mainLight >= 0 && mainLight < data.Lights.Length)
            {
                var main = data.Lights[mainLight].light;
                if (main != null && (main.cullingMask & (1 << layer)) != 0 && !(lightmapped && InLightmap(main))) mainLit = 1f;
            }
            var center = bounds.center;
            for (int i = 0; i < data.Lights.Length; i++)
            {
                var light = data.Lights[i];
                var l = light.light;
                if (l == null || (l.cullingMask & (1 << layer)) == 0) continue;
                if (lightmapped && InLightmap(l)) continue;
                var matrix = light.localToWorldMatrix;
                var position = (Vector3)matrix.GetColumn(3);
                if (!LightImportance.Reaches(light.lightType, position, light.range, bounds)) continue;
                if (light.lightType == LightType.Spot && !LightImportance.SpotReaches(matrix, light.range, light.spotAngle, bounds)) continue;
                candidates.Add(new LightImportance.Candidate(i, LightImportance.Of(l, light.lightType, position, light.range, center), candidates.Count));
            }
            LightImportance.Sort(candidates);
            Span<float> indices = stackalloc float[MaxPerObjectLights];
            int count = 0;
            foreach (var candidate in candidates)
            {
                // The index map (the pipeline's) drops lights after the choice.
                int index = map != null ? (candidate.Index < map.Length ? map[candidate.Index] : -1) : candidate.Index;
                if (index < 0) continue;
                if (count < MaxPerObjectLights) indices[count] = index;
                count++;
            }
            var lights = DrawPropertyPool.Copy(null);
            lights[s_LightDataId] = new Vector4(0f, count, mainLit, 0f);
            lights[s_LightIndicesId] = new[] { new Vector4(indices[0], indices[1], indices[2], indices[3]), new Vector4(indices[4], indices[5], indices[6], indices[7]) };
            return lights;
        }

        /// <summary>
        /// Whether a light's direct light is in the lightmaps, so lightmapped renderers get none
        /// of it at run time (unity_LightData.z 0 for the main light, left out of the per-object
        /// lights): a baked or mixed light of a Subtractive bake. Measured: tests/projects/urp
        /// per-object-constants.jsonl (Subtractive: the lightmapped renderers get neither the
        /// mixed sun nor the baked point light, the dynamic ones both; Shadowmask: every renderer
        /// gets both).
        /// </summary>
        internal static bool InLightmap(Light light)
        {
            if (light == null) return false;
            var baking = light.bakingOutput;
            return baking.isBaked && baking.lightmapBakeType != LightmapBakeType.Realtime
                && baking.mixedLightingMode == MixedLightingMode.Subtractive;
        }

        /// <summary>A renderer list's order, as places in <paramref name="items"/>: by sorting
        /// layer and order, by queue, then back to front or (quantized) front to back as the
        /// criteria say.</summary>
        internal static void SortRendererList(List<DrawItem> items, Span<int> order, SortingCriteria criteria)
        {
            if (criteria == SortingCriteria.None) return;
            // The places are sorted with the comparison the draws themselves would be sorted
            // with, by the same algorithm: the same order, ties included.
            s_SortItems = items;
            s_SortCriteria = criteria;
            try { order.Sort(s_CompareAt); }
            finally { s_SortItems = null; }
        }

        static List<DrawItem> s_SortItems;
        static SortingCriteria s_SortCriteria;
        static readonly Comparison<int> s_CompareAt = (a, b) =>
        {
            var items = CollectionsMarshal.AsSpan(s_SortItems);
            return CompareForList(items[a], items[b], s_SortCriteria);
        };

        /// <summary>A renderer list's comparison of two draws for its sorting criteria.</summary>
        internal static int CompareForList(in DrawItem a, in DrawItem b, SortingCriteria criteria)
        {
            bool layer = (criteria & SortingCriteria.SortingLayer) != 0;
            bool queue = (criteria & SortingCriteria.RenderQueue) != 0;
            bool backToFront = (criteria & SortingCriteria.BackToFront) != 0;
            // Renderer priority (lower first) only where the list asks for it: the built-in
            // pipeline ignores it (measured: tests/projects/builtinrp sort_priority_*).
            bool priority = (criteria & SortingCriteria.RendererPriority) != 0;
            if (backToFront && layer && queue && !priority) return CompareTransparent(a, b);
            int c;
            if (layer)
            {
                c = a.SortingLayer.CompareTo(b.SortingLayer);
                if (c != 0) return c;
                c = a.SortingOrder.CompareTo(b.SortingOrder);
                if (c != 0) return c;
            }
            if (priority)
            {
                c = (a.Renderer != null ? a.Renderer.rendererPriority : 0).CompareTo(b.Renderer != null ? b.Renderer.rendererPriority : 0);
                if (c != 0) return c;
            }
            if (queue)
            {
                c = a.Queue.CompareTo(b.Queue);
                if (c != 0) return c;
            }
            if (backToFront)
            {
                c = b.Distance.CompareTo(a.Distance);
                if (c != 0) return c;
            }
            else if ((criteria & SortingCriteria.QuantizedFrontToBack) != 0)
            {
                c = a.Distance.CompareTo(b.Distance);
                if (c != 0) return c;
            }
            return a.Sequence.CompareTo(b.Sequence);
        }

        /// <summary>Culling-group callbacks can change renderer visibility. Read the camera and
        /// renderers afterwards, notify newly visible renderers, then send OnWillRenderObject
        /// before gathering draws (measured: tests/projects/cullinggroup).</summary>
        internal static CameraContext CullCamera(Camera camera)
        {
            Destruction.ImmediateForbiddenDepth++;
            try { return CullCameraCore(camera); }
            finally { Destruction.ImmediateForbiddenDepth--; }
        }

        static CameraContext CullCameraCore(Camera camera)
        {
            CullingGroup.EvaluateForCamera(camera);
            var context = ContextOf(camera);
            SpriteMasking.Begin(camera, context);
            int mask = camera.cullingMask;
            var culling = Culler.ForCamera(camera, context.Position, context.Forward);
            foreach (var renderer in RenderRegistry.Renderers)
            {
                if (!renderer || renderer.forceRenderingOff) continue;
                if ((mask & (1 << renderer.m_GameObject.m_Layer)) == 0) continue;
                if (!LODGroup.Drawn(renderer, camera, context.Position)) continue;
                if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                if (culling.Sees(renderer)) renderer.MarkRendered();
            }
            Renderer.SendWillRenderObject(context.Projection * context.View, mask);
            return context;
        }

        /// <summary>
        /// Renders a camera with the built-in forward pipeline, as Unity orders it: OnPreCull, culling,
        /// OnPreRender, the clear, command buffers at each camera event around the opaque and the
        /// transparent draws, OnPostRender, OnRenderObject, then the image effects (OnRenderImage) and
        /// the last events. Used each frame and by Camera.Render.
        /// </summary>
        internal static void RenderCamera(Camera camera, bool automatic = false, bool restoreActive = true)
        {
            Destruction.ImmediateForbiddenDepth++;
            try { RenderCameraCore(camera, automatic, restoreActive); }
            finally { Destruction.ImmediateForbiddenDepth--; }
        }

        static void RenderCameraCore(Camera camera, bool automatic, bool restoreActive)
        {
            using var temporaries = new CameraTemporaries();
            var previousCamera = Camera.current;
            var previousActive = RenderTexture.ActiveTarget;
            var previousStandIn = RenderTexture.ScreenStandIn;
            var previousProjection = Immediate.Projection;
            var previousModelView = Immediate.ModelView;
            Camera.current = camera;
            camera.UpdateVelocity();
            RenderTexture screenStandIn = null;
            // The engine's own capture of the sky is not a camera the game renders: scripts get
            // no messages or events for it.
            bool messages = !camera.m_EnvironmentCapture;
            try
            {
                // The camera's own message first, then the static event (measured:
                // tests/projects/cullinggroup; also before and after rendering).
                if (messages)
                {
                    SendToCamera(camera, m => m.OnPreCull);
                    Camera.onPreCull?.Invoke(camera);
                }
                var context = CullCamera(camera);
                s_Items.Clear();
                int mask = camera.cullingMask;
                var culling = Culler.ForCamera(camera, context.Position, context.Forward);
                s_Visible.Clear();
                foreach (var renderer in RenderRegistry.Renderers)
                {
                    if (!renderer || renderer.forceRenderingOff) continue;
                    var go = renderer.m_GameObject;
                    if ((mask & (1 << go.m_Layer)) == 0) continue;
                    if (!LODGroup.Drawn(renderer, camera, context.Position)) continue;
                    // Culled renderers are not visible (isVisible, OnBecameVisible during the
                    // camera's culling: measured, tests/projects/messages); ShadowsOnly ones are
                    // only shadow casters. Renderers whose bounds Unbound only approximates are
                    // drawn anyway (the draw of an off-screen one shows nothing).
                    bool inView = culling.Sees(renderer);
                    if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    if (!inView && Culler.TrustsBounds(renderer)) continue;
                    try
                    {
                        renderer.CollectDraws(context, s_Items);
                        s_Visible.Add(renderer);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, renderer);
                    }
                }
                CollectProjectorDraws(context, s_Visible, s_Items);
                foreach (var source in Sources)
                {
                    try { source.CollectCameraDraws(context, s_Items); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                CollectQueued(context, s_Items);
                ReplaceShaders(camera, s_Items);
                var path = camera.actualRenderingPath;
                bool deferred = path == RenderingPath.DeferredShading;
                bool vertexLit = path == RenderingPath.VertexLit;
                if (vertexLit) VertexLitShaders(s_Items);
                SortForCamera(s_Items);
                ForwardLights.BeginCamera(camera);
                s_CameraView = context.View;
                if (messages)
                {
                    SendToCamera(camera, m => m.OnPreRender);
                    Camera.onPreRender?.Invoke(camera);
                    // A camera's render, not the engine's own captures of the environment.
                    Unbound.Runtime.NativePlugins.QueryStereoOverride(3);
                }

                // Image effects render the scene into a temporary, then each effect into the next;
                // [ImageEffectOpaque] ones run on it after the opaque objects.
                var effects = ImageEffects(camera, false);
                var opaqueEffects = ImageEffects(camera, true);
                // Target buffers set from script stand for the target texture (several colors:
                // bound after the camera's setup).
                var buffers = camera.targetTexture == null ? camera.TargetBufferHandles() : null;
                var cameraTarget = camera.targetTexture ?? camera.BufferTarget;
                bool screenCamera = cameraTarget == null && buffers == null;
                // Measured in cameramsaa: an explicit target supplies its samples, including HDR
                // float targets. Quality AA belongs to the screen or to the camera's HDR
                // intermediate when converting into an LDR target. Buffer bindings bypass that
                // intermediate. This also explains graphics' quality-AA single-sample LDR target.
                bool hdrIntermediate = buffers == null && camera.allowHDR &&
                    (cameraTarget == null || !GraphicsFormatUtility.IsHDRFormat(cameraTarget.graphicsFormat));
                bool qualitySamples = buffers == null && (cameraTarget == null || hdrIntermediate);
                int cameraSamples = qualitySamples
                    ? (camera.allowMSAA ? Math.Max(1, QualitySettings.antiAliasing) : 1)
                    : cameraTarget?.antiAliasing ?? 1;
                // A screen camera whose command buffers may read or draw into CameraTarget draws
                // into a texture that stands for the screen, copied onto the screen at the end: a
                // Blit from CameraTarget copies the camera's image (measured: tests/projects/rtdepth
                // target.screen.*). Only full-screen cameras that clear their color take it;
                // image effects keep their null destination.
                if (cameraTarget == null && buffers == null && !deferred && camera.commandBufferCount > 0
                    && effects.Count == 0 && opaqueEffects.Count == 0 && camera.rect == new Rect(0f, 0f, 1f, 1f)
                    && camera.clearFlags is CameraClearFlags.Skybox or CameraClearFlags.SolidColor)
                {
                    screenStandIn = temporaries.Own(RenderTexture.GetTemporary(Math.Max(1, camera.pixelWidth), Math.Max(1, camera.pixelHeight), 24,
                        RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1));
                    cameraTarget = screenStandIn;
                    RenderTexture.ScreenStandIn = screenStandIn;
                }
                RenderTexture sceneTarget = cameraTarget;
                var viewport = context.Viewport;
                if (effects.Count > 0 || opaqueEffects.Count > 0)
                {
                    // Native keeps an explicit target's precision and encoding in the effect
                    // source. Only an HDR-to-LDR camera changes its format before the effects.
                    var format = hdrIntermediate ? RenderTextureFormat.DefaultHDR
                        : cameraTarget != null ? cameraTarget.format
                        : camera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
                    var readWrite = hdrIntermediate ? RenderTextureReadWrite.Linear
                        : cameraTarget != null ? (cameraTarget.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear)
                        : RenderTextureReadWrite.Default;
                    sceneTarget = temporaries.Own(RenderTexture.GetTemporary(Math.Max(1, camera.pixelWidth), Math.Max(1, camera.pixelHeight), 24,
                        format, readWrite, cameraSamples));
                    viewport = new Rect(0, 0, sceneTarget.width, sceneTarget.height);
                }
                // Resolve the selected camera samples before image effects. Their temporary
                // texture must not replace an explicit target's sample policy with quality AA.
                RenderTexture msaaTarget = null, resolveTarget = sceneTarget;
                // A screen camera keeps its intermediate for the full-screen cameras after it
                // that keep the color (Depth only, Don't Clear): they render into it, with its
                // depth and samples, HDR when either camera is (measured in the Editor:
                // tests/projects/camerastack *.full.*). Any other drawing on the screen ends it.
                bool onScreen = sceneTarget == null && buffers == null;
                bool fullScreen = camera.rect == new Rect(0f, 0f, 1f, 1f);
                bool keepsColor = camera.clearFlags is CameraClearFlags.Depth or CameraClearFlags.Nothing;
                // Only the frame's own rendering stacks; Camera.Render() from a script keeps its
                // intermediate to itself, as a nested render must not end the outer camera's.
                var stack = JoinableScreenStack(automatic);
                bool joins = onScreen && fullScreen && keepsColor && !deferred && stack != null;
                // Where a Don't Clear camera's intermediate takes its depth: the camera's before it.
                ulong depthSource = stack != null ? TextureHandles.Of(stack) : 0;
                // A deferred camera takes its depth into the G-buffer's (below) before it ends it.
                bool deferredSeed = deferred && onScreen && fullScreen && camera.clearFlags == CameraClearFlags.Nothing;
                if (automatic && screenCamera && !joins && !deferredSeed) EndScreenStack();
                // The stack's first camera takes what any camera of the stack asks for.
                var needs = (Hdr: hdrIntermediate, Samples: cameraSamples);
                if (automatic && onScreen && fullScreen && !joins && !deferred && s_StackNeeds.TryGetValue(camera, out var group))
                    needs = (hdrIntermediate || group.Hdr,
                        s_SingleSampleStacks.Contains(camera) ? 1 : Math.Max(cameraSamples, group.Samples));
                if (joins && (!hdrIntermediate || GraphicsFormatUtility.IsHDRFormat(stack.graphicsFormat)))
                {
                    msaaTarget = stack;
                    sceneTarget = msaaTarget;
                    viewport = new Rect(0, 0, stack.width, stack.height);
                }
                else if (joins)
                {
                    msaaTarget = RenderTexture.GetTemporary(stack.width, stack.height, 24, RenderTextureFormat.DefaultHDR,
                        RenderTextureReadWrite.Linear, stack.antiAliasing);
                    if (camera.clearFlags == CameraClearFlags.Nothing) GfxStream.CopyDepth(depthSource, TextureHandles.Of(msaaTarget));
                    // A fallback: the plan makes a stack HDR when any of its cameras is, so this runs
                    // only when allowHDR changed since. The stack's own colors, not the 8-bit screen's.
                    bool wireframe = GL.wireframe;
                    if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, false);
                    Graphics.Blit(stack, msaaTarget);
                    if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, true);
                    EndScreenStack();
                    KeepScreenStack(msaaTarget);
                    sceneTarget = msaaTarget;
                    viewport = new Rect(0, 0, stack.width, stack.height);
                }
                else if (deferred)
                {
                    // Deferred: the light buffer is the intermediate (no MSAA), copied to the
                    // target where the MSAA one is resolved.
                    // A partial screen camera's light buffer has its pixel size, as a forward
                    // intermediate has (measured: tests/projects/camerastack *.part.*.def.persp).
                    bool pixelSize = onScreen && !fullScreen;
                    int w = pixelSize ? Math.Max(1, camera.pixelWidth) : sceneTarget != null ? sceneTarget.width : Math.Max(1, Screen.width);
                    int h = pixelSize ? Math.Max(1, camera.pixelHeight) : sceneTarget != null ? sceneTarget.height : Math.Max(1, Screen.height);
                    msaaTarget = temporaries.Own(RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
                    sceneTarget = msaaTarget;
                    viewport = new Rect(0, 0, w, h);
                }
                else if (buffers == null
                    && ((needs.Hdr && (sceneTarget == null || !GraphicsFormatUtility.IsHDRFormat(sceneTarget.graphicsFormat)))
                        || (needs.Samples > 1 && (fullScreen || onScreen)
                            && (sceneTarget == null || sceneTarget.antiAliasing != needs.Samples))))
                {
                    // Native renders HDR at the camera's pixel size, even for a partial rect,
                    // then stretches the resolve across an explicit target. Buffer bindings
                    // bypass this path. The intermediate is needed at one sample too: LDR
                    // blending would clamp each HDR layer before it reaches the next layer.
                    // A partial screen camera's samples too (its edges are antialiased in the Editor).
                    bool pixelSize = needs.Hdr || (onScreen && !fullScreen);
                    int w = pixelSize ? Math.Max(1, camera.pixelWidth) : sceneTarget != null ? sceneTarget.width : Math.Max(1, Screen.width);
                    int h = pixelSize ? Math.Max(1, camera.pixelHeight) : sceneTarget != null ? sceneTarget.height : Math.Max(1, Screen.height);
                    // The HDR intermediate can also reduce explicit target samples to one when
                    // quality AA or allowMSAA is off. Resolve before LDR quantization, including
                    // alpha; quantizing each sample first loses a byte at partial coverage.
                    var format = needs.Hdr ? RenderTextureFormat.DefaultHDR : sceneTarget != null ? sceneTarget.format : RenderTextureFormat.Default;
                    var readWrite = !needs.Hdr && (sceneTarget == null || sceneTarget.sRGB) ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
                    msaaTarget = RenderTexture.GetTemporary(w, h, 24, format, readWrite, needs.Samples);
                    // A full-screen one is the screen's stack (above) until another camera draws.
                    if (automatic && onScreen && fullScreen) KeepScreenStack(msaaTarget);
                    else temporaries.Own(msaaTarget);
                    sceneTarget = msaaTarget;
                    viewport = new Rect(0, 0, w, h);
                    // Don't Clear: a full-screen screen intermediate starts with the depth of the
                    // screen or of the stack before it; every other one starts with cleared depth,
                    // never a pooled temporary's (measured: a partial screen camera, and explicit
                    // full-rect HDR-to-byte targets, tests/projects/cameraexplicitdepth, whose
                    // resolve keeps the owned target's depth). Explicit targets with image effects
                    // follow the same rule unmeasured (docs/COMPATIBILITY_GAPS.md).
                    if (camera.clearFlags == CameraClearFlags.Nothing && onScreen && fullScreen)
                        GfxStream.CopyDepth(depthSource, TextureHandles.Of(msaaTarget));
                    else if (camera.clearFlags == CameraClearFlags.Nothing)
                    {
                        GfxStream.SetTarget(TextureHandles.Of(msaaTarget));
                        GfxStream.Viewport(viewport);
                        GfxStream.Clear((byte)(GfxStream.ClearDepth | GfxStream.ClearStencil), Color.black);
                    }
                }
                // With no color clear, Native seeds the HDR intermediate from the existing
                // explicit target before blending. A pooled temporary's old color is unrelated.
                if (!deferred && hdrIntermediate && sceneTarget != cameraTarget && cameraTarget != null
                    && camera.clearFlags is CameraClearFlags.Depth or CameraClearFlags.Nothing)
                    SeedFromTarget(cameraTarget, sceneTarget);
                // On the screen the intermediate starts as the screen under the camera's rect:
                // Depth only and Don't Clear keep the color earlier cameras drew there.
                else if (!deferred && msaaTarget != null && !joins && resolveTarget == null && keepsColor)
                    SeedFromScreen(msaaTarget, camera.pixelRect);
                int width = (int)viewport.width, height = (int)viewport.height;
                // The camera's textures, as the camera asks and as directional shadows need
                // (measured: tests/projects/builtinrp events.*): depth, then depth and normals.
                var mode = camera.depthTextureMode;
                bool motion = (mode & DepthTextureMode.MotionVectors) != 0;
                // Deferred shading takes its depth texture from the G-buffer and draws the shadows
                // after it; the Vertex Lit path has no shadows.
                if (!deferred && ((mode & (DepthTextureMode.Depth | DepthTextureMode.MotionVectors)) != 0 || (!vertexLit && NeedsShadowDepth(camera))))
                    CameraDepth(context, width, height, camera);
                if ((mode & DepthTextureMode.DepthNormals) != 0) RenderDepthNormals(camera, context, width, height);
                // The other lights' shadows first: the main light's mask is the global the base
                // passes read.
                if (!deferred && !vertexLit)
                {
                    RenderAdditionalShadows(camera, context, width, height);
                    RenderMainLightShadows(camera, context, width, height);
                }
                ulong target = sceneTarget != null ? TextureHandles.Of(sceneTarget) : 0;
                RenderTexture.SetActiveQuietly(sceneTarget);
                camera.m_Rendering = true;
                camera.m_RenderingInto = sceneTarget;
                Immediate.Projection = context.Projection;
                Immediate.ModelView = context.View;
                SetupCamera(target, viewport, deferred ? CameraClearFlags.Nothing : camera.clearFlags, camera.backgroundColor, context.View, context.Projection, 0);
                if (buffers is { } b && sceneTarget == cameraTarget && (b.Colors.Length > 1 || b.Depth != 0))
                {
                    GfxStream.SetTargets(b.Colors, b.Depth);
                    GfxStream.Viewport(viewport);
                    if (camera.clearFlags is CameraClearFlags.Skybox or CameraClearFlags.SolidColor)
                        GfxStream.Clear((byte)(GfxStream.ClearColor | GfxStream.ClearDepth | GfxStream.ClearStencil),
                            QualitySettings.activeColorSpace == ColorSpace.Linear ? camera.backgroundColor.linear : camera.backgroundColor);
                    else if (camera.clearFlags == CameraClearFlags.Depth)
                        GfxStream.Clear((byte)(GfxStream.ClearDepth | GfxStream.ClearStencil), Color.black);
                }
                int transparent = 0;
                while (transparent < s_Items.Count && s_Items[transparent].Queue <= 2500) transparent++;
                int forwardOpaque = 0;
                if (deferred)
                {
                    var gbufferDepth = Temporary(new RenderTextureDescriptor(width, height, GraphicsFormat.None, GraphicsFormat.D24_UNorm_S8_UInt));
                    // Don't Clear on the screen: the G-buffer's depth as an intermediate's (above).
                    if (deferredSeed) GfxStream.CopyDepth(depthSource, TextureHandles.Of(gbufferDepth));
                    else if (onScreen && camera.clearFlags == CameraClearFlags.Nothing)
                    {
                        GfxStream.SetTarget(TextureHandles.Of(gbufferDepth));
                        GfxStream.Viewport(viewport);
                        GfxStream.Clear((byte)(GfxStream.ClearDepth | GfxStream.ClearStencil), Color.black);
                    }
                    if (deferredSeed && automatic) EndScreenStack();
                    // Depth only and Don't Clear: the light buffer starts as what is under the
                    // camera, as a forward intermediate does (measured on the screen: a perspective
                    // deferred camera in a stack, tests/projects/camerastack *.def.persp).
                    if (keepsColor) SeedLightBuffer(camera, onScreen, cameraTarget, sceneTarget);
                    forwardOpaque = RenderDeferredOpaque(camera, context, sceneTarget, gbufferDepth, viewport, transparent);
                }
                // The forward opaque objects (in deferred, those without a Deferred pass; their
                // events only when there are some).
                if (!deferred || forwardOpaque < transparent)
                {
                    RunEvent(camera, CameraEvent.BeforeForwardOpaque, sceneTarget);
                    EmitItems(forwardOpaque, transparent, !deferred && !vertexLit
                        ? RenderDispatchContext.BuiltinForwardOpaque : RenderDispatchContext.Unknown);
                    RunEvent(camera, CameraEvent.AfterForwardOpaque, sceneTarget);
                }
                // The sky's events come with the sky alone (measured: none with a solid color or
                // without a skybox material).
                bool sky = camera.clearFlags == CameraClearFlags.Skybox && Skybox.Of(camera) != null;
                if (sky) RunEvent(camera, CameraEvent.BeforeSkybox, sceneTarget);
                if (sky) DrawSkybox(camera);
                CameraPlanes?.Invoke(camera, false);
                if (sky) RunEvent(camera, CameraEvent.AfterSkybox, sceneTarget);
                // Motion vectors after the sky, before the opaque image effects (measured).
                if (motion) RenderMotionVectors(camera, context, width, height, transparent, automatic);
                RunEvent(camera, CameraEvent.BeforeImageEffectsOpaque, sceneTarget);
                if (opaqueEffects.Count > 0) RunOpaqueImageEffects(opaqueEffects, sceneTarget, temporaries);
                RunEvent(camera, CameraEvent.AfterImageEffectsOpaque, sceneTarget);
                RunEvent(camera, CameraEvent.BeforeForwardAlpha, sceneTarget);
                EmitItems(transparent, s_Items.Count);
                CameraPlanes?.Invoke(camera, true);
                RunEvent(camera, CameraEvent.AfterForwardAlpha, sceneTarget);
                // OnPostRender (the message, then Camera.onPostRender), the halos and lens flares
                // between their events, then OnRenderObject (measured: tests/projects/builtinrp
                // events.*.orderWithMessages, tests/projects/flares o.*).
                if (messages)
                {
                    Unbound.Runtime.NativePlugins.QueryStereoOverride(1);
                    SendToCamera(camera, m => m.OnPostRender);
                    Camera.onPostRender?.Invoke(camera);
                    Unbound.Runtime.NativePlugins.QueryStereoOverride(1);
                }
                RunEvent(camera, CameraEvent.BeforeHaloAndLensFlares, sceneTarget);
                {
                    ulong haloTarget = sceneTarget != null ? TextureHandles.Of(sceneTarget) : 0;
                    if (GfxStream.Current.Color != haloTarget)
                    {
                        GfxStream.SetTarget(haloTarget);
                        GfxStream.Viewport(viewport);
                    }
                    Halos.Render(camera, context.View, context.Projection, viewport);
                }
                RunEvent(camera, CameraEvent.AfterHaloAndLensFlares, sceneTarget);
                if (messages) SendToAll(m => m.OnRenderObject);
                void ResolveIntermediate()
                {
                    if (msaaTarget == null) return;
                    // The resolve is the device's, not a draw: GL.wireframe leaves it alone.
                    bool wireframe = GL.wireframe;
                    if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, false);
                    // An explicit target takes the stretched resolve (above); on the screen a
                    // partial camera only covers its own rect (measured: tests/projects/display).
                    if (resolveTarget == null && camera.rect != new Rect(0f, 0f, 1f, 1f))
                        Immediate.BlitInto(msaaTarget, null, camera.pixelRect);
                    else
                        Graphics.Blit(msaaTarget, resolveTarget);
                    if (resolveTarget == null && fullScreen)
                    {
                        // The full-screen resolve leaves the screen without depth: a later camera
                        // that draws on the screen itself sees none (measured, the Editor).
                        GfxStream.SetTarget(0);
                        GfxStream.Viewport(new Rect(0, 0, Math.Max(1, Screen.width), Math.Max(1, Screen.height)));
                        GfxStream.Clear((byte)(GfxStream.ClearDepth | GfxStream.ClearStencil), Color.black);
                        // What draws on the screen after this ends the stack.
                        if (msaaTarget == s_ScreenStack) MarkScreenStackResolved();
                    }
                    if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, true);
                    if (msaaTarget != s_ScreenStack) RenderTexture.ReleaseTemporary(msaaTarget);
                    sceneTarget = resolveTarget;
                    msaaTarget = null;
                }
                // Without image effects the late events still draw into the scene's MSAA buffer,
                // with its depth intact. Resolve only after those draws (resources commands probe).
                if (deferred || effects.Count > 0 || opaqueEffects.Count > 0) ResolveIntermediate();
                RunEvent(camera, CameraEvent.BeforeImageEffects, sceneTarget);
                if (effects.Count > 0) RunImageEffects(effects, sceneTarget, cameraTarget, hdrIntermediate, temporaries);
                else if (opaqueEffects.Count > 0)
                {
                    // Only opaque effects: the scene goes to the camera's target as it is.
                    Graphics.Blit(sceneTarget, cameraTarget);
                    RenderTexture.ReleaseTemporary(sceneTarget);
                }
                // A single-sample HDR intermediate is resolved before the late events: they see
                // the camera's target (measured: tests/projects/rtdepth target.*.hdr.AfterEverything).
                if (hdrIntermediate && (cameraSamples == 1 || msaaTarget?.antiAliasing == 1)) ResolveIntermediate();
                var lateTarget = msaaTarget != null ? sceneTarget : cameraTarget;
                RunEvent(camera, CameraEvent.AfterImageEffects, lateTarget);
                RunEvent(camera, CameraEvent.AfterEverything, lateTarget);
                if (automatic && onScreen && Gizmos.GameViewEnabled)
                {
                    var savedGizmoState = GfxStream.Current;
                    GfxStream.SetTarget(lateTarget != null ? TextureHandles.Of(lateTarget) : 0);
                    GfxStream.Camera(context.View, context.Projection);
                    GfxStream.Viewport(viewport);
                    Gizmos.Render(camera);
                    GfxStream.Restore(savedGizmoState);
                }
                ResolveIntermediate();
                if (screenStandIn != null)
                {
                    // Presenting the stand-in is a copy, not a draw: GL.wireframe leaves it alone.
                    RenderTexture.ScreenStandIn = previousStandIn;
                    bool wireframe = GL.wireframe;
                    if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, false);
                    // Stored as the screen is, it is presented row for row (sampled from its
                    // bottom up where both are stored top-down).
                    bool topDown = Unbound.Runtime.GraphicsDeviceProfile.Current.ScreenTopDown;
                    Graphics.Blit(screenStandIn, (RenderTexture)null, new Vector2(1f, topDown ? -1f : 1f), new Vector2(0f, topDown ? 1f : 0f));
                    if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, true);
                }
                // What the player loop rendered is the next render's previous view (measured:
                // Camera.Render leaves it).
                if (automatic)
                {
                    camera.KeepViewProjection();
                    if (motion) s_MotionRendered = true;
                }
            }
            finally
            {
                camera.m_Rendering = false;
                camera.m_RenderingInto = null;
                RenderTexture.ScreenStandIn = previousStandIn;
                if (screenStandIn != null) RenderTexture.ReleaseTemporary(screenStandIn);
                ReleaseShadowTemporaries();
                CommandBuffer.ReleaseNamedTemporaries();
                Camera.current = previousCamera;
                // RenderDontRestore leaves the camera's target active (measured).
                RenderTexture.SetActiveQuietly(restoreActive ? previousActive : camera.targetTexture ?? camera.BufferTarget);
                Immediate.Projection = previousProjection;
                Immediate.ModelView = previousModelView;
            }
        }

        /// <summary>The camera's OnRenderImage scripts, in component order: those marked
        /// [ImageEffectOpaque] (<paramref name="opaque"/>) or the others.</summary>
        static List<(MonoBehaviour Script, ScriptMessages Messages)> ImageEffects(Camera camera, bool opaque)
        {
            var effects = new List<(MonoBehaviour, ScriptMessages)>();
            foreach (var component in camera.m_GameObject.m_Components)
            {
                if (component is MonoBehaviour script && script.IsActiveAndEnabledNoThrow)
                {
                    var messages = ScriptMessages.For(script.GetType());
                    if (messages.OnRenderImage != null && messages.OpaqueImageEffect == opaque) effects.Add((script, messages));
                }
            }
            return effects;
        }

        /// <summary>[ImageEffectOpaque] effects: the opaque image is copied out, goes through the
        /// effects, and the last one writes it back into the scene's target, whose depth the
        /// transparent objects then test against (measured: tests/projects/builtinrp
        /// effect_opaque, the transparent sphere drawn over the inverted image).</summary>
        static void RunOpaqueImageEffects(List<(MonoBehaviour Script, ScriptMessages Messages)> effects, RenderTexture scene, CameraTemporaries temporaries)
        {
            var saved = GfxStream.Current;
            var descriptor = scene.descriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            var source = temporaries.Own(RenderTexture.GetTemporary(descriptor));
            Graphics.Blit(scene, source);
            for (int i = 0; i < effects.Count; i++)
            {
                bool last = i == effects.Count - 1;
                var destination = last ? scene : temporaries.Own(RenderTexture.GetTemporary(descriptor));
                RenderTexture.active = destination;
                try { effects[i].Messages.OnRenderImage(effects[i].Script, source, destination); }
                catch (Exception e) { Debug.LogException(e, effects[i].Script); }
                RenderTexture.ReleaseTemporary(source);
                source = destination;
            }
            GfxStream.Restore(saved);
            RenderTexture.SetActiveQuietly(scene);
        }

        static void RunImageEffects(List<(MonoBehaviour Script, ScriptMessages Messages)> effects, RenderTexture scene, RenderTexture cameraTarget, bool hdrIntermediate, CameraTemporaries temporaries)
        {
            var source = scene;
            for (int i = 0; i < effects.Count; i++)
            {
                bool last = i == effects.Count - 1;
                // Native's first effect uses a default-format temporary unless the camera
                // already converts HDR to LDR. Once an effect has supplied that temporary,
                // the last effect in a chain writes directly to the camera target.
                bool direct = last && (i > 0 || cameraTarget == null || hdrIntermediate);
                var destination = direct ? cameraTarget : temporaries.Own(RenderTexture.GetTemporary(source.width, source.height, 0,
                    GraphicsFormatUtility.IsHDRFormat(source.graphicsFormat)
                        ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default));
                if (destination != null) destination.Create();
                RenderTexture.active = source;
                try { effects[i].Messages.OnRenderImage(effects[i].Script, source, destination); }
                catch (Exception e) { Debug.LogException(e, effects[i].Script); }
                RenderTexture.ReleaseTemporary(source);
                source = destination;
                if (last && !direct)
                {
                    Graphics.Blit(destination, cameraTarget);
                    RenderTexture.ReleaseTemporary(destination);
                }
            }
        }

        static void SendToCamera(Camera camera, Func<ScriptMessages, Action<MonoBehaviour>> message)
        {
            // Rendering callbacks cannot remove components immediately. Additions participate
            // in this same message pass (destroyphases' manual-camera addition control).
            var components = camera.m_GameObject.m_Components;
            for (int i = 0; i < components.Count; i++)
                if (components[i] is MonoBehaviour script) SendMessage(script, message);
        }

        /// <summary>A message every active script with it receives (OnRenderObject).</summary>
        static void SendToAll(Func<ScriptMessages, Action<MonoBehaviour>> message)
        {
            var components = Lifecycle.RenderObjectBehaviours;
            int count = components.Count;
            if (count == 0) return;
            var scripts = ArrayPool<MonoBehaviour>.Shared.Rent(count);
            int length = 0;
            for (int i = 0; i < count; i++)
                if (components[i] is MonoBehaviour script && script.IsActiveAndEnabledNoThrow)
                    scripts[length++] = script;
            try
            {
                for (int i = 0; i < length; i++)
                {
                    var script = scripts[i];
                    SendMessage(script, message);
                }
            }
            finally
            {
                Array.Clear(scripts, 0, length);
                ArrayPool<MonoBehaviour>.Shared.Return(scripts);
            }
        }

        static void SendMessage(MonoBehaviour script, Func<ScriptMessages, Action<MonoBehaviour>> message)
        {
            if (!script.IsActiveAndEnabledNoThrow) return;
            var action = message(ScriptMessages.For(script.GetType()));
            if (action == null) return;
            try { action(script); }
            catch (Exception e) { Debug.LogException(e, script); }
        }

        /// <summary>The command buffers at a camera event; the camera's target and matrices are set
        /// again after them.</summary>
        static void RunEvent(Camera camera, CameraEvent evt, RenderTexture target)
        {
            var buffers = camera.CommandBuffersAt(evt);
            if (buffers == null || buffers.Count == 0) return;
            var saved = GfxStream.Current;
            // An earlier blit can leave another destination active. Event commands start on
            // this event's camera target.
            ulong handle = target != null ? TextureHandles.Of(target) : 0;
            if (saved.Color != handle) GfxStream.SetTarget(handle);
            foreach (var buffer in buffers.ToArray())
            {
                var context = new CommandBuffer.Context
                {
                    Camera = camera, CameraTarget = target,
                    Screen = camera.targetTexture == null && camera.BufferTarget == null,
                };
                try { buffer.ExecuteIn(context); }
                catch (Exception e) { Debug.LogException(e); }
            }
            GfxStream.Restore(saved);
            RenderTexture.SetActiveQuietly(target);
        }

        /// <summary>This camera's Graphics.DrawMesh draws (for its shadow casters too).</summary>
        static readonly List<DrawItem> s_Queued = new List<DrawItem>();

        /// <summary>Meshes queued with Graphics.DrawMesh that the camera draws: lit and shadowed as
        /// their settings say (ShadowsOnly ones only cast). A RenderParams draw with world bounds
        /// is culled by them, with the renderers' frustum test, and sorted by their centre; it
        /// still casts shadows as other queued draws do.</summary>
        static void CollectQueued(CameraContext camera, List<DrawItem> into)
        {
            int mask = camera.Camera.cullingMask;
            var culling = camera.Camera.projectionMatrix * camera.Camera.worldToCameraMatrix;
            s_Queued.Clear();
            foreach (var q in Immediate.Queued)
            {
                if (q.Camera != null && q.Camera != camera.Camera) continue;
                if ((mask & (1 << q.Layer)) == 0) continue;
                bool seen = q.WorldBounds is not Bounds world || InFrustum(culling, world);
                var item = QueuedItem(camera, q);
                s_Queued.Add(item);
                if (seen && item.Lighting.Cast != ShadowCastingMode.ShadowsOnly) into.Add(item);
            }
            // A mesh queued for one camera is drawn by its next render only (measured:
            // tests/projects/graphics, a second Camera.Render the same frame draws none).
            Immediate.TakeCameraDraws(camera.Camera);
        }

        /// <summary>A Graphics.DrawMesh / RenderMesh draw as a camera draws it: sorted by the
        /// centre of its world bounds (set ones, else its mesh's through its matrix).</summary>
        internal static DrawItem QueuedItem(CameraContext camera, in Immediate.QueuedMesh q)
        {
            var bounds = WorldBounds(q.Mesh.bounds, q.Matrix);
            return new DrawItem
            {
                Mesh = q.Mesh.Sync(),
                Submesh = Math.Min(q.Submesh, Math.Max(0, q.Mesh.subMeshCount - 1)),
                Material = q.Material,
                Model = q.Matrix,
                Color = Color.white,
                Queue = q.Material.renderQueue,
                Distance = Distance(camera, (q.WorldBounds ?? bounds).center),
                Sequence = s_Sequence++,
                Properties = q.Properties,
                Setup = q.Setup,
                Lighting = q.Lighting ?? ImmediateLighting.Default,
                Bounds = bounds,
                CullBounds = q.WorldBounds ?? bounds,
                Layer = q.Layer,
                RenderingLayerMask = q.RenderingLayerMask,
            };
        }

        /// <summary>Local bounds through a matrix, as world-aligned bounds.</summary>
        internal static Bounds WorldBounds(Bounds local, Matrix4x4 m)
        {
            var center = m.MultiplyPoint(local.center);
            var e = local.extents;
            var x = new Vector3(Mathf.Abs(m.m00), Mathf.Abs(m.m10), Mathf.Abs(m.m20)) * e.x;
            var y = new Vector3(Mathf.Abs(m.m01), Mathf.Abs(m.m11), Mathf.Abs(m.m21)) * e.y;
            var z = new Vector3(Mathf.Abs(m.m02), Mathf.Abs(m.m12), Mathf.Abs(m.m22)) * e.z;
            return new Bounds(center, 2f * (x + y + z));
        }

        /// <summary>The intermediate of the last full-screen camera on the screen this frame, kept
        /// for the full-screen cameras after it that keep the color (see RenderCamera).</summary>
        static RenderTexture s_ScreenStack;
        static long s_ScreenStackLease;
        /// <summary><see cref="GfxStream.ScreenWrites"/> after the stack's resolve: anything drawn on
        /// the screen since (late command buffers, blits, GL, scripts) ends it.</summary>
        static long s_StackScreenMark = -1;

        /// <summary>The stack a full-screen camera that keeps the color renders into: the kept one,
        /// for the frame's rendering (<paramref name="automatic"/>), when nothing drew on the screen
        /// since its resolve.</summary>
        internal static RenderTexture JoinableScreenStack(bool automatic) =>
            automatic && GfxStream.ScreenWrites == s_StackScreenMark ? s_ScreenStack : null;

        /// <summary>The kept stack was resolved onto the screen just now.</summary>
        internal static void MarkScreenStackResolved() => s_StackScreenMark = GfxStream.ScreenWrites;

        internal static void KeepScreenStack(RenderTexture texture)
        {
            s_ScreenStack = texture;
            s_ScreenStackLease = texture.m_PoolLease;
            s_StackScreenMark = -1;
        }

        /// <summary>What each full-screen camera that starts a stack this frame renders with: HDR
        /// when any camera of its stack does, the most samples one asks for (measured: a camera
        /// without MSAA is antialiased under a later one with it, tests/projects/camerastack
        /// q4.aLn.*, q4.aHn.*).</summary>
        internal static readonly Dictionary<Camera, (bool Hdr, int Samples)> s_StackNeeds = new Dictionary<Camera, (bool, int)>();
        /// <summary>The first cameras of stacks a deferred camera continues: those render with one
        /// sample whatever their cameras ask for.</summary>
        internal static readonly HashSet<Camera> s_SingleSampleStacks = new HashSet<Camera>();

        /// <summary>The screen's stacks: a full-screen camera, then the full-screen ones right after
        /// it that keep the color.</summary>
        internal static void PlanScreenStacks(List<Camera> cameras)
        {
            s_StackNeeds.Clear();
            s_SingleSampleStacks.Clear();
            Camera first = null;
            bool hdr = false;
            int samples = 1;
            foreach (var c in cameras)
            {
                // A full-screen deferred camera that keeps the color continues the stack, which
                // then renders without MSAA as the deferred path does (measured: camerastack
                // q4.*.full.*.def.persp, identical to q0).
                if (first != null && ContinuesStackDeferred(c))
                {
                    samples = 1;
                    s_SingleSampleStacks.Add(first);
                }
                bool member = StacksOnScreen(c);
                if (first == null || !member || c.clearFlags is not (CameraClearFlags.Depth or CameraClearFlags.Nothing))
                {
                    if (first != null) s_StackNeeds[first] = (hdr, samples);
                    first = member ? c : null;
                    hdr = false;
                    samples = 1;
                }
                if (first == null) continue;
                hdr |= c.allowHDR;
                samples = Math.Max(samples, c.allowMSAA ? Math.Max(1, QualitySettings.antiAliasing) : 1);
            }
            if (first != null) s_StackNeeds[first] = (hdr, samples);
        }

        static bool ContinuesStackDeferred(Camera c) =>
            c.targetTexture == null && c.BufferTarget == null && c.rect == new Rect(0f, 0f, 1f, 1f)
            && c.actualRenderingPath == RenderingPath.DeferredShading
            && c.clearFlags is CameraClearFlags.Depth or CameraClearFlags.Nothing;

        /// <summary>Whether a camera renders on the whole screen with nothing between it and the
        /// screen (RenderCamera's own intermediate aside): no target, image effects, deferred path
        /// or command-buffer stand-in.</summary>
        static bool StacksOnScreen(Camera c) =>
            c.targetTexture == null && c.BufferTarget == null && c.rect == new Rect(0f, 0f, 1f, 1f)
            && c.actualRenderingPath != RenderingPath.DeferredShading
            && ImageEffects(c, false).Count == 0 && ImageEffects(c, true).Count == 0
            && !(c.commandBufferCount > 0 && c.clearFlags is CameraClearFlags.Skybox or CameraClearFlags.SolidColor);

        internal static void EndScreenStack()
        {
            var texture = s_ScreenStack;
            if (texture != null && !texture.m_PooledFree && texture.m_PoolLease == s_ScreenStackLease)
                RenderTexture.ReleaseTemporary(texture);
            s_ScreenStack = null;
            s_StackScreenMark = -1;
        }

        /// <summary>A deferred camera's light buffer seeded with the color under the camera, as a
        /// forward intermediate is: the screen's under its rect, else its whole explicit target
        /// (the buffer goes back onto the whole target).</summary>
        static void SeedLightBuffer(Camera camera, bool onScreen, RenderTexture cameraTarget, RenderTexture light)
        {
            if (onScreen) SeedFromScreen(light, camera.pixelRect);
            else if (cameraTarget != null && cameraTarget != light) SeedFromTarget(cameraTarget, light);
        }

        /// <summary>An intermediate seeded with its explicit target's color (a copy, not a draw:
        /// GL.wireframe leaves it alone).</summary>
        static void SeedFromTarget(RenderTexture target, RenderTexture intermediate)
        {
            bool wireframe = GL.wireframe;
            if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, false);
            Graphics.Blit(target, intermediate);
            if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, true);
        }

        /// <summary>The screen's pixels under <paramref name="pixelRect"/>, stretched over the
        /// whole of a screen camera's intermediate (its pixel size). The screen is copied as the
        /// bytes it stores into an 8-bit texture of the same encoding (sRGB in Linear color
        /// space), so sampling reads the colors the screen shows.</summary>
        static void SeedFromScreen(RenderTexture intermediate, Rect pixelRect)
        {
            int width = Math.Max(1, Screen.width), height = Math.Max(1, Screen.height);
            var screen = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            screen.filterMode = FilterMode.Point;
            GfxStream.CopyTexture(RenderTexture.ScreenStandIn?.EnsureCreated() ?? 0, 0, 0, new RectInt(0, 0, width, height),
                screen.EnsureCreated(), 0, 0, 0, 0);
            // A copy, not a draw: GL.wireframe leaves it alone.
            bool wireframe = GL.wireframe;
            if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, false);
            // The screen's own rows top-down (copied as stored; its stand-in's too) are read
            // upside down.
            bool topDown = Unbound.Runtime.GraphicsDeviceProfile.Current.ScreenTopDown;
            float scaleY = pixelRect.height / height, offsetY = pixelRect.y / height;
            Graphics.Blit(screen, intermediate, new Vector2(pixelRect.width / width, topDown ? -scaleY : scaleY),
                new Vector2(pixelRect.x / width, topDown ? 1f - offsetY : offsetY));
            if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, true);
            RenderTexture.ReleaseTemporary(screen);
        }

        /// <summary>A camera's target, viewport, clear and matrices.</summary>
        static void SetupCamera(ulong target, Rect viewport, CameraClearFlags clearFlags, Color background,
            in Matrix4x4 view, in Matrix4x4 projection, uint flags, int slice = 0)
        {
            s_CameraCount++;
            if (Trace.RenderDetailOn && Time.frameCount % 60 == 1)
                s_DrawDescriptions.Add($"  camera viewport {viewport} clear {clearFlags} {background} draws {s_Items.Count} target {target}\n    view {view.ToString().Replace("\n", " | ")}\n    proj {projection.ToString().Replace("\n", " | ")}");
            GfxStream.SetTarget(target, 0, 0, slice);
            GfxStream.Viewport(viewport);
            byte clear = clearFlags switch
            {
                CameraClearFlags.Skybox or CameraClearFlags.SolidColor => (byte)(GfxStream.ClearColor | GfxStream.ClearDepth | GfxStream.ClearStencil),
                CameraClearFlags.Depth => (byte)(GfxStream.ClearDepth | GfxStream.ClearStencil),
                _ => 0,
            };
            // The background is an sRGB color: a linear project clears with its linear value (a
            // scriptable pipeline converts it itself, CoreUtils.ConvertSRGBToActiveColorSpace).
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) background = background.linear;
            if (clear != 0) GfxStream.Clear(clear, background);
            GfxStream.Camera(view, projection, flags | GfxStream.CameraBegin | GfxStream.CameraProperties);
        }

        static readonly ForwardLights.ObjectLights s_ObjectLights = new ForwardLights.ObjectLights();
        static readonly int s_WorldSpaceLightPos0Id = Shader.PropertyToID("_WorldSpaceLightPos0");
        static readonly int s_LightColor0Id = Shader.PropertyToID("_LightColor0");
        static readonly Dictionary<string, int> s_ForwardAddPass = new Dictionary<string, int>();

        static readonly Dictionary<string, bool> s_VertexLit = new Dictionary<string, bool>();
        static Matrix4x4 s_CameraView = Matrix4x4.identity;

        /// <summary>Whether the built-in renderer draws the shader's Vertex passes (it has no
        /// forward ones).</summary>
        static bool IsVertexLit(Shader shader)
        {
            if (shader == null) return false;
            if (s_VertexLit.TryGetValue(shader.DrawnName, out bool vertexLit)) return vertexLit;
            vertexLit = false;
            var info = ShaderPassInfo.Of(shader);
            if (info != null)
            {
                bool forward = false, vertex = false;
                for (int i = 0; i < info.Passes.Count; i++)
                {
                    var mode = info.LightMode(i);
                    forward |= mode == "ForwardBase" || mode == "ForwardAdd";
                    vertex |= mode == "Vertex";
                }
                vertexLit = vertex && !forward;
            }
            s_VertexLit[shader.DrawnName] = vertexLit;
            return vertexLit;
        }

        /// <summary>The shader's ForwardAdd pass (-1 when it has none).</summary>
        static int ForwardAddPass(Shader shader)
        {
            if (shader == null) return -1;
            if (s_ForwardAddPass.TryGetValue(shader.DrawnName, out int pass)) return pass;
            pass = -1;
            var info = ShaderPassInfo.Of(shader);
            if (info != null)
                for (int i = 0; i < info.Passes.Count; i++)
                    if (info.LightMode(i) == "ForwardAdd") { pass = i; break; }
            s_ForwardAddPass[shader.DrawnName] = pass;
            return pass;
        }

        /// <summary><see cref="s_Items"/>[from..to] as draws (sorted). A lit renderer's draw is its
        /// base passes (with its vertex and SH lights), then its ForwardAdd pass once per pixel
        /// light (<see cref="ForwardLights"/>).</summary>
        static void EmitItems(int from, int to, RenderDispatchContext dispatch = RenderDispatchContext.Unknown)
        {
            // Materials upload their changes first: an upload hands over what was recorded before it.
            if (s_MaterialHandles.Length < s_Items.Count) Array.Resize(ref s_MaterialHandles, Math.Max(s_Items.Count, s_MaterialHandles.Length * 2));
            for (int i = from; i < to; i++) s_MaterialHandles[i] = s_Items[i].Material.Sync();
            for (int i = from; i < to; i++)
            {
                var item = s_Items[i];
                // A maximum LOD that leaves the shader no SubShader hides it (measured).
                if (item.Material.shader != null && item.Material.shader.IsLodHidden) continue;
                bool meshLike = IsLit(item);
                int layer = meshLike ? LayerOf(item) : 0;
                // The object's base pass light: the main one, unless its culling mask leaves the
                // object out.
                var main = ForwardLights.Main;
                bool otherBase = meshLike && main != null && (main.cullingMask & (1 << layer)) == 0;
                bool lit = meshLike && (ForwardLights.Any || IsVertexLit(item.Material.shader) || otherBase);
                // The main light's screen-space shadows reach the renderers that receive shadows.
                string shadowKeyword = s_ScreenShadows && !otherBase && meshLike && ReceivesShadows(item) ? "SHADOWS_SCREEN" : null;
                if (!lit)
                {
                    EmitForward(item, s_MaterialHandles[i], true, true, null, shadowKeyword, dispatch: dispatch);
                    continue;
                }
                var bounds = BoundsOf(item);
                if (IsVertexLit(item.Material.shader))
                {
                    var vertexLit = new Dictionary<int, object>();
                    ForwardLights.VertexLitProperties(bounds, layer, s_CameraView, vertexLit);
                    EmitForward(item, s_MaterialHandles[i], true, true, vertexLit, dispatch: dispatch);
                    continue;
                }
                var baseLight = otherBase ? ForwardLights.BaseLightFor(layer) : main;
                ForwardLights.Classify(bounds, layer, s_ObjectLights, baseLight);
                Dictionary<int, object> extra = null;
                string keyword = null;
                if (otherBase)
                {
                    extra = new Dictionary<int, object>();
                    if (baseLight != null)
                    {
                        var forward = baseLight.transform.forward;
                        extra[s_WorldSpaceLightPos0Id] = new Vector4(-forward.x, -forward.y, -forward.z, 0f);
                        extra[s_LightColor0Id] = (Vector4)baseLight.FinalColor(QualitySettings.activeColorSpace == ColorSpace.Linear);
                    }
                    else
                    {
                        extra[s_WorldSpaceLightPos0Id] = Vector4.zero;
                        extra[s_LightColor0Id] = Vector4.zero;
                    }
                }
                if (s_ObjectLights.Vertex.Count > 0)
                {
                    extra ??= new Dictionary<int, object>();
                    ForwardLights.VertexLightProperties(s_ObjectLights, extra);
                    keyword = "VERTEXLIGHT_ON";
                }
                if (s_ObjectLights.Spherical.Count > 0)
                {
                    extra ??= new Dictionary<int, object>();
                    var sh = ForwardLights.WithSphericalLights(RenderSettings.ambientProbe, s_ObjectLights, bounds.center);
                    sh.ShaderConstants(out var ar, out var ag, out var ab, out var br, out var bg, out var bb, out var c);
                    extra[Shader.PropertyToID("unity_SHAr")] = ar;
                    extra[Shader.PropertyToID("unity_SHAg")] = ag;
                    extra[Shader.PropertyToID("unity_SHAb")] = ab;
                    extra[Shader.PropertyToID("unity_SHBr")] = br;
                    extra[Shader.PropertyToID("unity_SHBg")] = bg;
                    extra[Shader.PropertyToID("unity_SHBb")] = bb;
                    extra[Shader.PropertyToID("unity_SHC")] = c;
                }
                EmitForward(item, s_MaterialHandles[i], true, true, extra, keyword, shadowKeyword, dispatch);
                if (s_ObjectLights.Pixel.Count == 0) continue;
                int addPass = ForwardAddPass(item.Material.shader);
                if (addPass < 0 || item.Material.PassDisabled("ForwardAdd")) continue;
                bool receives = ReceivesShadows(item);
                for (int p = 0; p < s_ObjectLights.Pixel.Count; p++)
                {
                    var light = s_ObjectLights.Pixel[p];
                    var properties = new Dictionary<int, object>();
                    string lightKeyword = ForwardLights.AddPassProperties(light, s_ObjectLights.PixelScale(p), properties);
                    AddPassShadow(light, receives, properties, out var shadowDepth, out var shadowSoft);
                    EmitDraw(item, s_MaterialHandles[i], addPass, false, false, properties, lightKeyword, shadowDepth, shadowSoft, dispatch);
                }
            }
        }

        /// <summary>Unity's order within a camera: opaque before transparent; opaque by queue then
        /// front to back; transparent by sorting layer, order in layer, queue, then back to front.</summary>
        internal static void SortForCamera(List<DrawItem> items)
        {
            items.Sort((a, b) =>
            {
                bool aTransparent = a.Queue > 2500, bTransparent = b.Queue > 2500;
                if (aTransparent != bTransparent) return aTransparent ? 1 : -1;
                int c;
                if (!aTransparent)
                {
                    c = a.Queue.CompareTo(b.Queue);
                    if (c != 0) return c;
                    c = a.Distance.CompareTo(b.Distance);
                    return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
                }
                return CompareTransparent(a, b);
            });
        }

        /// <summary>Transparent order: sorting layer, order in layer, queue, then back to front. A
        /// sorting group takes part as one unit with its own layer, order and distance; draws in
        /// the same group compare one level down.</summary>
        static int CompareTransparent(in DrawItem a, in DrawItem b)
        {
            int depthA = a.Groups?.Length ?? 0, depthB = b.Groups?.Length ?? 0;
            for (int level = 0; ; level++)
            {
                bool groupA = level < depthA, groupB = level < depthB;
                if (groupA && groupB && a.Groups[level].Id == b.Groups[level].Id) continue;
                int layerA = groupA ? a.Groups[level].SortingLayer : a.SortingLayer;
                int layerB = groupB ? b.Groups[level].SortingLayer : b.SortingLayer;
                int c = layerA.CompareTo(layerB);
                if (c != 0) return c;
                int orderA = groupA ? a.Groups[level].SortingOrder : a.SortingOrder;
                int orderB = groupB ? b.Groups[level].SortingOrder : b.SortingOrder;
                c = orderA.CompareTo(orderB);
                if (c != 0) return c;
                if (!groupA && !groupB)
                {
                    c = a.Queue.CompareTo(b.Queue);
                    if (c != 0) return c;
                }
                float distanceA = groupA ? a.Groups[level].Distance : a.Distance;
                float distanceB = groupB ? b.Groups[level].Distance : b.Distance;
                c = distanceB.CompareTo(distanceA);
                if (c != 0) return c;
                if (groupA && groupB) return a.Groups[level].Id.CompareTo(b.Groups[level].Id);
                return a.Sequence.CompareTo(b.Sequence);
            }
        }

        static void TraceFrame()
        {
            if (!Unbound.Runtime.Trace.RenderOn || Time.frameCount % 60 != 1) return;
            Unbound.Runtime.Trace.Render($"frame: {s_CameraCount} cameras, {s_DrawCount} draws, {Screen.width}x{Screen.height}, pipeline draws overlay UI: {UnityEngine.Rendering.SupportedRenderingFeatures.active.rendersUIOverlay}");
            if (Unbound.Runtime.Trace.RenderDetailOn)
                foreach (var line in s_DrawDescriptions) Unbound.Runtime.Trace.Render(line);
        }
    }
}

namespace UnityEngine
{
    public sealed partial class QualitySettings : Object
    {
        internal static ColorSpace s_ColorSpace = ColorSpace.Gamma;

        public static ColorSpace activeColorSpace => s_ColorSpace;
        public static ColorSpace desiredColorSpace => s_ColorSpace;
        public static int vSyncCount { get; set; } = 1;
        public static int antiAliasing { get; set; }
        public static int pixelLightCount { get; set; } = 4;
        public static float lodBias { get; set; } = 1f;
        public static int maximumLODLevel { get; set; }
        public static ShadowQuality shadows { get; set; } = ShadowQuality.All;
        public static float shadowDistance { get; set; } = 50f;
        public static ShadowProjection shadowProjection { get; set; } = ShadowProjection.StableFit;
        public static int shadowCascades { get; set; } = 4;
        public static ShadowResolution shadowResolution { get; set; } = ShadowResolution.High;
        public static ShadowmaskMode shadowmaskMode { get; set; } = ShadowmaskMode.DistanceShadowmask;
        public static float shadowNearPlaneOffset { get; set; } = 3f;
        public static float shadowCascade2Split { get; set; } = 1f / 3f;
        public static Vector3 shadowCascade4Split { get; set; } = new Vector3(0.06666667f, 0.2f, 0.46666667f);
        public static int globalTextureMipmapLimit { get; set; }

        [global::System.@ObsoleteAttribute("masterTextureLimit has been deprecated. Use globalTextureMipmapLimit instead (UnityUpgradable) -> globalTextureMipmapLimit", false)]
        public static int masterTextureLimit { get => globalTextureMipmapLimit; set => globalTextureMipmapLimit = value; }
        public static AnisotropicFiltering anisotropicFiltering { get => Texture.anisotropicFiltering; set => Texture.anisotropicFiltering = value; }
        public static bool softParticles { get; set; }
        public static bool softVegetation { get; set; }
        public static bool realtimeReflectionProbes { get; set; }
        public static bool billboardsFaceCameraPosition { get; set; }
        public static int particleRaycastBudget { get; set; } = 4096;
        public static int realtimeGICPUUsage { get; set; } = 25;
        public static SkinWeights skinWeights { get; set; } = SkinWeights.FourBones;
        public static int asyncUploadTimeSlice { get; set; } = 2;
        public static int asyncUploadBufferSize { get; set; } = 16;
        public static bool asyncUploadPersistentBuffer { get; set; } = true;
        public static float resolutionScalingFixedDPIFactor { get; set; } = 1f;
        public static string[] names { get; internal set; } = { "Default" };
        public static int count => names.Length;
        static int s_Level;

        /// <summary>Per level, its settings as the project's QualitySettings asset has them.</summary>
        internal static List<Dictionary<string, System.Text.Json.JsonElement>> s_LevelSettings;

        public static event Action<int, int> activeQualityLevelChanged;

        public static int GetQualityLevel() => s_Level;

        public static void SetQualityLevel(int index) => SetQualityLevel(index, true);

        /// <summary>Selects a level and takes its settings; the change is announced when the level
        /// differs.</summary>
        public static void SetQualityLevel(int index, [Internal.DefaultValue("true")] bool applyExpensiveChanges)
        {
            int previous = s_Level;
            s_Level = Mathf.Clamp(index, 0, names.Length - 1);
            ApplyLevel(s_Level);
            if (previous != s_Level) activeQualityLevelChanged?.Invoke(previous, s_Level);
        }

        static void ApplyLevel(int level)
        {
            if (s_LevelSettings == null || level < 0 || level >= s_LevelSettings.Count || s_LevelSettings[level] == null) return;
            var d = s_LevelSettings[level];
            float F(string key, float fallback) =>
                d.TryGetValue(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetSingle() : fallback;
            int I(string key, int fallback) => (int)F(key, fallback);
            bool B(string key, bool fallback) => F(key, fallback ? 1 : 0) != 0;
            pixelLightCount = I("pixelLightCount", pixelLightCount);
            shadows = (ShadowQuality)I("shadows", (int)shadows);
            shadowResolution = (ShadowResolution)I("shadowResolution", (int)shadowResolution);
            shadowProjection = (ShadowProjection)I("shadowProjection", (int)shadowProjection);
            shadowCascades = I("shadowCascades", shadowCascades);
            shadowDistance = F("shadowDistance", shadowDistance);
            shadowNearPlaneOffset = F("shadowNearPlaneOffset", shadowNearPlaneOffset);
            shadowCascade2Split = F("shadowCascade2Split", shadowCascade2Split);
            if (d.TryGetValue("shadowCascade4Split", out var split) && split.ValueKind == System.Text.Json.JsonValueKind.Array && split.GetArrayLength() == 3)
                shadowCascade4Split = new Vector3(split[0].GetSingle(), split[1].GetSingle(), split[2].GetSingle());
            shadowmaskMode = (ShadowmaskMode)I("shadowmaskMode", (int)shadowmaskMode);
            skinWeights = (SkinWeights)I("skinWeights", (int)skinWeights);
            globalTextureMipmapLimit = I("globalTextureMipmapLimit", globalTextureMipmapLimit);
            anisotropicFiltering = (AnisotropicFiltering)I("anisotropicTextures", (int)anisotropicFiltering);
            antiAliasing = I("antiAliasing", antiAliasing);
            softParticles = B("softParticles", softParticles);
            softVegetation = B("softVegetation", softVegetation);
            realtimeReflectionProbes = B("realtimeReflectionProbes", realtimeReflectionProbes);
            billboardsFaceCameraPosition = B("billboardsFaceCameraPosition", billboardsFaceCameraPosition);
            vSyncCount = I("vSyncCount", vSyncCount);
            realtimeGICPUUsage = I("realtimeGICPUUsage", realtimeGICPUUsage);
            lodBias = F("lodBias", lodBias);
            maximumLODLevel = I("maximumLODLevel", maximumLODLevel);
            particleRaycastBudget = I("particleRaycastBudget", particleRaycastBudget);
            asyncUploadTimeSlice = I("asyncUploadTimeSlice", asyncUploadTimeSlice);
            asyncUploadBufferSize = I("asyncUploadBufferSize", asyncUploadBufferSize);
            asyncUploadPersistentBuffer = B("asyncUploadPersistentBuffer", asyncUploadPersistentBuffer);
            resolutionScalingFixedDPIFactor = F("resolutionScalingFixedDPIFactor", resolutionScalingFixedDPIFactor);
        }

        static readonly Dictionary<int, RenderPipelineAsset> s_Pipelines = new Dictionary<int, RenderPipelineAsset>();

        /// <summary>The current level's pipeline asset (null: the default one).</summary>
        public static RenderPipelineAsset renderPipeline
        {
            get => s_Level < names.Length ? GetRenderPipelineAssetAt(s_Level) : null;
            set => s_Pipelines[s_Level] = value;
        }

        public static RenderPipelineAsset GetRenderPipelineAssetAt(int index)
        {
            if (index < 0 || index >= names.Length) throw new IndexOutOfRangeException($"{nameof(index)} is out of range [0..{names.Length}[");
            if (!s_Pipelines.TryGetValue(index, out var asset))
            {
                var levels = GraphicsSettings.Project.QualityLevels;
                asset = index < levels.Count ? GraphicsSettings.LoadRef<RenderPipelineAsset>(levels[index]) : null;
                s_Pipelines[index] = asset;
            }
            return asset;
        }

        public static void IncreaseLevel() => SetQualityLevel(s_Level + 1);
        public static void IncreaseLevel([Internal.DefaultValue("false")] bool applyExpensiveChanges) => IncreaseLevel();
        public static void DecreaseLevel() => SetQualityLevel(s_Level - 1);
        public static void DecreaseLevel([Internal.DefaultValue("false")] bool applyExpensiveChanges) => DecreaseLevel();
    }

}
