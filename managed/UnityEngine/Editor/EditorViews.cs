using Unbound.Runtime;
using UnityEngine;

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
        }
    }

    internal partial class Toolbar : GUIView
    {
        public static Toolbar get;
    }
}
