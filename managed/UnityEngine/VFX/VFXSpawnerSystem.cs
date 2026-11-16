using System;
using System.Collections.Generic;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>One system of an effect: a spawner or a particle system, in the asset's order.</summary>
    internal abstract class VFXSystem
    {
        internal readonly VisualEffect Effect;
        /// <summary>The system's index in the asset (events name systems by it).</summary>
        internal readonly int Index;
        internal readonly VFXEditorSystemDesc Desc;

        protected VFXSystem(VisualEffect effect, int index)
        {
            Effect = effect;
            Index = index;
            Desc = effect.Data.Systems[index];
        }

        /// <summary>Back to the state of a new effect (a reinitialisation).</summary>
        internal abstract void Reset();

        /// <summary>One step of the effect.</summary>
        internal abstract void Update(VisualEffectState state);

        /// <summary>Releases what the system holds on the GPU.</summary>
        internal virtual void Release() { }

        /// <summary>The index of the mapping of a name in a list, -1 without one.</summary>
        internal static int Find(VFXMapping[] mappings, string name)
        {
            if (mappings != null)
                for (int i = 0; i < mappings.Length; i++)
                    if (mappings[i].name == name) return i;
            return -1;
        }

        /// <summary>The value a list maps a name to, -1 without one.</summary>
        internal static int Value(VFXMapping[] mappings, string name)
        {
            int at = Find(mappings, name);
            return at < 0 ? -1 : mappings[at].index;
        }
    }

    /// <summary>
    /// A spawn system: its loop state and the blocks that add to its spawn count each step.
    /// What the step counted stays in its event attributes for the particle systems it feeds,
    /// which take the whole particles; the fraction left carries to the next step unless a new
    /// loop begins. The rules match the Unity 6000.0.71f1 Editor (tests/projects/vfx).
    /// </summary>
    internal sealed class VFXSpawnerSystem : VFXSystem
    {
        internal readonly VFXSpawnerState State;
        readonly Task[] m_Tasks;
        /// <summary>The expressions evaluated for this spawner before its blocks run (the ones
        /// that read its state or draw a random number per spawn): ranges of expression indices.</summary>
        readonly int[] m_Ranges;
        readonly int m_LoopDuration, m_LoopCount, m_DelayBeforeLoop, m_DelayAfterLoop;
        /// <summary>The spawners whose spawn count starts or stops this one.</summary>
        VFXSpawnerSystem[] m_StartedBy = Array.Empty<VFXSpawnerSystem>(), m_StoppedBy = Array.Empty<VFXSpawnerSystem>();
        /// <summary>The spawn count and new-loop flag of the last step, for readers of the state.</summary>
        float m_LastSpawnCount;
        bool m_LastNewLoop;

        /// <summary>The names the compiler gives a spawn system's buffers and values.</summary>
        const string OutputBuffer = "spawner_output", StartInput = "spawner_input_OnPlay", StopInput = "spawner_input_OnStop";
        const string FirstRange = "start_a", Enabled = "_vfx_enabled";

        internal VFXSpawnerSystem(VisualEffect effect, int index) : base(effect, index)
        {
            State = new VFXSpawnerState(VFXEventAttribute.ForSpawner(effect.visualEffectAsset));
            m_LoopDuration = Value(Desc.values, "LoopDuration");
            m_LoopCount = Value(Desc.values, "LoopCount");
            m_DelayBeforeLoop = Value(Desc.values, "DelayBeforeLoop");
            m_DelayAfterLoop = Value(Desc.values, "DelayAfterLoop");
            m_Ranges = Ranges(Desc.values, Find(Desc.values, FirstRange));
            var tasks = new List<Task>();
            var processors = index < effect.Data.Processors.Length ? effect.Data.Processors[index] : Array.Empty<Object>();
            for (int i = 0; i < (Desc.tasks?.Length ?? 0); i++)
            {
                var task = Task.Create(this, Desc.tasks[i], i < processors.Length ? processors[i] : null);
                if (task != null) tasks.Add(task);
            }
            m_Tasks = tasks.ToArray();
            State.ResetForSystem();
        }

        /// <summary>The index of the CPU buffer the system writes its event attributes to.</summary>
        internal int OutputBufferIndex => Value(Desc.buffers, OutputBuffer);

        /// <summary>Links the spawners that start and stop this one, once all systems exist.</summary>
        internal void Link(IReadOnlyList<VFXSystem> systems)
        {
            m_StartedBy = Linked(systems, StartInput);
            m_StoppedBy = Linked(systems, StopInput);
        }

        VFXSpawnerSystem[] Linked(IReadOnlyList<VFXSystem> systems, string name) => Feeding(Desc.buffers, name, systems);

        /// <summary>The spawn systems whose output buffer a system's buffer mappings of a name
        /// point at, in the order of the mappings.</summary>
        internal static VFXSpawnerSystem[] Feeding(VFXMapping[] buffers, string name, IReadOnlyList<VFXSystem> systems)
        {
            List<VFXSpawnerSystem> linked = null;
            foreach (var mapping in buffers ?? Array.Empty<VFXMapping>())
            {
                if (mapping.name != name) continue;
                foreach (var system in systems)
                    if (system is VFXSpawnerSystem spawner && spawner.OutputBufferIndex == mapping.index)
                        (linked ??= new List<VFXSpawnerSystem>()).Add(spawner);
            }
            return linked?.ToArray() ?? Array.Empty<VFXSpawnerSystem>();
        }

        /// <summary>The pairs of mappings from <paramref name="first"/> on: each a range of
        /// expressions (its start, its end).</summary>
        internal static int[] Ranges(VFXMapping[] mappings, int first)
        {
            if (first < 0 || mappings == null) return Array.Empty<int>();
            var ranges = new int[(mappings.Length - first) / 2 * 2];
            for (int i = 0; i < ranges.Length; i++) ranges[i] = mappings[first + i].index;
            return ranges;
        }

        // What the expressions of a spawner read of its state.
        internal VFXEventAttribute EventAttribute => State.Attribute;
        internal bool NewLoop => State.NewLoop;
        internal VFXSpawnerLoopState LoopState => State.loopState;
        internal float SpawnCount => State.Attribute.SpawnCount;
        internal float DeltaTime => State.deltaTime;
        internal float TotalTime => State.totalTime;
        internal float DelayBeforeLoop => State.delayBeforeLoop;
        internal float LoopDuration => State.loopDuration;
        internal float DelayAfterLoop => State.delayAfterLoop;
        internal int LoopIndex => State.loopIndex;
        internal int LoopCount => State.loopCount;

        internal override void Reset()
        {
            var attribute = State.Attribute;
            var initial = VFXEventAttribute.ForSpawner(Effect.visualEffectAsset).Words;
            Array.Copy(initial, attribute.Words, Math.Min(initial.Length, attribute.Words.Length));
            State.ResetForSystem();
            m_LastSpawnCount = 0f;
            m_LastNewLoop = false;
            foreach (var task in m_Tasks) task.Reset();
        }

        void EvaluateOwnExpressions(VisualEffectState state)
        {
            for (int i = 0; i + 1 < m_Ranges.Length; i += 2)
                VFXExpressionEvaluator.Evaluate(Effect.Data, Effect.Values, m_Ranges[i], m_Ranges[i + 1], state, this);
        }

        /// <summary>The loop settings of the spawn context: a duration it does not give never
        /// ends, a count it does not give loops forever, a delay it does not give is none.</summary>
        VFXSpawnerState.Settings CurrentSettings()
        {
            var values = Effect.Values;
            return new VFXSpawnerState.Settings
            {
                LoopDuration = m_LoopDuration < 0 ? -1f : values.GetFloat(m_LoopDuration),
                LoopCount = m_LoopCount < 0 ? -1 : unchecked((int)values.GetWord(m_LoopCount)),
                DelayBeforeLoop = m_DelayBeforeLoop < 0 ? 0f : values.GetFloat(m_DelayBeforeLoop),
                DelayAfterLoop = m_DelayAfterLoop < 0 ? 0f : values.GetFloat(m_DelayAfterLoop),
            };
        }

        /// <summary>A start: the event's attributes become the spawner's, its expressions are
        /// evaluated, the loop begins and each block is told.</summary>
        internal void OnPlay(uint[] payload, VisualEffectState state)
        {
            State.Prepared = true;
            CopyPayload(payload);
            EvaluateOwnExpressions(state);
            State.OnPlay(CurrentSettings());
            foreach (var task in m_Tasks) task.Play(state);
        }

        internal void OnStop(uint[] payload, VisualEffectState state)
        {
            EvaluateOwnExpressions(state);
            CopyPayload(payload);
            State.OnStop();
            foreach (var task in m_Tasks) task.Stop(state);
        }

        void CopyPayload(uint[] payload)
        {
            var words = State.Attribute.Words;
            if (!ReferenceEquals(payload, words)) Array.Copy(payload, words, Math.Min(payload.Length, words.Length));
        }

        /// <summary>Whether a spawner asked for at least one particle this step: what starts or
        /// stops the spawners linked to it.</summary>
        internal bool Fired => State.Attribute.SpawnCount >= 1f;

        internal override void Update(VisualEffectState state)
        {
            var attribute = State.Attribute;
            foreach (var starter in m_StartedBy)
            {
                if (!starter.Fired) continue;
                float count = attribute.SpawnCount;
                OnPlay(starter.State.Attribute.Words, state);
                attribute.SpawnCount = count;
            }

            // The whole particles of the last step were taken; the fraction carries over, unless
            // a new loop begins.
            float carried = attribute.SpawnCount;
            carried -= (int)carried;
            if (State.NewLoop) carried = 0f;
            attribute.SpawnCount = 0f;

            EvaluateOwnExpressions(state);
            State.BeginUpdate(state.DeltaTime, CurrentSettings());
            foreach (var task in m_Tasks) task.Update(state);
            m_LastSpawnCount = attribute.SpawnCount;
            m_LastNewLoop = State.NewLoop;
            State.EndUpdate();
            attribute.SpawnCount += carried;

            foreach (var stopper in m_StoppedBy)
            {
                if (!stopper.Fired) continue;
                float count = attribute.SpawnCount;
                OnStop(stopper.State.Attribute.Words, state);
                attribute.SpawnCount = count;
            }
        }

        /// <summary>Fills a reader's state with the system's, as of its last step.</summary>
        internal void CopyStateTo(VFXSpawnerState target) => target.CopyFrom(State, m_LastSpawnCount, m_LastNewLoop);

        // -------------------------------------------------------------------------- the blocks

        /// <summary>A block of a spawn context.</summary>
        abstract class Task
        {
            protected VFXSpawnerSystem System;
            protected VisualEffectValues Values => System.Effect.Values;
            /// <summary>The expression that enables the block, -1 for one always enabled.</summary>
            protected int EnabledExpression = -1;

            internal static Task Create(VFXSpawnerSystem system, in VFXEditorTaskDesc desc, Object processor)
            {
                Task task;
                switch (desc.type)
                {
                    case VFXTaskType.ConstantRateSpawner: task = new ConstantRate(Value(desc.values, "Rate")); break;
                    case VFXTaskType.BurstSpawner: task = new Burst(Value(desc.values, "Count"), Value(desc.values, "Delay")); break;
                    case VFXTaskType.PeriodicBurstSpawner: task = new PeriodicBurst(Value(desc.values, "nb"), Value(desc.values, "period")); break;
                    case VFXTaskType.VariableRateSpawner: task = new VariableRate(Value(desc.values, "nb"), Value(desc.values, "period")); break;
                    case VFXTaskType.SetAttributeSpawner: task = new SetAttribute(system, desc.values); break;
                    case VFXTaskType.EvaluateExpressionsSpawner: task = new EvaluateExpressions(Ranges(desc.values, 0)); break;
                    case VFXTaskType.CustomCallbackSpawner: task = new CustomCallback(system, desc.values, processor); break;
                    default: return null;
                }
                task.System = system;
                task.EnabledExpression = Value(desc.values, Enabled);
                return task;
            }

            protected bool IsEnabled => EnabledExpression < 0 || (Values.GetWord(EnabledExpression) & 0xff) != 0;

            internal virtual void Reset() { }
            internal virtual void Play(VisualEffectState state) { }
            internal virtual void Stop(VisualEffectState state) { }
            internal abstract void Update(VisualEffectState state);

            /// <summary>A number between the two components of a range value, from the effect's
            /// random sequence.</summary>
            protected float Draw(int rangeExpression, VisualEffectState state)
            {
                // A block compiled without the input draws nothing.
                if (rangeExpression < 0) return 0f;
                var range = Values.GetVector(rangeExpression, 2);
                return (range.y - range.x) * state.Random.NextValue() + range.x;
            }
        }

        /// <summary>A block Unity implements itself: restarted when a loop begins, and running
        /// while the spawner loops and the block is enabled.</summary>
        abstract class BuiltIn : Task
        {
            internal sealed override void Update(VisualEffectState state)
            {
                var spawner = System.State;
                if (spawner.NewLoop) Restart(state);
                if (spawner.loopState == VFXSpawnerLoopState.Looping && IsEnabled) Run(state);
            }

            protected virtual void Restart(VisualEffectState state) { }
            protected abstract void Run(VisualEffectState state);

            protected void Add(float count) => System.State.Attribute.SpawnCount += count;
        }

        sealed class ConstantRate : BuiltIn
        {
            readonly int m_Rate;

            internal ConstantRate(int rate) => m_Rate = rate;

            protected override void Run(VisualEffectState state)
            {
                if (m_Rate >= 0) Add(Values.GetFloat(m_Rate) * System.State.deltaTime);
            }
        }

        /// <summary>A count spawned once per loop, after a delay.</summary>
        sealed class Burst : BuiltIn
        {
            readonly int m_Count, m_Delay;
            bool m_Done;
            float m_After;

            internal Burst(int count, int delay)
            {
                m_Count = count;
                m_Delay = delay;
            }

            internal override void Reset() => m_Done = false;

            protected override void Restart(VisualEffectState state)
            {
                m_Done = false;
                m_After = Draw(m_Delay, state);
            }

            protected override void Run(VisualEffectState state)
            {
                if (m_Done || !(m_After <= System.State.totalTime)) return;
                m_Done = true;
                Add(Draw(m_Count, state));
            }
        }

        /// <summary>A count spawned again and again, a period apart.</summary>
        sealed class PeriodicBurst : BuiltIn
        {
            readonly int m_Count, m_Period;
            float m_Next;

            internal PeriodicBurst(int count, int period)
            {
                m_Count = count;
                m_Period = period;
            }

            internal override void Reset() => m_Next = 0f;

            protected override void Restart(VisualEffectState state) => m_Next = Draw(m_Period, state);

            protected override void Run(VisualEffectState state)
            {
                float at = m_Next;
                if (!(at <= System.State.totalTime)) return;
                m_Next = at + Draw(m_Period, state);
                Add(Draw(m_Count, state));
            }
        }

        /// <summary>A rate that moves from one drawn value to the next over a drawn period.</summary>
        sealed class VariableRate : BuiltIn
        {
            readonly int m_Rate, m_Period;
            float m_From, m_To, m_Since, m_Until;

            internal VariableRate(int rate, int period)
            {
                m_Rate = rate;
                m_Period = period;
            }

            internal override void Reset() => m_From = m_To = m_Since = m_Until = 0f;

            void Roll(float now, VisualEffectState state)
            {
                m_From = m_To;
                m_Since = now;
                m_To = Draw(m_Rate, state);
                m_Until = Draw(m_Period, state) + now;
            }

            /// <summary>Two draws: the first rate is the one the loop starts from.</summary>
            protected override void Restart(VisualEffectState state)
            {
                Roll(0f, state);
                Roll(0f, state);
            }

            protected override void Run(VisualEffectState state)
            {
                var spawner = System.State;
                float period = m_Until - m_Since;
                if (period > 0f)
                {
                    float t = (spawner.totalTime - m_Since) / period;
                    t = 0f > t ? 0f : (1f < t ? 1f : t);
                    if (t != 1f)
                    {
                        Add(((m_To - m_From) * t + m_From) * spawner.deltaTime);
                        return;
                    }
                }
                // The period passed: the next begins at the rate reached.
                Roll(spawner.totalTime, state);
                Add(spawner.deltaTime * m_From);
            }
        }

        /// <summary>Writes a value into one of the spawner's event attributes.</summary>
        sealed class SetAttribute : Task
        {
            readonly int m_Expression = -1, m_Offset = -1, m_Words;

            internal SetAttribute(VFXSpawnerSystem system, VFXMapping[] values)
            {
                var data = system.Effect.Data;
                var layout = data.CpuBuffers.Length > 0 ? data.CpuBuffers[0].layout : null;
                foreach (var mapping in values ?? Array.Empty<VFXMapping>())
                {
                    if (mapping.name == Enabled || layout == null) continue;
                    foreach (var element in layout)
                    {
                        if (element.name != mapping.name) continue;
                        m_Expression = mapping.index;
                        m_Offset = (int)(element.offset.bucket + element.offset.element);
                        m_Words = VFXExpressionHelper.GetSizeOfType(element.type);
                    }
                }
            }

            internal override void Update(VisualEffectState state)
            {
                if (m_Offset < 0 || !IsEnabled) return;
                Array.Copy(Values.Words, System.Effect.Data.ValueOffsets[m_Expression], System.State.Attribute.Words, m_Offset, m_Words);
            }
        }

        /// <summary>Evaluates the expressions the next block reads that depend on the spawner.</summary>
        sealed class EvaluateExpressions : Task
        {
            readonly int[] m_Ranges;

            internal EvaluateExpressions(int[] ranges) => m_Ranges = ranges;

            internal override void Update(VisualEffectState state)
            {
                for (int i = 0; i + 1 < m_Ranges.Length; i += 2)
                    VFXExpressionEvaluator.Evaluate(System.Effect.Data, Values, m_Ranges[i], m_Ranges[i + 1], state, System);
            }
        }

        /// <summary>A block written as a script: a <see cref="VFXSpawnerCallbacks"/> object
        /// called on play, on each step and on stop with the spawner's state and the block's
        /// inputs.</summary>
        sealed class CustomCallback : Task
        {
            readonly VFXSpawnerCallbacks m_Callbacks;
            readonly VFXExpressionValues m_Inputs;

            internal CustomCallback(VFXSpawnerSystem system, VFXMapping[] values, Object processor)
            {
                var type = (processor as UnityEditor.MonoScript)?.GetClass();
                if (type != null && typeof(VFXSpawnerCallbacks).IsAssignableFrom(type) && !type.IsAbstract)
                    m_Callbacks = (VFXSpawnerCallbacks)ScriptableObject.CreateInstance(type);
                else
                    Debug.LogError($"Unbound: VisualEffect '{system.Effect.name}': the script of a custom spawner block of '{system.Desc.name}' is not a VFXSpawnerCallbacks class; the block does nothing", system.Effect);
                m_Inputs = new VFXExpressionValues(system.Effect, values);
            }

            internal override void Play(VisualEffectState state)
            {
                if (m_Callbacks != null) m_Callbacks.OnPlay(System.State, m_Inputs, System.Effect);
            }

            internal override void Update(VisualEffectState state)
            {
                if (m_Callbacks != null && IsEnabled) m_Callbacks.OnUpdate(System.State, m_Inputs, System.Effect);
            }

            internal override void Stop(VisualEffectState state)
            {
                if (m_Callbacks != null) m_Callbacks.OnStop(System.State, m_Inputs, System.Effect);
            }
        }
    }
}
