using System;
using System.Globalization;

namespace Unbound.Runtime
{
    /// <summary>
    /// Behaviour that differs between Unity releases, chosen by the project's release (the managed
    /// side of the compatibility profile). Each answer names what was measured.
    /// </summary>
    internal static class Compat
    {
        /// <summary>The project's release's major version (2022, 6000, ...).</summary>
        internal static int Major
        {
            get
            {
                var v = UnityEngine.ProjectInfo.UnityVersion ?? "";
                int dot = v.IndexOf('.');
                return int.TryParse(dot < 0 ? v : v.Substring(0, dot), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : 6000;
            }
        }

        /// <summary>Where Input's errors point to its settings: 6000.0's Editor says (measured)
        /// "Edit -> Project Settings... -> Input Manager"; earlier releases "Edit -> Settings -> Input".</summary>
        internal static string InputSettingsMenu => Major >= 6000 ? "Edit -> Project Settings... -> Input Manager" : "Edit -> Settings -> Input";
    }
}
