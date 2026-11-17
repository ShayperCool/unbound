using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The Editor API that package editor code calls while it loads, as the Unity 6000.0.71f1
    /// Editor answers it (tests/projects/editorload): drop handlers, the scene tools and their
    /// events, the product GUID and the case-insensitive class lookup, each next to a control.
    /// </summary>
    public sealed class EditorLoadTests
    {
        static DragAndDropVisualMode Hierarchy(int id, HierarchyDropFlags flags, Transform t, bool perform) => DragAndDropVisualMode.None;
        static DragAndDropVisualMode Inspector(Object[] targets, bool perform) => DragAndDropVisualMode.None;

        [Fact]
        public void DropHandlersRegisterOncePerView()
        {
            DragAndDrop.HierarchyDropHandler hierarchy = Hierarchy;
            DragAndDrop.InspectorDropHandler inspector = Inspector;
            Assert.Equal(new[] { -417863675, 1081994447, 1486780135, 1621880143 },
                new[] { DragAndDropWindowTarget.projectBrowser, DragAndDropWindowTarget.sceneView, DragAndDropWindowTarget.hierarchy, DragAndDropWindowTarget.inspector });
            try
            {
                Assert.False(DragAndDrop.HasHandler(DragAndDropWindowTarget.hierarchy, hierarchy));
                DragAndDrop.AddDropHandler(hierarchy);
                Assert.True(DragAndDrop.HasHandler(DragAndDropWindowTarget.hierarchy, hierarchy));
                // Control: a handler belongs to its own view only.
                Assert.False(DragAndDrop.HasHandler(DragAndDropWindowTarget.inspector, hierarchy));
                Assert.Equal("Delegate already registered for dropDestinationId:1486780135",
                    Assert.Throws<Exception>(() => DragAndDrop.AddDropHandler(hierarchy)).Message);
                DragAndDrop.AddDropHandler(inspector);
                Assert.True(DragAndDrop.HasHandler(DragAndDropWindowTarget.inspector, inspector));
                DragAndDrop.RemoveDropHandler(hierarchy);
                DragAndDrop.RemoveDropHandler(hierarchy);
                Assert.False(DragAndDrop.HasHandler(DragAndDropWindowTarget.hierarchy, hierarchy));
                Assert.False(DragAndDrop.HasHandler(12345, inspector));
            }
            finally
            {
                DragAndDrop.RemoveDropHandler(hierarchy);
                DragAndDrop.RemoveDropHandler(inspector);
            }
        }

        [Fact]
        public void ToolChangesRaiseTheirEventsOnlyWhenTheValueChanges()
        {
            var events = new List<string>();
            Action changing = () => events.Add("changing"), changed = () => events.Add("changed");
            Action pivot = () => events.Add("pivot"), rotation = () => events.Add("rotation");
            var tool = Tools.current;
            var mode = Tools.pivotMode;
            var savedRotation = Tools.pivotRotation;
            // Unbound writes preferences to its own file under the cache: a private one here.
            string cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unbound-prefs-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", directory);
            ToolManager.activeToolChanging += changing;
            ToolManager.activeToolChanged += changed;
            Tools.pivotModeChanged += pivot;
            Tools.pivotRotationChanged += rotation;
            try
            {
                Tools.current = Tool.Move;
                events.Clear();
                Tools.current = Tool.Rotate;
                Assert.Equal(new[] { "changing", "changed" }, events);
                Assert.Equal("UnityEditor.RotateTool", ToolManager.activeToolType.FullName);
                Assert.Equal("UnityEditor.EditorTools.GameObjectToolContext", ToolManager.activeContextType.FullName);
                events.Clear();
                // Control: the same tool again raises nothing.
                Tools.current = Tool.Rotate;
                Assert.Empty(events);
                // Custom, with no custom tool, selects none.
                Tools.current = Tool.Custom;
                Assert.Equal(Tool.None, Tools.current);
                Assert.Equal("UnityEditor.NoneTool", ToolManager.activeToolType.FullName);
                events.Clear();
                Tools.pivotMode = mode == PivotMode.Center ? PivotMode.Pivot : PivotMode.Center;
                Tools.pivotMode = Tools.pivotMode;
                Tools.pivotRotation = Tools.pivotRotation == PivotRotation.Local ? PivotRotation.Global : PivotRotation.Local;
                Assert.Equal(new[] { "pivot", "rotation" }, events);
            }
            finally
            {
                ToolManager.activeToolChanging -= changing;
                ToolManager.activeToolChanged -= changed;
                Tools.pivotModeChanged -= pivot;
                Tools.pivotRotationChanged -= rotation;
                Tools.current = tool;
                Tools.pivotMode = mode;
                Tools.pivotRotation = savedRotation;
                Environment.SetEnvironmentVariable("XDG_CACHE_HOME", cache);
                if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void PivotSettingsLiveInTheEditorPreferences()
        {
            var mode = Tools.pivotMode;
            var rotation = Tools.pivotRotation;
            // Unbound writes preferences to its own file under the cache: a private one here.
            string cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unbound-prefs-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", directory);
            try
            {
                Tools.pivotMode = PivotMode.Pivot;
                Tools.pivotRotation = PivotRotation.Global;
                Assert.Equal((int)PivotMode.Pivot, EditorPrefs.GetInt("PivotMode", -1));
                Assert.Equal((int)PivotRotation.Global, EditorPrefs.GetInt("PivotRotation", -1));
            }
            finally
            {
                Tools.pivotMode = mode;
                Tools.pivotRotation = rotation;
                // Control: setting them back writes them back.
                Assert.Equal((int)mode, EditorPrefs.GetInt("PivotMode", -1));
                Environment.SetEnvironmentVariable("XDG_CACHE_HOME", cache);
                if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void AnIncludeInBuildDelegateCanOnlyLeaveAPluginOut()
        {
            using var engine = Unbound.Runtime.MainThreadCheck.Engine();
            var importer = (PluginImporter)Unbound.Runtime.ObjectConstruction.CreateWithBacking(typeof(PluginImporter));
            try
            {
                string asked = null;
                importer.SetIncludeInBuildDelegate(path => { asked = path; return false; });
                Assert.False(importer.ShouldIncludeInBuild());
                Assert.Equal(importer.assetPath, asked);
                // Control: a delegate that keeps it leaves the importer's own answer.
                importer.SetIncludeInBuildDelegate(path => true);
                Assert.True(importer.ShouldIncludeInBuild());
            }
            finally
            {
                Object.DestroyImmediate(importer);
            }
        }

        [Fact]
        public void ProductGuidReadsTheSettingsInUnitysByteOrder()
        {
            Assert.Equal(new Guid("83698854-3747-92d3-811f-88562ca9becd"), PlayerSettings.ParseProductGuid("4588963874733d2918f18865c29aebdc"));
            // Controls: anything but 32 hex digits is no GUID.
            Assert.Equal(Guid.Empty, PlayerSettings.ParseProductGuid("4588963874733d2918f18865c29aebd"));
            Assert.Equal(Guid.Empty, PlayerSettings.ParseProductGuid("x588963874733d2918f18865c29aebdc"));
            Assert.Equal(Guid.Empty, PlayerSettings.ParseProductGuid(null));
        }

        [Fact]
        public void ClassesAreFoundByNameInAnyCase()
        {
            Assert.Equal("GameObject", UnityType.FindTypeByNameCaseInsensitive("gameobject").name);
            Assert.Equal(4, UnityType.FindTypeByNameCaseInsensitive("TRANSFORM").persistentTypeID);
            Assert.Null(UnityType.FindTypeByNameCaseInsensitive("NoSuchType"));
            Assert.Null(UnityType.FindTypeByNameCaseInsensitive(null));
            // Control: the exact lookup stays case-sensitive.
            Assert.Null(UnityType.FindTypeByName("gameobject"));
            Assert.NotNull(UnityType.FindTypeByName("GameObject"));
        }
    }
}
