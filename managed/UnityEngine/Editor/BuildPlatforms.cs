using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.Build
{
    /// <summary>A platform of the Editor's build settings.</summary>
    internal partial class BuildPlatform : ICloneable
    {
        public string name;
        public NamedBuildTarget namedBuildTarget;
        public bool installed;
        public bool hideInUi;
        public string tooltip;
        public BuildTarget defaultTarget;
        readonly string m_Title;

        public BuildPlatform(string locTitle, string iconId, NamedBuildTarget namedBuildTarget, BuildTarget defaultTarget, bool hideInUi, bool installed)
            : this(locTitle, "", iconId, namedBuildTarget, defaultTarget, hideInUi, installed) { }

        public BuildPlatform(string locTitle, string tooltip, string iconId, NamedBuildTarget namedBuildTarget, BuildTarget defaultTarget, bool hideInUi, bool installed)
        {
            this.namedBuildTarget = namedBuildTarget;
            name = namedBuildTarget.TargetName;
            m_Title = locTitle;
            this.tooltip = tooltip;
            this.defaultTarget = defaultTarget;
            this.hideInUi = hideInUi;
            this.installed = installed;
        }

        public object Clone() => MemberwiseClone();

        public BuildTargetGroup targetGroup => namedBuildTarget.ToBuildTargetGroup();

        public GUIContent title => new GUIContent(m_Title);
    }

    /// <summary>A Standalone platform of one subtarget (the player or the dedicated server).</summary>
    internal partial class BuildPlatformWithSubtarget : BuildPlatform
    {
        public int subtarget;

        public BuildPlatformWithSubtarget(string locTitle, string tooltip, string iconId, NamedBuildTarget namedBuildTarget, BuildTarget defaultTarget, int subtarget, bool hideInUi, bool installed)
            : base(locTitle, tooltip, iconId, namedBuildTarget, defaultTarget, hideInUi, installed)
        {
            this.subtarget = subtarget;
        }
    }

    /// <summary>
    /// The Editor's build platforms: the release's fixed list, in its order, with each platform's
    /// title, tooltip, default target and visibility (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/toolapis platforms). What the machine decides follows this host: the
    /// Standalone default target is the Editor's own, and a platform is installed when its
    /// support module is (Standalone always; the dedicated server's module is not looked for).
    /// </summary>
    internal partial class BuildPlatforms
    {
        public BuildPlatform[] buildPlatforms;

        static BuildPlatforms s_Instance;

        public static BuildPlatforms instance => s_Instance ??= new BuildPlatforms();

        internal BuildPlatforms()
        {
            var host = OperatingSystem.IsMacOS() ? BuildTarget.StandaloneOSX
                : OperatingSystem.IsWindows() ? BuildTarget.StandaloneWindows64
                : BuildTarget.StandaloneLinux64;
            BuildPlatform Platform(string title, NamedBuildTarget target, BuildTarget defaultTarget, bool hide) =>
                new BuildPlatform(title, title + " settings", "", target, defaultTarget, hide, Installed(defaultTarget));
            buildPlatforms = new[]
            {
                new BuildPlatformWithSubtarget("Windows, Mac, Linux", "Windows, Mac, Linux settings", "", NamedBuildTarget.Standalone, host, (int)StandaloneBuildSubtarget.Player, false, true),
                new BuildPlatformWithSubtarget("Dedicated Server", "Dedicated Server settings", "", NamedBuildTarget.Server, host, (int)StandaloneBuildSubtarget.Server, false, false),
                Platform("Android", NamedBuildTarget.Android, BuildTarget.Android, false),
                Platform("iOS", NamedBuildTarget.iOS, BuildTarget.iOS, false),
                Platform("PS4", NamedBuildTarget.PS4, BuildTarget.PS4, false),
                Platform("PS5", NamedBuildTarget.PS5, BuildTarget.PS5, false),
                Platform("Xbox One (Legacy XDK)", NamedBuildTarget.XboxOne, BuildTarget.XboxOne, true),
                Platform("Xbox One", NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.GameCoreXboxOne), BuildTarget.GameCoreXboxOne, true),
                Platform("Xbox Series X S", NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.GameCoreXboxSeries), BuildTarget.GameCoreXboxSeries, true),
                Platform("Nintendo Switch", NamedBuildTarget.NintendoSwitch, BuildTarget.Switch, true),
                Platform("Web", NamedBuildTarget.WebGL, BuildTarget.WebGL, false),
                Platform("Universal Windows Platform", NamedBuildTarget.WindowsStoreApps, BuildTarget.WSAPlayer, false),
                Platform("tvOS", NamedBuildTarget.tvOS, BuildTarget.tvOS, false),
                Platform("visionOS", NamedBuildTarget.VisionOS, BuildTarget.VisionOS, false),
                Platform("Linux Headless Simulation", NamedBuildTarget.LinuxHeadlessSimulation, BuildTarget.LinuxHeadlessSimulation, true),
                Platform("Embedded Linux", NamedBuildTarget.EmbeddedLinux, BuildTarget.EmbeddedLinux, true),
                Platform("QNX", NamedBuildTarget.QNX, BuildTarget.QNX, true),
                Platform("Kepler", NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.Kepler), BuildTarget.Kepler, true),
                Platform("Nintendo Switch 2", NamedBuildTarget.NintendoSwitch2, BuildTarget.Switch2, true),
            };
        }

        static bool Installed(BuildTarget target) =>
            BuildPipeline.GetPlaybackEngineDirectory(target, BuildOptions.None, false).Length > 0;

        public BuildPlatform BuildPlatformFromNamedBuildTarget(NamedBuildTarget target)
        {
            foreach (var platform in buildPlatforms)
                if (platform.namedBuildTarget == target) return platform;
            return null;
        }

        /// <summary>The installed platforms the build settings show (tests/projects/toolapis:
        /// the installed, not hidden ones).</summary>
        public List<BuildPlatform> GetValidPlatforms() => GetValidPlatforms(false);

        public List<BuildPlatform> GetValidPlatforms(bool includeMetaPlatforms)
        {
            var result = new List<BuildPlatform>();
            foreach (var platform in buildPlatforms)
                if (platform.installed && !platform.hideInUi) result.Add(platform);
            return result;
        }
    }
}
