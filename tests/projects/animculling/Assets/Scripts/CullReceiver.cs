using UnityEngine;

/// <summary>Counts the clips' "OnClipEvent" animation events on a probe rig.</summary>
public class CullReceiver : MonoBehaviour
{
    public int events;
    public float value = -1f;
    public int ik;

    void OnAnimatorIK(int layerIndex) => ik++;

    void OnClipEvent() => events++;
}
