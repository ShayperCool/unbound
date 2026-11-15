using System;
using System.Collections.Generic;
using UnityEngine.Events;
using Unbound.Runtime;

namespace UnityEngine.Networking.PlayerConnection
{
    public partial interface IEditorPlayerConnection { }
    /// <summary>Player connection subscriptions while the game runs without an attached peer.</summary>
    [Serializable]
    public partial class PlayerConnection : ScriptableObject, IEditorPlayerConnection
    {
        sealed class Subscription
        {
            internal readonly UnityEvent<MessageEventArgs> Event = new UnityEvent<MessageEventArgs>();
            internal int Count;
        }
        readonly Dictionary<Guid, Subscription> m_Messages = new Dictionary<Guid, Subscription>();
        readonly UnityEvent<int> m_Connected = new UnityEvent<int>();
        readonly UnityEvent<int> m_Disconnected = new UnityEvent<int>();
        static PlayerConnection s_Instance;
        bool m_Initialized;
        public PlayerConnection() { }
        public static PlayerConnection instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = CreateInstance<PlayerConnection>();
                    s_Instance.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_Instance;
            }
        }
        public bool isConnected { get { MainThreadCheck.Check("IsConnected"); return false; } }
        public void OnEnable()
        {
            if (m_Initialized) return;
            m_Initialized = true;
            MainThreadCheck.Check("Initialize");
        }
        static void Validate(Guid messageId)
        {
            if (messageId == Guid.Empty) throw new ArgumentException("Cant be Guid.Empty", nameof(messageId));
        }
        public void Register(Guid messageId, UnityAction<MessageEventArgs> callback)
        {
            Validate(messageId);
            if (!m_Messages.TryGetValue(messageId, out var entry))
            {
                MainThreadCheck.Check("RegisterInternal");
                m_Messages.Add(messageId, entry = new Subscription());
            }
            entry.Count++;
            entry.Event.AddListener(callback);
        }
        public void Unregister(Guid messageId, UnityAction<MessageEventArgs> callback)
        {
            if (m_Messages.TryGetValue(messageId, out var entry))
            {
                entry.Event.RemoveListener(callback);
                if (--entry.Count <= 0) m_Messages.Remove(messageId);
            }
            if (!m_Messages.ContainsKey(messageId)) MainThreadCheck.Check("UnregisterInternal");
        }
        public void RegisterConnection(UnityAction<int> callback) => m_Connected.AddListener(callback);
        public void RegisterDisconnection(UnityAction<int> callback) => m_Disconnected.AddListener(callback);
        public void UnregisterConnection(UnityAction<int> callback) => m_Connected.RemoveListener(callback);
        public void UnregisterDisconnection(UnityAction<int> callback) => m_Disconnected.RemoveListener(callback);
        public void Send(Guid messageId, byte[] data) { Validate(messageId); MainThreadCheck.Check("SendMessage"); }
        public bool TrySend(Guid messageId, byte[] data) { Validate(messageId); MainThreadCheck.Check("TrySendMessage"); return false; }
        public void DisconnectAll() => MainThreadCheck.Check("DisconnectAll");
    }
}
