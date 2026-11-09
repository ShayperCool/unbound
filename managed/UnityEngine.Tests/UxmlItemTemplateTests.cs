using System;
using System.Collections.Generic;
using System.Linq;
using ItemTemplateProbes;
using Unbound.Runtime;
using Unbound.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Xunit;
using Object = UnityEngine.Object;

// UXML ListView `item-template`: a reference to a UXML template (VisualTreeAsset) whose instances
// are the list's rows. The TreeView cases read the same attribute on <TreeView>
// (TreeView.itemTemplate); their red base is 7e4d5b9c, before the TreeView mapping. Each test
// names its source:
//   [doc]    the Unity 6000.0 Manual "ListView" (UXML attribute `item-template`, type
//            UIElements.VisualTreeAsset: "A UXML template that constructs each recycled and rebound
//            element within the list. This template is designed to replace the makeItem
//            definition."), the Scripting API ListView.itemTemplate (same text), and the Manual
//            "Reference other files from UXML" (absolute paths begin with `/` or
//            `project://database/`; relative paths start from the current file);
//   [measured] the uxmlattributes probe (tests/projects/uxmlattributes, listTemplate*, treeTemplate,
//            Unity 6000.0.71f1): the attribute is read as a [UxmlAttribute] VisualTreeAsset member
//            is: an unresolved url logs the importer's warning and gives no template, an asset of
//            another class logs an ArgumentException and the list keeps no template;
//   [choice] Unbound's choice where neither says anything: only the documented attribute name is
//            read.
//
// Rows without bindItem follow the Scripting API ListView.itemTemplate and
// BaseListView.bindingSourceSelectionMode (AutoAssign: "the bind callbacks don't need to be
// specified").
//
// Assets are registered with AssetDatabaseRuntime.AddLoaded and found by the reference's guid and
// fileID: without the native side, the resolver cannot look a path up (AssetDatabaseRuntime.InfoByPath
// is a native call), so a reference by path alone resolves to nothing in these tests.

namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UxmlItemTemplateTests
    {
        const string Path = "Assets/UI/Screens/UxmlItemTemplateProbe.uxml";
        const string RowPath = "Assets/UI/Rows/Row.uxml";
        const string RowGuid = "c16c9d7e8f0a4b1c4d5e6f708192a3b4";
        const string SheetGuid = "d27dae8f901b4c2d5e6f708192a3b4c5";
        const long SheetId = 7433441132597879392;

        /// <summary>The project-GUID reference UI Builder writes, here to <paramref name="fileId"/> of
        /// <paramref name="guid"/>, escaped for an XML attribute.</summary>
        static string Ref(string assetPath, string guid, long fileId, string name) =>
            $"project://database/{assetPath}?fileID={fileId}&amp;guid={guid}&amp;type=3#{name}";

        static string RowRef => Ref(RowPath, RowGuid, UxmlImporter.VisualTreeAssetFileId, "Row");

        static (VisualElement Root, VisualTreeAsset Asset) Clone(string body)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = "UxmlItemTemplateProbe";
            UxmlImporter.Import(Path, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" + body + "</ui:UXML>", asset);
            var root = new VisualElement();
            asset.CloneTree(root);
            return (root, asset);
        }

        /// <summary>Warnings and errors logged while <paramref name="action"/> runs that name this
        /// file's document (other tests may log at the same time).</summary>
        static List<string> Logged(Action action)
        {
            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type != LogType.Log && message != null && message.Contains(Path)) lock (logs) logs.Add(message);
            };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return logs;
        }

        /// <summary>Warnings, errors and exceptions logged while <paramref name="action"/> runs whose
        /// text contains <paramref name="part"/>, as "Type message".</summary>
        static List<string> LoggedMatching(string part, Action action)
        {
            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type != LogType.Log && message != null && message.Contains(part)) lock (logs) logs.Add(type + " " + message);
            };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return logs;
        }

        /// <summary>A row template file, as Unity's UXML importer makes it: a VisualTreeAsset main
        /// asset holding one Label named "row-label".</summary>
        sealed class RowFile : IDisposable
        {
            internal readonly VisualTreeAsset Template;
            readonly LoadedFile m_File;

            internal RowFile()
            {
                Template = ScriptableObject.CreateInstance<VisualTreeAsset>();
                Template.name = "Row";
                UxmlImporter.Import(RowPath, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label name=\"row-label\" /></ui:UXML>", Template);
                Template.IsPersistentAsset = true;
                m_File = new LoadedFile { Guid = RowGuid, Path = RowPath, MainObject = Template };
                m_File.Objects[UxmlImporter.VisualTreeAssetFileId] = Template;
                AssetDatabaseRuntime.AddLoaded(m_File);
            }

            public void Dispose()
            {
                AssetDatabaseRuntime.Invalidate(m_File.Guid);
                Object.DestroyImmediate(Template, true);
            }
        }

        /// <summary>The list's active rows by index, made for a 100x100 viewport of 20-pixel rows.</summary>
        static List<VisualElement> Rows(ListView list)
        {
            list.virtualizationController.Resize(new Vector2(100f, 100f));
            return list.activeItems.OrderBy(r => r.index).Select(r => r.bindableElement).ToList();
        }

        /// <summary>A ListView cloned from UXML with the row template, 20-pixel rows and three
        /// items, and no callback set by the test.</summary>
        static ListView TemplatedList(RowFile row)
        {
            var list = Assert.IsType<ListView>(Clone($"<ui:ListView item-template=\"{RowRef}\" />").Root[0]);
            Assert.Same(row.Template, list.itemTemplate);
            list.fixedItemHeight = 20f;
            list.itemsSource = new List<string> { "a", "b", "c" };
            return list;
        }

        // ------------------------------------------------------------------ resolution and rows

        // [doc] `item-template` takes a VisualTreeAsset, a UXML template that "constructs each
        // recycled and rebound element within the list"; the project-GUID form is a
        // `project://database/` absolute reference.
        // Red at 4a0c27ea: the builder cannot convert the text to a VisualTreeAsset and drops it
        // silently; Assert.Same(row.Template, list.itemTemplate) fails (null).
        [Fact]
        public void ItemTemplateResolvesAndRowsAreTemplateInstances()
        {
            using var row = new RowFile();
            VisualElement root = null;
            var logs = Logged(() => root = Clone($"<ui:ListView item-template=\"{RowRef}\" />").Root);
            var list = Assert.IsType<ListView>(root[0]);
            Assert.Same(row.Template, list.itemTemplate);
            Assert.Empty(logs);

            var bound = new List<(VisualElement Element, int Index)>();
            list.fixedItemHeight = 20f;
            list.bindItem = (e, i) => bound.Add((e, i));
            list.itemsSource = new List<string> { "a", "b", "c" };
            var rows = Rows(list);
            Assert.Equal(3, rows.Count);
            foreach (var element in rows)
            {
                var container = Assert.IsType<TemplateContainer>(element);
                Assert.Same(row.Template, container.templateSource);
                Assert.NotNull(container.Q<Label>("row-label"));
            }
            Assert.Equal(3, rows.Distinct().Count());
            Assert.Equal(new[] { 0, 1, 2 }, bound.Select(b => b.Index).OrderBy(i => i));
            Assert.All(bound, b => Assert.Contains(b.Element, rows));
        }

        // [doc] The same attribute on a game [UxmlElement] subclass of ListView, whose undeclared
        // attributes are ListView's own (the item template replaces makeItem).
        // Red at 4a0c27ea: the attribute is dropped silently; Assert.Same(row.Template,
        // list.itemTemplate) fails (null).
        [Fact]
        public void ItemTemplateAppliesToAListViewSubclass()
        {
            using var row = new RowFile();
            var (root, _) = Clone($"<ItemTemplateProbes.TemplatedListProbe item-template=\"{RowRef}\" />");
            var list = Assert.IsType<TemplatedListProbe>(root[0]);
            Assert.Same(row.Template, list.itemTemplate);
            var made = Assert.IsType<TemplateContainer>(list.makeItem());
            Assert.Same(row.Template, made.templateSource);
        }

        // ------------------------------------------------------------------ rows without bindItem

        // [doc] A template list with no bindItem is shown only when its rows get their data sources
        // automatically (bindingSourceSelectionMode AutoAssign): each row's data source is the items
        // source and its data source path the row's index, so bindings in the template read the item.
        [Fact]
        public void TemplateRowsWithoutBindItemNeedAutoAssignedSources()
        {
            using var row = new RowFile();
            var list = TemplatedList(row);
            Assert.Null(list.bindItem);
            Assert.False(list.HasValidDataAndBindings());
            list.bindingSourceSelectionMode = BindingSourceSelectionMode.AutoAssign;
            Assert.True(list.HasValidDataAndBindings());
            var rows = Rows(list);
            Assert.Equal(3, rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var container = Assert.IsType<TemplateContainer>(rows[i]);
                Assert.Same(row.Template, container.templateSource);
                Assert.Same(list.itemsSource, container.dataSource);
                Assert.Equal("[" + i + "]", container.dataSourcePath.ToString());
            }
        }

        // [doc] Control: a game makeItem without bindItem is still rejected, also on a list that had
        // a template: replacing the template's makeItem ends the exemption, and binding a row throws.
        [Fact]
        public void AGameMakeItemWithoutBindItemIsStillRejected()
        {
            using var row = new RowFile();
            var list = TemplatedList(row);
            list.makeItem = () => new Label();
            Assert.Same(row.Template, list.itemTemplate);
            Assert.Null(list.bindItem);
            Assert.False(list.HasValidDataAndBindings());

            var error = Assert.Throws<NotImplementedException>(() => Rows(list));
            Assert.Equal("You must specify bindItem if makeItem is specified.", error.Message);
        }

        // [doc] Control: a template list with a bindItem binds every row through it.
        [Fact]
        public void ATemplateWithBindItemBindsAsBefore()
        {
            using var row = new RowFile();
            var list = TemplatedList(row);
            var bound = new List<int>();
            list.bindItem = (e, i) => bound.Add(i);
            Assert.True(list.HasValidDataAndBindings());

            var rows = Rows(list);
            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.IsType<TemplateContainer>(r));
            Assert.Equal(new[] { 0, 1, 2 }, bound.OrderBy(i => i));
        }

        // Control: without a template the callback pairing is unchanged. No callbacks: valid, rows
        // are labels showing ToString(). A makeItem without bindItem: not valid, and binding throws.
        [Fact]
        public void WithoutATemplateThePairingIsUnchanged()
        {
            var plain = new ListView { fixedItemHeight = 20f, itemsSource = new List<string> { "a", "b", "c" } };
            Assert.Null(plain.itemTemplate);
            Assert.True(plain.HasValidDataAndBindings());
            Assert.Equal(new[] { "a", "b", "c" }, Rows(plain).Select(r => Assert.IsType<Label>(r).text));

            var unpaired = new ListView { fixedItemHeight = 20f, itemsSource = new List<string> { "a", "b", "c" } };
            unpaired.makeItem = () => new Label();
            Assert.Null(unpaired.itemTemplate);
            Assert.False(unpaired.HasValidDataAndBindings());
            Assert.Throws<NotImplementedException>(() => Rows(unpaired));

        }

        // [doc] Control: the automatic sources depend on makeItem, not on the template: a cleared
        // template keeps its factory as makeItem, so the list stays valid with AutoAssign and not
        // with Manual.
        [Fact]
        public void AutoAssignedSourcesDependOnMakeItem()
        {
            using var row = new RowFile();
            var list = TemplatedList(row);
            list.bindingSourceSelectionMode = BindingSourceSelectionMode.AutoAssign;
            list.itemTemplate = null;
            Assert.NotNull(list.makeItem);
            Assert.True(list.HasValidDataAndBindings());
            list.bindingSourceSelectionMode = BindingSourceSelectionMode.Manual;
            Assert.False(list.HasValidDataAndBindings());
        }

        // ------------------------------------------------------------------ diagnostics

        // [measured] listTemplateMissing: a reference that resolves to nothing logs the importer's
        // warning for the url; the list has no template and no makeItem.
        [Fact]
        public void UnresolvedReferenceWarnsAndLeavesNoTemplate()
        {
            VisualElement root = null;
            var logs = LoggedMatching("Missing.uxml", () => root = Clone("<ui:ListView item-template=\"Rows/Missing.uxml\" />").Root);
            var list = Assert.IsType<ListView>(root[0]);
            var warning = Assert.Single(logs);
            Assert.StartsWith("Warning ", warning);
            Assert.Contains("Semantic - The specified URI does not exist in the current project", warning);
            Assert.Null(list.itemTemplate);
            Assert.Null(list.makeItem);
        }

        // [measured] listTemplateWrongType: an asset of another class (here a StyleSheet) cannot be
        // assigned; the ArgumentException is logged and the list keeps no template.
        [Fact]
        public void ReferenceToAnotherAssetClassLogsTheAssignmentFailure()
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            sheet.name = "Row";
            sheet.IsPersistentAsset = true;
            var file = new LoadedFile { Guid = SheetGuid, Path = "Assets/UI/Rows/Row.uss", MainObject = sheet };
            file.Objects[SheetId] = sheet;
            AssetDatabaseRuntime.AddLoaded(file);
            try
            {
                VisualElement root = null;
                var logs = LoggedMatching("VisualTreeAsset", () => root = Clone($"<ui:ListView item-template=\"{Ref("Assets/UI/Rows/Row.uss", SheetGuid, SheetId, "Row")}\" />").Root);
                var list = Assert.IsType<ListView>(root[0]);
                var error = Assert.Single(logs);
                Assert.Equal("Exception ArgumentException: Object of type 'UnityEngine.UIElements.StyleSheet' cannot be converted to type 'UnityEngine.UIElements.VisualTreeAsset'.", error);
                Assert.Null(list.itemTemplate);
                Assert.Null(list.makeItem);
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(SheetGuid);
                Object.DestroyImmediate(sheet, true);
            }
        }

        // [measured] presetListMissing, presetListWrongType, presetListNone: on a list whose template
        // is set before the attributes are read, an unresolved reference clears the template, one of
        // another class keeps it (the assignment fails), and no attribute leaves it.
        [Fact]
        public void APresetTemplateIsClearedByAMissingReferenceAndKeptOnAnotherClass()
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            sheet.name = "Row";
            sheet.IsPersistentAsset = true;
            var file = new LoadedFile { Guid = SheetGuid, Path = "Assets/UI/Rows/Row.uss", MainObject = sheet };
            file.Objects[SheetId] = sheet;
            AssetDatabaseRuntime.AddLoaded(file);
            try
            {
                var (root, _) = Clone(
                    "<ItemTemplateProbes.PresetTemplateList item-template=\"Rows/Missing.uxml\" />"
                    + $"<ItemTemplateProbes.PresetTemplateList item-template=\"{Ref("Assets/UI/Rows/Row.uss", SheetGuid, SheetId, "Row")}\" />"
                    + "<ItemTemplateProbes.PresetTemplateList />");
                Assert.Null(Assert.IsType<PresetTemplateList>(root[0]).itemTemplate);
                Assert.Same(PresetTemplateList.Preset, Assert.IsType<PresetTemplateList>(root[1]).itemTemplate);
                Assert.Same(PresetTemplateList.Preset, Assert.IsType<PresetTemplateList>(root[2]).itemTemplate);
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(SheetGuid);
                Object.DestroyImmediate(sheet, true);
            }
        }

        // ------------------------------------------------------------------ TreeView

        // [doc] TreeView.itemTemplate: "A UXML template that constructs each recycled and rebound
        // element within the tree. This template is designed to replace the makeItem definition."
        // [doc] 6000.0 Manual "TreeView": UXML attribute `item-template` of type UIElements.VisualTreeAsset.
        // [measured] treeTemplate: it is read as on <ListView>, on the property TreeView declares
        // (the diagnostics as measured on ListView).
        // Red at 7e4d5b9c (this file overlaid; every member it uses exists there): the builder cannot
        // convert the text to a VisualTreeAsset and drops it silently; Assert.Same(row.Template,
        // view.itemTemplate) fails (null).
        [Fact]
        public void TreeViewItemTemplateResolvesAndRowsAreTemplateInstances()
        {
            using var row = new RowFile();
            VisualElement root = null;
            var logs = Logged(() => root = Clone($"<ui:TreeView item-template=\"{RowRef}\" />").Root);
            var view = Assert.IsType<TreeView>(root[0]);
            Assert.Same(row.Template, view.itemTemplate);
            Assert.Empty(logs);

            var bound = new List<(VisualElement Element, int Index)>();
            view.fixedItemHeight = 20f;
            view.bindItem = (e, i) => bound.Add((e, i));
            view.SetRootItems(new List<TreeViewItemData<string>>
            {
                new TreeViewItemData<string>(1, "a"),
                new TreeViewItemData<string>(2, "b"),
                new TreeViewItemData<string>(3, "c"),
            });
            view.virtualizationController.Resize(new Vector2(100f, 100f));
            var rows = view.activeItems.OrderBy(r => r.index).Select(r => r.bindableElement).ToList();
            Assert.Equal(3, rows.Count);
            foreach (var element in rows)
            {
                var container = Assert.IsType<TemplateContainer>(element);
                Assert.Same(row.Template, container.templateSource);
                Assert.NotNull(container.Q<Label>("row-label"));
            }
            Assert.Equal(3, rows.Distinct().Count());
            Assert.Equal(new[] { 0, 1, 2 }, bound.Select(b => b.Index).OrderBy(i => i));
            Assert.All(bound, b => Assert.Contains(b.Element, rows));
        }

        // [measured] treeTemplate with listTemplateMissing: on <TreeView> an unresolved reference also
        // logs the importer's warning; the tree has no template and no makeItem.
        [Fact]
        public void TreeViewUnresolvedReferenceWarnsAndLeavesNoTemplate()
        {
            VisualElement root = null;
            var logs = LoggedMatching("Missing.uxml", () => root = Clone("<ui:TreeView item-template=\"Rows/Missing.uxml\" />").Root);
            var view = Assert.IsType<TreeView>(root[0]);
            var warning = Assert.Single(logs);
            Assert.Contains("Semantic - The specified URI does not exist in the current project", warning);
            Assert.Null(view.itemTemplate);
            Assert.Null(view.makeItem);
        }

        // ------------------------------------------------------------------ guards

        // Guard (passes at 4a0c27ea and at head): without the attribute nothing changes; the list
        // has no template, no makeItem, logs nothing, and its rows are its default labels.
        // Red only under a mutation that assigns a template without the attribute (e.g. the
        // ListView constructor setting itemTemplate): Assert.Null(list.itemTemplate) fails.
        [Fact]
        public void AbsentAttributeLeavesTheListUnchanged()
        {
            VisualElement root = null;
            var logs = Logged(() => root = Clone("<ui:ListView />").Root);
            var list = Assert.IsType<ListView>(root[0]);
            Assert.Null(list.itemTemplate);
            Assert.Null(list.makeItem);
            Assert.Empty(logs);

            list.fixedItemHeight = 20f;
            list.itemsSource = new List<string> { "a" };
            var label = Assert.IsType<Label>(Assert.Single(Rows(list)));
            Assert.Equal("a", label.text);
        }

        // [choice] Guard (passes at 4a0c27ea and at head): only the documented name `item-template`
        // is read; the property's own spelling stays an ignored attribute, as before.
        // Red only under a mutation dropping the attribute-name check in IsItemTemplate (the
        // reference then resolves): Assert.Null(list.itemTemplate) fails.
        [Fact]
        public void OnlyTheDocumentedAttributeNameIsRead()
        {
            using var row = new RowFile();
            VisualElement root = null;
            var logs = Logged(() => root = Clone($"<ui:ListView itemTemplate=\"{RowRef}\" />").Root);
            var list = Assert.IsType<ListView>(root[0]);
            Assert.Null(list.itemTemplate);
            Assert.Empty(logs);
        }
    }
}

namespace ItemTemplateProbes
{
    [UxmlElement]
    public partial class TemplatedListProbe : ListView
    {
    }

    [UxmlElement]
    public partial class PresetTemplateList : ListView
    {
        internal static readonly VisualTreeAsset Preset = CreatePreset();

        static VisualTreeAsset CreatePreset()
        {
            var template = ScriptableObject.CreateInstance<VisualTreeAsset>();
            template.name = "Preset";
            return template;
        }

        public PresetTemplateList() => itemTemplate = Preset;
    }
}
