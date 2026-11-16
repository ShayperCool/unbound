using System;
using System.Collections.Generic;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>
    /// An output event: in each step, each spawn context that feeds it and asked for at least
    /// one particle raises it once, in the order of its inputs (output event contexts of one
    /// name make one event fed by all of them); the effect hands the step's events to
    /// <see cref="VisualEffect.outputEventReceived"/> after all its systems updated, each with
    /// the attributes of the spawn context that raised it. Matches the Unity 6000.0.71f1 Editor
    /// (tests/projects/vfx, <c>events/*</c>).
    /// </summary>
    internal sealed class VFXOutputEventSystem : VFXSystem
    {
        const string Input = "spawner_input";

        readonly VFXSpawnerSystem[] m_Inputs;
        readonly int m_NameId;

        internal VFXOutputEventSystem(VisualEffect effect, int index, IReadOnlyList<VFXSystem> systems) : base(effect, index)
        {
            m_NameId = Shader.PropertyToID(Desc.name);
            var inputs = new List<VFXSpawnerSystem>();
            foreach (var mapping in Desc.buffers ?? Array.Empty<VFXMapping>())
            {
                if (mapping.name != Input) continue;
                foreach (var system in systems)
                    if (system is VFXSpawnerSystem spawner && spawner.OutputBufferIndex == mapping.index) inputs.Add(spawner);
            }
            m_Inputs = inputs.ToArray();
        }

        internal override void Reset() { }

        internal override void Update(VisualEffectState state)
        {
            foreach (var spawner in m_Inputs)
                if (spawner.Fired) Effect.RaiseOutputEvent(m_NameId, spawner.EventAttribute);
        }
    }
}
