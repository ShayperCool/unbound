using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Multiplayer.Playmode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// End-to-end probe for Multiplayer Play Mode virtual project clones (tests/projects/mppmclones):
/// - Main runs with `--virtual-player 2`, waits until the clone launched by MPPM's own
///   `UnityPlayer.Activate` has started, read its command line and SystemData.json, and written
///   its report, then checks the clone's folder layout and exits (closing the clone via MPPM's
///   `EditorApplication.quitting` handler).
/// - A second run without `--virtual-player` verifies that no clone is auto-activated from saved
///   state.
/// </summary>
public class MppmCloneProbe : MonoBehaviour
{
    static DateTime s_StartUtc;
    static DateTime? s_CloneSeenUtc;
    static bool s_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MPPM_PROBE_OUT"))) return;
        new GameObject("MppmCloneProbe").AddComponent<MppmCloneProbe>();
    }

    void Start()
    {
        s_StartUtc = DateTime.UtcNow;
        var outDir = Environment.GetEnvironmentVariable("MPPM_PROBE_OUT");
        var mode = Environment.GetEnvironmentVariable("MPPM_PROBE_MODE") ?? "activate";
        var args = Environment.GetCommandLineArgs();
        bool isClone = Array.IndexOf(args, "--virtual-project-clone") >= 0;

        if (isClone)
        {
            var sb = new StringBuilder();
            sb.AppendLine("isMainEditor=" + CurrentPlayer.IsMainEditor);
            string[] tags;
            try
            {
                tags = CurrentPlayer.ReadOnlyTags();
                sb.AppendLine("tagsOk=True");
                sb.AppendLine("tags=" + string.Join(",", tags));
            }
            catch (Exception e)
            {
                sb.AppendLine("tagsOk=False");
                sb.AppendLine("tagsError=" + e.GetType().Name + ": " + e.Message);
            }
            sb.AppendLine("hasLibraryRedirect=" + args.Any(a => a.Equals("-library-redirect", StringComparison.OrdinalIgnoreCase)));
            sb.AppendLine("hasReadOnly=" + args.Any(a => a.Equals("-readonly", StringComparison.OrdinalIgnoreCase)));
            sb.AppendLine("hasVpId=" + args.Any(a => a.StartsWith("-vpId=mppm", StringComparison.Ordinal)));
            sb.AppendLine("hasChannelName=" + args.Any(a => a == "-vp-channel-name=vp-channel"));
            sb.AppendLine("hasChannelPort=" + args.Any(a => a == "-ump-channel-service-port"));
            sb.AppendLine("hasMainProcessId=" + args.Any(a => a.StartsWith("-mainProcessId=", StringComparison.Ordinal)));
            sb.AppendLine("pid=" + System.Diagnostics.Process.GetCurrentProcess().Id);
            sb.AppendLine("dataPath=" + Application.dataPath);
            sb.AppendLine("args=" + string.Join(" | ", args));
            var tmp = Path.Combine(outDir, "clone.txt.tmp");
            var dst = Path.Combine(outDir, "clone.txt");
            File.WriteAllText(tmp, sb.ToString());
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(tmp, dst);
            return;
        }

        if (mode == "no-auto")
        {
            var sb = new StringBuilder();
            sb.AppendLine("isMainEditor=" + CurrentPlayer.IsMainEditor);
            sb.AppendLine("mode=no-auto");
            File.WriteAllText(Path.Combine(outDir, "noauto.txt"), sb.ToString());
        }
    }

    void Update()
    {
        if (s_Done) return;
        var outDir = Environment.GetEnvironmentVariable("MPPM_PROBE_OUT");
        var mode = Environment.GetEnvironmentVariable("MPPM_PROBE_MODE") ?? "activate";
        var args = Environment.GetCommandLineArgs();
        bool isClone = Array.IndexOf(args, "--virtual-project-clone") >= 0;
        double elapsed = (DateTime.UtcNow - s_StartUtc).TotalSeconds;

        if (isClone)
        {
            // Stay alive briefly so Main can observe the clone and close it via EditorApplication.quitting,
            // with a safety timeout so a clone never lingers if Main crashed.
            if (elapsed > 90.0)
            {
                s_Done = true;
                EditorApplication.Exit(0);
                return;
            }
            System.Threading.Thread.Sleep(50);
            return;
        }

        if (mode == "no-auto")
        {
            // Wait ~2 wall-clock seconds to confirm MPPM does not auto-launch a clone from saved SystemData.json.
            if (elapsed >= 2.0)
            {
                s_Done = true;
                EditorApplication.Exit(0);
                return;
            }
            System.Threading.Thread.Sleep(50);
            return;
        }

        var cloneReport = Path.Combine(outDir, "clone.txt");
        bool hasReport = File.Exists(cloneReport);
        if (hasReport && s_CloneSeenUtc == null) s_CloneSeenUtc = DateTime.UtcNow;
        bool waitedAfterClone = s_CloneSeenUtc != null && (DateTime.UtcNow - s_CloneSeenUtc.Value).TotalSeconds >= 2.0;
        if (waitedAfterClone || elapsed > 180.0)
        {
            s_Done = true;
            var sb = new StringBuilder();
            sb.AppendLine("isMainEditor=" + CurrentPlayer.IsMainEditor);
            sb.AppendLine("cloneReportExists=" + hasReport);
            sb.AppendLine("channelPort=" + UnityEditor.MPE.ChannelService.GetPort());
            sb.AppendLine("channelClients=" + UnityEditor.MPE.ChannelService.GetChannelClientList().Length);
            sb.AppendLine("dataPath=" + Application.dataPath);
            File.WriteAllText(Path.Combine(outDir, "main.txt"), sb.ToString());
            EditorApplication.Exit(0);
            return;
        }
        System.Threading.Thread.Sleep(50);
    }
}
