using UnityEngine;

/// <summary>An OnGUI behaviour of the guiinput probe (A or B): logs each OnGUI call's event type,
/// sets GUI.depth -3 in every call of its phase (A in aLower, B in bLower), and in the used phase
/// the first behaviour to get a MouseDown uses it.</summary>
public class GuiInputBehaviour : MonoBehaviour
{
    static int s_UsedFrame = -1;

    void Update() => GuiInputProbe.Updates.Append(name);

    void OnGUI()
    {
        var e = Event.current;
        string phase = GuiInputProbe.Phase(Time.frameCount);
        if ((name == "A" && phase == "aLower") || (name == "B" && phase == "bLower")) GUI.depth = -3;
        // Named here: EventType has lowercase aliases whose ToString differs between runtimes.
        string type = e.type switch
        {
            EventType.Layout => "Layout",
            EventType.Repaint => "Repaint",
            EventType.MouseDown => "MouseDown",
            EventType.MouseUp => "MouseUp",
            EventType.MouseMove => "MouseMove",
            EventType.MouseDrag => "MouseDrag",
            EventType.KeyDown => "KeyDown",
            EventType.KeyUp => "KeyUp",
            EventType.Used => "Used",
            EventType.Ignore => "Ignore",
            _ => "Other" + (int)e.type,
        };
        if (e.type == EventType.KeyDown || e.type == EventType.KeyUp) type += "(" + (int)e.keyCode + "," + (int)e.character + ")";
        GuiInputProbe.Calls.Add(name + ":" + type);
        if (phase == "used" && e.type == EventType.MouseDown && s_UsedFrame != Time.frameCount)
        {
            s_UsedFrame = Time.frameCount;
            e.Use();
        }
    }
}
