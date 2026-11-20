using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor
{
    /// <summary>
    /// The Editor's view tree, as far as editor code reads it: each view's position in its parent,
    /// and where it is on the screen. Unbound's Editor has one main window, the game window, with the
    /// toolbar along its top (<see cref="Toolbar.get"/>, which inspector plugins use to find the
    /// main window).
    /// </summary>
    internal partial class View : ScriptableObject
    {
        View m_Parent;
        Rect m_Position = new Rect(0f, 0f, 100f, 100f);

        public View parent => m_Parent;

        public Rect position
        {
            get => m_Position;
            set => m_Position = value;
        }

        /// <summary>The position within the top view.</summary>
        public Rect windowPosition
        {
            get
            {
                if (m_Parent == null) return m_Position;
                var p = m_Parent.windowPosition;
                return new Rect(p.x + m_Position.x, p.y + m_Position.y, m_Position.width, m_Position.height);
            }
        }

        /// <summary>The position on the screen: the top view is the game window at the origin.</summary>
        public Rect screenPosition => windowPosition;

        internal void AttachTo(View parent) => m_Parent = parent;
    }

    internal partial class GUIView : View
    {
    }

    internal partial class MainView : View
    {
        static MainView s_Main;

        /// <summary>Creates the main window and its toolbar, the size of the game window.</summary>
        internal static void Open()
        {
            if (s_Main != null) return;
            using (ObjectRegistry.AsAssets())
            {
                s_Main = (MainView)CreateInstanceWithoutCallbacks(typeof(MainView));
                var toolbar = (Toolbar)CreateInstanceWithoutCallbacks(typeof(Toolbar));
                s_Main.name = "MainView";
                toolbar.name = "Toolbar";
                s_Main.hideFlags = toolbar.hideFlags = HideFlags.HideAndDontSave;
                s_Main.IsPersistentAsset = toolbar.IsPersistentAsset = true;
                s_Main.position = new Rect(0f, 0f, Screen.width, Screen.height);
                toolbar.position = new Rect(0f, 0f, Screen.width, 36f);
                toolbar.AttachTo(s_Main);
                Toolbar.get = toolbar;
            }
            Toolbar.get.CreateContents();
        }
    }

    /// <summary>
    /// The main toolbar. Its contents are the elements the Editor builds from its toolbar document
    /// (`UXML/Toolbars/MainToolbar.uxml` of the Editor's resources) under a root container
    /// (`m_Root`), which toolbar extensions reflect over to add their own elements to the toolbar's
    /// zones. Matches the Unity 6000.0.71f1 Editor (tests/projects/editorapi toolbar.root.*). Unbound
    /// draws no toolbar: the root has no panel, the Editor's own buttons are not created in the
    /// zones, and what extensions add there is kept but never drawn.
    /// </summary>
    internal partial class Toolbar : GUIView
    {
        internal const string MainToolbarDocument = "UXML/Toolbars/MainToolbar.uxml";
        const string k_MainToolbarUSSClassName = "unity-editor-main-toolbar";
        const string k_RootName = "rootVisualContainer";

        public static Toolbar get;

        VisualElement m_Root;

        /// <summary>(Re)creates the toolbar's contents from the Editor's toolbar document.</summary>
        internal void CreateContents()
        {
            var document = UiResources.EditorLoad(MainToolbarDocument) as VisualTreeAsset;
            // Without the native side (managed tests) no resource of the Editor is readable.
            if (document == null && Native.IsReady)
                Debug.LogError($"Unbound: the main toolbar's document '{MainToolbarDocument}' is not available (it comes from the Unity Editor's resources).");
            CreateContents(document);
        }

        /// <summary>The root container, holding a clone of <paramref name="document"/> (none without one)
        /// with the toolbar's style sheets.</summary>
        internal void CreateContents(VisualTreeAsset document)
        {
            m_Root?.RemoveFromHierarchy();
            m_Root = CreateRoot();
            var contents = new VisualElement();
            contents.style.flexGrow = 1f;
            if (document != null) document.CloneTree(contents);
            AddStyleSheet(contents, "StyleSheets/Toolbars/MainToolbarCommon.uss");
            AddStyleSheet(contents, "StyleSheets/Toolbars/MainToolbar" + (EditorGUIUtility.isProSkin ? "Dark" : "Light") + ".uss");
            m_Root.Add(contents);
        }

        static VisualElement CreateRoot()
        {
            var root = new VisualElement
            {
                name = UniqueName(k_RootName),
                pickingMode = PickingMode.Ignore,
                viewDataKey = k_RootName,
            };
            root.pseudoStates |= PseudoStates.Root;
            root.AddToClassList(k_MainToolbarUSSClassName);
            root.StretchToParentSize();
            root.style.overflow = Overflow.Hidden;
            return root;
        }

        static void AddStyleSheet(VisualElement target, string path)
        {
            if (UiResources.EditorLoad(path) is StyleSheet sheet) target.styleSheets.Add(sheet);
        }

        static readonly HashSet<string> s_UsedNames = new HashSet<string>();

        /// <summary>The Editor's unique element names: the base name, then the base name with 2, 3, ...
        /// for each later request within the session.</summary>
        static string UniqueName(string nameBase)
        {
            string name = nameBase;
            for (int n = 2; s_UsedNames.Contains(name); n++) name = nameBase + n;
            s_UsedNames.Add(name);
            return name;
        }
    }
}
