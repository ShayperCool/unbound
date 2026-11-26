using System;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>A command buffer executed outside a camera's rendering starts from GL's matrices
    /// (identity unless GL loaded others; not a camera's that rendered before, even one left
    /// current), and the stream's matrices come back after it (tests/projects/nativegfx
    /// metal.jsonl outside.*; tests/projects/framestate outside.*, matrices.*). Camera.Render
    /// gives GL's matrices back, the frame's own camera rendering leaves its last camera's, and
    /// the frame's cleanup sets identity for the next frame.</summary>
    public class OutsideCameraCommandsTests
    {
        static byte[] CameraOf(Matrix4x4 view, Matrix4x4 projection)
        {
            var bytes = new System.Collections.Generic.List<byte> { 6 };
            foreach (var m in new[] { view, projection })
                for (int i = 0; i < 16; i++) bytes.AddRange(BitConverter.GetBytes(m[i]));
            return bytes.ToArray();
        }

        static int Occurrences(byte[] bytes, byte[] part)
        {
            int n = 0;
            for (int at = 0; at + part.Length <= bytes.Length; at++)
                if (bytes.AsSpan(at, part.Length).SequenceEqual(part)) n++;
            return n;
        }

        static byte[] Execute()
        {
            GfxStream.Flush();
            using (var cmd = new CommandBuffer())
                Graphics.ExecuteCommandBuffer(cmd);
            var recorded = GfxStream.Recorded.ToArray();
            GfxStream.Flush();
            return recorded;
        }

        /// <summary>The frame's cleanup leaves no render texture active for the next frame
        /// (tests/projects/nativegfx metal.jsonl active.nextframe; tests/projects/framestate
        /// active.*).</summary>
        [Fact]
        public void TheNextFrameStartsWithNoActiveRenderTexture()
        {
            FrameBuilder.EnsureRegistered();
            var previous = RenderTexture.ActiveTarget;
            var (projection, modelView) = (Immediate.Projection, Immediate.ModelView);
            var texture = new RenderTexture(4, 4, 0);
            try
            {
                RenderTexture.active = texture;
                Assert.Same(texture, RenderTexture.active);
                CleanupFrame();
                Assert.Null(RenderTexture.active);
            }
            finally
            {
                (Immediate.Projection, Immediate.ModelView) = (projection, modelView);
                RenderTexture.SetActiveQuietly(previous);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        static void CleanupFrame() =>
            typeof(PlayerLoopDriver).GetMethod("CleanupFrame",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);

        /// <summary>tests/projects/framestate matrices.aftercamera (windowed Editors: Camera.Render
        /// gives the matrices before it back), matrices.endofframe (the frame's last camera's) and
        /// matrices.early (the next frame starts from the windowed Editor's last window drawing:
        /// the screen's pixel matrix).</summary>
        [Fact]
        public void CameraRenderingAndTheFrameEndSetGlMatrices()
        {
            FrameBuilder.EnsureRegistered();
            var go = new GameObject("framestate camera");
            go.transform.position = new Vector3(1f, 2f, -5f);
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            var target = new RenderTexture(8, 8, 16);
            camera.targetTexture = target;
            camera.cullingMask = 0;
            var (projection, modelView) = (Immediate.Projection, Immediate.ModelView);
            try
            {
                GL.LoadOrtho();
                camera.Render();
                Assert.Equal(Immediate.Ortho, Immediate.Projection);
                Assert.Equal(Matrix4x4.identity, Immediate.ModelView);
                FrameBuilder.RenderCamera(camera, automatic: true);
                Assert.Equal(camera.projectionMatrix, Immediate.Projection);
                Assert.Equal(camera.worldToCameraMatrix, Immediate.ModelView);
                CleanupFrame();
                Assert.Equal(Matrix4x4.Ortho(0f, Screen.width, Screen.height, 0f, -1f, 100f), Immediate.Projection);
                Assert.Equal(Matrix4x4.identity, GL.modelview);
            }
            finally
            {
                (Immediate.Projection, Immediate.ModelView) = (projection, modelView);
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        sealed class RepaintMatrices : MonoBehaviour
        {
            internal Matrix4x4? Projection, ModelView;
            void OnGUI()
            {
                if (Event.current.type != EventType.Repaint) return;
                Projection = Immediate.Projection;
                ModelView = GL.modelview;
            }
        }

        /// <summary>OnGUI's Repaint draws with the screen's pixel matrix from its top left, whatever
        /// the last camera left, and the matrix stays for the end-of-frame callbacks
        /// (tests/projects/framestate matrices.ongui, matrices.endofframe.gui).</summary>
        [Fact]
        public void OnGuiRepaintLoadsTheScreensPixelMatrix()
        {
            var go = new GameObject("framestate gui");
            var gui = go.AddComponent<RepaintMatrices>();
            var (projection, modelView) = (Immediate.Projection, Immediate.ModelView);
            try
            {
                Immediate.Projection = Matrix4x4.Perspective(40f, 2f, 0.3f, 1000f);
                Immediate.ModelView = Matrix4x4.TRS(new Vector3(0f, 0f, -2f), Quaternion.identity, Vector3.one);
                typeof(GuiDispatch).GetMethod("RunFrame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    .Invoke(null, null);
                var pixels = Matrix4x4.Ortho(0f, Screen.width, Screen.height, 0f, -1f, 100f);
                Assert.Equal(pixels, gui.Projection);
                Assert.Equal(Matrix4x4.identity, gui.ModelView);
                Assert.Equal(pixels, Immediate.Projection);
                Assert.Equal(Matrix4x4.identity, Immediate.ModelView);
            }
            finally
            {
                (Immediate.Projection, Immediate.ModelView) = (projection, modelView);
                GuiRender.Clear();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void ExecutedCommandsStartFromGlMatrices()
        {
            var saved = GfxStream.Current;
            var previous = Camera.current;
            var view = Matrix4x4.TRS(new Vector3(0, 0, -5), Quaternion.identity, Vector3.one);
            var projection = Matrix4x4.Perspective(60f, 1f, 0.3f, 100f);
            var go = new GameObject("left current");
            var (glProjection, glModelView) = (Immediate.Projection, Immediate.ModelView);
            try
            {
                // GL's matrices as a batchmode Editor's frame starts.
                (Immediate.Projection, Immediate.ModelView) = (Matrix4x4.identity, Matrix4x4.identity);
                GfxStream.Camera(view, projection);
                // A camera left current (as a pipeline leaves it) is not rendering.
                Camera.Internal_SetCurrent(go.AddComponent<Camera>());
                var recorded = Execute();
                Assert.Equal(1, Occurrences(recorded, CameraOf(Matrix4x4.identity, Matrix4x4.identity)));
                Assert.Equal(1, Occurrences(recorded, CameraOf(view, projection)));
                Assert.Equal(view, GfxStream.Current.View);
                Assert.Equal(projection, GfxStream.Current.Projection);
                GL.PushMatrix();
                GL.LoadOrtho();
                try
                {
                    recorded = Execute();
                    Assert.Equal(1, Occurrences(recorded, CameraOf(Matrix4x4.identity, Immediate.Ortho)));
                }
                finally { GL.PopMatrix(); }
                // Within a camera's rendering the commands keep the camera's matrices.
                FrameBuilder.BeginCameraRendering();
                try { recorded = Execute(); }
                finally { FrameBuilder.EndCameraRendering(); }
                Assert.Equal(0, Occurrences(recorded, CameraOf(Matrix4x4.identity, Matrix4x4.identity)));
                // As a frame starts in Unbound: the screen's pixel matrix.
                Immediate.LoadScreenPixels();
                recorded = Execute();
                Assert.Equal(1, Occurrences(recorded, CameraOf(Matrix4x4.identity, Matrix4x4.Ortho(0f, Screen.width, Screen.height, 0f, -1f, 100f))));
                Assert.Equal(view, GfxStream.Current.View);
            }
            finally
            {
                (Immediate.Projection, Immediate.ModelView) = (glProjection, glModelView);
                Camera.Internal_SetCurrent(previous);
                GfxStream.Restore(saved);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static readonly System.Collections.Generic.List<string> s_Passes = new System.Collections.Generic.List<string>();

        sealed class OrderedGui : MonoBehaviour
        {
            internal string Tag;
            internal int Depth;
            internal MonoBehaviour DisableInLayout;
            void OnGUI()
            {
                GUI.depth = Depth;
                s_Passes.Add((Event.current.type == EventType.Layout ? "L " : "R ") + Tag);
                if (Event.current.type == EventType.Layout && DisableInLayout != null) DisableInLayout.enabled = false;
            }
        }

        static readonly System.Collections.Generic.List<string> s_Entries = new System.Collections.Generic.List<string>();

        sealed class DepthGui : MonoBehaviour
        {
            internal string Tag;
            internal int? Set;
            void OnGUI()
            {
                s_Entries.Add(Tag + ":" + GUI.depth);
                if (Set.HasValue) GUI.depth = Set.Value;
                s_Passes.Add((Event.current.type == EventType.Layout ? "L " : "R ") + Tag);
            }
        }

        /// <summary>Every OnGUI call starts at GUI.depth 1 and 1 is left after it, whatever was set
        /// before; the Layouts keep the behaviours' order whatever their depths, and the depth a
        /// behaviour's Layout leaves orders the Repaints, the higher first (tests/projects/guidepth).</summary>
        [Fact]
        public void OnGuiCallsStartAtDepthOneAndTheLowerDepthRepaintsLast()
        {
            var go = new GameObject("gui depth");
            try
            {
                var x = go.AddComponent<DepthGui>();
                var y = go.AddComponent<DepthGui>();
                (x.Tag, y.Tag, y.Set) = ("x", "y", -3);
                GUI.depth = 7;
                RunGuiFrame();
                Assert.Equal(new[] { "L x", "L y", "R x", "R y" }, s_Passes);
                Assert.All(s_Entries, entry => Assert.EndsWith(":1", entry));
                Assert.Equal(GUI.DefaultDepth, GUI.depth);
                // Without the set the depths tie: the Repaints reverse the Layouts.
                y.Set = null;
                s_Passes.Clear();
                RunGuiFrame();
                Assert.Equal(new[] { "L x", "L y", "R y", "R x" }, s_Passes);
            }
            finally
            {
                GUI.depth = GUI.DefaultDepth;
                s_Passes.Clear();
                s_Entries.Clear();
                GuiRender.Clear();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        sealed class InputGui : MonoBehaviour
        {
            internal string Tag;
            internal int? Set;
            internal bool UseMouseDown;
            void OnGUI()
            {
                if (Set.HasValue) GUI.depth = Set.Value;
                var e = Event.current;
                s_Passes.Add(Tag + ":" + (e.type == EventType.Layout ? "L" : e.type == EventType.MouseDown ? "D" : e.type == EventType.MouseUp ? "Up" : e.type == EventType.Used ? "U" : e.type.ToString()));
                if (UseMouseDown && e.type == EventType.MouseDown) e.Use();
            }
        }

        static void SendInput(bool mouseUp = false)
        {
            GuiEvents.Clear();
            GuiEvents.Queue(new Event { type = EventType.MouseDown, button = 0 });
            if (mouseUp) GuiEvents.Queue(new Event { type = EventType.MouseUp, button = 0 });
            GuiDispatch.SendQueuedEvents();
            GuiEvents.Clear();
        }

        /// <summary>An input event runs every behaviour's Layout first, in their order, then reaches
        /// them from the lowest depth up (equal depths in their order) until one uses it: the rest
        /// get no call (tests/projects/guiinput).</summary>
        [Fact]
        public void InputEventsRunTheLayoutsThenReachTheLowestDepthFirstUntilUsed()
        {
            var go = new GameObject("gui input");
            try
            {
                var x = go.AddComponent<InputGui>();
                var y = go.AddComponent<InputGui>();
                (x.Tag, y.Tag) = ("x", "y");
                SendInput();
                Assert.Equal(new[] { "x:L", "y:L", "x:D", "y:D" }, s_Passes);
                y.Set = -3;
                s_Passes.Clear();
                SendInput();
                Assert.Equal(new[] { "x:L", "y:L", "y:D", "x:D" }, s_Passes);
                y.UseMouseDown = true;
                s_Passes.Clear();
                SendInput(mouseUp: true);
                // The used MouseDown reaches no one after y; the next event reaches both again.
                Assert.Equal(new[] { "x:L", "y:L", "y:D", "x:L", "y:L", "y:Up", "x:Up" }, s_Passes);
            }
            finally
            {
                GUI.depth = GUI.DefaultDepth;
                s_Passes.Clear();
                GuiEvents.Clear();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static void RunGuiFrame() =>
            typeof(GuiDispatch).GetMethod("RunFrame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);

        /// <summary>A Repaint runs every behaviour's Layout, then the Repaints in the reverse order,
        /// the higher depth first (measured: tests/projects/imguicontrolids order,
        /// tests/projects/guidepth). Not measured, the engine's own choice kept from before: a
        /// behaviour another disables during its Layout still getting its own Layout and Repaint
        /// as a pair.</summary>
        [Fact]
        public void OnGuiRepaintRunsTheLayoutsThenTheRepaintsInReverse()
        {
            var go = new GameObject("gui order");
            try
            {
                var a = go.AddComponent<OrderedGui>();
                var b = go.AddComponent<OrderedGui>();
                var c = go.AddComponent<OrderedGui>();
                (a.Tag, b.Tag, c.Tag, c.Depth) = ("a", "b", "c", 1);
                RunGuiFrame();
                s_Passes.Clear();
                RunGuiFrame();
                Assert.Equal(new[] { "L a", "L b", "L c", "R c", "R b", "R a" }, s_Passes);

                c.enabled = false;
                a.DisableInLayout = b;
                s_Passes.Clear();
                RunGuiFrame();
                Assert.Equal(new[] { "L a", "L b", "R b", "R a" }, s_Passes);
            }
            finally
            {
                GUI.depth = GUI.DefaultDepth;
                s_Passes.Clear();
                GuiRender.Clear();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
