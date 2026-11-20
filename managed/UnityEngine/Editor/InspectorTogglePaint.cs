using System;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>Checkbox backgrounds from the Inspector theme. The image occupies a 14-point
    /// square at the left edge and the vertical center of the control (tests/projects/editorstyles,
    /// Unity 6000.0.71f1).</summary>
    internal sealed class InspectorTogglePaint
    {
        const float ImageSize = 14f;
        static InspectorTogglePaint s_Light;
        static InspectorTogglePaint s_Dark;

        readonly Texture2D m_Unchecked;
        readonly Texture2D m_UncheckedHover;
        readonly Texture2D m_UncheckedFocus;
        readonly Texture2D m_Checked;
        readonly Texture2D m_CheckedHover;
        readonly Texture2D m_CheckedFocus;

        InspectorTogglePaint(bool dark)
        {
            string prefix = "stylesheets/northstar/images/" + (dark ? "d_toggle_" : "toggle_");
            m_Unchecked = Image(prefix + "bg.png");
            m_UncheckedHover = Image(prefix + "bg_hover.png");
            m_UncheckedFocus = Image(prefix + "bg_focus.png");
            m_Checked = Image(prefix + "on.png");
            m_CheckedHover = Image(prefix + "on_hover.png");
            m_CheckedFocus = Image(prefix + "on_focus.png");
        }

        static Texture2D Image(string path)
        {
            return Unbound.UIElements.UiResources.EditorLoad(path) as Texture2D
                ?? throw new InvalidOperationException($"Unbound: Inspector checkbox image '{path}' is unavailable.");
        }

        internal static void Draw(Rect control, bool hover, bool pressed, bool on, bool focus, float enabledAlpha)
        {
            if (!GUIClip.visibleRect.Overlaps(control)) return;
            var images = EditorGUIUtility.isProSkin
                ? s_Dark ??= new InspectorTogglePaint(dark: true)
                : s_Light ??= new InspectorTogglePaint(dark: false);

            bool enabled = GUI.enabled;
            bool focusImage = enabled && focus && Application.isFocused;
            bool hoverImage = enabled && hover && GUIUtility.hotControl == 0 && IsPointerInView();
            var image = images.Select(on, focusImage, hoverImage, enabled && pressed);
            float scale = GUIUtility.pixelsPerPoint;
            // At a half pixel the captured Game view places the image left in X and down in Y.
            float x = Mathf.Ceil(control.x * scale - 0.5f) / scale;
            float y = Mathf.Floor((control.center.y - ImageSize * 0.5f) * scale + 0.5f) / scale;
            var rect = new Rect(x, y, ImageSize, ImageSize);
            var tint = GUI.color * GUI.backgroundColor;
            tint.a *= enabledAlpha;
            GuiRender.DrawTexture(rect, image, new Rect(0, 0, 1, 1), tint);
        }

        Texture2D Select(bool on, bool focus, bool hover, bool pressed)
        {
            if (focus) return on ? m_CheckedFocus : m_UncheckedFocus;
            // The checked pressed state uses the checked image even with hover.
            if (on && pressed) return m_Checked;
            if (on || pressed) return hover ? m_CheckedHover : m_Checked;
            return hover ? m_UncheckedHover : m_Unchecked;
        }

        static bool IsPointerInView()
        {
            var pointer = Input.s_MousePosition;
            return pointer.x >= 0f && pointer.x < Screen.width && pointer.y > 0f && pointer.y < Screen.height;
        }
    }
}
