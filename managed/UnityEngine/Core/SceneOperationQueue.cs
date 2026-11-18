using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unbound.Runtime;

namespace UnityEngine.SceneManagement
{
    /// <summary>
    /// Scene loads, scene unloads and unused-asset unloads share one queue, as the background
    /// loader and the main-thread integration of Unity 6000.0 do (Scripting API:
    /// Application.backgroundLoadingPriority, AsyncOperation.priority, allowSceneActivation;
    /// measured: tests/projects/editorcallbacks --queue-budget):
    /// <list type="bullet">
    /// <item>The loader prepares requests one after another, in the order it takes them: a request
    /// made while it is idle is taken at once; the others, at its next pass, highest
    /// <see cref="AsyncOperation.priority"/> first (request order among equals). A request it has
    /// taken keeps its place whatever its priority becomes. An unused-asset unload stops the
    /// loader until it has been integrated.</item>
    /// <item>The preloading pass integrates prepared requests in that order. A scene load waits one
    /// pass after it first reaches the head prepared (its progress stays at 0.9), then integrates
    /// in a later frame once allowSceneActivation allows it; unloads integrate as soon as they
    /// head the queue. The
    /// pass goes on to the next request while its rounded elapsed time is below the
    /// backgroundLoadingPriority limit, measured on the main thread's clock like Unity's.</item>
    /// <item>LoadScene requests drain their prefix in the next frame, outside the time limit.</item>
    /// </list>
    /// Other releases retain the previous one-pass timing (<see cref="Compat.SceneAsyncPreparationMeasured"/>).
    /// </summary>
    internal sealed class SceneOperationQueue
    {
        // AsyncOperation.allowSceneActivation documents this pending progress plateau.
        const float ReadyProgress = .9f;

        internal enum Kind { Load, Unload, UnloadUnused }

        sealed class Request
        {
            internal AsyncOperation Operation;
            internal SceneData Scene;
            internal Action Work, PrepareWork;
            internal int Frame, ForcedFrame, ArmedFrame;
            internal Kind Kind;
            internal bool Started, Prepared, Armed, Forced;
            internal bool Load => Kind == Kind.Load;
            // The loader does not take further requests before this one has been integrated.
            internal bool BlocksLoader => Kind == Kind.UnloadUnused;
        }

        // Taken requests come first, in the order the loader took them; untaken ones follow in
        // request order.
        readonly List<Request> m_Requests = new List<Request>();
        readonly List<Request> m_Preparation = new List<Request>();
        // Milliseconds on a monotonic clock: the main thread's own by default; tests drive it.
        readonly Func<double> m_Clock;
        uint m_Generation;
        Request m_Current;
        bool m_Processing, m_Preparing;

        internal SceneOperationQueue(Func<double> clockMilliseconds = null) =>
            m_Clock = clockMilliseconds ?? (() => Stopwatch.GetTimestamp() * MillisecondsPerTick);

        static readonly double MillisecondsPerTick = 1000.0 / Stopwatch.Frequency;

        internal bool IsUnloading
        {
            get
            {
                if (m_Current != null && m_Current.Kind == Kind.Unload && m_Current.Scene.IsLoaded) return true;
                foreach (var request in m_Requests)
                    if (request.Kind == Kind.Unload && request.Scene.IsLoaded) return true;
                return false;
            }
        }

        internal void Enqueue(AsyncOperation operation, SceneData scene, Action work, Kind kind, bool async, int frame, Action prepare = null)
        {
            bool load = kind == Kind.Load;
            AsyncOperation.Bind(operation, AsyncOperation.OperationCapabilities.Priority
                | (load ? AsyncOperation.OperationCapabilities.SceneActivation : AsyncOperation.OperationCapabilities.None));
            bool forced = load && !async;
            // The LoadScene API forces earlier asynchronous work, including held activations,
            // to complete at its next-frame load. Keep allowSceneActivation's public value.
            if (forced)
                foreach (var pending in m_Requests)
                {
                    if (pending.Forced) continue;
                    pending.Forced = true;
                    pending.ForcedFrame = frame + 1;
                }
            m_Requests.Add(new Request { Operation = operation, Scene = scene, Work = work, Kind = kind,
                Frame = frame, Forced = forced, ForcedFrame = frame + 1, PrepareWork = prepare, Started = LoaderIdle() });
        }

        /// <summary>A request behind a LoadScene request is forced with it.</summary>
        internal void EnqueueAfterLast(AsyncOperation operation, Action work, Kind kind, int frame)
        {
            var last = m_Requests.Count > 0 ? m_Requests[m_Requests.Count - 1] : null;
            Enqueue(operation, null, work, kind, async: true, frame);
            if (last != null && last.Forced)
            {
                var added = m_Requests[m_Requests.Count - 1];
                added.Forced = true;
                added.ForcedFrame = last.ForcedFrame;
            }
        }

        bool LoaderIdle()
        {
            foreach (var request in m_Requests)
                if (!request.Started || !request.Prepared || request.BlocksLoader) return false;
            return true;
        }

