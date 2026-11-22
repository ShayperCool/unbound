using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>The desktop's opener for URLs and files: <c>xdg-open</c> on Linux, <c>open</c> on
    /// macOS. <c>UNBOUND_OPEN_URL</c> names another program, so tests start no application.</summary>
    internal static class DesktopOpener
    {
        /// <summary>Starts the opener with the target; false, with a warning naming the caller,
        /// when it cannot be started.</summary>
        internal static bool Open(string target, string caller)
        {
            string opener = Environment.GetEnvironmentVariable("UNBOUND_OPEN_URL");
            if (string.IsNullOrEmpty(opener)) opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo(opener) { UseShellExecute = false };
                start.ArgumentList.Add(target);
                System.Diagnostics.Process.Start(start)?.Dispose();
                return true;
            }
            catch (Exception e)
            {
                Native.Log((int)LogType.Warning, $"{Log.DiagnosticPrefix}{caller} could not run {opener}: {e.Message}");
                return false;
            }
        }
    }
}
