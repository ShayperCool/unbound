using System.Collections;
using UnityEngine;

/// <summary>Logs the OnMouse* messages it gets, with the frame's phase.</summary>
public class MouseTarget : MonoBehaviour
{
    void Log(string message) => ProbeLog.Add("mouse", "o", ProbeLog.Q(name), "m", ProbeLog.Q(message), "p", ProbeLog.Q(MessagesProbe.Phase));
    void OnMouseEnter() => Log("Enter");
    void OnMouseOver() => Log("Over");
    void OnMouseExit() => Log("Exit");
    void OnMouseDown() => Log("Down");
    void OnMouseDrag() => Log("Drag");
    void OnMouseUp() => Log("Up");
    void OnMouseUpAsButton() => Log("UpAsButton");
}

/// <summary>Logs OnBecameVisible / OnBecameInvisible, with the frame's phase.</summary>
public class VisTarget : MonoBehaviour
{
    void Log(string message)
    {
        if (Time.frameCount >= MessagesProbe.VisibilityEnd) return;
        ProbeLog.Add("vis", "o", ProbeLog.Q(name), "m", ProbeLog.Q(message), "p", ProbeLog.Q(MessagesProbe.Phase));
    }

    void OnBecameVisible() => Log("Visible");
    void OnBecameInvisible() => Log("Invisible");
}

/// <summary>Logs the parent messages, with the receiver's parent when it gets them.</summary>
public class ParentLog : MonoBehaviour
{
    void Log(string message) => ProbeLog.Add("parent", "o", ProbeLog.Q(name), "m", ProbeLog.Q(message),
        "parent", ProbeLog.Q(transform.parent != null ? transform.parent.name : null), "active", gameObject.activeInHierarchy ? "true" : "false");
    void OnBeforeTransformParentChanged() => Log("BeforeParentChanged");
    void OnTransformParentChanged() => Log("ParentChanged");
    void OnTransformChildrenChanged() => Log("ChildrenChanged");
}

/// <summary>A behaviour coroutines are started on.</summary>
public class CoroutineHost : MonoBehaviour
{
    public IEnumerator Routine(string label)
    {
        for (int i = 0; i < 3; i++)
        {
            ProbeLog.Add("routine", "label", ProbeLog.Q(label), "step", i.ToString(), "p", ProbeLog.Q(MessagesProbe.Phase));
            yield return null;
        }
        ProbeLog.Add("routine", "label", ProbeLog.Q(label), "step", "\"done\"");
    }
}

/// <summary>Logs the SendMessage calls it gets.</summary>
public class PingLog : MonoBehaviour
{
    void Ping() => ProbeLog.Add("ping", "o", ProbeLog.Q(name), "enabled", enabled ? "true" : "false",
        "active", gameObject.activeInHierarchy ? "true" : "false");
}
