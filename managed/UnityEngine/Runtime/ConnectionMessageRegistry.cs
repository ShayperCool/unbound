using System;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.Networking.PlayerConnection;

namespace Unbound.Runtime
{
    // Shared message membership and listener counts for runtime and Editor connections.
    internal sealed class ConnectionMessageRegistry
    {
        sealed class Subscription
        {
            internal readonly UnityEvent<MessageEventArgs> Event = new UnityEvent<MessageEventArgs>();
            internal int Count;
        }
        readonly Dictionary<Guid, Subscription> m_Messages = new Dictionary<Guid, Subscription>();
        internal bool Contains(Guid id) => m_Messages.ContainsKey(id);
        internal void Register(Guid id, UnityAction<MessageEventArgs> callback, bool persistent)
        {
            if (!m_Messages.TryGetValue(id, out var entry)) m_Messages.Add(id, entry = new Subscription());
            entry.Count++;
            if (persistent) entry.Event.AddPersistentListener(callback, UnityEventCallState.EditorAndRuntime);
            else entry.Event.AddListener(callback);
        }
        internal void Unregister(Guid id, UnityAction<MessageEventArgs> callback)
        {
            if (!m_Messages.TryGetValue(id, out var entry)) return;
            entry.Event.RemoveListener(callback);
            if (--entry.Count <= 0) m_Messages.Remove(id);
        }
        internal void Invoke(Guid id, MessageEventArgs message)
        {
            if (m_Messages.TryGetValue(id, out var entry)) entry.Event.Invoke(message);
        }
    }
}
