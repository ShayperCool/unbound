using System;

namespace Unbound.Runtime.Profiling
{
    /// <summary>
    /// The render counters scripts read through ProfilerRecorder ("Draw Calls Count",
    /// "Batches Count", "SetPass Calls Count"): what Unbound's renderer drew in the last frame,
    /// counted as its draw commands are written. A draw command is one draw call and one batch
    /// (Unbound does not batch draws; the instances of one instancing call drawn one after another
    /// with the same material and pass are one, Unbound's choice, unmeasured), and a SetPass
    /// call whenever its material or pass differs from the draw before it in the frame; a command
    /// for several passes of a shader (the forward passes, a renderer list's tagged passes, every
    /// pass) counts a draw call, a batch and a SetPass call per pass. The engine's own draws
    /// (environment captures, probe convolution) count too. The Unity 6000.0.71f1 Editor counts
    /// its own work (tests/projects/renderstats): it batches small meshes into one draw, so the
    /// counts differ where the work differs.
    /// </summary>
    static class RenderStats
    {
        static long s_Draws, s_SetPasses;
        /// <summary>The next draw is instance <see cref="s_NextInstance"/> of an instancing call of
        /// <see cref="s_NextInstanceCount"/> instances.</summary>
        static bool s_NextIsInstance;
        static uint s_NextInstance, s_NextInstanceCount;
        /// <summary>The last draw command's instance (-1: it was no instance), its call's instance
        /// count and its pass as written.</summary>
        static long s_LastInstance = -1;
        static uint s_LastInstanceCount;
        static int s_LastDrawnPass = int.MinValue;
        /// <summary>How many passes the next draw command draws.</summary>
        static int s_NextPasses = 1;
        /// <summary>The "pass" after a command for several passes: the next draw is a pass change.</summary>
        const int SeveralPasses = int.MinValue + 1;
        static ulong s_LastMaterial;
        static int s_LastPass = int.MinValue;

        /// <summary>The last frame's counts.</summary>
        internal static long LastDraws, LastSetPasses;

        /// <summary>A draw command of <paramref name="material"/>'s <paramref name="pass"/>.</summary>
        internal static void Draw(ulong material, int pass)
        {
            int passes = s_NextPasses;
            s_NextPasses = 1;
            bool instance = s_NextIsInstance;
            s_NextIsInstance = false;
            // A further instance of the run the last command began: the same call (a higher
            // instance of as many), material and pass. A run starts wherever the call's instances
            // are interrupted (another pass, a light's forward-add draws of some of them).
            bool further = instance && s_LastInstance >= 0 && s_NextInstance > s_LastInstance
                && s_NextInstanceCount == s_LastInstanceCount && material == s_LastMaterial && pass == s_LastDrawnPass;
            s_LastInstance = instance ? s_NextInstance : -1;
            s_LastInstanceCount = s_NextInstanceCount;
            s_LastDrawnPass = pass;
            if (further) return;
            if (passes == 0) return;
            if (passes > 1)
            {
                s_Draws += passes;
                s_SetPasses += passes;
                s_LastMaterial = material;
                s_LastPass = SeveralPasses;
                return;
            }
            s_Draws++;
            if (material == s_LastMaterial && pass == s_LastPass) return;
            s_SetPasses++;
            s_LastMaterial = material;
            s_LastPass = pass;
        }

        /// <summary>The next draw command draws <paramref name="passes"/> passes (0: none, nothing
        /// counts).</summary>
        internal static void NextDrawPasses(int passes) => s_NextPasses = Math.Max(passes, 0);

        /// <summary>The next draw command draws every pass of <paramref name="material"/>'s shader,
        /// as the renderer lists them (one when it does not know the shader).</summary>
        internal static void NextDrawAllPasses(UnityEngine.Material material) =>
            NextDrawPasses(material != null && material.shader is { } shader && shader.PassInfo is { Found: true } info ? info.Passes.Count : 1);

        /// <summary>The next draw is instance <paramref name="index"/> of an instancing call of
        /// <paramref name="count"/> instances: a run of its instances counts once.</summary>
        internal static void MeshInstance(uint index, uint count)
        {
            s_NextIsInstance = true;
            s_NextInstance = index;
            s_NextInstanceCount = count;
        }

        /// <summary>The frame ended: its counts become the last frame's.</summary>
        internal static void EndFrame()
        {
            LastDraws = s_Draws;
            LastSetPasses = s_SetPasses;
            s_Draws = s_SetPasses = 0;
            s_LastMaterial = 0;
            s_LastPass = int.MinValue;
            s_NextIsInstance = false;
            s_LastInstance = -1;
            s_LastDrawnPass = int.MinValue;
            s_NextPasses = 1;
        }
    }
}
