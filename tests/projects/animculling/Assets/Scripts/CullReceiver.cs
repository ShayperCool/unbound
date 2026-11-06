using UnityEngine;

/// <summary>Counts the clips' "OnClipEvent" animation events on a probe rig.</summary>
public class CullReceiver : MonoBehaviour
{
    public int events;

    void OnClipEvent() => events++;
}
