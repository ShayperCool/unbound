using System;

namespace UnityEngine.Rendering
{
    /// <summary>Rendering the screen every Nth frame (measured: tests/projects/coreapis). The player
    /// loop still runs every frame. Whether a frame draws on the screen is decided with the
    /// interval in force when the frame begins (<see cref="RendersThisFrame"/>), which is not always
    /// <see cref="willCurrentFrameRender"/>: that one reads the interval as it is now.</summary>
    public partial class OnDemandRendering
    {
        static int m_RenderFrameInterval = 1;

        /// <summary>The interval when the frame began: a change in Update, or at the end of the
        /// frame before, applies from the next frame (measured).</summary>
        internal static int s_FrameInterval = 1;
        static bool s_Registered;

        /// <summary>Latches the interval at the frame's start, after its time update.</summary>
        internal static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            Unbound.Runtime.PlayerLoopDriver.Register(
                typeof(UnityEngine.PlayerLoop.TimeUpdate.WaitForLastPresentationAndUpdateTime), LatchFrameInterval);
        }

        internal static void LatchFrameInterval() => s_FrameInterval = m_RenderFrameInterval;

        /// <summary>Whether this frame draws on the screen: its screen cameras, overlay canvases and
        /// OnGUI, its presentation and its coroutines' WaitForEndOfFrame. Cameras that render into
        /// textures render on every frame (measured).</summary>
        internal static bool RendersThisFrame => Time.frameCount % s_FrameInterval == 0;

        public static bool willCurrentFrameRender => Time.frameCount % renderFrameInterval == 0;

        public static int renderFrameInterval
        {
            get => m_RenderFrameInterval;
            set => m_RenderFrameInterval = Math.Max(1, value);
        }

        internal static void GetRenderFrameInterval(out int frameInterval) => frameInterval = renderFrameInterval;

        /// <summary>In the Editor: the target frame rate over the interval, -1 without a target
        /// (targetFrameRate 0 or less); vSyncCount does not enter it (measured).</summary>
        internal static float GetEffectiveRenderFrameRate()
        {
            int target = Application.targetFrameRate;
            return target <= 0 ? -1f : (float)target / renderFrameInterval;
        }

        public static int effectiveRenderFrameRate
        {
            get
            {
                float frameRate = GetEffectiveRenderFrameRate();
                if (frameRate <= 0.0) return (int)frameRate;
                return (int)(frameRate + 0.5f);
            }
        }
    }
}
