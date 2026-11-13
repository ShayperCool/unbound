using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor
{
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
