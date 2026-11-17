using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;

/// <summary>Draws screen-space overlay UI over the camera's solid background through URP:
/// translucent uGUI Images on two canvases, a UI Toolkit panel between them and IMGUI, reads the screen at
/// the end of the frame and writes it as a PNG (and values.jsonl) into URP_OVERLAY_UI_OUT, then
/// exits. See README.md.</summary>
public class UrpOverlayUiProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("UrpOverlayUiProbe").AddComponent<UrpOverlayUiProbe>();

    readonly StringBuilder m_Out = new StringBuilder();
    string m_Dir;

    void Value(string key, string json) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(json).Append("}\n");

    static Canvas MakeCanvas(string name, int sortingOrder)
    {
        var canvas = new GameObject(name, typeof(RectTransform)).AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        return canvas;
    }

    /// <summary>An Image without a sprite (the default UI material's white texture) at a pixel rect
    /// from the screen's bottom-left corner.</summary>
    static void MakeImage(Canvas canvas, Color color, float x, float y, float width, float height)
    {
        var image = new GameObject("Image", typeof(RectTransform)).AddComponent<Image>();
        image.transform.SetParent(canvas.transform, false);
        image.color = color;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    Texture2D m_Grey, m_LinearGrey;

    /// <summary>A 4x4 texture of grey 128 (sRGB unless <paramref name="linear"/>).</summary>
    static Texture2D Grey(bool linear)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear);
        var texels = new Color32[16];
        for (int i = 0; i < texels.Length; i++) texels[i] = new Color32(128, 128, 128, 255);
        texture.SetPixels32(texels);
        texture.Apply();
        return texture;
    }

    /// <summary>Whether OnGUI draws (only in the imgui captures).</summary>
    bool m_Imgui;
    /// <summary>OnGUI's Repaint events in this frame.</summary>
    int m_Repaints;
    Material m_VertexColor;

    void Update() => m_Repaints = 0;

    void OnGUI()
    {
        if (!m_Imgui || Event.current.type != EventType.Repaint) return;
        m_Repaints++;
        // A translucent quad drawn with GL in the Repaint, in the screen's pixels from the top left.
        m_VertexColor.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.Color(new Color(0.2f, 0.4f, 1f, 0.5f));
        GL.Vertex3(4, 5, 0);
        GL.Vertex3(4, 60, 0);
        GL.Vertex3(17, 60, 0);
        GL.Vertex3(17, 5, 0);
        GL.End();
        // IMGUI's origin is the top-left corner. A translucent white box over the background and
        // the red image, a white box in half grey, and opaque grey textures (sRGB and linear),
        // all over the background.
        var previous = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.5f);
        GUI.DrawTexture(new Rect(220, 30, 80, 100), Texture2D.whiteTexture);
        GUI.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        GUI.DrawTexture(new Rect(150, 5, 60, 20), Texture2D.whiteTexture);
        GUI.color = previous;
        GUI.DrawTexture(new Rect(220, 140, 80, 30), m_Grey);
        GUI.DrawTexture(new Rect(150, 140, 60, 30), m_LinearGrey);
    }

    IEnumerator Start()
    {
        m_Dir = Environment.GetEnvironmentVariable("URP_OVERLAY_UI_OUT");
        m_Grey = Grey(false);
        m_LinearGrey = Grey(true);
        m_VertexColor = new Material(Shader.Find("Hidden/Probe/VertexColor"));
        var camera = Camera.main;
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;

        // The panel of a large URP scene's HUD: white at three quarters' opacity.
        var back = MakeCanvas("Back", 0);
        MakeImage(back, new Color(1f, 1f, 1f, 0.7490196f), 20, 20, 120, 140);
        MakeImage(back, new Color(0f, 1f, 0f, 0.5f), 100, 60, 100, 60);
        // A canvas of a higher sorting order over the first one's green image.
        var front = MakeCanvas("Front", 1);
        MakeImage(front, new Color(1f, 0f, 0f, 0.5f), 150, 40, 100, 60);
        // A UI Toolkit panel sorted between the two canvases: a translucent blue square over the
        // green image and under the red one.
        var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
        panelSettings.sortingOrder = 0.5f;
        var document = new GameObject("Panel").AddComponent<UIDocument>();
        document.panelSettings = panelSettings;
        var square = new VisualElement();
        square.style.position = Position.Absolute;
        square.style.left = 170;
        square.style.top = 50;
        square.style.width = 60;
        square.style.height = 60;
        square.style.backgroundColor = new Color(0f, 0f, 1f, 0.5f);
        document.rootVisualElement.Add(square);

        yield return Capture("overlay");
        m_Imgui = true;
        yield return Capture("imgui");
        Value("imgui.repaints", m_Repaints.ToString());
        m_Imgui = false;

        // Post-processing with FXAA: URP's final post pass draws to the screen before the UI.
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        yield return Capture("overlay_post");

        // The built-in pipeline: the engine draws the overlay UI itself (a control).
        GraphicsSettings.defaultRenderPipeline = null;
        QualitySettings.renderPipeline = null;
        yield return Capture("overlay_builtin");
        m_Imgui = true;
        yield return Capture("imgui_builtin");
        Value("imgui_builtin.repaints", m_Repaints.ToString());

        Value("complete", "true");
        int code = 0;
        if (string.IsNullOrEmpty(m_Dir)) { Debug.LogError("URP_OVERLAY_UI_OUT is not set"); code = 1; }
        else File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    /// <summary>Renders a few frames, then records the last at its end.</summary>
    IEnumerator Capture(string name)
    {
        for (int i = 0; i < 3; i++) yield return null;
        yield return new WaitForEndOfFrame();
        Record(name);
    }

    /// <summary>The screen's pixels, read at the end of the frame.</summary>
    void Record(string name)
    {
        Value(name + ".screen", "[" + Screen.width + "," + Screen.height + "]");
        if (string.IsNullOrEmpty(m_Dir)) return;
        var read = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        read.Apply();
        Directory.CreateDirectory(m_Dir);
        File.WriteAllBytes(Path.Combine(m_Dir, name + ".png"), read.EncodeToPNG());
        Destroy(read);
    }
}
