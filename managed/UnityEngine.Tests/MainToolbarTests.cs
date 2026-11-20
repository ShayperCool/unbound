using System;
using System.Reflection;
using System.Text.Json;
using Unbound.UIElements;
using UnityEditor;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The main toolbar's root container, as the Unity 6000.0.71f1 Editor makes it
    /// (tests/projects/editorapi toolbar.root.*): toolbar extensions find the toolbar with
    /// Resources.FindObjectsOfTypeAll, read its private `m_Root` and add their elements to the zones
    /// of the Editor's toolbar document. Also the reader of the Editor's serialized UXML documents
    /// the toolbar's document comes from.
    /// </summary>
    public sealed class MainToolbarTests
    {
        const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>The toolbar document's elements as the 6000.0.71f1 Editor's resources serialize them
        /// (ids, parent ids, order among siblings, attributes as name/value pairs), listed here out of
        /// document order.</summary>
        static readonly string ToolbarDocument = @"{
            ""m_Name"": ""MainToolbar"",
            ""m_ContentHash"": ""123"",
            ""m_ImportedWithErrors"": ""0"",
            ""m_ImportedWithWarnings"": ""0"",
            ""inlineSheet"": { ""fileID"": 0 },
            ""m_TemplateAssets"": [],
            ""m_UxmlObjectEntries"": [],
            ""m_Slots"": [],
            ""m_VisualElementAssets"": [
                " + Element(1384197131, -938877913, 3, "ToolbarZonePlayMode", Zone) + @",
                " + Element(764578750, 0, 0, null, null, "UnityEngine.UIElements.UXML") + @",
                " + Element(-938877913, 764578750, 1, "ToolbarContainerContent", "unity-editor-toolbar-container") + @",
                " + Element(131498425, -938877913, 2, "ToolbarZoneLeftAlign", Zone) + @",
                " + Element(-319259532, 131498425, 3, "ToolbarProductCaption", "unity-editor-toolbar-product-caption") + @",
                " + Element(-136937164, -938877913, 4, "ToolbarZoneRightAlign", Zone) + @"
            ]
        }";

        const string Zone = "unity-editor-toolbar-container__zone";

        static string Element(int id, int parent, int order, string name, string cls, string type = "UnityEngine.UIElements.VisualElement")
        {
            string properties = name == null ? "" : $@"""name"", ""{name}"", ""class"", ""{cls}""";
            string classes = cls == null ? "" : $@"""{cls}""";
            return $@"{{ ""m_Id"": ""{id}"", ""m_ParentId"": ""{parent}"", ""m_OrderInDocument"": ""{order}"",
                ""m_FullTypeName"": ""{type}"", ""m_RuleIndex"": ""-1"", ""m_Text"": """",
                ""m_Classes"": [{classes}], ""m_StylesheetPaths"": [], ""m_Stylesheets"": [], ""m_Properties"": [{properties}] }}";
        }

        static VisualTreeAsset Read(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return UiResources.ReadVisualTreeAsset(doc.RootElement);
        }

        [Fact]
        public void ASerializedEditorDocumentClonesItsElementsInDocumentOrder()
        {
            var document = Read(ToolbarDocument);
            Assert.NotNull(document);
            Assert.Equal("MainToolbar", document.name);
            Assert.Equal(123, document.contentHash);
            var target = new VisualElement();
            document.CloneTree(target);
            var content = Assert.Single(target.Children());
            Assert.Equal("ToolbarContainerContent", content.name);
            Assert.True(content.ClassListContains("unity-editor-toolbar-container"));
            Assert.Equal(new[] { "ToolbarZoneLeftAlign", "ToolbarZonePlayMode", "ToolbarZoneRightAlign" },
                Array.ConvertAll(new System.Collections.Generic.List<VisualElement>(content.Children()).ToArray(), e => e.name));
            foreach (var zone in content.Children()) Assert.True(zone.ClassListContains(Zone));
            var caption = Assert.Single(content.Q("ToolbarZoneLeftAlign").Children());
            Assert.Equal("ToolbarProductCaption", caption.name);
        }

        [Fact]
        public void ADocumentWithWhatTheReaderDoesNotReadIsRefused()
        {
            // Control: the same document with a template instance, an element outside <UXML>, or
            // inline styles.
            Assert.Null(Read(ToolbarDocument.Replace(@"""m_TemplateAssets"": []", @"""m_TemplateAssets"": [{}]")));
            Assert.Null(Read(ToolbarDocument.Replace(Element(764578750, 0, 0, null, null, "UnityEngine.UIElements.UXML"),
                Element(764578750, 0, 0, null, null, "UnityEngine.UIElements.VisualElement"))));
            Assert.Null(Read(ToolbarDocument.Replace(@"""m_RuleIndex"": ""-1""", @"""m_RuleIndex"": ""0""")));
        }

        [Fact]
        public void ToolbarExtensionsReachTheZonesThroughTheToolbarsRoot()
        {
            MainView.Open();
            Toolbar.get.CreateContents(Read(ToolbarDocument));
            // What toolbar extensions do: find the toolbar, read its root by reflection, add to its zones.
            var toolbarType = typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");
            Assert.NotNull(toolbarType);
            var toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
            Assert.Contains(Toolbar.get, toolbars);
            var field = toolbarType.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.Equal(typeof(VisualElement), field.FieldType);
            var root = field.GetValue(Toolbar.get) as VisualElement;
            Assert.NotNull(root);
            Assert.StartsWith("rootVisualContainer", root.name);
            Assert.Equal(PickingMode.Ignore, root.pickingMode);
            Assert.True(root.ClassListContains("unity-editor-main-toolbar"));
            foreach (var zone in new[] { "ToolbarZoneLeftAlign", "ToolbarZoneRightAlign" })
            {
                var target = root.Q(zone);
                Assert.NotNull(target);
                var container = new IMGUIContainer();
                target.Add(container);
                Assert.Same(target, container.parent);
            }
            // The Editor's own toolbar buttons are not created: the zones hold the document's
            // elements and what was added.
            Assert.Equal(2, root.Q("ToolbarZoneLeftAlign").childCount);
            Assert.Equal(1, root.Q("ToolbarZoneRightAlign").childCount);
        }

        [Fact]
        public void RecreatedContentsReplaceTheRootWithANewlyNamedOne()
        {
            MainView.Open();
            var field = typeof(Toolbar).GetField("m_Root", Instance);
            Toolbar.get.CreateContents(Read(ToolbarDocument));
            var first = (VisualElement)field.GetValue(Toolbar.get);
            Toolbar.get.CreateContents(null);
            var second = (VisualElement)field.GetValue(Toolbar.get);
            Assert.NotSame(first, second);
            Assert.NotEqual(first.name, second.name);
            // Control: without the document the root has no zones.
            Assert.Null(second.Q("ToolbarZoneLeftAlign"));
        }
    }
}
