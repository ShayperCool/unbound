using System;

namespace UnityEditor
{
    /// <summary>Cloud project state changes: there are none outside the Editor's services
    /// window, so the events never fire.</summary>
    public partial class CloudProjectSettingsEventManager
    {
        static readonly CloudProjectSettingsEventManager s_Instance = new CloudProjectSettingsEventManager();

        CloudProjectSettingsEventManager() { }

        public static CloudProjectSettingsEventManager instance => s_Instance;

#pragma warning disable CS0067 // Never raised.
        public event Action projectStateChanged;
        public event Action projectRefreshed;
#pragma warning restore CS0067
    }
}
