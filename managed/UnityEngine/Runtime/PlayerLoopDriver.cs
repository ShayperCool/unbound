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
        static double s_FixedAccumulator;
        static bool s_QuitRequested;
        static int s_ExitCode;
        static bool s_Booted;
        static bool s_QuitDone;
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

        internal static void RequestQuit(int exitCode)
        {
            s_QuitRequested = true;
            s_ExitCode = exitCode;
        }

        /// <summary>Runs the game's start: runtime initializers around the first scene load.</summary>
        internal static void Boot(string firstScene)
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext.SetSynchronizationContext(MainThreadContext.Instance);
            FrameBuilder.EnsureRegistered();
            AssetImporters.EnsureRegistered();
            CanvasSystem.EnsureRegistered();
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
            RuntimeInitializers.Run(RuntimeInitializeLoadType.AfterSceneLoad);
            s_Booted = true;
        }

        internal static unsafe void Tick(FrameInput* input)
        {
            if (!s_Booted) return;
            if (input->QuitRequested != 0 && Application.Internal_WantsToQuit())
                s_QuitRequested = true;
            s_Input = input;
            var loop = CurrentLoop;
            if (loop.subSystemList != null)
            {
                foreach (var phase in loop.subSystemList)
                {
                    if (phase.type == typeof(PL.FixedUpdate)) RunFixedSteps(phase);
                    else RunSystem(phase);
                }
            }
            s_Input = null;
            if (s_QuitRequested) Quit();
        }

        static void RunFixedSteps(PlayerLoopSystem system)
        {
            s_FixedAccumulator += Time.s_DeltaTime;
            int steps = 0;
            while (s_FixedAccumulator >= Time.s_FixedDeltaTime && steps < 8)
            {
                s_FixedAccumulator -= Time.s_FixedDeltaTime;
                steps++;
                Time.s_InFixedTimeStep = true;
                Time.s_FixedTime += Time.s_FixedDeltaTime;
                Time.s_FixedUnscaledTime += Time.s_FixedDeltaTime;
                try
                {
                    RunSystem(system);
                }
                finally
                {
                    Time.s_InFixedTimeStep = false;
                }
            }
            if (steps == 8) s_FixedAccumulator = 0;
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
            Input.BeginFrame(s_Input);
            if ((s_Input->Focused != 0) != Application.isFocused)
                Application.Internal_FocusChanged(s_Input->Focused != 0);
        }

        internal static void Quit()
        {
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
