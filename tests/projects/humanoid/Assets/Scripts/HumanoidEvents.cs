using System.Globalization;
using UnityEngine;

/// <summary>Receives the probe clips' ProbeEvent animation events and records each with the step
/// that fired it.</summary>
public class HumanoidEvents : MonoBehaviour
{
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    int m_Calls;

    void ProbeEvent(AnimationEvent e)
    {
        string key = (HumanoidProbe.s_IKKey ?? "events.frame") + ".event" + m_Calls++;
        var state = e.animatorStateInfo;
        var clip = e.animatorClipInfo;
        HumanoidProbe.PutIK(key, "{\"marker\":" + F(e.floatParameter) + ",\"time\":" + F(e.time)
            + ",\"byAnimator\":" + (e.isFiredByAnimator ? "true" : "false")
            + ",\"state\":" + state.shortNameHash + ",\"stateTime\":" + F(state.normalizedTime)
            + ",\"clip\":\"" + (clip.clip != null ? clip.clip.name : "") + "\",\"clipWeight\":" + F(clip.weight) + "}");
    }
}
