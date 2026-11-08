using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class InvokeScaleProbe : MonoBehaviour
{
    Action m_Finish;
    int m_Frame, m_Calls, m_PausedCalls;
    double m_Start, m_Last;
    bool m_Intervals = true, m_Delay = true;
    readonly List<string> m_Values = new List<string>();
    readonly List<string> m_Repeat = new List<string>();
    public void Begin(Action finish)
    {
        m_Finish = finish;
        Time.fixedDeltaTime = 0.03125f;
    }
    void Update()
    {
        m_Frame++;
        if (m_Frame == 5)
        {
            Time.timeScale = 0;
            m_Start = Time.timeAsDouble;
            InvokeRepeating(nameof(Tick), 1f, 0.5f);
        }
        if (m_Frame == 9)
        {
            Put("paused/noCalls", m_Calls == 0);
            Time.timeScale = 0.5f;
        }
        if (m_Frame == 21)
        {
            Put("halfSpeed/stillBeforeDelay", m_Calls == 0);
        }
        if (m_Frame == 37)
        {
            Put("resumed/multipleCalls", m_Calls >= 2);
            Put("delay/usesScaledTime", m_Delay);
            Put("interval/usesScaledTime", m_Intervals);
            m_Values.Add("{\"k\":\"complete\",\"v\":true}");
            File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable("INVOKE_STEPS_OUT"), "scale.jsonl"), m_Values);
            Repeat("halfSpeed/multipleCalls", m_Calls >= 2);
            Repeat("halfSpeed/scaledIntervals", m_Intervals);
            Time.timeScale = 0;
        }
        if (m_Frame == 38) m_PausedCalls = m_Calls;
        if (m_Frame == 45)
        {
            Repeat("runningRepeat/noCallsWhilePaused", m_Calls == m_PausedCalls);
            Time.timeScale = 0.5f;
        }
        if (m_Frame == 61)
        {
            Repeat("runningRepeat/resumesAtHalfSpeed", m_Calls > m_PausedCalls);
            Repeat("runningRepeat/scaledIntervalsAfterPause", m_Intervals);
            CancelInvoke();
            m_Repeat.Add("{\"k\":\"complete\",\"v\":true}");
            File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable("INVOKE_STEPS_OUT"), "scaled-repeat.jsonl"), m_Repeat);
            Time.timeScale = 1;
            enabled = false;
            m_Finish();
        }
    }
    void Tick()
    {
        double now = Time.timeAsDouble;
        if (m_Calls == 0) m_Delay = now - m_Start >= 0.8;
        else m_Intervals &= now - m_Last >= 0.45 && now - m_Last <= 0.65;
        m_Last = now;
        m_Calls++;
    }
    void Repeat(string key, bool value) => m_Repeat.Add("{\"k\":\"" + key + "\",\"v\":" + (value ? "true" : "false") + "}");
    void Put(string key, bool value) => m_Values.Add("{\"k\":\"" + key + "\",\"v\":" + (value ? "true" : "false") + "}");
}
