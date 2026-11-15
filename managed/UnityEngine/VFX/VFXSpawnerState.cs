using System;

namespace UnityEngine.VFX
{
    /// <summary>A spawner block written as a script: called when its spawn context plays, on
    /// each step and when it stops, with the context's state and the block's inputs.</summary>
    [Serializable]
    public abstract partial class VFXSpawnerCallbacks : ScriptableObject
    {
        protected VFXSpawnerCallbacks() { }

        public abstract void OnPlay(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent);
        public abstract void OnUpdate(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent);
        public abstract void OnStop(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent);
    }

    /// <summary>
    /// The state of a spawn system: where it is in its loop (delaying, looping, finished), the
    /// loop's durations and count, the time in the current stage, the step's time and the event
    /// attributes it spawns with (whose spawn count is how many particles the step asks for).
    /// A spawn system has one; <see cref="VisualEffect.GetSpawnSystemInfo(int, VFXSpawnerState)"/>
    /// fills a copy.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public sealed partial class VFXSpawnerState : IDisposable
    {
        VFXSpawnerLoopState m_LoopState;
        bool m_NewLoop;
        /// <summary>Whether the loop's durations are read again when the next step begins (a
        /// loop that started over).</summary>
        internal bool ReloadDurations;
        /// <summary>Whether a start event's attributes were taken for the play that follows.</summary>
        internal bool Prepared;
        // A new state loops without end: no duration, no count.
        float m_DelayBeforeLoop, m_LoopDuration = -1f, m_DelayAfterLoop, m_TotalTime, m_DeltaTime;
        int m_LoopIndex, m_LoopCount = -1;
        VFXEventAttribute m_Attribute;
        bool m_Disposed;

        public VFXSpawnerState() { }

        /// <summary>The state of a spawn system, spawning with the given attributes.</summary>
        internal VFXSpawnerState(VFXEventAttribute attribute) => m_Attribute = attribute;

        public void Dispose()
        {
            m_Disposed = true;
            m_Attribute = null;
        }

        /// <summary>A disposed state has nothing behind it, as a released native object.</summary>
        void Check()
        {
            if (m_Disposed) throw new NullReferenceException();
        }

        public bool playing
        {
            get => loopState == VFXSpawnerLoopState.Looping;
            set => loopState = value ? VFXSpawnerLoopState.Looping : VFXSpawnerLoopState.Finished;
        }

        public bool newLoop
        {
            get { Check(); return m_NewLoop; }
        }

        /// <summary>The stage of the loop. Setting another stage starts it at time zero, and
        /// stages of no duration are passed at once.</summary>
        public VFXSpawnerLoopState loopState
        {
            get { Check(); return m_LoopState; }
            set
            {
                Check();
                if (m_LoopState == value) return;
                m_TotalTime = 0f;
                m_LoopState = value;
                SkipEmptyStages();
            }
        }

        public float spawnCount
        {
            get { Check(); return m_Attribute == null ? 0f : m_Attribute.SpawnCount; }
            set { Check(); if (m_Attribute != null) m_Attribute.SpawnCount = value; }
        }

        public float deltaTime
        {
            get { Check(); return m_DeltaTime; }
            set { Check(); m_DeltaTime = value; }
        }

        public float totalTime
        {
            get { Check(); return m_TotalTime; }
            set { Check(); m_TotalTime = value; }
        }

        public float delayBeforeLoop
        {
            get { Check(); return m_DelayBeforeLoop; }
            set { Check(); m_DelayBeforeLoop = value; }
        }

        public float loopDuration
        {
            get { Check(); return m_LoopDuration; }
            set { Check(); m_LoopDuration = value; }
        }

        public float delayAfterLoop
        {
            get { Check(); return m_DelayAfterLoop; }
            set { Check(); m_DelayAfterLoop = value; }
        }

        public int loopIndex
        {
            get { Check(); return m_LoopIndex; }
            set { Check(); m_LoopIndex = value; }
        }

        public int loopCount
        {
            get { Check(); return m_LoopCount; }
            set { Check(); m_LoopCount = value; }
        }

        public VFXEventAttribute vfxEventAttribute
        {
            get { Check(); return m_Attribute; }
        }

        internal VFXEventAttribute Attribute => m_Attribute;

        internal bool NewLoop => m_NewLoop;

        /// <summary>The state a spawn system starts from: finished, with no durations and no
        /// count, and marked as a new loop whose durations are still to be read (so its blocks
        /// restart on the first step, played or not).</summary>
        internal void ResetForSystem()
        {
            m_LoopState = VFXSpawnerLoopState.Finished;
            m_NewLoop = true;
            ReloadDurations = true;
            Prepared = false;
            m_DelayBeforeLoop = m_LoopDuration = m_DelayAfterLoop = m_TotalTime = m_DeltaTime = 0f;
            m_LoopIndex = m_LoopCount = 0;
        }

        /// <summary>The durations and count a spawn context gives its loop; a negative duration
        /// never ends, a negative count loops forever.</summary>
        internal struct Settings
        {
            internal int LoopCount;
            internal float LoopDuration, DelayBeforeLoop, DelayAfterLoop;
        }

        /// <summary>The duration of the current stage; none when finished.</summary>
        bool StageDuration(out float duration)
        {
            switch (m_LoopState)
            {
                case VFXSpawnerLoopState.DelayingBeforeLoop: duration = m_DelayBeforeLoop; return true;
                case VFXSpawnerLoopState.Looping: duration = m_LoopDuration; return true;
                case VFXSpawnerLoopState.DelayingAfterLoop: duration = m_DelayAfterLoop; return true;
                default: duration = 0f; return false;
            }
        }

        /// <summary>The stage after the current one. After the last delay the loop starts over
        /// (a new loop, whose durations are read again) until its count is reached.</summary>
        void NextStage()
        {
            switch (m_LoopState)
            {
                case VFXSpawnerLoopState.DelayingBeforeLoop:
                    m_LoopState = VFXSpawnerLoopState.Looping;
                    break;
                case VFXSpawnerLoopState.Looping:
                    m_LoopState = VFXSpawnerLoopState.DelayingAfterLoop;
                    break;
                case VFXSpawnerLoopState.DelayingAfterLoop:
                    m_LoopIndex++;
                    if (m_LoopCount < 0 || m_LoopIndex < m_LoopCount)
                    {
                        m_LoopState = VFXSpawnerLoopState.DelayingBeforeLoop;
                        ReloadDurations = true;
                        m_NewLoop = true;
                    }
                    else m_LoopState = VFXSpawnerLoopState.Finished;
                    break;
            }
        }

        /// <summary>Passes the stages that last no time. A new loop that neither loops nor delays
        /// after stays where it is, or it would never end.</summary>
        void SkipEmptyStages()
        {
            while (StageDuration(out float duration) && duration == 0f
                && !(m_NewLoop && m_DelayAfterLoop == 0f && m_LoopDuration == 0f))
                NextStage();
        }

        /// <summary>A step begins: a loop that started over takes its durations.</summary>
        internal void BeginUpdate(float deltaTime, in Settings settings)
        {
            if (ReloadDurations)
            {
                ReloadDurations = false;
                m_DelayBeforeLoop = settings.DelayBeforeLoop;
                m_LoopDuration = settings.LoopDuration;
                m_DelayAfterLoop = settings.DelayAfterLoop;
            }
            m_DeltaTime = deltaTime;
        }

        /// <summary>A step ends: the stage's time advances, and a stage whose duration passed
        /// gives way to the next (a negative duration never passes).</summary>
        internal void EndUpdate()
        {
            m_NewLoop = false;
            m_TotalTime += m_DeltaTime;
            if (!StageDuration(out float duration) || duration < 0f || m_TotalTime < duration) return;
            m_TotalTime = 0f;
            NextStage();
            SkipEmptyStages();
        }

        /// <summary>A start event: the loop begins again with the context's settings.</summary>
        internal void OnPlay(in Settings settings)
        {
            Prepared = false;
            m_LoopIndex = 0;
            ReloadDurations = false;
            m_NewLoop = true;
            m_DelayBeforeLoop = settings.DelayBeforeLoop;
            m_LoopDuration = settings.LoopDuration;
            m_DelayAfterLoop = settings.DelayAfterLoop;
            m_LoopCount = settings.LoopCount;
            // A state already delaying before its loop keeps its time.
            if (m_LoopState == VFXSpawnerLoopState.DelayingBeforeLoop) return;
            m_TotalTime = 0f;
            m_LoopState = VFXSpawnerLoopState.DelayingBeforeLoop;
            SkipEmptyStages();
        }

        /// <summary>A stop event: the loop is finished.</summary>
        internal void OnStop()
        {
            if (m_LoopState == VFXSpawnerLoopState.Finished) return;
            m_TotalTime = 0f;
            m_LoopState = VFXSpawnerLoopState.Finished;
        }

        /// <summary>Takes another state's loop (for a reader of a system's state): its stage,
        /// durations, counts and times and a copy of its attributes, with the spawn count and
        /// new-loop flag given (the ones of the system's last step).</summary>
        internal void CopyFrom(VFXSpawnerState source, float spawnCount, bool newLoop)
        {
            m_LoopState = source.m_LoopState;
            ReloadDurations = source.ReloadDurations;
            m_LoopCount = source.m_LoopCount;
            m_LoopDuration = source.m_LoopDuration;
            m_DelayBeforeLoop = source.m_DelayBeforeLoop;
            m_DelayAfterLoop = source.m_DelayAfterLoop;
            m_LoopIndex = source.m_LoopIndex;
            m_TotalTime = source.m_TotalTime;
            m_DeltaTime = source.m_DeltaTime;
            if (source.m_Attribute != null)
            {
                m_Attribute = new VFXEventAttribute(source.m_Attribute);
                m_Attribute.SpawnCount = spawnCount;
            }
            m_NewLoop = newLoop;
        }
    }
}
