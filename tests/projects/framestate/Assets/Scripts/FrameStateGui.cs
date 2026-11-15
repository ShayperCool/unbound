using UnityEngine;

/// <summary>The matrices scripts' drawing gets in OnGUI's first Repaint (FrameStateProbe adds this
/// for one frame).</summary>
public class FrameStateGui : MonoBehaviour
{
    public FrameStateProbe Probe;
    bool done;

    void OnGUI()
    {
        if (done || Event.current.type != EventType.Repaint) return;
        done = true;
        Probe.RecordGui();
    }
}
