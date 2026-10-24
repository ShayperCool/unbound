using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking.PlayerConnection;

namespace UnityEditor.SceneManagement
{
    /// <summary>An editing stage (the main scenes, prefab mode).</summary>
    public abstract partial class Stage : ScriptableObject
    {
    }

    public abstract partial class PreviewSceneStage : Stage
    {
    }

    /// <summary>Prefab mode. Unbound's Editor never opens it.</summary>
    public sealed partial class PrefabStage : PreviewSceneStage
    {
    }

    public static partial class PrefabStageUtility
    {
        public static PrefabStage GetCurrentPrefabStage() => null;

        public static PrefabStage GetPrefabStage(GameObject gameObject) => null;
    }
}

namespace UnityEditor.Networking.PlayerConnection
{
    /// <summary>The Editor's connection to players: none connect to Unbound's Editor (the game
    /// runs in it), so registered handlers are kept and never called.</summary>
    public partial class EditorConnection : ScriptableSingleton<EditorConnection>
    {
        readonly Dictionary<Guid, UnityAction<MessageEventArgs>> m_Handlers = new Dictionary<Guid, UnityAction<MessageEventArgs>>();
        readonly List<UnityAction<int>> m_Connected = new List<UnityAction<int>>();
        readonly List<UnityAction<int>> m_Disconnected = new List<UnityAction<int>>();

        public EditorConnection() { }

        public void Initialize() { }

        public void Register(Guid messageId, UnityAction<MessageEventArgs> callback)
        {
            if (messageId == Guid.Empty) throw new ArgumentException("Cant be Guid.Empty", nameof(messageId));
            m_Handlers.TryGetValue(messageId, out var existing);
            m_Handlers[messageId] = existing + callback;
        }

        public void Unregister(Guid messageId, UnityAction<MessageEventArgs> callback)
        {
            if (m_Handlers.TryGetValue(messageId, out var existing)) m_Handlers[messageId] = existing - callback;
        }

        public void RegisterConnection(UnityAction<int> callback) => m_Connected.Add(callback);

        public void RegisterDisconnection(UnityAction<int> callback) => m_Disconnected.Add(callback);

        public void UnregisterConnection(UnityAction<int> callback) => m_Connected.Remove(callback);

        public void UnregisterDisconnection(UnityAction<int> callback) => m_Disconnected.Remove(callback);

        public void Send(Guid messageId, byte[] data) { }

        public void Send(Guid messageId, byte[] data, int playerId) { }

        public bool TrySend(Guid messageId, byte[] data) => false;

        public bool TrySend(Guid messageId, byte[] data, int playerId) => false;

        public void DisconnectAll() { }
    }
}
