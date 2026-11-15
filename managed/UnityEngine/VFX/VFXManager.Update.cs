using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine.VFX
{
    /// <summary>
    /// The frame's update of the enabled effects, at PostLateUpdate.VFXUpdate: the frame's time
    /// as effects take it (the fixed steps the frame holds and its delta time, both limited by
    /// the settings), then each effect's steps. The rules match the Unity 6000.0.71f1 Editor
    /// (tests/projects/vfx).
    /// </summary>
    public static partial class VFXManager
    {
        /// <summary>The time of a frame for effects.</summary>
        internal struct FrameTime
        {
            /// <summary>The fixed steps the frame holds (with and without the time scale), and
            /// the time they add up to.</summary>
            internal uint FixedSteps, UnscaledFixedSteps;
            internal float FixedTime, UnscaledFixedTime;
            /// <summary>The frame's delta time, limited to the longest step.</summary>
            internal float DeltaTime, UnscaledDeltaTime;
        }

        /// <summary>The smallest fixed time step the settings are taken as (so a step of zero
        /// cannot divide), and what the longest step is extended by when counting the fixed
        /// steps it holds (so a longest step that is a whole number of steps, as the default
        /// 0.05 is of three sixtieths, counts them all). Neither has a control of its own: the
        /// default settings' three steps are measured (tests/projects/vfx, `sim/rate*` from frame 37).</summary>
        const float MinFixedTimeStep = 1e-4f, StepCountSlack = 1e-5f;

        static readonly List<VisualEffect> s_Effects = new List<VisualEffect>();
        static VisualEffect[] s_Snapshot = new VisualEffect[8];
        /// <summary>The time not yet turned into fixed steps (negative after a step rounded up).</summary>
        static float s_Remainder, s_UnscaledRemainder;
        static uint s_Frame;
        static ComputeShader s_CopyBuffer;
        static int s_CopySelfKernel = -1;
        static bool s_CopyBufferMissing;

        static VFXManager() => PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.VFXUpdate), Update);

        /// <summary>How many updates ran: the frame index effects see.</summary>
        internal static uint Frame => s_Frame;

        internal static void Add(VisualEffect effect)
        {
            if (!s_Effects.Contains(effect)) s_Effects.Add(effect);
        }

        internal static void Remove(VisualEffect effect) => s_Effects.Remove(effect);

        /// <summary>The enabled effects.</summary>
        public static VisualEffect[] GetComponents() => s_Effects.ToArray();

        /// <summary>The fixed steps in <paramref name="delta"/> more seconds: the nearest whole
        /// number of steps to the time waiting, which keeps the rest (the count used is limited
        /// to the steps the longest step holds).</summary>
        internal static uint FixedSteps(float delta, float step, uint most, ref float remainder)
        {
            float waiting = remainder + delta;
            int steps = (int)MathF.Floor(waiting / step + 0.5f);
            remainder = waiting - steps * step;
            return (uint)Math.Clamp(steps, 0, (int)most);
        }

        static float Limited(float delta, float longest) => delta >= 0f ? (delta <= longest ? delta : longest) : 0f;

        internal static FrameTime CurrentFrameTime()
        {
            float step = Math.Max(fixedTimeStep, MinFixedTimeStep), longest = maxDeltaTime;
            uint most = Math.Max(1u, (uint)Math.Max(0f, (longest + StepCountSlack) / step));
            var time = new FrameTime();
            time.FixedSteps = FixedSteps(Time.deltaTime, step, most, ref s_Remainder);
            time.UnscaledFixedSteps = FixedSteps(Time.unscaledDeltaTime, step, most, ref s_UnscaledRemainder);
            time.FixedTime = time.FixedSteps * step;
            time.UnscaledFixedTime = time.UnscaledFixedSteps * step;
            time.DeltaTime = Limited(Time.deltaTime, longest);
            time.UnscaledDeltaTime = Limited(Time.unscaledDeltaTime, longest);
            return time;
        }

        static void Update()
        {
            int count = s_Effects.Count;
            if (count == 0) return;
            s_Frame++;
            var time = CurrentFrameTime();
            // A snapshot: an effect's callbacks (custom spawners, output events) may enable or
            // disable effects.
            if (s_Snapshot.Length < count) s_Snapshot = new VisualEffect[Math.Max(count, s_Snapshot.Length * 2)];
            s_Effects.CopyTo(s_Snapshot);
            for (int i = 0; i < count; i++)
            {
                var effect = s_Snapshot[i];
                s_Snapshot[i] = null;
                if (!effect || !effect.isActiveAndEnabled) continue;
                // One effect's failure (a script's spawner block throwing) is its own.
                try { effect.UpdateEffect(time); }
                catch (Exception e) { Debug.LogException(e, effect); }
            }
        }

        /// <summary>Copies a dead list's count into its second word on the GPU, with the
        /// package's buffer copy kernel (the initialize kernel reads the copy while it takes
        /// particles from the list).</summary>
        internal static void CopyDeadCount(GraphicsBuffer deadList)
        {
            if (s_CopyBuffer == null && !s_CopyBufferMissing)
            {
                const string Path = "Packages/com.unity.visualeffectgraph/Shaders/VFXCopyBuffer.compute";
                s_CopyBuffer = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(Path);
                s_CopyBufferMissing = s_CopyBuffer == null;
                if (s_CopyBufferMissing)
                {
                    Debug.LogError($"Unbound: the Visual Effect Graph package's {Path} was not found; particle systems that kill particles cannot spawn");
                    return;
                }
                s_CopyBuffer = Object.Instantiate(s_CopyBuffer);
                s_CopySelfKernel = s_CopyBuffer.FindKernel("CSVFXCopyStructBufferSelf");
                // One instance: its count at word zero, the copy right after.
                s_CopyBuffer.SetInt("srcOffset", 0);
                s_CopyBuffer.SetInt("dstOffset", 1);
                s_CopyBuffer.SetInt("size", 1);
            }
            if (s_CopyBufferMissing) return;
            s_CopyBuffer.SetBuffer(s_CopySelfKernel, "dstStructBuffer", deadList);
            s_CopyBuffer.Dispatch(s_CopySelfKernel, 1, 1, 1);
        }
    }
}
