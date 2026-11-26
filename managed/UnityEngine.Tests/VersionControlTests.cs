using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor.VersionControl;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// UnityEditor.VersionControl without a provider, as the Unity 6000.0.71f1 Editor answers
    /// (tests/projects/vcsqueries).
    /// </summary>
    [Collection("EditorState")]
    public sealed class VersionControlTests : IDisposable
    {
        readonly string m_Root = Path.Combine(Path.GetTempPath(), "unbound-vcs-" + System.Guid.NewGuid().ToString("N"));
        readonly string m_ProjectPath = ProjectInfo.ProjectPath;
        readonly ILogHandler m_Handler = Debug.unityLogger.logHandler;
        readonly Capture m_Logs = new Capture();

        public VersionControlTests()
        {
            Directory.CreateDirectory(Path.Combine(m_Root, "Assets", "Data"));
            File.WriteAllText(Path.Combine(m_Root, "Assets", "Data", "A.txt"), "a\n");
            File.WriteAllText(Path.Combine(m_Root, "Assets", "Data", "A.txt.meta"), "fileFormatVersion: 2\n");
            ProjectInfo.ProjectPath = m_Root;
            SettingsAssets.Reset();
            Debug.unityLogger.logHandler = m_Logs;
        }

        public void Dispose()
        {
            Debug.unityLogger.logHandler = m_Handler;
            ProjectInfo.ProjectPath = m_ProjectPath;
            SettingsAssets.Reset();
            try { Directory.Delete(m_Root, true); } catch (IOException) { }
        }

        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add("Exception: " + exception.Message);
            public void LogFormat(LogType type, Object context, string format, params object[] args) =>
                Lines.Add(type + ": " + string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
        }

        static string A(Asset asset) => asset == null ? "null"
            : asset.path + "|" + asset.name + "|" + asset.fullName + "|" + asset.assetPath + "|" + asset.isFolder + "|" + asset.isMeta + "|" + asset.state
              + "|" + asset.isInCurrentProject;

        [Fact]
        public void AnAssetIsAnsweredFromItsPath()
        {
            Assert.Equal("Assets/Data/A.txt|A|A.txt|Assets/Data/A.txt|False|False|None|True", A(new Asset("Assets/Data/A.txt")));
            Assert.Equal("Assets/Data/A.txt|A|A.txt|Assets/Data/A.txt|False|False|Updating|True", A(Provider.GetAssetByPath("Assets/Data/A.txt")));
            Assert.Equal("Assets/Data/|||Assets/Data/|True|False|None|False", A(new Asset("Assets/Data")));
            Assert.Equal("Assets/Data/A.txt.meta|A.txt|A.txt.meta|Assets/Data/A.txt|False|True|MetaFile|True", A(new Asset("Assets/Data/A.txt.meta")));
            Assert.Equal("Assets/Data/Missing.txt|Missing|Missing.txt|Assets/Data/Missing.txt|False|False|None|False", A(new Asset("Assets/Data/Missing.txt")));
            Assert.Equal("||||False|False|None|False", A(new Asset(null)));
            Assert.Null(Provider.GetAssetByPath("Assets/Data/Missing.txt"));
            Assert.Null(Provider.GetAssetByPath(""));
            Assert.Null(Provider.GetAssetByPath(null));
        }

        [Fact]
        public void TheProviderIsOffline()
        {
            Assert.False(Provider.enabled);
            Assert.False(Provider.isActive);
            Assert.Equal(OnlineState.Offline, Provider.onlineState);
            Assert.Equal("", Provider.offlineReason);
            Assert.Null(Provider.activeTask);
            Assert.Null(Provider.GetActivePlugin());
            Assert.True(Provider.IsOpenForEdit(new Asset("Assets/Data/A.txt")));
            Assert.False(Provider.CheckoutIsValid(new Asset("Assets/Data/A.txt")));
            Assert.Empty(m_Logs.Lines);
        }

        [Fact]
        public void ACheckoutFailsWithAnErrorNamingTheFiles()
        {
            var list = new AssetList { new Asset("Assets/Data/A.txt"), new Asset("Assets/Data/B.txt") };
            var task = Provider.Checkout(list, CheckoutMode.Both);
            task.Wait();
            Assert.False(task.success);
            Assert.Equal(2, task.resultCode);
            var message = Assert.Single(task.messages);
            Assert.Equal(Message.Severity.Error, message.severity);
            Assert.Equal("Cannot check out files in offline mode\nAssets/Data/A.txt\nAssets/Data/B.txt", message.message);
            Assert.Equal(new[] { "Error: Version Control: Cannot check out files in offline mode\nAssets/Data/A.txt\nAssets/Data/B.txt" }, m_Logs.Lines);
            m_Logs.Lines.Clear();
            Assert.Equal("Cannot get latest files in offline mode\nAssets/Data/A.txt", Assert.Single(Provider.GetLatest(new Asset("Assets/Data/A.txt")).messages).message);
            m_Logs.Lines.Clear();
            var status = Provider.Status("Assets/Data/A.txt");
            Assert.False(status.success);
            Assert.Empty(status.messages);
            Assert.Empty(m_Logs.Lines);
        }

        [Theory]
        [InlineData("Assets/Data/A.txt", true)]
        [InlineData("Assets/Data/Missing.txt", true)]
        [InlineData("Packages/manifest.json", true)]
        [InlineData("ProjectSettings/ProjectSettings.asset", true)]
        [InlineData("Library/ArtifactDB", false)]
        [InlineData("UserSettings/x", false)]
        [InlineData("Other/A.txt", false)]
        [InlineData("Assets\\Data\\A.txt", false)]
        [InlineData("assets/Data/A.txt", false)]
        [InlineData("Assets", true)]
        [InlineData("AssetsX/A.txt", false)]
        [InlineData("Packages/manifest.json.meta", true)]
        [InlineData("Assets/../Other/A.txt", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void APathIsVersionedInTheProjectsVersionedFolders(string path, bool versioned) =>
            Assert.Equal(versioned, VersionControlUtils.IsPathVersioned(path));

        [Fact]
        public void AnAbsolutePathIsNotVersioned() =>
            Assert.False(VersionControlUtils.IsPathVersioned(Path.Combine(m_Root, "Assets", "Data", "A.txt")));

        /// <summary>A message shows as Unity's C# shows it: information, "Version control:" and a new line.</summary>
        [Fact]
        public void AMessageShowsAsInformation()
        {
            var message = Assert.Single(Provider.Checkout("Assets/Data/A.txt", CheckoutMode.Both).messages);
            m_Logs.Lines.Clear();
            message.Show();
            Assert.Equal(new[] { "Log: Version control:\nCannot check out files in offline mode\nAssets/Data/A.txt" }, m_Logs.Lines);
        }

        /// <summary>A pre-checkout callback is not run (Unity's C# runs it): said.</summary>
        [Fact]
        public void APreCheckoutCallbackIsSaid()
        {
            bool ran = false;
            Stubs.Forget("UnityEditor.VersionControl.Provider.preCheckoutCallback");
            Provider.preCheckoutCallback = (AssetList list, ref string id, ref string description) => ran = true;
            try { Provider.Checkout("Assets/Data/A.txt", CheckoutMode.Both); }
            finally { Provider.preCheckoutCallback = null; }
            Assert.Contains(m_Logs.Lines, l => l.Contains("preCheckoutCallback is not run by a checkout yet"));
        }

        /// <summary>A project whose settings choose a provider is not supported, as AssetDatabase's
        /// editability queries.</summary>
        [Fact]
        public void AProviderModeIsNotSupported()
        {
            Directory.CreateDirectory(Path.Combine(m_Root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(m_Root, "ProjectSettings", "VersionControlSettings.asset"),
                "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!890905787 &1\nVersionControlSettings:\n  m_ObjectHideFlags: 0\n  m_Mode: Perforce\n  m_CollabEditorSettings:\n    inProgressEnabled: 1\n");
            SettingsAssets.Reset();
            Assert.Equal("Perforce", UnityEditor.VersionControlSettings.mode);
            Assert.Throws<NotSupportedException>(() => Provider.isActive);
        }

        [Fact]
        public void FilterChildrenLeavesOutTheAssetsOfAFolderInTheList()
        {
            var list = new AssetList { new Asset("Assets/Data/A.txt"), new Asset("Assets/Data") };
            var filtered = list.FilterChildren();
            Assert.Equal("Assets/Data/", Assert.Single(filtered).path);
        }
    }
}
