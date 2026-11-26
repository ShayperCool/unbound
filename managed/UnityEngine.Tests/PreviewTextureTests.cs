using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    // Expected values: tests/projects/editorpreviewtexture (Unity 6000.0.71f1 Game view OnGUI).
    [Collection("Serial")]
    public sealed class PreviewTextureTests : IDisposable
    {
        readonly IDisposable m_Release = TestRelease.Pin(TestRelease.Measured);

        public void Dispose() => m_Release.Dispose();

        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Logs = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Logs.Add(logType + ": " + string.Format(format, args));
            public void LogException(Exception exception, Object context) => Logs.Add("Exception: " + exception.Message);
        }

        static Material UserMaterial()
        {
            var shader = Shader.Create("Preview test/user", 3000);
            shader.m_Properties.Clear();
            shader.m_Properties.Add(new ShaderPropertyInfo { Name = "_MainTex", Type = ShaderPropertyType.Texture });
            shader.m_Properties.Add(new ShaderPropertyInfo { Name = "_ColorMask", Type = ShaderPropertyType.Color });
            shader.m_Properties.Add(new ShaderPropertyInfo { Name = "_Mip", Type = ShaderPropertyType.Float });
            shader.m_Properties.Add(new ShaderPropertyInfo { Name = "_Exposure", Type = ShaderPropertyType.Float });
            return new Material(shader) { name = "user" };
        }

        static void Repaint(Action draw)
        {
            var previous = Event.current;
            Event.current = new Event { type = EventType.Repaint };
            GUIUtility.BeginGUI(0, 41, 0);
            bool painting = GuiRender.Painting;
            GuiRender.Painting = true;
            GuiDispatch.LoadRepaintMatrices();
            try { draw(); }
            finally
            {
                GuiRender.Painting = painting;
                GUIUtility.EndGUI(0);
                Event.current = previous;
            }
        }

        static List<Dictionary<int, object>> MaterialDraws() =>
            (List<Dictionary<int, object>>)typeof(GuiRender).GetField("s_MaterialProperties", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        [Fact]
        public void ACallersMaterialGetsTheMaskMipExposureAndFormatAndDrawsInTheGuiPass()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var material = UserMaterial();
            try
            {
                GuiRender.Clear();
                int quads = GuiRender.QuadCount;
                // material: _ColorMask (0, 1, 0, 0), _Mip 3, _Exposure 2, _SNormFormat 0.
                Repaint(() => EditorGUI.DrawPreviewTexture(new Rect(0, 64, 32, 32), texture, material, ScaleMode.StretchToFill, 0, 3, ColorWriteMask.Green, 2));
                Assert.Equal(new Color(0, 1, 0, 0), material.GetColor("_ColorMask"));
                Assert.Equal(3f, material.GetFloat("_Mip"));
                Assert.Equal(2f, material.GetFloat("_Exposure"));
                Assert.Equal(0, material.GetInt("_SNormFormat"));
                // Drawn among the GUI's draws (an immediate draw had no target in the GUI pass).
                Assert.Equal(quads + 1, GuiRender.QuadCount);
                // A second draw with other values: each keeps the values of its call.
                Repaint(() => EditorGUI.DrawPreviewTexture(new Rect(32, 64, 32, 32), texture, material, ScaleMode.StretchToFill, 0, 1));
                var draws = MaterialDraws();
                int mip = Shader.PropertyToID("_Mip");
                Assert.Equal(3f, draws[0][mip]);
                Assert.Equal(1f, draws[1][mip]);
            }
            finally
            {
                GuiRender.Clear();
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(material);
            }
        }

        static List<FrameBuilder.DrawItem> Items()
        {
            var items = new List<FrameBuilder.DrawItem>();
            GuiRender.CollectItems(0, 180, items);
            return items;
        }

        [Fact]
        public void APreviewSlotTakenByAnotherMaterialKeepsNoneOfTheFirstOnesValues()
        {
            var texture = new Texture2D(4, 4);
            var first = UserMaterial();
            var second = UserMaterial();
            int cutoff = Shader.PropertyToID("_Cutoff");
            try
            {
                first.SetFloat(cutoff, 0.9f);
                GuiRender.Clear();
                Repaint(() => EditorGUI.DrawPreviewTexture(new Rect(0, 0, 32, 32), texture, first));
                Assert.Equal(0.9f, MaterialDraws()[0][cutoff]);
                // The next frame draws another material first: the slot starts afresh.
                GuiRender.Clear();
                Repaint(() => EditorGUI.DrawPreviewTexture(new Rect(0, 0, 32, 32), texture, second));
                Assert.False(MaterialDraws()[0].ContainsKey(cutoff));
                // A caller's material draws unclipped; a destroyed one is not drawn (nor throws).
                Assert.False(Assert.Single(Items()).ClipEnabled);
                Object.DestroyImmediate(second);
                Assert.Empty(Items());
            }
            finally
            {
                GuiRender.Clear();
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(first);
                if (second) Object.DestroyImmediate(second);
            }
        }

        [Fact]
        public void APreviewIntoARenderTextureOrUnderOtherMatricesIsNotRecordedInTheGuiPass()
        {
            var texture = new Texture2D(4, 4);
            var material = UserMaterial();
            var target = new RenderTexture(16, 16, 0);
            try
            {
                GuiRender.Clear();
                int quads = GuiRender.QuadCount;
                Repaint(() =>
                {
                    RenderTexture.active = target;
                    try { EditorGUI.DrawPreviewTexture(new Rect(0, 0, 8, 8), texture, material); }
                    finally { RenderTexture.active = null; }
                    GL.PushMatrix();
                    GL.LoadOrtho();
                    try { EditorGUI.DrawPreviewTexture(new Rect(0, 0, 1, 1), texture, material); }
                    finally { GL.PopMatrix(); }
                });
                Assert.Equal(quads, GuiRender.QuadCount);
                // The pass's own target and matrices: recorded.
                Repaint(() => EditorGUI.DrawPreviewTexture(new Rect(0, 0, 8, 8), texture, material));
                Assert.Equal(quads + 1, GuiRender.QuadCount);
            }
            finally
            {
                GuiRender.Clear();
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(target);
            }
        }

        [Fact]
        public void OutsideOnGuiThePreviewThrowsAsTheEditors()
        {
            var texture = new Texture2D(4, 4);
            var previous = Event.current;
            Event.current = null;
            try
            {
                // outside: the Editor's NullReferenceException.
                var e = Assert.Throws<NullReferenceException>(() => EditorGUI.DrawPreviewTexture(new Rect(0, 0, 32, 32), texture));
                Assert.Equal("Object reference not set to an instance of an object", e.Message);
            }
            finally
            {
                Event.current = previous;
                Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void ATransparentPreviewWithoutImageOrAspectLogsAnError()
        {
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                // missing, drawn/logs.
                Repaint(() => EditorGUI.DrawTextureTransparent(new Rect(0, 0, 32, 32), null));
                // (A first GUI pass of the process also reports the host's missing skin and font.)
                Assert.Single(capture.Logs, l => l.Contains("imageAspect"));
                Assert.Contains("Error: Please specify an image or a imageAspect", capture.Logs);
            }
            finally { Debug.unityLogger.logHandler = previous; }
        }

        [Fact]
        public void AnUnmeasuredFamilySaysThePreviewsAreNotImplemented()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var texture = new Texture2D(4, 4);
            try
            {
                int before = Stubs.HitReport().FirstOrDefault(h => h.Key == "UnityEditor.EditorGUI.DrawPreviewTexture").Value;
                Repaint(() => EditorGUI.DrawPreviewTexture(new Rect(0, 0, 32, 32), texture));
                Assert.Equal(before + 1, Stubs.HitReport().FirstOrDefault(h => h.Key == "UnityEditor.EditorGUI.DrawPreviewTexture").Value);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Fact]
        public void TexturesTellTheirKindForTheirPreviewMaterial()
        {
            var script = new Texture2D(4, 4);
            var alpha = new Texture2D(4, 1, TextureFormat.Alpha8, false);
            try
            {
                Assert.Equal(TextureUsageMode.Default, TextureUtil.GetUsageMode(script));
                Assert.Equal(TextureFormat.Alpha8, TextureUtil.GetTextureFormat(alpha));
                Assert.True(GraphicsFormatUtility.IsAlphaOnlyFormat(TextureFormat.Alpha8));
                Assert.False(GraphicsFormatUtility.IsAlphaOnlyFormat(TextureFormat.RGBA32));
                Assert.True(TextureUtil.IsNormalMapUsageMode(TextureUsageMode.NormalmapDXT5nm));
                Assert.False(TextureUtil.IsNormalMapUsageMode(TextureUsageMode.BakedLightmapRGBM));
                Assert.Throws<ArgumentNullException>(() => TextureUtil.GetUsageMode(null));
            }
            finally
            {
                Object.DestroyImmediate(script);
                Object.DestroyImmediate(alpha);
            }
        }
    }
}
