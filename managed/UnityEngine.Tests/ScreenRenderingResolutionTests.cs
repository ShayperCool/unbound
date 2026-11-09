using System;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>PlayModeWindow.SetCustomRenderingResolution: the rest of the frame's update sees
    /// the old size, the frame's rendering and later frames the new one, whatever the window's size;
    /// mouse positions are scaled from the window to it (tests/projects/screenread).</summary>
    [Collection("Frames")]
    public class ScreenRenderingResolutionTests : IDisposable
    {
        readonly int m_Width = Screen.width, m_Height = Screen.height;

        public ScreenRenderingResolutionTests() => Screen.ResetRenderingResolutionForTests();

        public void Dispose()
        {
            Screen.ResetRenderingResolutionForTests();
            Screen.Update(m_Width, m_Height);
            Screen.ConsumeSizeChanged();
        }

        [Fact]
        public void NewResolutionAppliesWhenTheFrameRenders()
        {
            Screen.Update(64, 32);
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(80, 40, "test");
            Assert.Equal(new Vector2Int(64, 32), new Vector2Int(Screen.width, Screen.height));
            UnityEditor.PlayModeWindow.GetRenderingResolution(out uint w, out uint h);
            Assert.Equal((80u, 40u), (w, h));
            Screen.BeginRendering();
            Assert.Equal(new Vector2Int(80, 40), new Vector2Int(Screen.width, Screen.height));
            // The window keeps its size; the Game view keeps the resolution.
            Screen.Update(64, 32);
            Assert.Equal(new Vector2Int(80, 40), new Vector2Int(Screen.width, Screen.height));
            float x = 32f, y = 16f;
            Screen.WindowToRendering(ref x, ref y);
            Assert.Equal((40f, 20f), (x, y));
        }

        [Fact]
        public void WithoutAResolutionTheScreenFollowsTheWindow()
        {
            Screen.Update(100, 50);
            Screen.BeginRendering();
            Assert.Equal(new Vector2Int(100, 50), new Vector2Int(Screen.width, Screen.height));
            float x = 7f, y = 9f;
            Screen.WindowToRendering(ref x, ref y);
            Assert.Equal((7f, 9f), (x, y));
        }

        /// <summary>Input readers see the rendering size and the mouse in its pixels; a cursor
        /// warp goes back to window pixels.</summary>
        [Fact]
        public unsafe void InputWorksInRenderingPixels()
        {
            Screen.Update(128, 64);
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(64, 32, "test");
            var input = new Unbound.Runtime.FrameInput { ScreenWidth = 128, ScreenHeight = 64, MouseX = 100, MouseY = 10 };
            Screen.BeginFrame(&input);
            Assert.Equal((64, 32), (input.ScreenWidth, input.ScreenHeight));
            Assert.Equal((50f, 5f), (input.MouseX, input.MouseY));
            Assert.Equal(new Vector2(100, 60), Screen.RenderingToWindow(new Vector2(50, 30)));
        }
    }
}
