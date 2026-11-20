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
        internal const string MixedStyleName = "ToggleMixed";
        static InspectorTogglePaint s_Light;
        static InspectorTogglePaint s_Dark;

        readonly struct Backgrounds
        {
            internal readonly Texture2D Normal;
            internal readonly Texture2D Hover;
            internal readonly Texture2D Focus;

            internal Backgrounds(string stem)
            {
                Normal = Image(stem + ".png");
                Hover = Image(stem + "_hover.png");
                Focus = Image(stem + "_focus.png");
            }
        }

        readonly string m_Prefix;
        readonly Backgrounds m_Unchecked;
        readonly Backgrounds m_Checked;
        Backgrounds? m_Mixed;

        InspectorTogglePaint(bool dark)
        {
            m_Prefix = "stylesheets/northstar/images/" + (dark ? "d_toggle_" : "toggle_");
            m_Unchecked = new Backgrounds(m_Prefix + "bg");
            m_Checked = new Backgrounds(m_Prefix + "on");
        }

        static Texture2D Image(string path)
        {
            return Unbound.UIElements.UiResources.EditorLoad(path) as Texture2D
                ?? throw new InvalidOperationException($"Unbound: Inspector checkbox image '{path}' is unavailable.");
        }

        internal static void Draw(Rect control, bool hover, bool pressed, bool on, bool focus, bool mixed, float enabledAlpha)
        {
            if (!GUIClip.visibleRect.Overlaps(control)) return;
            var images = EditorGUIUtility.isProSkin
                ? s_Dark ??= new InspectorTogglePaint(dark: true)
                : s_Light ??= new InspectorTogglePaint(dark: false);

            bool enabled = GUI.enabled;
            bool focusImage = enabled && focus && Application.isFocused;
            bool hoverImage = enabled && hover && GUIUtility.hotControl == 0 && IsPointerInView();
            var image = images.Select(on, focusImage, hoverImage, enabled && pressed, mixed);
            float scale = GUIUtility.pixelsPerPoint;
            // At a half pixel the captured Game view places the image left in X and down in Y.
            float x = Mathf.Ceil(control.x * scale - 0.5f) / scale;
            float y = Mathf.Floor((control.center.y - ImageSize * 0.5f) * scale + 0.5f) / scale;
            var rect = new Rect(x, y, ImageSize, ImageSize);
            var tint = GUI.color * GUI.backgroundColor;
            tint.a *= enabledAlpha;
            GuiRender.DrawTexture(rect, image, new Rect(0, 0, 1, 1), tint);
        }

        Texture2D Select(bool on, bool focus, bool hover, bool pressed, bool mixed)
        {
            // Checked images are shared by the ordinary and mixed styles.
            bool checkedImage = on || (pressed && !focus);
            var images = checkedImage ? m_Checked : m_Unchecked;
            if (mixed && !checkedImage)
                images = m_Mixed ??= new Backgrounds(m_Prefix + "mixed_bg");
            if (focus) return images.Focus;
            return hover && !(on && pressed) ? images.Hover : images.Normal;
        }

        static bool IsPointerInView()
        {
            var pointer = Input.s_MousePosition;
            return pointer.x >= 0f && pointer.x < Screen.width && pointer.y > 0f && pointer.y < Screen.height;
        }
    }
}
