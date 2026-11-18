using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

/// <summary>Adds PROBE_DELEGATE_DEFINE through the internal BuildDefines.getScriptCompilationDefinesDelegates
/// event, as packages do, and notes each call (tests/projects/gameinterop builddefines.*).</summary>
[InitializeOnLoad]
public static class ProbeDefinesHook
{
    public static int Calls;
    public static string Registered = "not tried";

    static ProbeDefinesHook()
    {
        try
        {
            var type = typeof(EditorApplication).Assembly.GetType("UnityEditor.Build.BuildDefines");
            var e = type?.GetEvent("getScriptCompilationDefinesDelegates", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (e == null) { Registered = "no event"; return; }
            var handler = Delegate.CreateDelegate(e.EventHandlerType, typeof(ProbeDefinesHook).GetMethod(nameof(AddDefines), BindingFlags.Static | BindingFlags.NonPublic));
            e.GetAddMethod(true).Invoke(null, new object[] { handler });
            Registered = "added";
        }
        catch (Exception ex)
        {
            Registered = "throws " + (ex.InnerException ?? ex).GetType().Name;
        }
    }

    static void AddDefines(BuildTarget target, HashSet<string> defines)
    {
        Calls++;
        defines.Add("PROBE_DELEGATE_DEFINE");
    }
}
