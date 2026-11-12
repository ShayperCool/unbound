using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class QuitWitness : MonoBehaviour
{
    string label;
    void Awake() { label = gameObject.name; QuitControl.Note(label, "Awake"); }
    void OnEnable() => QuitControl.Note(label, "OnEnable");
    void Update() => QuitControl.Note(label, "Update");
    void OnApplicationQuit() => QuitControl.Note(label, "OnApplicationQuit");
    void OnDisable() => QuitControl.Note(label, "OnDisable");
    void OnDestroy() { QuitControl.Note(label, "OnDestroy"); QuitControl.TryFinish(); }
}

public static class QuitControl
{
    static readonly string[] Names = { "MainQuit", "ExtraQuit", "KeepQuit" };
    static readonly Dictionary<string, List<string>> Calls = new Dictionary<string, List<string>>();
    static readonly Dictionary<string, QuitWitness> Objects = new Dictionary<string, QuitWitness>();
    static readonly Dictionary<string, string> Unloaded = new Dictionary<string, string>();
    static bool recording;
    static string output;
    public static bool Leave => Environment.GetEnvironmentVariable("QUIT_CONTROL_LEAVE") == "1";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        output = Environment.GetEnvironmentVariable("QUIT_CONTROL_OUT");
        if (string.IsNullOrEmpty(output)) return;
        File.WriteAllText(output, "");
        recording = true;
        Put("mode", Leave ? "leave" : "quit");
        SceneManager.sceneUnloaded += scene =>
        {
            if (!recording) return;
            string state = "valid=" + scene.IsValid() + ",loaded=" + scene.isLoaded + ",playing=" + Application.isPlaying;
            foreach (string name in Names) state += "," + name + "Alive=" + (Objects[name] != null);
            Unloaded[scene.name] = state;
            Put("unloaded." + scene.name, state);
            bool throws = Environment.GetEnvironmentVariable("QUIT_CONTROL_THROW") == "1";
            if (throws) Put("throw." + scene.name, "1");
            TryFinish();
            if (throws) throw new InvalidOperationException("Quit control unload exception");
        };
        if (Environment.GetEnvironmentVariable("QUIT_CONTROL_THROW") == "1")
            SceneManager.sceneUnloaded += scene => Put("afterThrow." + scene.name, "1");
        var main = new GameObject(Names[0]);
        Objects[Names[0]] = main.AddComponent<QuitWitness>();
        var extra = SceneManager.CreateScene("ExtraQuit");
        var second = new GameObject(Names[1]);
        SceneManager.MoveGameObjectToScene(second, extra);
        Objects[Names[1]] = second.AddComponent<QuitWitness>();
        var keep = new GameObject(Names[2]);
        UnityEngine.Object.DontDestroyOnLoad(keep);
        Objects[Names[2]] = keep.AddComponent<QuitWitness>();
        keep.AddComponent<QuitControlDriver>();
        var disabled = new GameObject("DisabledQuit").AddComponent<QuitWitness>();
        disabled.enabled = false;
        var inactive = new GameObject("InactiveQuit");
        inactive.AddComponent<QuitWitness>();
        inactive.SetActive(false);
    }

    public static void Note(string label, string message)
    {
        if (!recording) return;
        if (!Calls.TryGetValue(label, out var list)) Calls[label] = list = new List<string>();
        if (message == "Update" && list.Contains(message)) return;
        Put("calls." + label + "." + list.Count, message);
        list.Add(message);
    }
    static void Put(string key, string value) => File.AppendAllText(output,
        "{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}\n");
    public static void CompleteCapture()
    {
        recording = false;
        string path = Environment.GetEnvironmentVariable("QUIT_CONTROL_OUT");
        var lines = File.ReadAllLines(path);
        foreach (string line in lines) if (line.Contains("\"k\":\"complete\"")) return;
        Array.Sort(lines, StringComparer.Ordinal);
        File.WriteAllLines(path, lines);
        File.AppendAllText(path, "{\"k\":\"complete\",\"v\":\"1\"}\n");
    }
    public static void TryFinish()
    {
        if (!recording || Unloaded.Count < 2) return;
        foreach (string name in Names)
            if (!Calls.TryGetValue(name, out var list) || !list.Contains("OnDestroy")) return;
        CompleteCapture();
#if UNITY_EDITOR
        UnityEditor.SessionState.SetBool("QuitControl.complete", true);
#endif
    }
}
public sealed class QuitControlDriver : MonoBehaviour
{
    int frames;
    void Update()
    {
        if (++frames != 12) return;
#if UNITY_EDITOR
        if (QuitControl.Leave)
        {
            UnityEditor.SessionState.SetBool("QuitControl.finished", true);
            UnityEditor.EditorApplication.ExitPlaymode();
        }
        else UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
