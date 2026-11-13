using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>The Sprite Editor window hooks packages register when their editor code loads
    /// (the 2D Sprite package, the SVG importer). Unbound has no editor windows, so the
    /// registrations are kept and never called.</summary>
    [UIFramework(UIFrameworkUsage.IMGUI)]
    internal partial class SpriteUtilityWindow : EditorWindow
    {
        const string NoWindows = "Unbound has no editor windows: a Sprite Editor window is never opened, so the registered callback is kept and never called";

        static Func<Object, bool> s_ShowSpriteEditorWindow;

        [Unbound.EditorFaithful(NoWindows)]
        internal static void SetShowSpriteEditorWindowWithObject(Func<Object, bool> spriteEditorWindow) =>
            s_ShowSpriteEditorWindow = spriteEditorWindow;
    }

    /// <summary>The UI framework an editor window draws with.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal partial class UIFrameworkAttribute : Attribute
    {
        public UIFrameworkAttribute(UIFrameworkUsage frameworkUsage) => this.frameworkUsage = frameworkUsage;

        public UIFrameworkUsage frameworkUsage { get; private set; }
    }

    internal enum UIFrameworkUsage
    {
        AutoDetected = 0,
        IMGUI = 1,
        UITK = 2,
        Mixed = 3,
        Excluded = 4,
    }

    /// <summary>The hyperlink an editor label's click names.</summary>
    public partial class HyperLinkClickedEventArgs
    {
        internal HyperLinkClickedEventArgs(Dictionary<string, string> hyperLinkData) => this.hyperLinkData = hyperLinkData;

        public Dictionary<string, string> hyperLinkData { get; private set; }
    }

    public sealed partial class EditorGUI
    {
        static Action<EditorWindow, HyperLinkClickedEventArgs> s_HyperLinkClicked;

        /// <summary>Raised when a hyperlink in an editor window's label is clicked. Unbound has no
        /// editor windows, so listeners are kept and the event is never raised.</summary>
        public static event Action<EditorWindow, HyperLinkClickedEventArgs> hyperLinkClicked
        {
            [Unbound.EditorFaithful("Unbound has no editor windows: no hyperlink is ever clicked, so listeners are kept and never called")]
            add => s_HyperLinkClicked += value;
            remove => s_HyperLinkClicked -= value;
        }
    }
}

namespace UnityEditor.U2D
{
    /// <summary>A Sprite's ID: the GUID of its Sprite Editor rect, kept with the sprite.</summary>
    public static partial class SpriteEditorExtension
    {
        /// <summary>The ID the sprite was imported with (its .meta's spriteID) or was given by
        /// SetSpriteID; empty for a sprite made at runtime.</summary>
        public static GUID GetSpriteID(this Sprite sprite)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            return sprite.m_SpriteID;
        }

        public static void SetSpriteID(this Sprite sprite, GUID guid)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            sprite.m_SpriteID = guid;
        }
    }
}
