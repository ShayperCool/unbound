using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// An object made at one point of Play mode's start (StartupProbe.Make names the point): the
// frames in which the end-of-frame waits it starts in Awake and in a coroutine Start resume, and
// the frame its Start ran in, all counted from the first Update's frame.
public class MadeWaiter : MonoBehaviour
{
    static readonly List<MadeWaiter> s_All = new List<MadeWaiter>();
    public string point;
    readonly int[] m_Awake = new int[12], m_Start = new int[12];
    int m_StartFrame = int.MinValue;

    void Awake()
    {
        s_All.Add(this);
        StartCoroutine(Wait());
    }

    IEnumerator Wait()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();
            Count(m_Awake);
        }
    }

    IEnumerator Start()
    {
        m_StartFrame = Time.frameCount;
        while (true)
        {
            yield return new WaitForEndOfFrame();
            Count(m_Start);
        }
    }

    static void Count(int[] frames)
    {
        int step = StartupProbe.Step;
        if (step >= 0 && step < frames.Length) frames[step]++;
    }

    public static void Finish()
    {
        foreach (var waiter in s_All)
        {
            StartupProbe.Record("made." + waiter.point + ".start.frame", waiter.m_StartFrame == int.MinValue ? -99 : waiter.m_StartFrame - StartupProbe.FirstUpdate);
            for (int i = 0; i < 6; i++)
            {
                StartupProbe.Record("made." + waiter.point + ".frame." + i + ".awake", waiter.m_Awake[i]);
                StartupProbe.Record("made." + waiter.point + ".frame." + i + ".start", waiter.m_Start[i]);
            }
        }
    }
}
