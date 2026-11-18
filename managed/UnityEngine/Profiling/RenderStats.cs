using System;

namespace Unbound.Runtime.Profiling
{
    /// <summary>
    /// The render counters scripts read through ProfilerRecorder ("Draw Calls Count",
    /// "Batches Count", "SetPass Calls Count"): what Unbound's renderer drew in the last frame,
    /// counted as its draw commands are written. A draw command is one draw call and one batch
    /// (Unbound does not batch draws; a run of one instancing call's instances with the same
    /// material, pass and light is one, Unbound's choice, unmeasured), and a SetPass
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
        /// <summary>The next draw is instance <see cref="s_NextInstance"/> of instancing call
        /// <see cref="s_NextCall"/>, lit by the light <see cref="s_NextLight"/> (0: no
        /// light's own pass).</summary>
        static bool s_NextIsInstance;
        static uint s_NextInstance, s_NextCall;
        static int s_NextLight;
        /// <summary>The runs of instancing calls' instances counted lately: the call, material,
        /// pass and light of each and the highest instance drawn in it (a lower one begins a new
        /// run, as another camera's render does). A fixed table, the oldest run replaced.</summary>
        const int RunSlots = 16;
        static readonly (uint Call, ulong Material, int Pass, int Light, uint Instance)[] s_Runs = new (uint, ulong, int, int, uint)[RunSlots];
        static int s_RunCount, s_NextRunSlot;
        static uint s_Calls;
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
            int light = s_NextLight;
            s_NextIsInstance = false;
            s_NextLight = 0;
            if (instance && FurtherInstance(material, pass, light)) return;
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

        /// <summary>A new instancing call's id for <see cref="MeshInstance"/> (never 0).</summary>
        internal static uint NewInstancingCall() => ++s_Calls == 0 ? ++s_Calls : s_Calls;

        /// <summary>The next draw is instance <paramref name="index"/> of instancing call
        /// <paramref name="call"/> (<see cref="NewInstancingCall"/>; 0: a call of its own): a run of
        /// the call's instances with one material, pass and light counts once.</summary>
        internal static void MeshInstance(uint index, uint call)
        {
            s_NextIsInstance = call != 0;
            s_NextInstance = index;
            s_NextCall = call;
        }

        /// <summary>The next draw is a pass of its own for <paramref name="light"/> (a forward-add
        /// draw): instances of a call lit by different lights are different runs.</summary>
        internal static void NextDrawLight(UnityEngine.Light light) => s_NextLight = light != null ? light.GetInstanceID() : 0;

        /// <summary>Whether the next instance continues a run counted before, which it extends.</summary>
        static bool FurtherInstance(ulong material, int pass, int light)
        {
            for (int i = 0; i < s_RunCount; i++)
            {
                ref var run = ref s_Runs[i];
                if (run.Call != s_NextCall || run.Material != material || run.Pass != pass || run.Light != light) continue;
                bool further = s_NextInstance > run.Instance;
                run.Instance = s_NextInstance;
                return further;
            }
            s_Runs[s_NextRunSlot] = (s_NextCall, material, pass, light, s_NextInstance);
            s_NextRunSlot = (s_NextRunSlot + 1) % RunSlots;
            if (s_RunCount < RunSlots) s_RunCount++;
            return false;
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
            s_NextLight = 0;
            s_RunCount = s_NextRunSlot = 0;
            s_NextPasses = 1;
        }
    }
}
