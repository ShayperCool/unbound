using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class EditorDrawRectProbe : MonoBehaviour
{
    readonly StringBuilder m_Values = new StringBuilder();
    bool m_Restored;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("EDITOR_DRAW_RECT_OUT") != null)
            new GameObject("EditorDrawRectProbe").AddComponent<EditorDrawRectProbe>();
    }

    void Put(string key, string value) =>
        m_Values.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");

    IEnumerator Start()
    {
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.allowHDR = false;
        camera.allowMSAA = false;

        try
        {
            EditorGUI.DrawRect(new Rect(0, 0, 1, 1), Color.white);
            Put("outsideOnGUI", "\"returned\"");
        }
        catch (Exception exception) { Put("outsideOnGUI", "\"" + exception.GetType().Name + "\""); }

        yield return null;
        yield return new WaitForEndOfFrame();
        var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
        var pixels = image.GetPixels32();
        void Pixel(string name, int x, int y)
        {
            Color32 value = pixels[(image.height - 1 - y) * image.width + x];
            Put(name, $"[{value.r},{value.g},{value.b}]");
        }
        Put("size", $"[{image.width},{image.height}]");
        Put("guiStateRestored", m_Restored ? "true" : "false");
        Pixel("layoutOnly", 5, 5);
        Pixel("opaque", 18, 15);
        Pixel("tinted", 49, 15);
        Pixel("disabled", 80, 15);
        Pixel("outsideClip", 11, 54);
        Pixel("insideClip", 13, 54);
        Pixel("afterClip", 31, 54);
        Pixel("transformed", 97, 57);
        Pixel("outsideTransform", 92, 57);
        Pixel("zeroWidth", 139, 16);
        Pixel("negativeWidth", 172, 16);
        Pixel("followingControl", 215, 16);
        Destroy(image);
        Put("complete", "true");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("EDITOR_DRAW_RECT_OUT"), "values.jsonl"), m_Values.ToString());
        EditorApplication.Exit(0);
    }

    void OnGUI()
    {
        if (Event.current.type == EventType.Layout)
            EditorGUI.DrawRect(new Rect(2, 2, 6, 6), Color.blue);
        if (Event.current.type != EventType.Repaint) return;

        EditorGUI.DrawRect(new Rect(10, 10, 24, 16), new Color(1, .5f, .25f));
        var tint = new Color(.5f, 1, .25f, .5f);
        GUI.color = tint;
        GUI.backgroundColor = Color.green;
        GUI.contentColor = Color.blue;
        EditorGUI.DrawRect(new Rect(41, 10, 24, 16), new Color(1, .5f, 1, .8f));
        m_Restored = GUI.color == tint && GUI.backgroundColor == Color.green && GUI.contentColor == Color.blue;
        GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;

        GUI.enabled = false;
        EditorGUI.DrawRect(new Rect(72, 10, 24, 16), Color.white);
        m_Restored &= !GUI.enabled;
        GUI.enabled = true;

        GUI.BeginGroup(new Rect(12, 50, 18, 16));
        EditorGUI.DrawRect(new Rect(-3, 3, 35, 10), Color.green);
        GUI.EndGroup();

        var matrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(new Vector3(92, 48, 0), Quaternion.identity, new Vector3(2, 2, 1));
        EditorGUI.DrawRect(new Rect(1, 2, 9, 5), Color.yellow);
        GUI.matrix = matrix;

        EditorGUI.DrawRect(new Rect(139, 10, 0, 12), Color.red);
        EditorGUI.DrawRect(new Rect(180, 10, -12, 12), Color.cyan);
        GUI.DrawTexture(new Rect(210, 10, 12, 12), Texture2D.whiteTexture);
    }
}
