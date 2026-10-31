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

        /// <summary>6000.0's BakeTrailsMesh joins ribbon strips in its index count and rejects
        /// the resulting out-of-bounds indices. Regular ribbon rendering is unaffected.</summary>
        internal static bool ParticleRibbonBakeRejectsMultipleStrips => Major >= 6000;

        /// <summary>6000.0.71f1 has no serialized PCM switch and ignores an injected m_EnablePCM
        /// (measured by physicsscenesettings). Older settings documents expose this legacy switch;
        /// their selectable SAT path is documented, but not yet measured by this fixture.</summary>
        internal static bool PhysicsPcmEnabled(bool projectValue) => Major >= 6000 || projectValue;
    }
}
