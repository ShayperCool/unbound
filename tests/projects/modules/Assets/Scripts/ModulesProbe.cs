using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// What a scene's components from a module the project's manifest leaves out (here Physics:
/// the box's Rigidbody and BoxCollider) do in Play mode: the components the box has, whether
/// the module's types are found by name, and where the box is after ten fixed steps. Also every
/// message logged from the start of the session. Written as JSON lines to <c>MODULES_PROBE_OUT</c>.
/// This script itself uses only always-present modules.
/// </summary>
public class ModulesProbe : MonoBehaviour
{
    static readonly StringBuilder s_Log = new StringBuilder();
    int m_FixedSteps;
    Transform m_Body;

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    static void Record(string what, string value) =>
        s_Log.Append("{\"what\":\"").Append(Escape(what)).Append("\",\"value\":\"").Append(Escape(value)).Append("\"}\n");

    static string Number(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Listen() => Application.logMessageReceived += (condition, stackTrace, type) => Record("log", type + ": " + condition);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("ModulesProbe").AddComponent<ModulesProbe>();

    void Start()
    {
        var body = GameObject.Find("Body");
        Record("body", body != null ? "found" : "missing");
        if (body == null) { Finish(); return; }
        m_Body = body.transform;
        foreach (var component in body.GetComponents<Component>())
            Record("component", component == null ? "missing" : component.GetType().FullName + " in " + component.GetType().Assembly.GetName().Name);
        foreach (var name in new[] { "UnityEngine.Rigidbody, UnityEngine.PhysicsModule", "UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule" })
            Record("type " + name, Type.GetType(name) != null ? "found" : "not found");
        Record("start y", Number(m_Body.position.y));
    }

    void FixedUpdate()
    {
        if (m_Body == null) return;
        m_FixedSteps++;
        if (m_FixedSteps == 10)
        {
            Record("y after 10 fixed steps", Number(m_Body.position.y));
            Finish();
        }
    }

    static void Finish()
    {
        var output = Environment.GetEnvironmentVariable("MODULES_PROBE_OUT");
        if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, s_Log.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
