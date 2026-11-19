using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor
{
    public sealed partial class PlayerSettings
    {
        /// <summary>Play Mode tests run in Play mode.</summary>
        internal static bool runPlayModeTestAsEditModeTest { get; set; }
    }

    internal partial class ConsoleWindow
    {
        static bool s_ErrorPause;

        /// <summary>The console's "Error Pause" toggle (off; nothing pauses on errors).</summary>
        public static bool GetConsoleErrorPause() => s_ErrorPause;

        public static void SetConsoleErrorPause(bool enabled) => s_ErrorPause = enabled;
    }
}
