using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking.PlayerConnection;

namespace UnityEditor.Networking.PlayerConnection
{
    /// <summary>Editor subscriptions and sends in the measured disconnected context.</summary>
    [Serializable]
    public partial class EditorConnection : ScriptableSingleton<EditorConnection>, IEditorPlayerConnection
    {
        readonly ConnectionMessageRegistry m_Messages = new ConnectionMessageRegistry();
        readonly UnityEvent<int> m_Connected = new UnityEvent<int>();
        readonly UnityEvent<int> m_Disconnected = new UnityEvent<int>();
        [SerializeField] List<ConnectedPlayer> m_connectedPlayers = new List<ConnectedPlayer>();
        public List<ConnectedPlayer> ConnectedPlayers => m_connectedPlayers;

        public EditorConnection() { }
        public void Initialize() => MainThreadCheck.Check("Initialize");
        public void Register(Guid messageId, UnityAction<MessageEventArgs> callback)
        {
            if (messageId == Guid.Empty) throw new ArgumentException("Cant be Guid.Empty", nameof(messageId));
            if (!m_Messages.Contains(messageId)) MainThreadCheck.Check("RegisterInternal");
            m_Messages.Register(messageId, callback, persistent: true);
        }
        public void Unregister(Guid messageId, UnityAction<MessageEventArgs> callback)
        {
            m_Messages.Unregister(messageId, callback);
            if (!m_Messages.Contains(messageId)) MainThreadCheck.Check("UnregisterInternal");
        }
        public void RegisterConnection(UnityAction<int> callback)
        {
            foreach (var player in m_connectedPlayers) callback(player.playerId);
            m_Connected.AddPersistentListener(callback, UnityEventCallState.EditorAndRuntime);
        }
        public void RegisterDisconnection(UnityAction<int> callback) => m_Disconnected.AddPersistentListener(callback, UnityEventCallState.EditorAndRuntime);
        public void UnregisterConnection(UnityAction<int> callback) => m_Connected.RemovePersistentListener(PersistentTarget(callback), callback.Method);
        public void UnregisterDisconnection(UnityAction<int> callback) => m_Disconnected.RemovePersistentListener(PersistentTarget(callback), callback.Method);
        static UnityEngine.Object PersistentTarget(Delegate callback)
        {
            object target = callback.Target;
            if (Compat.EditorManagedResourceErrors && target != null && !(target is UnityEngine.Object))
                throw new InvalidCastException("Specified cast is not valid.");
            return (UnityEngine.Object)target;
        }
        public void Send(Guid messageId, byte[] data) => Send(messageId, data, 0);
        public void Send(Guid messageId, byte[] data, int playerId)
        {
            ValidateSend(messageId);
            MainThreadCheck.Check("SendMessageInternal");
        }
        public bool TrySend(Guid messageId, byte[] data) => TrySend(messageId, data, 0);
        public bool TrySend(Guid messageId, byte[] data, int playerId)
        {
            ValidateSend(messageId);
            MainThreadCheck.Check("TrySendMessageInternal");
            return false;
        }
        static void ValidateSend(Guid messageId)
        {
            if (messageId == Guid.Empty) throw new ArgumentException("Can not be Guid.Empty", nameof(messageId));
        }
        public void DisconnectAll() => MainThreadCheck.Check("DisconnectAll");
        static void MessageCallbackInternal(IntPtr data, ulong size, ulong guid, string messageId)
        {
            byte[] bytes = null;
            if (size != 0)
            {
                bytes = new byte[checked((int)size)];
                Marshal.Copy(data, bytes, 0, bytes.Length);
            }
            instance.m_Messages.Invoke(new Guid(messageId), new MessageEventArgs { playerId = (int)guid, data = bytes });
        }
    }
}
namespace UnityEditor.Networking.PlayerConnection
{
    [Serializable]
    public partial class ConnectedPlayer
    {
        [SerializeField] int m_PlayerId;
        [SerializeField] string m_PlayerName;
        public ConnectedPlayer() { }
        public ConnectedPlayer(int playerId) { m_PlayerId = playerId; }
        public ConnectedPlayer(int playerId, string name) { m_PlayerId = playerId; m_PlayerName = name; }
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Use playerId instead (UnityUpgradable) -> playerId", true)]
        public int PlayerId => m_PlayerId;
        public int playerId => m_PlayerId;
        public string name => m_PlayerName;
    }
}
