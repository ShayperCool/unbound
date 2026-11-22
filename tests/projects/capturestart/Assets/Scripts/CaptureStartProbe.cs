using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

// When a capture rate (Time.captureDeltaTime) set at one point of Play mode's start first steps
// the game clock. CAPTURE_START_AT names the point: none, subsystem, before, bootAwake,
// sceneAwake, sceneEnable, after, sceneStart, bootStart, update0. One run per point; each
// records the clocks where the rate is set and in the first Updates and FixedUpdates. Two points
// set no rate and record the clocks at every later point and in the first frame (the frames
// after it follow real time): fixedBefore sets another fixed timestep and time scale before the
// scene loads, projectFixed runs a project whose Time settings hold them (the runner writes
// them into the run's copy of the project).
public class CaptureStartProbe : MonoBehaviour
{
    public const float Step = 1f / 30f;
    const int Frames = 6;
    const string FixedBefore = "fixedBefore", ProjectFixed = "projectFixed";
    static bool NoRate => s_At == FixedBefore || s_At == ProjectFixed;
    static StreamWriter s_Out;
    static string s_At;
    static int s_FirstFrame = -1;
    int m_Updates;
    int m_FixedSinceUpdate;

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    // The frame counted from the first Update's (the Editor's frame count is not 0 there).
    static string Frame() => s_FirstFrame < 0 ? "pre" : (Time.frameCount - s_FirstFrame).ToString(CultureInfo.InvariantCulture);

    static void Put(string key, string value) => s_Out?.WriteLine("{\"key\":\"" + key + "\",\"value\":\"" + value + "\"}");

    static string Clocks() => F(Time.time) + " dt=" + F(Time.deltaTime) + " fixed=" + F(Time.fixedTime) + " capture=" + F(Time.captureDeltaTime);

    /// <summary>Sets the rate when <paramref name="point"/> is the run's, and records the clocks there.</summary>
    public static void At(string point)
    {
        if (NoRate && point != "subsystem" && point != "before") Put("at." + point, "frame=" + Frame() + " " + Deltas());
        if (s_At != point) return;
        Put("set." + point, "frame=" + Frame() + " " + Deltas());
        Time.captureDeltaTime = Step;
    }

    // With the deltas that do not follow real time: the unscaled one only before the first Update.
    static string Deltas() => Clocks() + (s_FirstFrame < 0 ? " unscaledDt=" + F(Time.unscaledDeltaTime) : "") + " smoothDt=" + F(Time.smoothDeltaTime)
        + " fixedDt=" + F(Time.fixedDeltaTime) + " scale=" + F(Time.timeScale);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Subsystem()
    {
        s_FirstFrame = -1;
        s_Out = null;
        string path = Environment.GetEnvironmentVariable("CAPTURE_START_OUT");
        s_At = Environment.GetEnvironmentVariable("CAPTURE_START_AT");
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(s_At)) return;
        s_Out = new StreamWriter(path);
        At("subsystem");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Before()
    {
        if (s_Out == null) return;
        At("before");
        if (s_At == FixedBefore)
        {
            Time.fixedDeltaTime = 0.05f;
            Time.timeScale = 0.5f;
        }
        DontDestroyOnLoad(new GameObject("CaptureStartProbe").AddComponent<CaptureStartProbe>().gameObject);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void After() => At("after");

    void Awake() => At("bootAwake");

    void Start() => At("bootStart");

    void FixedUpdate() => m_FixedSinceUpdate++;

    void Update()
    {
        if (s_Out == null) return;
        if (s_FirstFrame < 0) s_FirstFrame = Time.frameCount;
        At("update" + m_Updates.ToString(CultureInfo.InvariantCulture));
        Put("frame." + m_Updates.ToString(CultureInfo.InvariantCulture), Clocks() + " fixedSteps=" + m_FixedSinceUpdate.ToString(CultureInfo.InvariantCulture));
        m_FixedSinceUpdate = 0;
        if (++m_Updates < (NoRate ? 1 : Frames)) return;
        Put("complete", "1");
        s_Out.Dispose();
        s_Out = null;
        EditorApplication.Exit(0);
    }
}
