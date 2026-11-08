using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Records when timed waits resume, counted in frames from the frame that started them, and in
/// which part of that frame: coroutines that yield WaitForSeconds (started from FixedUpdate, Update,
/// LateUpdate, a coroutine resumed after Update and one resumed at the end of the frame),
/// Awaitable.WaitForSecondsAsync and MonoBehaviour.Invoke. Game time advances by a fixed
/// Time.captureDeltaTime of 0.125 s (exact in binary) so every frame count is deterministic; one
/// control runs on real time with a long frame. Writes values.jsonl to $WAIT_FOR_SECONDS_OUT.
/// </summary>
public sealed class WaitForSecondsProbe : MonoBehaviour
{
    const float Step = 0.125f;
    // Delays: negative, none, less than one frame, between two and three frames, between seven and
    // eight. None ends on a frame boundary: there the clocks' rounding decides (the Editor answers
    // either way for 1 s from one capture to the next).
    static readonly float[] Delays = { -1f, 0f, 0.1f, 0.3f, 0.9f };
    // Frames to wait after Play mode starts and after captureDeltaTime is set before a stage starts.
    const int Settle = 5;
    // The real-time control: a frame that sleeps this long, then waits this long.
    const int HitchMilliseconds = 250;
    const float HitchDelay = 0.2f;
    // Gives up (and writes what it has) after this much real time.
    const float TimeoutSeconds = 60f;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly SortedDictionary<string, string> m_Values = new SortedDictionary<string, string>(StringComparer.Ordinal);
    string m_Dir;
    string m_Phase = "start";
    int m_Expected;
    int m_StartFrame = -1;
    int m_FixedFrame = -1;
    int m_HitchFrame = -1;
    double m_HitchTime;
    float m_StartRealtime = float.MaxValue;
    int m_InvokeFrame;
    int m_FirstFrame = -1;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var dir = Environment.GetEnvironmentVariable("WAIT_FOR_SECONDS_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        new GameObject("WaitForSeconds probe").AddComponent<WaitForSecondsProbe>().m_Dir = dir;
    }

    void FixedUpdate()
    {
        m_Phase = "fixedUpdate";
        // The first fixed step of the start frame.
        if (m_StartFrame < 0 || Time.frameCount != m_StartFrame || m_FixedFrame == m_StartFrame) return;
        m_FixedFrame = m_StartFrame;
        foreach (var delay in Delays) StartCoroutine(Wait("fixedUpdate", delay));
        Invoke(nameof(FixedInvokedZero), 0f);
        Invoke(nameof(FixedInvokedLater), 0.3f);
    }

    void Update()
    {
        m_Phase = "update";
        if (m_Done) return;
        // Frames count from the probe's first Update: the Editor's frameCount does not start at 0.
        if (m_FirstFrame < 0)
        {
            m_FirstFrame = Time.frameCount;
            m_StartRealtime = Time.realtimeSinceStartup;
        }
        int frame = Time.frameCount - m_FirstFrame;
        if (frame == Settle)
        {
            Time.captureDeltaTime = Step;
            Put("setup/captureDeltaTime", F(Step));
        }
        else if (frame == 2 * Settle - 1)
        {
            m_StartFrame = Time.frameCount + 1; // FixedUpdate starts its cases in the next frame
        }
        else if (Time.frameCount == m_StartFrame)
        {
            Put("setup/fixedStepsBeforeStart", m_FixedFrame == m_StartFrame ? "true" : "false");
            Put("setup/deltaTime", F(Time.deltaTime));
            foreach (var delay in Delays)
            {
                StartCoroutine(Wait("update", delay));
                StartCoroutine(AfterNull(delay));
                StartCoroutine(AfterEndOfFrame(delay));
                Awaited(delay);
            }
            StartCoroutine(TwoZeroWaits());
            m_InvokeFrame = Time.frameCount;
            Invoke(nameof(InvokedZero), 0f);
            Invoke(nameof(InvokedLater), 0.3f);
            m_Expected = Delays.Length * 6 + 5;
        }
        else if (m_StartFrame > 0 && m_HitchFrame < 0 && Count("case/") >= m_Expected)
        {
            // Real time from now on; the next frame is short, the one after it sleeps.
            Time.captureDeltaTime = 0f;
            m_HitchFrame = frame + 2;
        }
        else if (frame == m_HitchFrame)
        {
            m_HitchTime = Time.timeAsDouble;
            Thread.Sleep(HitchMilliseconds);
            StartCoroutine(Hitch());
        }
        if (Count("case/") >= m_Expected + 1 || Time.realtimeSinceStartup > TimeoutSeconds + m_StartRealtime) Finish();
    }

