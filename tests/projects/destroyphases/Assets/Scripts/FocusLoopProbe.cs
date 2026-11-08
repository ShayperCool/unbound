using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class FocusLoopProbe : MonoBehaviour
{
    public static bool Finished;
    public static int Step;
    static readonly List<string> Lines = new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("FOCUS_LOOP_OUT") == null) return;
        Application.runInBackground = true;
        Application.targetFrameRate = 50;
        new GameObject("FocusLoopProbe").AddComponent<FocusLoopProbe>();
    }
    static void Record(string message)
    {
        if (Step == 0) File.AppendAllText(Environment.GetEnvironmentVariable("FOCUS_LOOP_OUT") + ".startup", message + "\n");
        else Lines.Add(message);
    }
    void OnApplicationPause(bool paused) { Record("pause:" + paused); }
    void OnApplicationFocus(bool focused)
    {
        Record("focus:" + focused);
        if (Step == 1 && !focused) Step = 2;
        else if (Step == 2 && focused) Step = 3;
        WriteStep();
    }
    static void WriteStep() { File.WriteAllText(Environment.GetEnvironmentVariable("FOCUS_LOOP_OUT") + ".step", Step.ToString()); }
    IEnumerator Start()
    {
        for (int i = 0; i < 5; i++) yield return null;
        Lines.Add("ready"); Step = 1; WriteStep();
        while (Step != 3) yield return null;
        for (int i = 0; i < 5; i++) yield return null;
        Lines.Add("complete");
        File.WriteAllLines(Environment.GetEnvironmentVariable("FOCUS_LOOP_OUT"), Lines);
        Finished = true;
    }
}
