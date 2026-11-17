using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// Calls a native function in the probe's macOS plugin bundles (built by run.py into
/// Assets/Plugins): `Renamed.bundle`, whose executable (CFBundleExecutable) is `RenamedLib`, by the
/// bundle's name and by the executable's, and `Same.bundle`, whose executable is `Same`. Records
/// each call's answer or the exception's type. Then the bundle's native function calls back a
/// managed `Action&lt;string, int, string&gt;` through the pointer
/// `Marshal.GetFunctionPointerForDelegate` makes of it (a delegate of a generic type), and the
/// arguments the action receives are recorded (`callback.generic`). Writes values.jsonl in
/// PLUGIN_BUNDLE_OUT and exits.
/// </summary>
public sealed class PluginBundleProbe : MonoBehaviour
{
    [DllImport("Renamed", EntryPoint = "pluginbundle_answer")] static extern int RenamedByBundle();
    [DllImport("RenamedLib", EntryPoint = "pluginbundle_answer")] static extern int RenamedByExecutable();
    [DllImport("Same", EntryPoint = "pluginbundle_answer")] static extern int SameByBundle();
    [DllImport("Same", EntryPoint = "pluginbundle_call")] static extern void CallBack(IntPtr callback);
    [DllImport("Same", EntryPoint = "pluginbundle_call")] static extern void CallBackGeneric(Action<string, int, string> callback);

    static string s_Received = "not called";
    static readonly Action<string, int, string> s_Callback = (a, b, c) => s_Received = a + "|" + b + "|" + c;

    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("PluginBundleProbe").AddComponent<PluginBundleProbe>();

    void Call(string key, Func<int> call)
    {
        string value;
        try { value = call().ToString(); }
        catch (Exception e) { value = e.GetType().Name; }
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
    }

    void Start()
    {
        Call("renamed.byBundle", RenamedByBundle);
        Call("renamed.byExecutable", RenamedByExecutable);
        Call("same.byBundle", SameByBundle);
        string generic;
        try
        {
            CallBack(Marshal.GetFunctionPointerForDelegate(s_Callback));
            generic = s_Received;
        }
        catch (Exception e) { generic = e.GetType().Name; }
        m_Out.Append("{\"k\":\"callback.generic\",\"v\":\"").Append(generic).Append("\"}\n");
        // The delegate as a P/Invoke argument.
        s_Received = "not called";
        string pinvoke;
        try
        {
            CallBackGeneric((a, b, c) => s_Received = a + "/" + b + "/" + c);
            pinvoke = s_Received;
        }
        catch (Exception e) { pinvoke = e.GetType().Name; }
        m_Out.Append("{\"k\":\"callback.pinvoke\",\"v\":\"").Append(pinvoke).Append("\"}\n");
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        string dir = Environment.GetEnvironmentVariable("PLUGIN_BUNDLE_OUT");
        if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
        else
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
