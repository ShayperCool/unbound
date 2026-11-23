using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public class BuildIdentityTests
{
    const string SettingsPath = "ProjectSettings/ProjectSettings.asset";

    static void Identifier(string key, NamedBuildTarget target) =>
        ProbeLog.Try(key, () => PlayerSettings.GetApplicationIdentifier(target));

    static void SetIdentifier(string key, NamedBuildTarget target, string value) =>
        ProbeLog.Try(key, () =>
        {
            PlayerSettings.SetApplicationIdentifier(target, value);
            return PlayerSettings.GetApplicationIdentifier(target);
        });

    static void LegacyIdentifier(string value)
    {
        var logger = Debug.unityLogger;
        var saved = logger.logHandler;
        logger.logHandler = new Recorder();
        try
        {
            // The obsolete alias is a compile-time error for new callers.
            var alias = typeof(PlayerSettings).GetProperty("bundleIdentifier");
            alias.SetValue(null, "net.probe.legacy");
            ProbeLog.Row("legacy.value", alias.GetValue(null));
            ProbeLog.Row("legacy.active", PlayerSettings.applicationIdentifier);
            PlayerSettings.applicationIdentifier = value;
            ProbeLog.Row("active.value", PlayerSettings.applicationIdentifier);
        }
        finally
        {
            logger.logHandler = saved;
        }
    }

    sealed class Recorder : ILogHandler
    {
        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args) =>
            ProbeLog.Row("legacy.log", $"{logType}: {string.Format(format, args)}");

        public void LogException(Exception exception, UnityEngine.Object context) =>
            ProbeLog.Row("legacy.log", exception.GetType().Name + ": " + exception.Message);
    }

    [Test]
    public void Persist()
    {
        ProbeLog.Row("initial.file", ProbeLog.Hash(SettingsPath));
        Identifier("initial.standalone", NamedBuildTarget.Standalone);
        Identifier("initial.android", NamedBuildTarget.Android);
        Identifier("initial.ios", NamedBuildTarget.iOS);
        Identifier("initial.server", NamedBuildTarget.Server);
        Identifier("initial.webgl", NamedBuildTarget.WebGL);
        Identifier("initial.unknown", NamedBuildTarget.Unknown);
        ProbeLog.Row("initial.active", PlayerSettings.applicationIdentifier);
        ProbeLog.Row("initial.runtime", Application.identifier);
        ProbeLog.Row("initial.bundle", PlayerSettings.bundleVersion);
        ProbeLog.Row("initial.androidVersion", PlayerSettings.Android.bundleVersionCode);
        ProbeLog.Row("initial.iosBuild", PlayerSettings.iOS.buildNumber);

        SetIdentifier("other.android", NamedBuildTarget.Android, "net.probe.android");
        Identifier("other.iosDefault", NamedBuildTarget.iOS);
        ProbeLog.Row("other.active", PlayerSettings.applicationIdentifier);
        ProbeLog.Row("other.runtime", Application.identifier);
        ProbeLog.Row("other.beforeSave.file", ProbeLog.Hash(SettingsPath));
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("other.saved.file", SettingsPath);

#pragma warning disable CS0618
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, "net.probe.ios");
        ProbeLog.Row("legacyGroup.ios", PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.iOS));
#pragma warning restore CS0618
        SetIdentifier("target.standalone", NamedBuildTarget.Standalone, "net.probe.standalone");
        SetIdentifier("target.server", NamedBuildTarget.Server, "net.probe.server");
        SetIdentifier("target.webgl", NamedBuildTarget.WebGL, "net.probe.web");
        var previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
        try
        {
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
            ProbeLog.Row("target.serverActive", PlayerSettings.applicationIdentifier);
            ProbeLog.Row("target.serverRuntime", Application.identifier);
        }
        finally
        {
            EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;
        }
        SetIdentifier("target.unknown", NamedBuildTarget.Unknown, "net.probe.unknown");
        Identifier("target.android", NamedBuildTarget.Android);
        ProbeLog.Row("target.runtime", Application.identifier);

        LegacyIdentifier("net.probe.active");
        SetIdentifier("sanitize.android", NamedBuildTarget.Android, "9 Studio.Probe-Team.App");
        SetIdentifier("sanitize.ios", NamedBuildTarget.iOS, "9 Studio.Probe-Team.App");
        PlayerSettings.bundleVersion = "2.4.1-beta.2";
        PlayerSettings.Android.bundleVersionCode = 214;
        PlayerSettings.iOS.buildNumber = "27.3";
        ProbeLog.Row("versions.bundle", PlayerSettings.bundleVersion);
        ProbeLog.Row("versions.android", PlayerSettings.Android.bundleVersionCode);
        ProbeLog.Row("versions.ios", PlayerSettings.iOS.buildNumber);
        ProbeLog.Row("versions.runtime", Application.version);
        ProbeLog.Row("versions.beforeSave.file", ProbeLog.Hash(SettingsPath));
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("versions.saved.file", SettingsPath);

        SetIdentifier("empty.android", NamedBuildTarget.Android, "");
        SetIdentifier("empty.ios", NamedBuildTarget.iOS, null);
        Identifier("empty.standalone", NamedBuildTarget.Standalone);
        PlayerSettings.bundleVersion = "3.0";
        PlayerSettings.Android.bundleVersionCode = 315;
        PlayerSettings.iOS.buildNumber = "41";
        ProbeLog.Row("quit.beforeSave.file", ProbeLog.Hash(SettingsPath));
    }
}
