using System;
using UnityEngine.Internal;

// Types of other modules the Input System package uses: its analytics declare their event with
// AnalyticInfoAttribute, its remoting exchanges MessageEventArgs over the player connection.

namespace UnityEngine.Analytics
{
    [ExcludeFromDocs]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public partial class AnalyticInfoAttribute : Attribute
    {
        public int version { get; }
        public string vendorKey { get; }
        public string eventName { get; }
        internal int maxEventsPerHour { get; }
        internal int maxNumberOfElements { get; }

        public AnalyticInfoAttribute(string eventName, string vendorKey = "", int version = 1, int maxEventsPerHour = 1000, int maxNumberOfElements = 1000)
        {
            this.version = version;
            this.vendorKey = vendorKey;
            this.eventName = eventName;
            this.maxEventsPerHour = maxEventsPerHour;
            this.maxNumberOfElements = maxNumberOfElements;
        }
    }
}

namespace UnityEngine.Networking.PlayerConnection
{
    [Serializable]
    public partial class MessageEventArgs
    {
        public int playerId;
        public byte[] data;
    }
}
