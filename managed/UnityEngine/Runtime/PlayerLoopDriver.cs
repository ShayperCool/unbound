using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using PL = UnityEngine.PlayerLoop;

namespace Unbound.Runtime
{
    /// <summary>Input the native side samples once per frame.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal unsafe struct FrameInput
    {
        internal double RealDeltaTime;
        internal int ScreenWidth;
        internal int ScreenHeight;
        internal float MouseX;
        internal float MouseY;
        internal float ScrollX;
        internal float ScrollY;
        /// <summary>Bit i: mouse button i is down.</summary>
        internal uint MouseButtons;
        internal int Focused;
        /// <summary>512 bits, one per KeyCode below 512.</summary>
        internal byte* Keys;
        internal byte* Text;
        internal int TextLength;
        internal int QuitRequested;
        /// <summary>The frame's key presses (repeats included) and releases, in order.</summary>
        internal KeyEvent* KeyEvents;
        internal int KeyEventCount;
        /// <summary>Frames follow the wall clock in a window: keep <c>targetFrameRate</c>.</summary>
        internal int Paced;
        /// <summary>The mouse's raw motion since the last frame (right, down), when
        /// <see cref="RawMotion"/> is 1: what a locked cursor reports.</summary>
        internal float RawMotionX;
        internal float RawMotionY;
        internal int RawMotion;
        internal ulong DisplayVersion;
        internal ulong ImeVersion;
    }

    /// <summary>A key press or release; <see cref="TextStart"/> and <see cref="TextLength"/> are
    /// the range of <see cref="FrameInput.Text"/> the press typed.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct KeyEvent
    {
        internal int Key;
        internal int Pressed;
        internal int TextStart;
        internal int TextLength;
    }

    /// <summary>
    /// Runs frames as a tree of player loop systems, as Unity does: the engine's systems are
    /// identified by their types (UnityEngine.PlayerLoop.*) and run engine code; systems scripts
    /// insert (UniTask does) run their update delegates. The FixedUpdate system runs once per fixed
    /// step the frame's time covers.
    /// </summary>
    internal static class PlayerLoopDriver
    {
        static int s_MainThreadId = -1;
        static bool s_QuitRequested;
        static int s_ExitCode;
        static bool s_Booted;
        static bool s_QuitDone;
        static bool s_EnteredPlayMode;
        static bool s_EndOfFrameAfterLoop;
        static bool s_AdvanceAfterLoop;
        static readonly List<Action> s_FrameCleanup = new List<Action>();
        static Action s_FrameRendering;
        static unsafe FrameInput* s_Input;
        static readonly Dictionary<Type, List<Action>> s_Engine = new Dictionary<Type, List<Action>>();
        static PlayerLoopSystem? s_Current;

        internal static bool IsMainThread => s_MainThreadId < 0 || Environment.CurrentManagedThreadId == s_MainThreadId;

        internal static int MainThreadId => s_MainThreadId;

        /// <summary>Runs an action on the main thread: now when called there, otherwise in the frame.</summary>
        internal static void RunOnMainThread(Action action)
        {
            if (IsMainThread) action();
            else MainThreadContext.Instance.Post(_ => action(), null);
        }
        internal static bool QuitRequested => s_QuitRequested;
        internal static int ExitCode => s_ExitCode;

        internal static PlayerLoopSystem CurrentLoop
        {
            get => s_Current ?? (s_Current = DefaultLoop()).Value;
            set => s_Current = value;
        }

        /// <summary>The measured manual Destroy delivery uses these three scripting points.
        /// Custom loops omitting one retain the prior end-of-frame drain instead of stranding
        /// requests. This fallback preserves our previous behavior; it is not a measured Unity
        /// contract for arbitrary custom loops. Inspect the current loop so a later replacement
        /// cannot leave an earlier request waiting on a node that no longer runs.</summary>
        internal static bool HasDestroyDeliveryPoints
        {
            get
            {
                var phases = CurrentLoop.subSystemList;
                return HasPointInPhase(phases, typeof(PL.FixedUpdate), typeof(PL.FixedUpdate.ScriptRunDelayedFixedFrameRate))
                    && HasPointInPhase(phases, typeof(PL.Update), typeof(PL.Update.ScriptRunDelayedDynamicFrameRate))
                    && HasPointInPhase(phases, typeof(PL.PostLateUpdate), typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate));
            }
        }

