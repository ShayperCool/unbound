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
        static readonly List<CameraPacket> s_Cameras = new List<CameraPacket>();
        static readonly List<DrawPacket> s_Draws = new List<DrawPacket>();
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
            s_DrawDescriptions.Add($"  draw {s_DrawDescriptions.Count}: mesh {item.Mesh}/{item.Submesh} material '{item.Material?.name}' shader '{item.Material?.shader?.name}' queue {item.Queue} tex {item.MainTexture} color {item.Color} pos {item.Model.GetColumn(3)} scale {item.Model.lossyScale} bounds {MeshBounds(item.Mesh)}");
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
                });
            }
        }

        internal static float Distance(CameraContext camera, Vector3 point) =>
            camera.Orthographic ? Vector3.Dot(point - camera.Position, camera.Forward) : (point - camera.Position).sqrMagnitude;

        static void Render()
        {
            Camera.current = null;
            s_Cameras.Clear();
            s_Draws.Clear();
            s_DrawDescriptions.Clear();
            var cameras = Camera.CollectActive();
            cameras.Sort((a, b) =>
            {
                int rt = (b.targetTexture != null).CompareTo(a.targetTexture != null);
                return rt != 0 ? rt : a.depth.CompareTo(b.depth);
            });
            foreach (var camera in cameras)
            {
                Camera.current = camera;
                Camera.onPreCull?.Invoke(camera);
                Camera.onPreRender?.Invoke(camera);
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
                SortForCamera(s_Items);
                int first = s_Draws.Count;
                foreach (var item in s_Items) { Describe(item); s_Draws.Add(ToPacket(item)); }
                s_Cameras.Add(new CameraPacket
                {
                    View = context.View,
                    Projection = context.Projection,
                    Viewport = context.Viewport,
                    ClearFlags = (int)camera.clearFlags,
                    ClearColor = camera.backgroundColor,
                    FirstDraw = first,
                    DrawCount = s_Draws.Count - first,
                    TargetTexture = camera.targetTexture != null ? TextureHandles.Of(camera.targetTexture) : 0,
                });
                Camera.onPostRender?.Invoke(camera);
            }
            Camera.current = null;

            // Overlay canvases: a pixel-space camera over the whole screen, drawn last.
            s_Items.Clear();
            foreach (var source in Sources)
            {
                try { source.CollectOverlayDraws(s_Items); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (s_Items.Count > 0 || s_Cameras.Count == 0)
            {
                int first = s_Draws.Count;
                foreach (var item in s_Items) { Describe(item); s_Draws.Add(ToPacket(item)); }
                s_Cameras.Add(new CameraPacket
                {
                    View = Matrix4x4.identity,
                    Projection = Matrix4x4.Ortho(0, Screen.width, 0, Screen.height, -100000f, 100000f),
                    Viewport = new Rect(0, 0, Screen.width, Screen.height),
                    // With no camera at all Unity shows black ("No cameras rendering").
                    ClearFlags = s_Cameras.Count == 0 ? (int)CameraClearFlags.SolidColor : (int)CameraClearFlags.Nothing,
                    ClearColor = Color.black,
                    FirstDraw = first,
                    DrawCount = s_Draws.Count - first,
                });
            }
            Submit();
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

        static DrawPacket ToPacket(DrawItem item) => new DrawPacket
        {
            Mesh = item.Mesh,
            Submesh = item.Submesh,
            Material = item.Material.Sync(),
            Model = item.Model,
            Color = item.Color,
            MainTexture = item.MainTexture,
            Clip = item.Clip,
            ClipEnabled = item.ClipEnabled ? 1 : 0,
        };

        static unsafe void Submit()
        {
            var cameras = s_Cameras.ToArray();
            var draws = s_Draws.ToArray();
            if (Trace.RenderOn && Time.frameCount % 60 == 1)
            {
                Trace.Render($"frame: {cameras.Length} cameras, {draws.Length} draws, {Screen.width}x{Screen.height}");
                if (Trace.RenderDetailOn)
                {
                    foreach (var cam in cameras)
                        Trace.Render($"  camera viewport {cam.Viewport} clear {cam.ClearFlags} {cam.ClearColor} draws {cam.FirstDraw}+{cam.DrawCount} target {cam.TargetTexture}\n    view {cam.View.ToString().Replace("\n", " | ")}\n    proj {cam.Projection.ToString().Replace("\n", " | ")}");
                    foreach (var line in s_DrawDescriptions) Trace.Render(line);
                }
            }
            // Global shader properties and keywords: the lighting environment's built-in variables
            // first (the renderer sets them over anything a script set), then Shader.SetGlobal*.
            var names = new List<byte[]>();
            var globals = new List<MaterialPropertyPacket>();
            var lighting = new List<(string Name, Vector4 Value)>();
            var keywordList = new List<string>(Shader.s_GlobalKeywords);
            LightingGlobals.Collect(lighting, keywordList);
            foreach (var (name, value) in lighting)
            {
                var n = Encoding.UTF8.GetBytes(name);
                names.Add(n);
                globals.Add(new MaterialPropertyPacket { Kind = 1, Value = value, NameLength = n.Length });
            }
            foreach (var g in Shader.s_Globals)
            {
                var packet = new MaterialPropertyPacket();
                switch (g.Value)
                {
                    case float f: packet.Kind = 0; packet.Value = new Vector4(f, 0, 0, 0); break;
                    case int i: packet.Kind = 3; packet.Value = new Vector4(i, 0, 0, 0); break;
                    case Vector4 v: packet.Kind = 1; packet.Value = v; break;
                    case Texture t: packet.Kind = 2; packet.Texture = TextureHandles.Of(t); packet.TextureST = new Vector4(1, 1, 0, 0); break;
                    default: continue;
                }
                var n = Encoding.UTF8.GetBytes(Shader.IdToName(g.Key) ?? "");
                packet.NameLength = n.Length;
                names.Add(n);
                globals.Add(packet);
            }
            var keywords = Encoding.UTF8.GetBytes(string.Join(" ", keywordList));
            var pinned = new GCHandle[names.Count];
            try
            {
                var globalArray = globals.ToArray();
                for (int i = 0; i < names.Count; i++)
                {
                    pinned[i] = GCHandle.Alloc(names[i], GCHandleType.Pinned);
                    globalArray[i].Name = (byte*)pinned[i].AddrOfPinnedObject();
                }
                fixed (CameraPacket* c = cameras)
                fixed (DrawPacket* d = draws)
                fixed (MaterialPropertyPacket* g = globalArray)
                fixed (byte* k = keywords)
                {
                    var frame = new FramePacket
                    {
                        ScreenWidth = Screen.width,
                        ScreenHeight = Screen.height,
                        ColorSpace = (int)QualitySettings.activeColorSpace,
                        CameraCount = cameras.Length,
                        Cameras = c,
                        DrawCount = draws.Length,
                        Draws = d,
                        GlobalCount = globalArray.Length,
                        Globals = g,
                        Keywords = k,
                        KeywordsLength = keywords.Length,
                    };
                    Native.SubmitFrame(&frame);
                }
            }
            finally
            {
                foreach (var h in pinned) if (h.IsAllocated) h.Free();
            }
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

        public static void IncreaseLevel() => SetQualityLevel(s_Level + 1);
        public static void IncreaseLevel([Internal.DefaultValue("false")] bool applyExpensiveChanges) => IncreaseLevel();
        public static void DecreaseLevel() => SetQualityLevel(s_Level - 1);
        public static void DecreaseLevel([Internal.DefaultValue("false")] bool applyExpensiveChanges) => DecreaseLevel();
    }

    public partial class ReflectionProbe : Behaviour
    {
    }
}
