namespace Unbound.Runtime.Profiling
{
    /// <summary>
    /// The render counters scripts read through ProfilerRecorder ("Draw Calls Count",
    /// "Batches Count", "SetPass Calls Count"): what Unbound's renderer drew in the last frame,
    /// counted as its draw commands are written. A draw command is one draw call and one batch
    /// (Unbound does not batch draws; the instances of one instancing call are one), and a SetPass
    /// call whenever its material or pass differs from the draw before it in the frame. A command
    /// for several passes of a shader (the forward passes, a tag's passes, every pass) counts
    /// once, and the engine's own draws (environment captures, probe convolution) count too. The Unity 6000.0.71f1 Editor counts its own work
    /// (tests/projects/renderstats): it batches small meshes into one draw, draws a render
    /// texture's colour clear as a quad and draws a forward-add pass per lit object, so the counts
    /// differ where the work differs.
    /// </summary>
    static class RenderStats
    {
        static long s_Draws, s_SetPasses;
        /// <summary>The next draw is a further instance of an instancing call (not counted).</summary>
        static bool s_FurtherInstance;
        static ulong s_LastMaterial;
        static int s_LastPass = int.MinValue;

        /// <summary>The last frame's counts.</summary>
        internal static long LastDraws, LastSetPasses;

        /// <summary>A draw command of <paramref name="material"/>'s <paramref name="pass"/>.</summary>
        internal static void Draw(ulong material, int pass)
        {
            if (s_FurtherInstance)
            {
                s_FurtherInstance = false;
                return;
            }
            s_Draws++;
            if (material == s_LastMaterial && pass == s_LastPass) return;
            s_SetPasses++;
            s_LastMaterial = material;
            s_LastPass = pass;
        }

        /// <summary>The next draw is instance <paramref name="index"/> of an instancing call: only
        /// the first counts.</summary>
        internal static void MeshInstance(uint index) => s_FurtherInstance = index > 0;

        /// <summary>The frame ended: its counts become the last frame's.</summary>
        internal static void EndFrame()
        {
            LastDraws = s_Draws;
            LastSetPasses = s_SetPasses;
            s_Draws = s_SetPasses = 0;
            s_LastMaterial = 0;
            s_LastPass = int.MinValue;
            s_FurtherInstance = false;
        }
    }
}