        static bool HasPointInPhase(PlayerLoopSystem[] phases, Type phase, Type point)
        {
            if (phases != null)
                foreach (var system in phases)
                    if (system.type == phase && HasPoint(system.subSystemList, point)) return true;
            return false;
        }

        static bool HasPoint(PlayerLoopSystem[] systems, Type point)
        {
            if (systems != null)
                foreach (var system in systems)
                    if (system.type == point || HasPoint(system.subSystemList, point)) return true;
            return false;
        }

        static PlayerLoopDriver()
        {
            Register(typeof(PL.TimeUpdate.WaitForLastPresentationAndUpdateTime), UpdateTime);
            Register(typeof(PL.EarlyUpdate.UpdatePreloading), SceneManagerInternal.ProcessPending);
            Register(typeof(PL.EarlyUpdate.RendererNotifyInvisible), Renderer.NotifyInvisible);
            Register(typeof(PL.EarlyUpdate.UpdateInputManager), UpdateInput);
            Register(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame), Lifecycle.RunPendingStarts);
            // The Input System's updates: in each fixed step, after the frame's start, before rendering.
            Register(typeof(PL.FixedUpdate.NewInputFixedUpdate), NativeInput.FixedUpdate);
            Register(typeof(PL.PreUpdate.IMGUISendQueuedEvents), GuiDispatch.SendQueuedEvents);
            Register(typeof(PL.PreUpdate.NewInputUpdate), NativeInput.DynamicUpdate);
            Register(typeof(PL.PreUpdate.InputForUIUpdate), UnityEngine.InputForUI.EventProvider.NotifyUpdate);
            // The Editor skips the cameras that render into textures.
            Register(typeof(PL.PreUpdate.SendMouseEvents), () => UnityEngine.SendMouseEvents.DoSendMouseEvents(1));
            Register(typeof(PL.FixedUpdate.ScriptRunBehaviourFixedUpdate), Lifecycle.RunFixedUpdate);
            // Destroy requests present on entry finish at these three scripting points. Starts
            // precede delivery, while requests made by Starts, OnDestroy and coroutines wait for
            // the next point (6000.0.71f1: destroyphases, destroycontract, instantiateasync).
            Register(typeof(PL.FixedUpdate.ScriptRunDelayedFixedFrameRate), Destruction.BeginDelivery);
            Register(typeof(PL.FixedUpdate.ScriptRunDelayedFixedFrameRate), Destruction.DeliverRequested);
            // Invokes come due at the fixed steps too (measured, tests/projects/waitforseconds:
            // Invoke(0.3) from Update runs inside a fixed step); see Invokes for the clock.
            Register(typeof(PL.FixedUpdate.ScriptRunDelayedFixedFrameRate), () =>
            {
                Invokes.Run();
                Coroutines.Run(Coroutines.Phase.FixedUpdate);
            });
            Register(typeof(PL.Update.ScriptRunBehaviourUpdate), Lifecycle.RunUpdate);
            // Objects enabled or created during Update start at this node, before coroutines
            // (measured: tests/projects/instantiateasync phase markers and lifecycle records).
            Register(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate), Destruction.BeginDelivery);
            Register(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate), Lifecycle.RunPendingStarts);
            Register(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate), Destruction.DeliverRequested);
            Register(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate), () =>
            {
                Invokes.Run();
                Coroutines.Run(Coroutines.Phase.Update);
            });
            Register(typeof(PL.Update.ScriptRunDelayedTasks), MainThreadContext.Instance.RunPending);
            Register(typeof(PL.PreLateUpdate.ScriptRunBehaviourLateUpdate), Lifecycle.RunLateUpdate);
            // Automatic stop actions and custom loops without the scripting pump retain their
            // end-of-frame fallback. Other releases keep their previous, unmeasured timing.
            Register(typeof(PL.PostLateUpdate.PlayerSendFrameStarted), () =>
            {
                if (!Compat.DestroyPhasesMeasured || !HasDestroyDeliveryPoints) Destruction.RunDelayed();
                Destruction.FlushEndOfFrame();
            });
            Register(typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate), Destruction.BeginDelivery);
            Register(typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate), () =>
            {
                if (Compat.PendingStartsAfterLateUpdate) Lifecycle.RunPendingStarts();
            });
            Register(typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate), Destruction.DeliverRequested);
            Register(typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate), () =>
            {
                if (Compat.InvokeTimingMeasured) Invokes.Run();
            });
            Register(typeof(PL.PostLateUpdate.UpdateAllRenderers), NativeInput.BeforeRenderUpdate);
            Register(typeof(PL.PostLateUpdate.UpdateAllRenderers), Application.Internal_BeforeRender);
            // A coroutine's WaitForEndOfFrame resumes only on a frame that renders the screen, as
            // the interval latched when the frame began decides (measured: tests/projects/coreapis).
            Register(typeof(PL.PostLateUpdate.TriggerEndOfFrameCallbacks), () =>
            {
                if (Compat.DestroyPhasesMeasured) s_EndOfFrameAfterLoop = true;
                else if (UnityEngine.Rendering.OnDemandRendering.RendersThisFrame) Coroutines.Run(Coroutines.Phase.EndOfFrame);
            });
            Register(typeof(PL.PostLateUpdate.PlayerSendFrameComplete), Destruction.FlushEndOfFrame);
            Register(typeof(PL.PostLateUpdate.InputEndFrame), Input.EndFrame);
            Register(typeof(PL.PostLateUpdate.ProfilerEndFrame), Unbound.Runtime.NativePlugins.EndFrame);
            Register(typeof(PL.EarlyUpdate.DispatchEventQueueEvents), Unbound.Runtime.NativePlugins.DispatchEvents);
            // Camera callbacks, OnGUI and WaitForEndOfFrame read the count of the frames before
            // this one, and the next Update reads it advanced; it advances on skipped frames too
            // (measured: tests/projects/coreapis). GUIClearEvents is the first node after the
            // end-of-frame callbacks; the exact point between them and the next Update is not measured.
            Register(typeof(PL.PostLateUpdate.GUIClearEvents), AdvanceRenderedFrameCount);
        }

        static void AdvanceRenderedFrameCount()
        {
            if (Compat.DestroyPhasesMeasured) s_AdvanceAfterLoop = true;
            else
            {
                Time.s_RenderedFrameCount++;
                CleanupFrame();
            }
        }

        static void AfterLoop()
        {
            bool endOfFrame = s_EndOfFrameAfterLoop;
            bool advance = s_AdvanceAfterLoop;
            s_EndOfFrameAfterLoop = s_AdvanceAfterLoop = false;
            // Automatic cameras render outside the public loop, including custom tail systems.
            // Explicit Camera.Render calls remain synchronous at their caller's current point.
            if (Compat.RenderingAfterPublicLoop)
            {
                try { s_FrameRendering?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            // In the measured Editor, these coroutine resumes follow every public loop leaf.
            // They still see the previous rendered-frame count (destroyphases, coreapis).
            if (endOfFrame && UnityEngine.Rendering.OnDemandRendering.RendersThisFrame)
            {
                Coroutines.Run(Coroutines.Phase.EndOfFrame);
                if (Compat.RenderingAfterPublicLoop) AwaitableScheduler.RunEndOfFrame();
            }
            if (advance)
            {
                Time.s_RenderedFrameCount++;
                CleanupFrame();
            }
        }

        /// <summary>Drawing cleanup and screenshot writes follow end-of-frame coroutine drawing.
        /// Other releases retain their original GUIClearEvents delivery.</summary>
        internal static void RegisterFrameCleanup(Action action) => s_FrameCleanup.Add(action);

        internal static void RegisterFrameRendering(Action action) => s_FrameRendering += action;

        static void CleanupFrame()
        {
            for (int i = 0; i < s_FrameCleanup.Count; i++)
            {
                try { s_FrameCleanup[i](); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>Engine code that runs where Unity runs the system of the given type.</summary>
        internal static void Register(Type system, Action action)
        {
            if (!s_Engine.TryGetValue(system, out var list)) s_Engine[system] = list = new List<Action>();
            list.Add(action);
        }

        /// <summary>The engine actions registered at the system of the given type (for tests).</summary>
        internal static IReadOnlyList<Action> Registered(Type system) =>
            s_Engine.TryGetValue(system, out var list) ? list : Array.Empty<Action>();

        /// <summary>Runs an engine action before the existing actions at this same system node.</summary>
        internal static void RegisterFirst(Type system, Action action)
        {
            // Keep any node invocation already enumerating its old list unchanged.
            var next = new List<Action> { action };
            if (s_Engine.TryGetValue(system, out var list)) next.AddRange(list);
            s_Engine[system] = next;
        }

        /// <summary>Appends without changing a list that a current node invocation is enumerating.</summary>
        internal static void RegisterLast(Type system, Action action)
        {
            var next = s_Engine.TryGetValue(system, out var list) ? new List<Action>(list) : new List<Action>();
            next.Add(action);
            s_Engine[system] = next;
        }

        internal static PlayerLoopSystem DefaultLoop()
        {
            var phases = new[]
            {
                typeof(PL.TimeUpdate), typeof(PL.Initialization), typeof(PL.EarlyUpdate), typeof(PL.FixedUpdate),
                typeof(PL.PreUpdate), typeof(PL.Update), typeof(PL.PreLateUpdate), typeof(PL.PostLateUpdate),
            };
            if (Compat.DestroyPhasesMeasured) return MeasuredDefaultLoop(phases);
            var root = new PlayerLoopSystem { subSystemList = new PlayerLoopSystem[phases.Length] };
            for (int i = 0; i < phases.Length; i++)
            {
                var nested = phases[i].GetNestedTypes(BindingFlags.Public);
                var subs = new List<PlayerLoopSystem>();
                foreach (var t in nested)
                    if (t.GetCustomAttribute<ObsoleteAttribute>() == null)
                        subs.Add(new PlayerLoopSystem { type = t });
                root.subSystemList[i] = new PlayerLoopSystem { type = phases[i], subSystemList = subs.ToArray() };
            }
            return root;
        }

        static string[] s_MeasuredLoopLeaves;

        // The active loop is not the set of public API structs. Its order and internal leaves
        // come from destroycontract's loop.leaves reference for this compatibility profile.
        static PlayerLoopSystem MeasuredDefaultLoop(Type[] phases)
        {
            if (s_MeasuredLoopLeaves == null)
            {
                using var stream = typeof(PlayerLoopDriver).Assembly.GetManifestResourceStream("Unbound.PlayerLoop.unity-6000.0.71f1.json");
                if (stream == null) throw new InvalidOperationException("The measured player-loop profile is missing.");
                s_MeasuredLoopLeaves = System.Text.Json.JsonSerializer.Deserialize<string[]>(stream);
            }
            var root = new PlayerLoopSystem { subSystemList = new PlayerLoopSystem[phases.Length] };
            for (int i = 0; i < phases.Length; i++)
            {
                var subs = new List<PlayerLoopSystem>();
                string prefix = phases[i].Name + ".";
                foreach (string name in s_MeasuredLoopLeaves)
                {
                    if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    var type = phases[i].GetNestedType(name.Substring(prefix.Length), BindingFlags.Public | BindingFlags.NonPublic);
                    if (type == null) throw new InvalidOperationException("The player-loop profile requires " + name + ". Regenerate the compatibility API.");
                    subs.Add(new PlayerLoopSystem { type = type });
                }
                root.subSystemList[i] = new PlayerLoopSystem { type = phases[i], subSystemList = subs.ToArray() };
            }
            return root;
        }

        static int s_SignalQuit;
        static System.Runtime.InteropServices.PosixSignalRegistration s_SigTerm, s_SigInt;

        /// <summary>SIGTERM and SIGINT end the run like closing the window (the game's quit
        /// callbacks run, OnApplicationQuit saves); a second signal ends the process at once.</summary>
        internal static void HandleTerminationSignals()
        {
            if (s_SigTerm != null) return;
            void OnSignal(System.Runtime.InteropServices.PosixSignalContext context)
            {
                if (System.Threading.Interlocked.Increment(ref s_SignalQuit) == 1) context.Cancel = true;
            }
            s_SigTerm = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, OnSignal);
            s_SigInt = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGINT, OnSignal);
        }

        internal static void RequestQuit(int exitCode)
        {
            s_QuitRequested = true;
            s_ExitCode = exitCode;
        }

        /// <summary>Runs the game's start: runtime initializers around the first scene load.</summary>
        internal static void Boot(string firstScene) => Boot(firstScene, false);

        /// <summary>Starts the Editor in Edit mode (`unbound test`): the editor domain loads and the
        /// scene, if any, opens without playing; editor code enters Play mode itself.</summary>
        internal static void BootEditMode(string firstScene)
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext.SetSynchronizationContext(MainThreadContext.Instance);
            FrameBuilder.EnsureRegistered();
            AssetImporters.EnsureRegistered();
            CanvasSystem.EnsureRegistered();
            GuiDispatch.EnsureRegistered();
            UnityEditor.EditorApplication.EnsureRegistered();
            EditorSession.StartedInEditMode = true;
            EditorSession.IsPlaying = false;
            UnityEditor.EditorInitialization.LoadEditorDomain();
            if (!string.IsNullOrEmpty(firstScene))
            {
                try { SceneManagerInternal.LoadPath(firstScene); }
                catch (Exception e) { Debug.LogException(e); }
            }
            s_Booted = true;
        }

        /// <summary>Play mode was entered from Edit mode: EnteredPlayMode follows its first frame.</summary>
        internal static void RestartPlayMode()
        {
            s_EnteredPlayMode = false;
            AnimationSystem.BeginPlayMode();
            UnityEngine.SendMouseEvents.Reset();
        }

        static void Boot(string firstScene, bool unused)
        {
            AnimationSystem.BeginPlayMode();
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            Unbound.Runtime.NativePlugins.Boot();
            // The Editor loads them before entering Play mode.
            Unbound.Runtime.NativePlugins.Preload(GameDomain.PreloadedNativePlugins);
            SynchronizationContext.SetSynchronizationContext(MainThreadContext.Instance);
            FrameBuilder.EnsureRegistered();
            AssetImporters.EnsureRegistered();
            CanvasSystem.EnsureRegistered();
            // UnityGUI draws after the overlay canvases.
            GuiDispatch.EnsureRegistered();
            // Play mode runs inside the Editor: its editor code prepares Play mode first.
            UnityEditor.EditorInitialization.BeforePlayMode();
            RuntimeInitializers.Run(RuntimeInitializeLoadType.SubsystemRegistration);
            RuntimeInitializers.Run(RuntimeInitializeLoadType.AfterAssembliesLoaded);
            RuntimeInitializers.Run(RuntimeInitializeLoadType.BeforeSplashScreen);
            if (!string.IsNullOrEmpty(firstScene))
            {
                try
                {
                    SceneManagerInternal.LoadPath(firstScene, playModeEntry: true);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            else RuntimeInitializers.Run(RuntimeInitializeLoadType.BeforeSceneLoad);
            SendPlayModeStart();
            RuntimeInitializers.Run(RuntimeInitializeLoadType.AfterSceneLoad);
            s_Booted = true;
        }

        /// <summary>Entering Play mode, after the first scene's objects woke up and before the
        /// AfterSceneLoad initializers, the Editor sends OnApplicationPause(false) and then
        /// OnApplicationFocus(true) to every behaviour of the active objects, disabled ones too
        /// (measured: tests/projects/messages).</summary>
        internal static void SendPlayModeStart()
        {
            Application.isFocused = true;
            Lifecycle.BroadcastByObject(m => m.OnApplicationPause, false);
            Lifecycle.BroadcastByObject(m => m.OnApplicationFocus, true);
        }

        /// <summary>The window gained or lost the focus: between frames, the Editor sends
        /// OnApplicationFocus to every behaviour of the active objects (disabled ones too) and no
        /// OnApplicationPause (measured: tests/projects/messages).</summary>
        static void FocusChanged(bool focused)
        {
            Application.Internal_FocusChanged(focused);
            Lifecycle.BroadcastByObject(m => m.OnApplicationFocus, focused);
        }

        internal static unsafe void Tick(FrameInput* input)
        {
            if (!s_Booted) return;
            if ((input->QuitRequested != 0 || System.Threading.Volatile.Read(ref s_SignalQuit) != 0) && Application.Internal_WantsToQuit())
                s_QuitRequested = true;
            s_Input = input;
            EditorSession.ApplyRequest();
            if (!EditorSession.IsPlaying)
            {
                EditModeFrame(input);
                return;
            }
            if ((input->Focused != 0) != Application.isFocused) FocusChanged(input->Focused != 0);
            if (PauseShortcut(input)) UnityEditor.EditorApplication.isPaused = !UnityEditor.EditorApplication.isPaused;
            if (UnityEditor.EditorApplication.isPaused && !UnityEditor.EditorApplication.TakeStep())
            {
                PausedFrame(input);
                return;
            }
            var loop = CurrentLoop;
            if (loop.subSystemList != null)
            {
                foreach (var phase in loop.subSystemList)
                {
                    if (phase.type == typeof(PL.FixedUpdate)) RunFixedSteps(phase);
                    else RunSystem(phase);
                    // The engine hands the jobs scheduled in a phase to the workers.
                    Jobs.JobSystem.Flush();
                }
            }
            AfterLoop();
            if (!s_EnteredPlayMode)
            {
                AnimationSystem.EndPlayModeEntry();
                s_EnteredPlayMode = true;
                UnityEditor.EditorInitialization.AfterFirstFrame();
            }
            // The Editor runs its own input update after each frame of Play mode.
            NativeInput.EditorUpdate();
            // Allocator.Temp memory lives until the end of the frame; recorders take their sample.
            Safety.EndFrame();
            Profiling.ProfilerCore.EndFrame();
            bool paced = input->Paced != 0;
            Trace.Memory();
            s_Input = null;
            if (s_QuitRequested) Quit();
            else if (paced) Pace();
        }

        /// <summary>A frame of paused Play mode: the Editor's updates run, the game does not.</summary>
        static unsafe void PausedFrame(FrameInput* input)
        {
            MainThreadContext.Instance.RunPending();
            UnityEditor.EditorApplication.RunEditorCallbacksNow();
            Jobs.JobSystem.Flush();
            Safety.EndFrame();
            bool paced = input->Paced != 0;
            s_Input = null;
            if (s_QuitRequested) Quit();
            else if (paced) Pace();
        }

        const int KeyP = 112, KeyLeftShift = 304, KeyRightShift = 303, KeyLeftControl = 306, KeyRightControl = 305;

        /// <summary>The Editor's pause shortcut, Ctrl+Shift+P, pressed this frame.</summary>
        static unsafe bool PauseShortcut(FrameInput* input)
        {
            if (input->Keys == null || input->KeyEvents == null) return false;
            bool Held(int key) => (input->Keys[key >> 3] & (1 << (key & 7))) != 0;
            if (!(Held(KeyLeftControl) || Held(KeyRightControl)) || !(Held(KeyLeftShift) || Held(KeyRightShift))) return false;
            for (int i = 0; i < input->KeyEventCount; i++)
                if (input->KeyEvents[i].Key == KeyP && input->KeyEvents[i].Pressed != 0) return true;
            return false;
        }

        /// <summary>An Edit mode frame: the Editor's own updates and editor callbacks; no scene plays.</summary>
        static unsafe void EditModeFrame(FrameInput* input)
        {
            UpdateInput();
            MainThreadContext.Instance.RunPending();
            UnityEditor.EditorApplication.RunEditorCallbacksNow();
            Jobs.JobSystem.Flush();
            Safety.EndFrame();
            s_Input = null;
            if (s_QuitRequested) Quit();
        }

        static readonly System.Diagnostics.Stopwatch s_FrameClock = System.Diagnostics.Stopwatch.StartNew();
        static double s_NextFrameAt;

        /// <summary>The vertical sync last asked of the window: 0 off, 1 on, -1 not yet.</summary>
        static int s_PresentedVSync = -1;

        /// <summary>The window's vertical sync for <paramref name="vSyncCount"/>, when it differs
        /// from the one last asked (<paramref name="presented"/>, updated): frames wait for the
        /// vertical sync only while vSyncCount is above 0 (Unity 6000.0 QualitySettings.vSyncCount:
        /// with 0, rendering is not synchronized; the count's divisor is paced here).</summary>
        internal static string VSyncRequest(ref int presented, int vSyncCount)
        {
            int vsync = vSyncCount != 0 ? 1 : 0;
            if (vsync == presented) return null;
            presented = vsync;
            return vsync == 1 ? "1" : "0";
        }

        /// <summary>Unity's frame rate cap: without vertical sync (<c>vSyncCount</c> 0) a frame
        /// lasts at least <c>1 / targetFrameRate</c>; the wait is at the end of the frame.</summary>
        static void Pace()
        {
            if (VSyncRequest(ref s_PresentedVSync, QualitySettings.vSyncCount) is string vsync)
                Native.Call("vsync", vsync);
            int target = Application.targetFrameRate;
            double now = s_FrameClock.Elapsed.TotalSeconds;
            double period = FramePeriod(target, QualitySettings.vSyncCount);
            if (period == 0)
            {
                s_NextFrameAt = now;
                return;
            }
            // A late frame starts the schedule again rather than racing to catch up.
            s_NextFrameAt = Math.Max(s_NextFrameAt + period, now);
            double wait = s_NextFrameAt - now;
            // Sleep most of the wait, then yield the last millisecond away for precision.
            if (wait > 0.002) System.Threading.Thread.Sleep(TimeSpan.FromSeconds(wait - 0.0015));
            while (s_FrameClock.Elapsed.TotalSeconds < s_NextFrameAt) System.Threading.Thread.Yield();
        }

        /// <summary>Uses the physical host refresh for pacing. Screen's public resolution
        /// answer independently follows the Editor. Present is scheduled 5% early so it does
        /// not miss the compositor's refresh deadline.</summary>
        internal static double FramePeriod(int target, int vSyncCount)
        {
            if (vSyncCount != 0) return vSyncCount / Screen.HostRefreshRate * 0.95;
            return target > 0 ? 1.0 / target : 0;
        }

        /// <summary>The fixed steps the game clock has reached: the first at fixed time 0 in the
        /// first frame, then one each time the fixed clock plus a step is not past the game clock
        /// (whose frame delta maximumDeltaTime already clamps: no other limit). Measured:
        /// tests/projects/messages, 17 steps after a 0.7 s frame, the fixed clock within a step
        /// behind the game clock after every frame's steps.</summary>
        static void RunFixedSteps(PlayerLoopSystem system)
        {
            bool first = !Time.s_FixedStarted;
            Time.s_FixedStarted = true;
            while (first || Time.s_FixedTime + Time.FixedClockDuration <= Time.s_Time + 1e-7)
            {
                if (!first)
                {
                    Time.s_FixedTime += Time.FixedClockDuration;
                    // The unscaled fixed clock is the unscaled clock less the step's lag behind
                    // the game clock (measured at timeScale 1, tests/projects/inputframes: under a
                    // capture rate too, where the unscaled clock keeps real time).
                    Time.s_FixedUnscaledTime = Time.s_UnscaledTime - (Time.s_Time - Time.s_FixedTime);
                }
                first = false;
                Time.s_InFixedTimeStep = true;
                try
                {
                    RunSystem(system);
                }
                finally
                {
                    Time.s_InFixedTimeStep = false;
                }
            }
        }

        static void RunSystem(PlayerLoopSystem system)
        {
            if (system.type != null && s_Engine.TryGetValue(system.type, out var actions))
            {
                foreach (var action in actions)
                {
                    try { action(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            if (system.updateDelegate != null)
            {
                try { system.updateDelegate(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (system.subSystemList != null)
                foreach (var sub in system.subSystemList)
                    RunSystem(sub);
        }

        static unsafe void UpdateTime()
        {
            if (s_Input == null) return;
            Time.BeginFrame(s_Input->RealDeltaTime);
            ReflectionProbe.BeginFrame();
            SceneManagerInternal.PreparePending();
        }

        static unsafe void UpdateInput()
        {
            if (s_Input == null) return;
            Screen.BeginFrame(s_Input);
            Screen.UpdateDisplayVersion(s_Input->DisplayVersion);
            Input.UpdateImeVersion(s_Input->ImeVersion);
            Gamepads.Poll();
            Input.BeginFrame(s_Input);
            NativeInput.BeginFrame(s_Input);
        }

        internal static void Quit()
        {
            Trace.ReportPerf();
            Trace.DumpHierarchy();
            Trace.ReportUnread();
            if (s_QuitDone) return;
            s_QuitDone = true;
            Lifecycle.Broadcast(m => m.OnApplicationQuit, null);
            Application.Internal_Quitting();
            PlayerPrefs.SaveOnQuit();
            Unbound.Runtime.NativePlugins.Shutdown();
        }
    }

    /// <summary>The main thread's SynchronizationContext: continuations of awaits started on the main
    /// thread run there, during the frame after Update, like UnitySynchronizationContext.</summary>
    internal sealed class MainThreadContext : SynchronizationContext
    {
        internal static readonly MainThreadContext Instance = new MainThreadContext();
        readonly Queue<(SendOrPostCallback Callback, object State)> m_Queue = new Queue<(SendOrPostCallback, object)>();
        readonly object m_Lock = new object();

        public override void Post(SendOrPostCallback d, object state)
        {
            lock (m_Lock) m_Queue.Enqueue((d, state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            if (PlayerLoopDriver.IsMainThread)
            {
                d(state);
                return;
            }
            using var done = new ManualResetEventSlim();
            Post(s => { try { d(s); } finally { done.Set(); } }, state);
            done.Wait();
        }

        public override SynchronizationContext CreateCopy() => this;

        internal void RunPending()
        {
            int count;
            lock (m_Lock) count = m_Queue.Count;
            // Only what was queued before this pass: continuations that queue more run next frame.
            for (int i = 0; i < count; i++)
            {
                (SendOrPostCallback Callback, object State) item;
                lock (m_Lock)
                {
                    if (m_Queue.Count == 0) break;
                    item = m_Queue.Dequeue();
                }
                try { item.Callback(item.State); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }

    /// <summary>[RuntimeInitializeOnLoadMethod] methods of the game's assemblies, by load type.</summary>
    internal static class RuntimeInitializers
    {
        internal static void Run(RuntimeInitializeLoadType loadType)
        {
            foreach (var assembly in GameDomain.Assemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                foreach (var type in types)
                {
                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    foreach (var method in methods)
                    {
                        RuntimeInitializeOnLoadMethodAttribute attribute;
                        try { attribute = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>(); }
                        catch (Exception) { continue; }
                        if (attribute == null || attribute.loadType != loadType) continue;
                        if (method.GetParameters().Length != 0 || method.ContainsGenericParameters) continue;
                        try
                        {
                            method.Invoke(null, null);
                        }
                        catch (TargetInvocationException e)
                        {
                            Debug.LogException(e.InnerException ?? e);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
            }
        }
    }
}
