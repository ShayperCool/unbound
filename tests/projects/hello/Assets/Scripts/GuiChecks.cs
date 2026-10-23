using UnityEngine;

/// <summary>UnityGUI in the fixture test: a button clicked and a text field typed into by
/// scripted input, and where automatic layout puts its controls.</summary>
public class GuiChecks : MonoBehaviour
{
    string m_Text = "";
    int m_Clicks;
    Rect m_Wide, m_Right;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create() => new GameObject("GuiChecks").AddComponent<GuiChecks>();

    void OnGUI()
    {
        if (GUI.Button(new Rect(10, 10, 120, 40), "Click"))
            m_Clicks++;
        m_Text = GUI.TextField(new Rect(10, 60, 200, 30), m_Text);

        GUILayout.BeginArea(new Rect(300, 10, 200, 300));
        GUILayout.Label("Layout");
        GUILayout.Button("Wide");
        if (Event.current.type == EventType.Repaint) m_Wide = GUILayoutUtility.GetLastRect();
        GUILayout.BeginHorizontal();
        GUILayout.Button("A");
        GUILayout.FlexibleSpace();
        GUILayout.Button("B", GUILayout.Width(50));
        if (Event.current.type == EventType.Repaint) m_Right = GUILayoutUtility.GetLastRect();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void Update()
    {
        if (Time.frameCount == 80)
        {
            Debug.Log($"gui clicks {m_Clicks} text '{m_Text}' wide {m_Wide.x} {m_Wide.width} right {m_Right.xMax} {m_Right.width}");
            // The run fixes the frame rate: game time no longer follows the wall clock.
            Debug.Log($"fixed time {Time.time:F4} delta {Time.deltaTime:F4}");
        }
    }
}
