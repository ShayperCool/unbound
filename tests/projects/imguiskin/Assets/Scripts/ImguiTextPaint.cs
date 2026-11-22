using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>Draws texts of the game skin every Repaint and writes the Game view's pixels of that
/// region once, at the end of a later frame: IMGUI text as it is rasterized (sizes, a bold and a
/// tinted style, wrapping, alignments, a clipped and a disabled text), and images placed left of,
/// above and without text: aligned, scaled into their room, and at an icon size.</summary>
public sealed class ImguiTextPaint : MonoBehaviour
{
    const int Width = 320;
    const int Height = 180;
    const int FramesBeforeCapture = 6;

    string m_Dir;
    Texture2D m_Back;
    Texture2D m_Small, m_Large;
    GUIStyle m_Plain, m_Big, m_Bold, m_Wrap, m_Centered, m_Right, m_Tinted, m_Clipped;
    GUIStyle m_ImageLeft, m_ImageLeftCentered, m_ImageLeftMiddle, m_ImageAbove, m_ImageLeftWrap, m_ImageOnly;

    public bool Done { get; private set; }

    public static ImguiTextPaint Create(string dir)
    {
        var paint = new GameObject("IMGUI text paint").AddComponent<ImguiTextPaint>();
        paint.m_Dir = dir;
        return paint;
    }

    IEnumerator Start()
    {
        for (int i = 0; i < FramesBeforeCapture; i++) yield return new WaitForEndOfFrame();
        var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, Screen.height - Height, Width, Height), 0, 0);
        File.WriteAllBytes(Path.Combine(m_Dir, "text-paint.png"), image.EncodeToPNG());
        Done = true;
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (m_Back == null)
        {
            m_Back = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            m_Back.SetPixel(0, 0, new Color(0.15f, 0.2f, 0.3f, 1f));
            m_Back.Apply();
            m_Plain = new GUIStyle { normal = { textColor = Color.white } };
            m_Big = new GUIStyle(m_Plain) { fontSize = 24 };
            m_Bold = new GUIStyle(m_Plain) { fontSize = 16, fontStyle = FontStyle.Bold };
            m_Wrap = new GUIStyle(m_Plain) { wordWrap = true };
            m_Centered = new GUIStyle(m_Plain) { alignment = TextAnchor.MiddleCenter };
            m_Right = new GUIStyle(m_Plain) { alignment = TextAnchor.LowerRight };
            m_Tinted = new GUIStyle(m_Plain) { normal = { textColor = new Color(1f, 0.8f, 0.2f, 0.75f) } };
            m_Clipped = new GUIStyle(m_Plain) { clipping = TextClipping.Clip };
            m_Small = Quadrants(20);
            m_Large = Quadrants(40);
            m_ImageLeft = new GUIStyle(m_Plain) { imagePosition = ImagePosition.ImageLeft };
            m_ImageLeftCentered = new GUIStyle(m_ImageLeft) { alignment = TextAnchor.MiddleCenter };
            m_ImageLeftMiddle = new GUIStyle(m_ImageLeft) { alignment = TextAnchor.MiddleLeft };
            m_ImageAbove = new GUIStyle(m_Plain) { imagePosition = ImagePosition.ImageAbove, alignment = TextAnchor.MiddleCenter };
            m_ImageLeftWrap = new GUIStyle(m_ImageLeft) { wordWrap = true };
            m_ImageOnly = new GUIStyle(m_Plain) { imagePosition = ImagePosition.ImageOnly, alignment = TextAnchor.LowerRight };
        }
        GUI.DrawTexture(new Rect(0, 0, Width, Height), m_Back);
        Draw(m_Plain, new Rect(4, 2, 150, 16), "Hello World 0123456789");
        Draw(m_Big, new Rect(4, 18, 150, 30), "Big Text");
        Draw(m_Bold, new Rect(4, 48, 150, 20), "Bold gjpqy");
        Draw(m_Wrap, new Rect(4, 68, 90, 42), "One two three four five six seven");
        Draw(m_Centered, new Rect(160, 2, 156, 24), "Centered");
        Draw(m_Right, new Rect(160, 26, 156, 24), "Lower right");
        Draw(m_Tinted, new Rect(160, 50, 156, 16), "Tinted AV To fi");
        Draw(m_Clipped, new Rect(160, 68, 60, 16), "Clipped at the rect");
        bool enabled = GUI.enabled;
        GUI.enabled = false;
        Draw(GUI.skin.label, new Rect(160, 86, 156, 22), "Disabled label");
        GUI.enabled = enabled;

        Draw(m_ImageLeft, new Rect(4, 112, 70, 30), "Left", m_Small);
        Draw(m_ImageLeftCentered, new Rect(160, 112, 70, 30), "Mid", m_Small);
        // Places that fall between pixels: a rect of a fractional height and one at a fractional
        // position, and content centred in room that leaves different fractions.
        Draw(m_ImageLeftMiddle, new Rect(78, 112, 70, 30.5f), "Half", m_Small);
        Draw(m_ImageLeft, new Rect(240.4f, 112.4f, 76, 30), "Off", m_Small);
        Draw(m_ImageAbove, new Rect(4, 144, 48, 36), "Abo", m_Small);
        Draw(m_ImageAbove, new Rect(54, 144, 48, 35.6f), "Abo", m_Small);
        Draw(m_ImageAbove, new Rect(104, 144, 48, 34.2f), "Abo", m_Small);
        Draw(m_ImageLeftWrap, new Rect(154, 144, 100, 36), "One two three four", m_Small);
        Draw(m_ImageOnly, new Rect(258, 144, 26, 36), "Unseen", m_Small);
        // An image larger than its room is scaled into it; an icon size replaces the image's.
        Draw(m_ImageLeft, new Rect(288, 144, 30, 18), "S", m_Large);
        var iconSize = UnityEditor.EditorGUIUtility.GetIconSize();
        UnityEditor.EditorGUIUtility.SetIconSize(new Vector2(12, 12));
        Draw(m_ImageLeft, new Rect(288, 164, 30, 16), "Ic", m_Small);
        UnityEditor.EditorGUIUtility.SetIconSize(iconSize);
    }

    /// <summary>A square of four plain colours, sampled without filtering.</summary>
    static Texture2D Quadrants(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = x < size / 2
                    ? (y < size / 2 ? new Color32(220, 40, 40, 255) : new Color32(40, 200, 60, 255))
                    : (y < size / 2 ? new Color32(50, 90, 230, 255) : new Color32(240, 220, 60, 255));
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    static void Draw(GUIStyle style, Rect rect, string text, Texture image = null) =>
        style.Draw(rect, new GUIContent(text, image), false, false, false, false);
}
