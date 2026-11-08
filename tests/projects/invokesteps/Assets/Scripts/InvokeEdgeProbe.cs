using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class InvokeEdgeProbe : MonoBehaviour
{
    readonly List<string> m_Values = new List<string>();
    InvokeEdgeTimer m_Timer;
    Action m_Finish;
    int m_Stage;
    public void Begin(Action finish) => m_Finish = finish;

    void Update()
    {
        if (m_Timer != null)
        {
            Put("stage/" + m_Stage + "/ticks", m_Timer.Ticks.ToString());
            Put("stage/" + m_Stage + "/neighbor", m_Timer.Neighbors.ToString());
            if (m_Stage == 3)
            {
                Put("nan/first", m_Timer.FirstNaN.ToString());
                Put("nan/second", m_Timer.SecondNaN.ToString());
                Put("nan/pending", m_Timer.IsInvoking("NaNOne") ? "true" : "false");
            }
            m_Timer.CancelInvoke();
            DestroyImmediate(m_Timer.gameObject);
            m_Stage++;
        }
        if (m_Stage == 4)
        {
            m_Values.Add("{\"k\":\"complete\",\"v\":true}");
            File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable("INVOKE_STEPS_OUT"), "edges.jsonl"), m_Values);
            enabled = false;
            m_Finish();
            return;
        }
        m_Timer = new GameObject("Timer " + m_Stage).AddComponent<InvokeEdgeTimer>();
        if (m_Stage == 3)
        {
            m_Timer.InvokeRepeating("NaNOne", float.NaN, 0.1f);
            m_Timer.InvokeRepeating("NaNTwo", float.NaN, 0.1f);
        }
        else
        {
            m_Timer.CancelInside = m_Stage == 2;
            m_Timer.InvokeRepeating("Tick", 0f, 0.1f);
            if (m_Stage != 0) m_Timer.Invoke("Neighbor", 0.05f);
            if (m_Stage == 1) m_Timer.CancelInvoke("Neighbor");
        }
    }

    void Put(string key, string value) => m_Values.Add("{\"k\":\"" + key + "\",\"v\":" + value + "}");
}

public sealed class InvokeEdgeTimer : MonoBehaviour
{
    public int Ticks, Neighbors, FirstNaN, SecondNaN;
    public bool CancelInside;
    void Tick() { Ticks++; if (CancelInside) CancelInvoke("Neighbor"); }
    void Neighbor() => Neighbors++;
    void NaNOne() => FirstNaN++;
    void NaNTwo() => SecondNaN++;
}
