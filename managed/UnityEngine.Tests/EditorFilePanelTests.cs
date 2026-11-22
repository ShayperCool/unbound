using System;
using System.Collections.Generic;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageRegistrationEventArgs = UnityEditor.PackageManager.PackageRegistrationEventArgs;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Dialogs and file panels nobody can answer, as the batch-mode Unity 6000.0.71f1
    /// Editor cancels them (tests/projects/filepanels), and the package registration event data.</summary>
    public sealed class EditorFilePanelTests : IDisposable
    {
        const string Suffix = "\nThis should not be called in batch mode.";

        readonly List<string> m_Logs = new List<string>();
        readonly Application.LogCallback m_OnLog;

        public EditorFilePanelTests()
        {
            m_OnLog = (condition, _, type) => m_Logs.Add(type + ":" + condition);
            Application.logMessageReceived += m_OnLog;
        }

        public void Dispose() => Application.logMessageReceived -= m_OnLog;

        string Cancelled(Func<string> panel)
        {
            m_Logs.Clear();
            Assert.Equal("", panel());
            return Assert.Single(m_Logs);
        }

        [Fact]
        public void EveryPanelIsCancelledWithAnAssertionThatNamesIt()
        {
            Assert.Equal("Assert:Cancelling FileDialog: Save thing Where to save it" + Suffix,
                Cancelled(() => EditorUtility.SaveFilePanelInProject("Save thing", "Thing", "asset", "Where to save it")));
            Assert.Equal("Assert:Cancelling FileDialog: Save thing Where" + Suffix,
                Cancelled(() => EditorUtility.SaveFilePanelInProject("Save thing", "Thing", "asset", "Where", "/tmp")));
            Assert.Equal("Assert:Cancelling FileDialog:  Only a message" + Suffix, Cancelled(() => EditorUtility.SaveFilePanelInProject(null, "Thing", "asset", "Only a message")));
            Assert.Equal("Assert:Cancelling FileDialog:  " + Suffix, Cancelled(() => EditorUtility.SaveFilePanelInProject(null, null, null, null)));
            Assert.Equal("Assert:Cancelling FileDialog: Save file" + Suffix, Cancelled(() => EditorUtility.SaveFilePanel("Save file", "Assets", "File", "txt")));
            Assert.Equal("Assert:Cancelling FileDialog: " + Suffix, Cancelled(() => EditorUtility.SaveFilePanel(null, null, null, null)));
            Assert.Equal("Assert:Cancelling FileDialog: Open file" + Suffix, Cancelled(() => EditorUtility.OpenFilePanel("Open file", "Assets", "txt")));
            // An open panel without a title is named.
            Assert.Equal("Assert:Cancelling FileDialog: Open" + Suffix, Cancelled(() => EditorUtility.OpenFilePanel(null, null, null)));
            Assert.Equal("Assert:Cancelling FileDialog: Open folder" + Suffix, Cancelled(() => EditorUtility.OpenFolderPanel("Open folder", "Assets", "Folder")));
            Assert.Equal("Assert:Cancelling FileDialog: " + Suffix, Cancelled(() => EditorUtility.OpenFolderPanel(null, null, null)));
            Assert.Equal("Assert:Cancelling FileDialog: Save folder" + Suffix, Cancelled(() => EditorUtility.SaveFolderPanel("Save folder", "Assets", "Folder")));
            Assert.Equal("Assert:Cancelling FileDialog: Open file" + Suffix, Cancelled(() => EditorUtility.OpenFilePanelWithFilters("Open file", "Assets", new[] { "Text", "txt" })));
            Assert.Equal("Assert:Cancelling FileDialog: Open file" + Suffix, Cancelled(() => EditorUtility.OpenFilePanelWithFilters("Open file", "Assets", new string[0])));
        }

        [Fact]
        public void FiltersComeInPairs()
        {
            var odd = Assert.Throws<Exception>(() => EditorUtility.OpenFilePanelWithFilters("Open file", "Assets", new[] { "Text", "txt", "All" }));
            Assert.Equal("Filters must be declared in pairs { \"FileType\", \"FileExtension\" }; for instance { \"CSharp\", \"cs\" }. ", odd.Message);
            Assert.Throws<NullReferenceException>(() => EditorUtility.OpenFilePanelWithFilters(null, null, null));
            Assert.Empty(m_Logs);
        }

        [Fact]
        public void ADialogNeedsAButtonAndSomethingToSayAndIsThenCancelled()
        {
            Assert.Equal("Both 'ok' and 'cancel' strings are null or empty", Assert.Throws<ArgumentException>(() => EditorUtility.DisplayDialog(null, null, null, null)).Message);
            Assert.Equal("Both 'ok' and 'cancel' strings are null or empty", Assert.Throws<ArgumentException>(() => EditorUtility.DisplayDialog("Title", "Message", "", null)).Message);
            Assert.Equal("Both 'title' and 'message' strings are null or empty", Assert.Throws<ArgumentException>(() => EditorUtility.DisplayDialog("", null, "OK", "Cancel")).Message);
            Assert.Empty(m_Logs);
            Assert.False(EditorUtility.DisplayDialog("Title", "Message", "OK"));
            Assert.False(EditorUtility.DisplayDialog("Title", null, null, "Cancel"));
            Assert.False(EditorUtility.DisplayDialog(null, "Message", "OK"));
            Assert.Equal(new[]
            {
                "Assert:Cancelling DisplayDialog: Title Message" + Suffix,
                "Assert:Cancelling DisplayDialog: Title " + Suffix,
                "Assert:Cancelling DisplayDialog:  Message" + Suffix,
            }, m_Logs);
        }

        [Fact]
        public void PackageRegistrationDataIsFourReadOnlyLists()
        {
            var one = new PackageInfo[1];
            var args = new PackageRegistrationEventArgs(one, null, null, new PackageInfo[2]);
            Assert.Single(args.added);
            Assert.Empty(args.removed);
            Assert.Empty(args.changedFrom);
            Assert.Equal(2, args.changedTo.Count);
            Assert.Throws<NotSupportedException>(() => ((IList<PackageInfo>)args.removed).Add(null));
        }
    }
}
