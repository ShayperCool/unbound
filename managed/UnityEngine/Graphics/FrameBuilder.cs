using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
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
    internal static class FrameBuilder
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
        }

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
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.FinishFrameRendering), Render);
        }

        internal static void EnsureRegistered() { }

        internal static long NextSequence() => s_Sequence++;

        /// <summary>Adds one draw per submesh/material pair of a mesh renderer.</summary>
        internal static void AddMeshDraws(Renderer renderer, Mesh mesh, Matrix4x4 model, Color color, CameraContext camera, List<DrawItem> into, Texture mainTexture = null)
        {
            var materials = renderer.m_Materials;
            if (materials.Length == 0) return;
            ulong meshHandle = mesh.Sync();
            ulong texture = mainTexture != null ? TextureHandles.Of(mainTexture) : 0;
            var center = renderer.bounds.center;
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
                });
            }
        }

        internal static float Distance(CameraContext camera, Vector3 point) =>
            camera.Orthographic ? Vector3.Dot(point - camera.Position, camera.Forward) : (point - camera.Position).sqrMagnitude;

        static readonly List<(string Name, Vector4 Value)> s_Lighting = new List<(string, Vector4)>();
        static readonly List<string> s_FrameKeywords = new List<string>();
        static readonly string[] s_FogKeywords = { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" };
        static int s_CameraCount, s_DrawCount;
        static ulong[] s_MaterialHandles = new ulong[256];

        static void Render()
        {
            Camera.current = null;
            s_DrawDescriptions.Clear();
            s_CameraCount = s_DrawCount = 0;
            GfxStream.BeginFrame(Screen.width, Screen.height, QualitySettings.activeColorSpace);
            EmitLightingGlobals();

            var cameras = Camera.CollectActive();
            cameras.Sort((a, b) =>
            {
                int rt = (b.targetTexture != null).CompareTo(a.targetTexture != null);
                return rt != 0 ? rt : a.depth.CompareTo(b.depth);
            });
            // A scriptable pipeline renders the cameras itself; else the built-in one does.
            bool srp = RenderPipelineManager.Render(cameras);
            if (srp) s_CameraCount += cameras.Count;
            else foreach (var camera in cameras) RenderCamera(camera);

            // Overlay canvases: a pixel-space camera over the whole screen, drawn last (unless the
            // pipeline draws them).
            if (!srp || !SupportedRenderingFeatures.active.rendersUIOverlay || s_CameraCount == 0)
                DrawOverlayCanvases(null);
            TraceFrame();
            GfxStream.EndFrame();
            Immediate.Queued.Clear();
            Culling.EndFrame();
            RenderTexture.CollectTemporaries();
        }

        /// <summary>The lighting environment's built-in variables (ambient probe, fog, main
        /// light) and fog keywords, for what renders next.</summary>
        internal static void EmitLightingGlobals()
        {
            s_Lighting.Clear();
            s_FrameKeywords.Clear();
            LightingGlobals.Collect(s_Lighting, s_FrameKeywords);
            foreach (var (name, value) in s_Lighting) GfxStream.GlobalVector(name, value);
            foreach (var fog in s_FogKeywords) GfxStream.Keyword(fog, s_FrameKeywords.Contains(fog));
            // Renderers outside every reflection probe reflect the default reflection.
            var reflection = ReflectionProbe.defaultTexture;
            if (reflection != null)
            {
                ulong handle = TextureHandles.Of(reflection);
                var decode = ReflectionProbe.defaultTextureHDRDecodeValues;
                GfxStream.GlobalTexture("unity_SpecCube0", handle);
                GfxStream.GlobalTexture("unity_SpecCube1", handle);
                GfxStream.GlobalVector("unity_SpecCube0_HDR", decode);
                GfxStream.GlobalVector("unity_SpecCube1_HDR", decode);
            }
        }

        /// <summary>Screen-space overlay canvases over the screen (with no camera at all, black:
        /// "No cameras rendering"). A pipeline draws them into its own target
        /// (<paramref name="camera"/> set).</summary>
        internal static void DrawOverlayCanvases(Camera camera)
        {
            s_Items.Clear();
            foreach (var source in Sources)
            {
                try { source.CollectOverlayDraws(s_Items); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (camera != null)
            {
                if (s_Items.Count == 0) return;
                var saved = GfxStream.Current;
                GfxStream.Viewport(new Rect(0, 0, Screen.width, Screen.height));
                GfxStream.Camera(Matrix4x4.identity, Matrix4x4.Ortho(0, Screen.width, 0, Screen.height, -100000f, 100000f), GfxStream.CameraOverlay);
                EmitItems(0, s_Items.Count);
                GfxStream.Restore(saved);
                return;
            }
            if (s_Items.Count > 0 || s_CameraCount == 0)
            {
                SetupCamera(0, new Rect(0, 0, Screen.width, Screen.height),
                    s_CameraCount == 0 ? CameraClearFlags.SolidColor : CameraClearFlags.Nothing, Color.black,
                    Matrix4x4.identity, Matrix4x4.Ortho(0, Screen.width, 0, Screen.height, -100000f, 100000f), GfxStream.CameraOverlay);
                EmitItems(0, s_Items.Count);
            }
        }

        /// <summary>OnRenderObject on every script with it.</summary>
        internal static void InvokeOnRenderObject() => SendToAll(m => m.OnRenderObject);

        /// <summary>The skybox (not drawn yet: the camera's clear color stands in).</summary>
        static Mesh s_SkyboxMesh;

        /// <summary>The sky the camera sees: RenderSettings.skybox drawn on a sphere around the
        /// camera inside its far plane (skybox shaders read directions from the vertices; the
        /// procedural sky computes its colors per vertex, so the sphere is fine).</summary>
        internal static void DrawSkybox(Camera camera)
        {
            var material = RenderSettings.skybox;
            if (material == null || camera == null) return;
            var mesh = s_SkyboxMesh ??= SkyboxMesh();
            float radius = camera.farClipPlane * 0.9f;
            var model = Matrix4x4.TRS(camera.transform.position, Quaternion.identity, new Vector3(radius, radius, radius));
            GfxStream.Draw(mesh.Sync(), 0, material.Sync(), 0, model, Color.white);
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
            return new CameraContext
            {
                Camera = camera,
                Position = t.position,
                Forward = t.forward,
                View = camera.worldToCameraMatrix,
                Projection = camera.projectionMatrix,
                Viewport = camera.pixelRect,
                Orthographic = camera.orthographic,
            };
        }

        static readonly List<string> s_PassTagScratch = new List<string>();

        /// <summary>
        /// A renderer list: the culled renderers' draws whose queue, layer, rendering layer and
        /// sorting layer pass the filter, sorted by the criteria, each drawing its passes whose
        /// LightMode is one of the list's (or the override material's pass).
        /// </summary>
        /// <param name="shadows">A shadow caster list: renderers that cast shadows (with
        /// ShadowsOnly ones), else renderers drawn normally (without ShadowsOnly ones).</param>
        internal static void DrawRendererList(CullingData data, DrawingSettings drawing, FilteringSettings filtering, List<ShaderTagId> passNames, ShadowObjectsFilter? shadows = null)
        {
            var context = ContextOf(data.Camera);
            var sorting = drawing.sortingSettings;
            context.Position = sorting.cameraPosition;
            var view = sorting.worldToCameraMatrix;
            context.Forward = -new Vector3(view.m20, view.m21, view.m22);
            s_Items.Clear();
            var range = filtering.renderQueueRange;
            var layers = filtering.sortingLayerRange;
            bool allLayers = layers == SortingLayerRange.all;
            foreach (var renderer in data.Renderers)
            {
                if (!renderer) continue;
                var casting = renderer.shadowCastingMode;
                if (shadows.HasValue ? casting == ShadowCastingMode.Off : casting == ShadowCastingMode.ShadowsOnly) continue;
                if ((filtering.layerMask & (1 << renderer.gameObject.layer)) == 0) continue;
                if ((filtering.renderingLayerMask & renderer.renderingLayerMask) == 0) continue;
                if (filtering.excludeMotionVectorObjects && renderer.motionVectorGenerationMode == MotionVectorGenerationMode.Object) continue;
                if (!allLayers)
                {
                    int value = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID);
                    if (value < layers.lowerBound || value > layers.upperBound) continue;
                }
                int from = s_Items.Count;
                try { renderer.CollectDraws(context, s_Items); }
                catch (Exception e) { Debug.LogException(e, renderer); }
            }
            foreach (var source in Sources)
            {
                try { source.CollectCameraDraws(context, s_Items); }
                catch (Exception e) { Debug.LogException(e); }
            }
            // Only draws whose material has a pass with one of the list's LightModes (the override
            // material then draws them instead).
            s_PassTagScratch.Clear();
            foreach (var tag in passNames) s_PassTagScratch.Add(tag.name);
            s_Items.RemoveAll(item =>
            {
                if (item.Queue < range.lowerBound || item.Queue > range.upperBound) return true;
                var info = ShaderPassInfo.Of(item.Material.shader);
                return info != null && !info.HasPassFor(s_PassTagScratch);
            });
            SortRendererList(s_Items, sorting.criteria);

            var overrideMaterial = drawing.overrideMaterial;
            int pass = GfxStream.TaggedPasses;
            if (overrideMaterial != null) pass = drawing.overrideMaterialPassIndex;
            else GfxStream.PassTags(s_PassTagScratch.ToArray());
            // Per-object lights: the additional lights reaching each renderer, as the pipeline's
            // light index map numbers them.
            if ((drawing.perObjectData & (PerObjectData.LightData | PerObjectData.LightIndices)) != 0)
            {
                var lights = new Dictionary<Renderer, Dictionary<int, object>>();
                for (int i = 0; i < s_Items.Count; i++)
                {
                    var item = s_Items[i];
                    if (item.Renderer == null) continue;
                    if (!lights.TryGetValue(item.Renderer, out var perObject))
                        lights[item.Renderer] = perObject = PerObjectLights(data, item.Renderer);
                    var properties = item.Properties != null ? new Dictionary<int, object>(item.Properties) : new Dictionary<int, object>();
                    foreach (var p in perObject) properties[p.Key] = p.Value;
                    item.Properties = properties;
                    s_Items[i] = item;
                }
            }
            if (s_MaterialHandles.Length < s_Items.Count) Array.Resize(ref s_MaterialHandles, Math.Max(s_Items.Count, s_MaterialHandles.Length * 2));
            ulong overrideHandle = overrideMaterial != null ? overrideMaterial.Sync() : 0;
            for (int i = 0; i < s_Items.Count; i++) s_MaterialHandles[i] = overrideMaterial != null ? overrideHandle : s_Items[i].Material.Sync();
            for (int i = 0; i < s_Items.Count; i++)
            {
                var item = s_Items[i];
                Describe(item);
                s_DrawCount++;
                GfxStream.Draw(item.Mesh, item.Submesh, s_MaterialHandles[i], pass, item.Model, item.Color, item.MainTexture,
                    item.ClipEnabled ? item.Clip : (Rect?)null, item.Properties);
            }
        }

        static readonly int s_LightDataId = Shader.PropertyToID("unity_LightData");
        static readonly int s_LightIndicesId = Shader.PropertyToID("unity_LightIndices");
        const int MaxPerObjectLights = 8;

        /// <summary>A renderer's per-object lights (unity_LightData, unity_LightIndices): the
        /// visible lights other than those the index map drops whose range reaches its bounds,
        /// brightest at the bounds' center first, at most 8.</summary>
        static Dictionary<int, object> PerObjectLights(CullingData data, Renderer renderer)
        {
            var bounds = renderer.bounds;
            var candidates = new List<(int Index, float Importance)>();
            var map = data.LightIndexMap;
            for (int i = 0; i < data.Lights.Length; i++)
            {
                int index = map != null ? (i < map.Length ? map[i] : -1) : i;
                if (index < 0) continue;
                var light = data.Lights[i];
                float importance;
                if (light.lightType == LightType.Directional) importance = float.MaxValue;
                else
                {
                    var position = (Vector3)light.localToWorldMatrix.GetColumn(3);
                    float distance = Mathf.Sqrt(bounds.SqrDistance(position));
                    if (distance > light.range) continue;
                    var c = light.finalColor;
                    float distanceToCenter = (bounds.center - position).magnitude;
                    importance = (c.r + c.g + c.b) / (1f + 25f * (distanceToCenter / Mathf.Max(light.range, 1e-4f)) * (distanceToCenter / Mathf.Max(light.range, 1e-4f)));
                }
                candidates.Add((index, importance));
            }
            candidates.Sort((a, b) => b.Importance.CompareTo(a.Importance));
            int count = Math.Min(candidates.Count, MaxPerObjectLights);
            var indices = new float[8];
            for (int k = 0; k < count; k++) indices[k] = candidates[k].Index;
            return new Dictionary<int, object>
            {
                [s_LightDataId] = new Vector4(0f, count, 1f, 0f),
                [s_LightIndicesId] = new[] { new Vector4(indices[0], indices[1], indices[2], indices[3]), new Vector4(indices[4], indices[5], indices[6], indices[7]) },
            };
        }

        /// <summary>A renderer list's order: by sorting layer and order, by queue, then back to
        /// front or (quantized) front to back as the criteria say.</summary>
        static void SortRendererList(List<DrawItem> items, SortingCriteria criteria)
        {
            if (criteria == SortingCriteria.None) return;
            bool layer = (criteria & SortingCriteria.SortingLayer) != 0;
            bool queue = (criteria & SortingCriteria.RenderQueue) != 0;
            bool backToFront = (criteria & SortingCriteria.BackToFront) != 0;
            bool frontToBack = (criteria & SortingCriteria.QuantizedFrontToBack) != 0;
            if (backToFront && layer && queue)
            {
                items.Sort((a, b) => CompareTransparent(a, b));
                return;
            }
            items.Sort((a, b) =>
            {
                int c;
                if (layer)
                {
                    c = a.SortingLayer.CompareTo(b.SortingLayer);
                    if (c != 0) return c;
                    c = a.SortingOrder.CompareTo(b.SortingOrder);
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
                else if (frontToBack)
                {
                    c = a.Distance.CompareTo(b.Distance);
                    if (c != 0) return c;
                }
                return a.Sequence.CompareTo(b.Sequence);
            });
        }

        /// <summary>
        /// Renders a camera with the built-in forward pipeline, as Unity orders it: OnPreCull, culling,
        /// OnPreRender, the clear, command buffers at each camera event around the opaque and the
        /// transparent draws, OnRenderObject, OnPostRender, then the image effects (OnRenderImage) and
        /// the last events. Used each frame and by Camera.Render.
        /// </summary>
        internal static void RenderCamera(Camera camera)
        {
            var previousCamera = Camera.current;
            var previousActive = RenderTexture.active;
            var previousProjection = Immediate.Projection;
            var previousModelView = Immediate.ModelView;
            Camera.current = camera;
            try
            {
                Camera.onPreCull?.Invoke(camera);
                SendToCamera(camera, m => m.OnPreCull);
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
                s_Items.Clear();
                int mask = camera.cullingMask;
                foreach (var renderer in RenderRegistry.Renderers)
                {
                    if (!renderer || renderer.forceRenderingOff) continue;
                    var go = renderer.m_GameObject;
                    if ((mask & (1 << go.m_Layer)) == 0) continue;
                    try
                    {
                        renderer.CollectDraws(context, s_Items);
                        renderer.SetVisible(true);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, renderer);
                    }
                }
                foreach (var source in Sources)
                {
                    try { source.CollectCameraDraws(context, s_Items); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                CollectQueued(context, s_Items);
                SortForCamera(s_Items);
                Camera.onPreRender?.Invoke(camera);
                SendToCamera(camera, m => m.OnPreRender);

                // Image effects render the scene into a temporary, then each effect into the next.
                var effects = ImageEffects(camera);
                var cameraTarget = camera.targetTexture;
                RenderTexture sceneTarget = cameraTarget;
                var viewport = context.Viewport;
                if (effects.Count > 0)
                {
                    sceneTarget = RenderTexture.GetTemporary(Math.Max(1, camera.pixelWidth), Math.Max(1, camera.pixelHeight), 24,
                        camera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);
                    viewport = new Rect(0, 0, sceneTarget.width, sceneTarget.height);
                }
                ulong target = sceneTarget != null ? TextureHandles.Of(sceneTarget) : 0;
                RenderTexture.SetActiveQuietly(sceneTarget);
                Immediate.Projection = context.Projection;
                Immediate.ModelView = context.View;
                SetupCamera(target, viewport, camera.clearFlags, camera.backgroundColor, context.View, context.Projection, 0);
                int transparent = 0;
                while (transparent < s_Items.Count && s_Items[transparent].Queue <= 2500) transparent++;
                RunEvent(camera, CameraEvent.BeforeForwardOpaque, sceneTarget);
                EmitItems(0, transparent);
                RunEvent(camera, CameraEvent.AfterForwardOpaque, sceneTarget);
                RunEvent(camera, CameraEvent.BeforeSkybox, sceneTarget);
                if (camera.clearFlags == CameraClearFlags.Skybox) DrawSkybox(camera);
                RunEvent(camera, CameraEvent.AfterSkybox, sceneTarget);
                RunEvent(camera, CameraEvent.BeforeImageEffectsOpaque, sceneTarget);
                RunEvent(camera, CameraEvent.AfterImageEffectsOpaque, sceneTarget);
                RunEvent(camera, CameraEvent.BeforeForwardAlpha, sceneTarget);
                EmitItems(transparent, s_Items.Count);
                RunEvent(camera, CameraEvent.AfterForwardAlpha, sceneTarget);
                SendToAll(m => m.OnRenderObject);
                Camera.onPostRender?.Invoke(camera);
                SendToCamera(camera, m => m.OnPostRender);
                RunEvent(camera, CameraEvent.BeforeImageEffects, sceneTarget);
                if (effects.Count > 0) RunImageEffects(effects, sceneTarget, cameraTarget, camera);
                RunEvent(camera, CameraEvent.AfterImageEffects, cameraTarget);
                RunEvent(camera, CameraEvent.AfterEverything, cameraTarget);
            }
            finally
            {
                CommandBuffer.ReleaseNamedTemporaries();
                Camera.current = previousCamera;
                RenderTexture.SetActiveQuietly(previousActive);
                Immediate.Projection = previousProjection;
                Immediate.ModelView = previousModelView;
            }
        }

        /// <summary>The camera's OnRenderImage scripts, in component order.</summary>
        static List<(MonoBehaviour Script, ScriptMessages Messages)> ImageEffects(Camera camera)
        {
            var effects = new List<(MonoBehaviour, ScriptMessages)>();
            foreach (var component in camera.m_GameObject.m_Components)
            {
                if (component is MonoBehaviour script && script.isActiveAndEnabled)
                {
                    var messages = ScriptMessages.For(script.GetType());
                    if (messages.OnRenderImage != null) effects.Add((script, messages));
                }
            }
            return effects;
        }

        static void RunImageEffects(List<(MonoBehaviour Script, ScriptMessages Messages)> effects, RenderTexture scene, RenderTexture cameraTarget, Camera camera)
        {
            var source = scene;
            for (int i = 0; i < effects.Count; i++)
            {
                bool last = i == effects.Count - 1;
                var destination = last ? cameraTarget : RenderTexture.GetTemporary(source.descriptor);
                RenderTexture.active = destination;
                try { effects[i].Messages.OnRenderImage(effects[i].Script, source, destination); }
                catch (Exception e) { Debug.LogException(e, effects[i].Script); }
                RenderTexture.ReleaseTemporary(source);
                source = destination;
            }
        }

        static void SendToCamera(Camera camera, Func<ScriptMessages, Action<MonoBehaviour>> message)
        {
            foreach (var component in camera.m_GameObject.m_Components)
            {
                if (component is not MonoBehaviour script || !script.isActiveAndEnabled) continue;
                var action = message(ScriptMessages.For(script.GetType()));
                if (action == null) continue;
                try { action(script); }
                catch (Exception e) { Debug.LogException(e, script); }
            }
        }

        /// <summary>A message every active script with it receives (OnRenderObject).</summary>
        static void SendToAll(Func<ScriptMessages, Action<MonoBehaviour>> message)
        {
            if (Lifecycle.RenderObjectBehaviours.Count == 0) return;
            var scripts = new MonoBehaviour[Lifecycle.RenderObjectBehaviours.Count];
            for (int i = 0; i < scripts.Length; i++) scripts[i] = Lifecycle.RenderObjectBehaviours[i];
            foreach (var script in scripts)
            {
                var action = message(ScriptMessages.For(script.GetType()));
                if (action == null) continue;
                try { action(script); }
                catch (Exception e) { Debug.LogException(e, script); }
            }
        }

        /// <summary>The command buffers at a camera event; the camera's target and matrices are set
        /// again after them.</summary>
        static void RunEvent(Camera camera, CameraEvent evt, RenderTexture target)
        {
            var buffers = camera.CommandBuffersAt(evt);
            if (buffers == null || buffers.Count == 0) return;
            var saved = GfxStream.Current;
            foreach (var buffer in buffers.ToArray())
            {
                var context = new CommandBuffer.Context { Camera = camera, CameraTarget = target };
                try { buffer.ExecuteIn(context); }
                catch (Exception e) { Debug.LogException(e); }
            }
            GfxStream.Restore(saved);
            RenderTexture.SetActiveQuietly(target);
        }

        /// <summary>Meshes queued with Graphics.DrawMesh that the camera draws.</summary>
        static void CollectQueued(CameraContext camera, List<DrawItem> into)
        {
            int mask = camera.Camera.cullingMask;
            foreach (var q in Immediate.Queued)
            {
                if (q.Camera != null && q.Camera != camera.Camera) continue;
                if ((mask & (1 << q.Layer)) == 0) continue;
                var center = q.Matrix.MultiplyPoint(q.Mesh.bounds.center);
                into.Add(new DrawItem
                {
                    Mesh = q.Mesh.Sync(),
                    Submesh = Math.Min(q.Submesh, Math.Max(0, q.Mesh.subMeshCount - 1)),
                    Material = q.Material,
                    Model = q.Matrix,
                    Color = Color.white,
                    Queue = q.Material.renderQueue,
                    Distance = Distance(camera, center),
                    Sequence = s_Sequence++,
                    Properties = q.Properties,
                });
            }
        }

        /// <summary>A camera's target, viewport, clear and matrices.</summary>
        static void SetupCamera(ulong target, Rect viewport, CameraClearFlags clearFlags, Color background,
            in Matrix4x4 view, in Matrix4x4 projection, uint flags)
        {
            s_CameraCount++;
            if (Trace.RenderDetailOn && Time.frameCount % 60 == 1)
                s_DrawDescriptions.Add($"  camera viewport {viewport} clear {clearFlags} {background} draws {s_Items.Count} target {target}\n    view {view.ToString().Replace("\n", " | ")}\n    proj {projection.ToString().Replace("\n", " | ")}");
            GfxStream.SetTarget(target);
            GfxStream.Viewport(viewport);
            byte clear = clearFlags switch
            {
                CameraClearFlags.Skybox or CameraClearFlags.SolidColor => (byte)(GfxStream.ClearColor | GfxStream.ClearDepth | GfxStream.ClearStencil),
                CameraClearFlags.Depth => (byte)(GfxStream.ClearDepth | GfxStream.ClearStencil),
                _ => 0,
            };
            if (clear != 0) GfxStream.Clear(clear, background);
            GfxStream.Camera(view, projection, flags);
        }

        /// <summary><see cref="s_Items"/>[from..to] as draws (sorted).</summary>
        static void EmitItems(int from, int to)
        {
            // Materials upload their changes first: an upload hands over what was recorded before it.
            if (s_MaterialHandles.Length < s_Items.Count) Array.Resize(ref s_MaterialHandles, Math.Max(s_Items.Count, s_MaterialHandles.Length * 2));
            for (int i = from; i < to; i++) s_MaterialHandles[i] = s_Items[i].Material.Sync();
            for (int i = from; i < to; i++)
            {
                var item = s_Items[i];
                Describe(item);
                s_DrawCount++;
                GfxStream.Draw(item.Mesh, item.Submesh, s_MaterialHandles[i], GfxStream.ForwardPasses, item.Model, item.Color, item.MainTexture,
                    item.ClipEnabled ? item.Clip : (Rect?)null, item.Properties);
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
            Unbound.Runtime.Trace.Render($"frame: {s_CameraCount} cameras, {s_DrawCount} draws, {Screen.width}x{Screen.height}");
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
        public static int globalTextureMipmapLimit { get; set; }
        public static AnisotropicFiltering anisotropicFiltering { get; set; } = AnisotropicFiltering.Enable;
        public static bool softParticles { get; set; }
        public static bool realtimeReflectionProbes { get; set; }
        public static bool billboardsFaceCameraPosition { get; set; }
        public static int asyncUploadTimeSlice { get; set; } = 2;
        public static int asyncUploadBufferSize { get; set; } = 16;
        public static string[] names { get; internal set; } = { "Default" };
        public static int count => names.Length;
        static int s_Level;

        public static int GetQualityLevel() => s_Level;

        public static void SetQualityLevel(int index) => s_Level = Mathf.Clamp(index, 0, names.Length - 1);

        public static void SetQualityLevel(int index, [Internal.DefaultValue("true")] bool applyExpensiveChanges) => SetQualityLevel(index);

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

    public partial class ReflectionProbe : Behaviour
    {
        /// <summary>What objects outside every probe reflect: the scene's default reflection.</summary>
        public static Texture defaultTexture => RenderSettings.DefaultReflectionTexture;

        /// <summary>How shaders decode it: an RGBM reflection's range (5, raised to 2.2 in linear
        /// color space), else as stored.</summary>
        public static Vector4 defaultTextureHDRDecodeValues
        {
            get
            {
                var texture = defaultTexture;
                if (texture is Cubemap cube && cube.m_Rgbm)
                    return QualitySettings.activeColorSpace == ColorSpace.Linear ? new Vector4(Mathf.Pow(5f, 2.2f), 2.2f, 0f, 1f) : new Vector4(5f, 1f, 0f, 1f);
                return new Vector4(1f, 1f, 0f, 0f);
            }
        }
    }
}
