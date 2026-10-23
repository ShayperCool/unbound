using System;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Developer tracing of what the engine does, for finding why a game stalls: set
    /// <c>UNBOUND_TRACE</c> to a comma-separated list of channels (<c>lifecycle</c>, <c>scenes</c>,
    /// <c>audio</c>, or <c>all</c>). Off by default; traces go to the host log, not to the game's log handlers.
    /// </summary>
    internal static class Trace
    {
        static readonly string s_Channels = Environment.GetEnvironmentVariable("UNBOUND_TRACE") ?? "";

        static bool On(string channel) =>
            s_Channels.Length > 0 && (s_Channels == "all" || Array.IndexOf(s_Channels.Split(','), channel) >= 0);

        internal static readonly bool LifecycleOn = On("lifecycle");
        internal static readonly bool ScenesOn = On("scenes");

        internal static void Lifecycle(string message, MonoBehaviour mb)
        {
            if (!LifecycleOn) return;
            Native.Log(3, $"[trace] {message} {mb.GetType().FullName} on '{mb.m_GameObject?.m_Name}' (frame {Time.frameCount})");
        }

        internal static void Scenes(string message)
        {
            if (!ScenesOn) return;
            Native.Log(3, $"[trace] {message} (frame {Time.frameCount})");
        }

        internal static readonly bool AudioOn = On("audio");

        internal static void Audio(string message)
        {
            if (!AudioOn) return;
            Native.Log(3, $"[trace] audio: {message} (frame {Time.frameCount}, dsp {AudioSettings.dspTime:F3})");
        }
    }
}
