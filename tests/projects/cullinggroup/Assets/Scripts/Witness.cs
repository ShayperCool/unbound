using UnityEngine;

/// <summary>A renderer whose culling messages place the culling groups' callbacks within the camera's culling.</summary>
public class Witness : MonoBehaviour
{
    static string Current() => ProbeLog.Q(Camera.current != null ? Camera.current.name : null);
    void OnWillRenderObject() => ProbeLog.Add("OnWillRenderObject", "cam", Current());
    void OnBecameVisible() => ProbeLog.Add("OnBecameVisible", "cam", Current());
    void OnBecameInvisible() => ProbeLog.Add("OnBecameInvisible", "cam", Current());
}
