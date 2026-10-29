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
        static unsafe FrameInput* s_Input;
        static readonly Dictionary<Type, List<Action>> s_Engine = new Dictionary<Type, List<Action>>();
        static PlayerLoopSystem? s_Current;

        internal static bool IsMainThread => s_MainThreadId < 0 || Thread.CurrentThread.ManagedThreadId == s_MainThreadId;

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

        static PlayerLoopDriver()
        {
            Register(typeof(PL.TimeUpdate.WaitForLastPresentationAndUpdateTime), UpdateTime);
            Register(typeof(PL.EarlyUpdate.UpdatePreloading), SceneManagerInternal.ProcessPending);
            Register(typeof(PL.EarlyUpdate.UpdateInputManager), UpdateInput);
            Register(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame), Lifecycle.RunPendingStarts);
            // The Input System's updates: in each fixed step, after the frame's start, before rendering.
            Register(typeof(PL.FixedUpdate.NewInputFixedUpdate), NativeInput.FixedUpdate);
            Register(typeof(PL.PreUpdate.NewInputUpdate), NativeInput.DynamicUpdate);
            Register(typeof(PL.PreUpdate.InputForUIUpdate), UnityEngine.InputForUI.EventProvider.NotifyUpdate);
            // The Editor skips the cameras that render into textures.
            Register(typeof(PL.PreUpdate.SendMouseEvents), () => UnityEngine.SendMouseEvents.DoSendMouseEvents(1));
            Register(typeof(PL.FixedUpdate.ScriptRunBehaviourFixedUpdate), Lifecycle.RunFixedUpdate);
            Register(typeof(PL.FixedUpdate.ScriptRunDelayedFixedFrameRate), () => Coroutines.Run(Coroutines.Phase.FixedUpdate));
            Register(typeof(PL.Update.ScriptRunBehaviourUpdate), Lifecycle.RunUpdate);
            Register(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate), () =>
            {
                Invokes.Run();
                Coroutines.Run(Coroutines.Phase.Update);
            });
            Register(typeof(PL.Update.ScriptRunDelayedTasks), MainThreadContext.Instance.RunPending);
            Register(typeof(PL.PreLateUpdate.ScriptRunBehaviourLateUpdate), Lifecycle.RunLateUpdate);
            Register(typeof(PL.PostLateUpdate.PlayerSendFrameStarted), () =>
            {
                Destruction.RunDelayed();
                Destruction.FlushEndOfFrame();
            });
            Register(typeof(PL.PostLateUpdate.UpdateAllRenderers), NativeInput.BeforeRenderUpdate);
            Register(typeof(PL.PostLateUpdate.UpdateAllRenderers), Application.Internal_BeforeRender);
            Register(typeof(PL.PostLateUpdate.FinishFrameRendering), () => Time.s_RenderedFrameCount++);
            Register(typeof(PL.PostLateUpdate.TriggerEndOfFrameCallbacks), () => Coroutines.Run(Coroutines.Phase.EndOfFrame));
            Register(typeof(PL.PostLateUpdate.PlayerSendFrameComplete), Destruction.FlushEndOfFrame);
            Register(typeof(PL.PostLateUpdate.InputEndFrame), Input.EndFrame);
        }

        /// <summary>Engine code that runs where Unity runs the system of the given type.</summary>
        internal static void Register(Type system, Action action)
        {
            if (!s_Engine.TryGetValue(system, out var list)) s_Engine[system] = list = new List<Action>();
            list.Add(action);
        }

        internal static PlayerLoopSystem DefaultLoop()
        {
            var phases = new[]
            {
                typeof(PL.TimeUpdate), typeof(PL.Initialization), typeof(PL.EarlyUpdate), typeof(PL.FixedUpdate),
                typeof(PL.PreUpdate), typeof(PL.Update), typeof(PL.PreLateUpdate), typeof(PL.PostLateUpdate),
            };
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
            UnityEngine.SendMouseEvents.Reset();
        }

        static void Boot(string firstScene, bool unused)
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
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
            RuntimeInitializers.Run(RuntimeInitializeLoadType.BeforeSceneLoad);
            if (!string.IsNullOrEmpty(firstScene))
            {
                try
                {
                    SceneManagerInternal.LoadPath(firstScene);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
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
            if (!s_EnteredPlayMode)
            {
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

        /// <summary>Unity's frame rate cap: without vertical sync (<c>vSyncCount</c> 0) a frame
        /// lasts at least <c>1 / targetFrameRate</c>; the wait is at the end of the frame.</summary>
        static void Pace()
        {
            int target = Application.targetFrameRate;
            double now = s_FrameClock.Elapsed.TotalSeconds;
            double period;
            if (QualitySettings.vSyncCount != 0)
            {
                // Presenting waits for the display's refresh; a window the compositor does not
                // show (covered, on another workspace) presents at once, so the frames are also
                // held to the refresh rate here, a little early so a waiting present is never late.
                double refresh = Screen.currentResolution.refreshRateRatio.value;
                if (!(refresh >= 1)) refresh = 60;
                period = QualitySettings.vSyncCount / refresh * 0.95;
            }
            else if (target <= 0)
            {
                s_NextFrameAt = now;
                return;
            }
            else period = 1.0 / target;
            // A late frame starts the schedule again rather than racing to catch up.
            s_NextFrameAt = Math.Max(s_NextFrameAt + period, now);
            double wait = s_NextFrameAt - now;
            // Sleep most of the wait, then yield the last millisecond away for precision.
            if (wait > 0.002) System.Threading.Thread.Sleep(TimeSpan.FromSeconds(wait - 0.0015));
            while (s_FrameClock.Elapsed.TotalSeconds < s_NextFrameAt) System.Threading.Thread.Yield();
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
            while (first || Time.s_FixedTime + Time.s_FixedDeltaTime <= Time.s_Time + 1e-7)
            {
                if (!first)
                {
                    Time.s_FixedTime += Time.s_FixedDeltaTime;
                    Time.s_FixedUnscaledTime += Time.s_FixedDeltaTime;
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
        }

        static unsafe void UpdateInput()
        {
            if (s_Input == null) return;
            Screen.Update(s_Input->ScreenWidth, s_Input->ScreenHeight);
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
