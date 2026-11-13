using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine.SceneManagement
{
    /// <summary>Scene work shares a FIFO. Preparation can finish behind a held activation;
    /// integration and unload delivery remain in request order. Scene integrations use separate
    /// frames in the measured release; LoadScene requests drain their prefix next frame.</summary>
    internal sealed class SceneOperationQueue
    {
        // AsyncOperation.allowSceneActivation documents this pending progress plateau.
        const float ReadyProgress = .9f;
        sealed class Request
        {
            internal AsyncOperation Operation;
            internal SceneData Scene;
            internal Action Work, PrepareWork;
            internal int Frame, ForcedFrame, ReadyFrame;
            internal bool Prepared;
            internal bool Load, Forced;
        }

        readonly Queue<Request> m_Requests = new Queue<Request>();
        readonly List<Request> m_Preparation = new List<Request>();
        int m_LastIntegrationFrame = -1;
        uint m_Generation;
        Request m_Current;
        bool m_Processing, m_Preparing;

        internal bool IsUnloading
        {
            get
            {
                if (m_Current != null && !m_Current.Load && m_Current.Scene.IsLoaded) return true;
                foreach (var request in m_Requests)
                    if (!request.Load && request.Scene.IsLoaded) return true;
                return false;
            }
        }

        internal void Enqueue(AsyncOperation operation, SceneData scene, Action work, bool load, bool async, int frame, Action prepare = null)
        {
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
            m_Requests.Enqueue(new Request { Operation = operation, Scene = scene, Work = work,
                Load = load, Frame = frame, Forced = forced, ForcedFrame = frame + 1, PrepareWork = prepare });
        }

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
                    if (request.Load && !request.Prepared && frame > request.Frame) Prepare(request, frame);
                }
            }
            finally { m_Preparation.Clear(); m_Preparing = false; }
        }

        static void Prepare(Request request, int frame)
        {
            try { request.PrepareWork?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            request.Prepared = true;
            request.ReadyFrame = frame;
            request.Operation.m_Progress = ReadyProgress;
        }

        internal void Process(int frame)
        {
            if (m_Processing) return;
            m_Processing = true;
            try
            {
                int count = m_Requests.Count;
                uint generation = m_Generation;
                while (count-- > 0 && m_Requests.Count != 0 && generation == m_Generation)
                {
                    var request = m_Requests.Peek();
                    bool measured = Compat.SceneAsyncPreparationMeasured;
                    if (request.Forced && frame < request.ForcedFrame) break;
                    if (!request.Forced)
                    {
                        if (measured && request.Load && m_LastIntegrationFrame == frame) break;
                        if (request.Load)
                        {
                            if (measured && (!request.Prepared || frame <= request.ReadyFrame)) break;
                            if (!request.Operation.allowSceneActivation)
                            {
                                request.Operation.m_Progress = ReadyProgress;
                                break;
                            }
                        }
                    }
                    // Work queued inside callbacks waits for a later pass, including reentry.
                    m_Requests.Dequeue();
                    m_Current = request;
                    if (request.Load) m_LastIntegrationFrame = frame;
                    try
                    {
                        if (request.Load && !request.Prepared) Prepare(request, frame);
                        request.Work();
                    }
                    catch (Exception e) { Debug.LogException(e); }
                    finally
                    {
                        try { request.Operation.Complete(); }
                        finally { m_Current = null; }
                    }
                }
            }
            finally { m_Processing = false; }
        }

        internal void Clear()
        {
            m_Requests.Clear();
            m_Generation++;
            m_LastIntegrationFrame = -1;
        }
    }
}
