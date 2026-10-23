using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
            int submeshes = Math.Max(1, mesh.subMeshCount);
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
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
                foreach (var item in s_Items) s_Draws.Add(ToPacket(item));
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
                foreach (var item in s_Items) s_Draws.Add(ToPacket(item));
                s_Cameras.Add(new CameraPacket
                {
                    View = Matrix4x4.identity,
                    Projection = Matrix4x4.Ortho(0, Screen.width, 0, Screen.height, -1000f, 1000f),
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
        static void SortForCamera(List<DrawItem> items)
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
                c = a.SortingLayer.CompareTo(b.SortingLayer);
                if (c != 0) return c;
                c = a.SortingOrder.CompareTo(b.SortingOrder);
                if (c != 0) return c;
                c = a.Queue.CompareTo(b.Queue);
                if (c != 0) return c;
                c = b.Distance.CompareTo(a.Distance);
                return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
            });
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
            fixed (CameraPacket* c = cameras)
            fixed (DrawPacket* d = draws)
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
                };
                Native.SubmitFrame(&frame);
            }
        }
    }
}

namespace UnityEngine
{
    public sealed partial class QualitySettings
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
