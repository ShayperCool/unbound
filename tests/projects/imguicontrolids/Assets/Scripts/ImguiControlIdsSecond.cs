using System.Collections.Generic;
using UnityEngine;

/// <summary>A second component on the probe's object: the ids its own OnGUI gets.</summary>
public sealed class ImguiControlIdsSecond : MonoBehaviour
{
    void OnGUI()
    {
        var type = Event.current.type;
        int frame = ImguiControlIdsProbe.Frame;
        if (ImguiControlIdsProbe.Output == null || frame < 0 || frame > 5) return;
        if (type != EventType.Layout && type != EventType.Repaint) return;
        string pass = $"f{frame}/{(type == EventType.Layout ? "layout" : "repaint")}";
        ImguiControlIdsProbe.Order.Add(pass + "/second");
        int start = ImguiControlIdsProbe.Permanent();
        int a = GUIUtility.GetControlID(FocusType.Passive);
        int b = GUIUtility.GetControlID(FocusType.Keyboard);
        object p = frame == 1 ? (object)("p" + ImguiControlIdsProbe.Permanent()) : null;
        ImguiControlIdsProbe.Put(pass + "/second", a, b, p);
        var ids = p == null ? new object[] { a, b } : new object[] { a, b, p };
        ImguiControlIdsProbe.Put("canon/" + pass + "/second", ImguiControlIdsProbe.Canon(start, ids));
    }
}
