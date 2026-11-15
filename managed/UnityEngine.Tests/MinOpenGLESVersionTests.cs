using System;
using System.IO;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Graphics.minOpenGLESVersion from the active target's graphics APIs and the OpenGL ES
    /// requirements (the Unity 6000.0.71f1 Editor rule, tests/projects/glesversion).</summary>
    [CollectionDefinition("Build target", DisableParallelization = true)]
    public sealed class BuildTargetCollection { }

    /// <summary>Changes the project path and the active build target, which other tests read: runs alone.</summary>
    [Collection("Build target")]
    public sealed class MinOpenGLESVersionTests : IDisposable
    {
        readonly string previousProject = ProjectInfo.ProjectPath;
        readonly BuildTargetGroup previousGroup = EditorUserBuildSettingsInfo.ActiveGroup;
        readonly BuildTarget previousTarget = EditorUserBuildSettingsInfo.ActiveBuildTarget;
        readonly string directory = Path.Combine(Path.GetTempPath(), "unbound-gles-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            ProjectInfo.ProjectPath = previousProject;
            EditorUserBuildSettingsInfo.ActiveGroup = previousGroup;
            EditorUserBuildSettingsInfo.ActiveBuildTarget = previousTarget;
            ProjectSettingsText.Invalidate();
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }

        OpenGLESVersion With(string group, string apis, int es31, int aep, int es32)
        {
            Directory.CreateDirectory(Path.Combine(directory, "ProjectSettings"));
            File.WriteAllText(Path.Combine(directory, "ProjectSettings", "ProjectSettings.asset"),
                "%YAML 1.1\n--- !u!129 &1\nPlayerSettings:\n  productName: P\n" +
                "  m_BuildTargetGraphicsAPIs:" + (apis ?? " []") + "\n" +
                "  m_BuildTargetVRSettings:\n  - m_BuildTarget: AndroidPlayer\n    m_Enabled: 0\n" +
                $"  openGLRequireES31: {es31}\n  openGLRequireES31AEP: {aep}\n  openGLRequireES32: {es32}\n");
            ProjectInfo.ProjectPath = directory;
            ProjectSettingsText.Invalidate();
            EditorUserBuildSettingsInfo.SetTargetGroup(group);
            return Graphics.minOpenGLESVersion;
        }

        static string Android(string hex, int automatic) =>
            $"\n  - m_BuildTarget: AndroidPlayer\n    m_APIs: {hex}\n    m_Automatic: {automatic}";

        [Fact]
        public void AnAutomaticAndroidTargetRequiresTheHighestVersionAsked()
        {
            Assert.Equal(OpenGLESVersion.OpenGLES30, With("Android", null, 0, 0, 0));
            Assert.Equal(OpenGLESVersion.OpenGLES31, With("Android", null, 1, 0, 0));
            Assert.Equal(OpenGLESVersion.OpenGLES31AEP, With("Android", null, 0, 1, 0));
            Assert.Equal(OpenGLESVersion.OpenGLES32, With("Android", null, 0, 0, 1));
            // A list left automatic is not the one used.
            Assert.Equal(OpenGLESVersion.OpenGLES31, With("Android", Android("15000000", 1), 1, 0, 0));
        }

        [Fact]
        public void AListWithoutOpenGLES3RequiresNone()
        {
            Assert.Equal(OpenGLESVersion.None, With("Android", Android("15000000", 0), 1, 0, 0));
            Assert.Equal(OpenGLESVersion.OpenGLES31AEP, With("Android", Android("150000000b000000", 0), 1, 1, 0));
            Assert.Equal(OpenGLESVersion.None, With("Standalone", null, 1, 1, 1));
            // Another platform's list does not count for Android's, nor Android's for another's.
            Assert.Equal(OpenGLESVersion.None, With("Standalone", Android("0b000000", 0), 0, 0, 0));
            Assert.Equal(OpenGLESVersion.OpenGLES30, With("Android",
                "\n  - m_BuildTarget: LinuxStandaloneSupport\n    m_APIs: 11000000\n    m_Automatic: 0", 0, 0, 0));
        }
    }
}