    void LateUpdate()
    {
        m_Phase = "lateUpdate";
        if (Time.frameCount != m_StartFrame) return;
        foreach (var delay in Delays) StartCoroutine(Wait("lateUpdate", delay));
    }

    IEnumerator Wait(string from, float delay)
    {
        int frame = Time.frameCount;
        yield return new WaitForSeconds(delay);
        Record($"case/{from}/{F(delay)}", frame);
    }

    IEnumerator AfterNull(float delay)
    {
        yield return null;
        int frame = Time.frameCount;
        yield return new WaitForSeconds(delay);
        Record($"case/coroutine/{F(delay)}", frame);
    }

    IEnumerator AfterEndOfFrame(float delay)
    {
        yield return new WaitForEndOfFrame();
        int frame = Time.frameCount;
        yield return new WaitForSeconds(delay);
        Record($"case/endOfFrame/{F(delay)}", frame);
    }

    IEnumerator TwoZeroWaits()
    {
        int frame = Time.frameCount;
        yield return new WaitForSeconds(0f);
        yield return new WaitForSeconds(0f);
        Record("case/update/twoZeroWaits", frame);
    }

    async void Awaited(float delay)
    {
        int frame = Time.frameCount;
        await Awaitable.WaitForSecondsAsync(delay);
        Record($"case/awaitable/{F(delay)}", frame);
    }

    void InvokedZero() => Record("case/invoke/0", m_InvokeFrame);

    void InvokedLater() => Record("case/invoke/0.3", m_InvokeFrame);

    void FixedInvokedZero() => Record("case/fixedInvoke/0", m_FixedFrame);

    void FixedInvokedLater() => Record("case/fixedInvoke/0.3", m_FixedFrame);

    IEnumerator Hitch()
    {
        int frame = Time.frameCount;
        yield return new WaitForSeconds(HitchDelay);
        // Frames are short apart from the hitch, so the count only tells "next frame" from "later".
        Put("case/hitch/resumedAfterNextFrame", Time.frameCount - frame >= 2 ? "true" : "false");
        Put("case/hitch/waitedDelayAfterTheLongFrame",
            Time.timeAsDouble - m_HitchTime >= HitchMilliseconds / 1000.0 + HitchDelay - 0.01 ? "true" : "false");
    }

    void Record(string key, int frame)
    {
        Put(key + "/frames", (Time.frameCount - frame).ToString(Inv));
        Put(key + "/phase", Json(m_Phase));
    }

    int Count(string prefix)
    {
        int n = 0;
        foreach (var key in m_Values.Keys)
            if (key.StartsWith(prefix, StringComparison.Ordinal) && key.EndsWith("/frames", StringComparison.Ordinal)) n++;
        if (m_Values.ContainsKey("case/hitch/resumedAfterNextFrame")) n++;
        return n;
    }

    void Put(string key, string json) => m_Values[key] = json;

    void Finish()
    {
        m_Done = true;
        Time.captureDeltaTime = 0f;
        var text = new StringBuilder();
        foreach (var pair in m_Values) text.Append("{\"k\":").Append(Json(pair.Key)).Append(",\"v\":").Append(pair.Value).Append("}\n");
        text.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), text.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static string F(float v) => v.ToString("R", Inv);

    static string Json(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
