using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public sealed class InvokeStepsProbe : MonoBehaviour
{
    const float FrameStep = 0.125f;
    const float FixedStep = 0.03125f;
    readonly SortedDictionary<string, object> m_Values = new SortedDictionary<string, object>();
    readonly List<string> m_Fast = new List<string>();
    readonly List<string> m_Slow = new List<string>();
    int m_First = -1, m_Start = -1, m_Step, m_LastFixedFrame = -1;
    double m_Due, m_FixedStart;
    string m_Phase;
    int m_Cancelled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INVOKE_STEPS_OUT")))
            new GameObject("Invoke steps").AddComponent<InvokeStepsProbe>();
    }

    void FixedUpdate()
    {
        if (m_LastFixedFrame != Time.frameCount) { m_LastFixedFrame = Time.frameCount; m_Step = 0; }
        m_Step++;
        m_Phase = "fixed/" + m_Step;
    }

    void Update()
    {
        m_Phase = "update";
        if (m_First < 0) m_First = Time.frameCount;
        int frame = Time.frameCount - m_First;
        if (frame == 3) { Time.captureDeltaTime = FrameStep; Time.fixedDeltaTime = FixedStep; }
        // Keep the frame and fixed clocks apart when testing which clock schedules an Invoke.
        // Play mode's initial offset varies. One half-step frame moves a small gap away from
        // the 0.09 s deadline's third fixed step without changing the later four-step frames.
        if (frame == 5 && Time.timeAsDouble - Time.fixedTimeAsDouble < FixedStep / 4)
            Time.captureDeltaTime = FrameStep + FixedStep / 2;
        if (frame == 6) Time.captureDeltaTime = FrameStep;
        if (frame == 8)
        {
            m_Start = Time.frameCount;
            m_FixedStart = Time.fixedTimeAsDouble;
            m_Due = Time.timeAsDouble + 0.09f;
            Invoke(nameof(Clock), 0.09f);
            InvokeRepeating(nameof(Fast), 0f, 0.007f);
            InvokeRepeating(nameof(Slow), 0f, 0.07f);
            InvokeRepeating(nameof(SelfCancel), 0f, 0.001f);
        }
        if (m_Start >= 0 && Time.frameCount == m_Start + 6)
        {
            CancelInvoke();
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("INVOKE_STEPS_OUT"), "raw.txt"), string.Join(",", m_Fast) + "\n" + string.Join(",", m_Slow));
            m_Values["repeat/fast"] = string.Join(",", m_Fast.FindAll(v => !v.StartsWith("0/") && !v.StartsWith("6/")));
            m_Values["repeat/slowCount"] = m_Slow.Count;
            m_Values["repeat/selfCancel"] = m_Cancelled;
            // A control with no fixed steps: only the two dynamic checks remain.
            Time.fixedDeltaTime = 10f;
            m_Fast.Clear();
            InvokeRepeating(nameof(Fast), 0f, 0.007f);
        }
        if (m_Start >= 0 && Time.frameCount == m_Start + 10)
        {
            CancelInvoke();
            m_Values["repeat/noFixed"] = string.Join(",", m_Fast.FindAll(v => !v.StartsWith("6/")));
            new GameObject("Invoke edge cases").AddComponent<InvokeEdgeProbe>().Begin(Finish);
        }
    }

    void LateUpdate() { m_Phase = "late"; }

    void Clock()
    {
        m_Values["clock/isInvokingInsideCallback"] = IsInvoking(nameof(Clock));
        m_Values["clock/phase"] = m_Phase;
        m_Values["clock/currentClockReachedDue"] = Time.timeAsDouble >= m_Due;
        m_Values["clock/fixedDeadlineReached"] = Time.fixedTimeAsDouble >= m_FixedStart + 0.09f;
        m_Values["clock/frame"] = Time.frameCount - m_Start;
        m_Values["clock/fixedClockReachedDue"] = Time.fixedTimeAsDouble >= m_Due;
        m_Values["clock/fixedStepsSinceSchedule"] = (int)Math.Round((Time.fixedTimeAsDouble - m_FixedStart) / FixedStep);
    }
    void Fast() => m_Fast.Add((Time.frameCount - m_Start) + "/" + m_Phase);
    void Slow() => m_Slow.Add((Time.frameCount - m_Start) + "/" + m_Phase);
    void SelfCancel() { m_Cancelled++; CancelInvoke(nameof(SelfCancel)); }

    void Finish()
    {
        var lines = new List<string>();
        foreach (var kv in m_Values)
        {
            string v = kv.Value is string s ? "\"" + s + "\"" : kv.Value is bool b ? (b ? "true" : "false") : Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            lines.Add("{\"k\":\"" + kv.Key + "\",\"v\":" + v + "}");
        }
        lines.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable("INVOKE_STEPS_OUT"), "values.jsonl"), lines);
        Time.captureDeltaTime = 0;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