        /// <summary>The loader's work between frames: it prepares every request made before this
        /// frame that it can take.</summary>
        internal void Prepare(int frame)
        {
            if (!Compat.SceneAsyncPreparationMeasured || m_Preparing) return;
            m_Preparing = true;
            uint generation = m_Generation;
            try
            {
                foreach (var request in m_Requests) m_Preparation.Add(request);
                for (int i = 0; i < m_Preparation.Count && generation == m_Generation; i++)
                {
                    var request = m_Preparation[i];
                    if (request.Started && request.Prepared && request.BlocksLoader) return;
                    if (request.Started && !request.Prepared)
                    {
                        if (frame <= request.Frame) return;
                        Prepare(request);
                        if (request.BlocksLoader) return;
                    }
                }
                while (generation == m_Generation)
                {
                    var next = Take(frame);
                    if (next == null) return;
                    Prepare(next);
                    if (next.BlocksLoader) return;
                }
            }
            finally { m_Preparation.Clear(); m_Preparing = false; }
        }

        /// <summary>The untaken request made before this frame with the highest priority, the
        /// earliest among equals, moved behind the taken ones. Nothing is taken past an untaken
        /// LoadScene request (or work forced with one): it keeps its place, so its next-frame
        /// load is not delayed by later asynchronous requests.</summary>
        Request Take(int frame)
        {
            int first = -1, best = -1;
            for (int i = 0; i < m_Requests.Count; i++)
            {
                var request = m_Requests[i];
                if (request.Started) continue;
                if (first < 0) first = i;
                if (request.Forced)
                {
                    if (best < 0 && request.Frame < frame) best = i;
                    break;
                }
                if (request.Frame < frame && (best < 0 || request.Operation.priority > m_Requests[best].Operation.priority)) best = i;
            }
            if (best < 0) return null;
            var taken = m_Requests[best];
            taken.Started = true;
            if (best != first)
            {
                m_Requests.RemoveAt(best);
                m_Requests.Insert(first, taken);
            }
            return taken;
        }

        static void Prepare(Request request)
        {
            request.Started = true;
            request.Prepared = true;
            if (!request.Load) return;
            try { request.PrepareWork?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            request.Operation.m_Progress = ReadyProgress;
        }

        internal void Process(int frame)
        {
            if (m_Processing) return;
            m_Processing = true;
            try
            {
                if (Compat.SceneAsyncPreparationMeasured) ProcessMeasured(frame);
                else ProcessOnePass(frame);
            }
            finally { m_Processing = false; }
        }

        void ProcessMeasured(int frame)
        {
            int limit = LoadingLimitMilliseconds(Application.backgroundLoadingPriority);
            double start = m_Clock();
            // Work queued inside callbacks waits for a later pass, including reentry.
            int count = m_Requests.Count;
            uint generation = m_Generation;
            while (count-- > 0 && m_Requests.Count != 0 && generation == m_Generation)
            {
                var request = m_Requests[0];
                if (request.Forced)
                {
                    if (frame < request.ForcedFrame) break;
                    Integrate(request);
                    continue;
                }
                if (request.Load)
                {
                    if (!request.Prepared) break;
                    if (!request.Armed)
                    {
                        request.Armed = true;
                        request.ArmedFrame = frame;
                        break;
                    }
                    if (frame <= request.ArmedFrame || !request.Operation.allowSceneActivation) break;
                }
                Integrate(request);
                // Rounded to whole milliseconds, as the limit is.
                if ((long)(m_Clock() - start + .5) >= limit) break;
            }
        }

        void ProcessOnePass(int frame)
        {
            int count = m_Requests.Count;
            uint generation = m_Generation;
            while (count-- > 0 && m_Requests.Count != 0 && generation == m_Generation)
            {
                var request = m_Requests[0];
                if (request.Forced && frame < request.ForcedFrame) break;
                if (!request.Forced && request.Load && !request.Operation.allowSceneActivation)
                {
                    request.Operation.m_Progress = ReadyProgress;
                    break;
                }
                Integrate(request);
            }
        }

        void Integrate(Request request)
        {
            m_Requests.RemoveAt(0);
            m_Current = request;
            try
            {
                if (request.Load && !request.Prepared) Prepare(request);
                request.Work();
            }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                try { request.Operation.Complete(); }
                finally { m_Current = null; }
            }
        }

        /// <summary>The main-thread time asynchronous integration may use per frame (Scripting API
        /// 6000.0, Application.backgroundLoadingPriority: Low 2 ms, BelowNormal 4 ms, Normal 10 ms,
        /// High 50 ms); a value ThreadPriority does not name gets the BelowNormal limit.</summary>
        internal static int LoadingLimitMilliseconds(ThreadPriority priority) => priority switch
        {
            ThreadPriority.Low => 2,
            ThreadPriority.BelowNormal => 4,
            ThreadPriority.Normal => 10,
            ThreadPriority.High => 50,
            _ => 4,
        };

        internal void Clear()
        {
            m_Requests.Clear();
            m_Generation++;
        }
    }
}
