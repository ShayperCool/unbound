using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SubsystemsImplementation;
using UnityEngine.SubsystemsImplementation.Extensions;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Writes values.jsonl in $SUBSYSTEMS_OUT (tests/value_probe.py rows): the subsystem descriptors and
/// subsystems Play mode starts with, and what registering descriptors with providers, creating,
/// starting, stopping and destroying their subsystems does, in which order the subsystem's and the
/// provider's callbacks run, and what the manager lists after each step.
/// </summary>
public class SubsystemsProbe : MonoBehaviour
{
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();
    internal static readonly List<string> calls = new List<string>();
    internal static bool initializes = true;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Try(string key, Func<object> action)
    {
        logs.Clear();
        calls.Clear();
        string result;
        try
        {
            var value = action();
            result = ReferenceEquals(value, null) ? "returns null" : "returns " + value;
        }
        catch (Exception e)
        {
            if (e is TargetInvocationException t && t.InnerException != null) e = t.InnerException;
            result = "throws " + e.GetType().FullName + ": " + e.Message;
        }
        Row(key, result);
        if (calls.Count > 0) Row(key + ".calls", string.Join(",", calls));
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    static void Do(string key, Action action) => Try(key, () => { action(); return "done"; });

    static string Ids<T>(List<T> list) where T : ISubsystemDescriptor => list.Count + ":" + string.Join(",", list.Select(d => d.id + "/" + d.GetType().Name));

    static string Descriptors()
    {
        var all = new List<ISubsystemDescriptor>();
        SubsystemManager.GetAllSubsystemDescriptors(all);
        return Ids(all);
    }

    static string Subsystems()
    {
        var all = new List<ISubsystem>();
        SubsystemManager.GetSubsystems(all);
        return all.Count + ":" + string.Join(",", all.Select(s => s.GetType().Name + (s.running ? "+" : "-")));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SUBSYSTEMS_OUT"))) return;
        new GameObject("SubsystemsProbe").AddComponent<SubsystemsProbe>();
    }

    static int s_BeforeReload, s_AfterReload;

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("SUBSYSTEMS_OUT");
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        SubsystemManager.beforeReloadSubsystems += () => s_BeforeReload++;
        SubsystemManager.afterReloadSubsystems += () => s_AfterReload++;
        yield return null;
        try
        {
            Startup();
            Registration();
            Lifecycle();
            Variants();
        }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        yield return null;
        Try("end.subsystems", Subsystems);
        Try("end.reload_events", () => s_BeforeReload + "," + s_AfterReload);
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static void Startup()
    {
        Try("start.descriptors", Descriptors);
        var integrated = new List<IntegratedSubsystemDescriptor>();
        Try("start.integrated_descriptors", () => { SubsystemManager.GetSubsystemDescriptors(integrated); return integrated.Count; });
        Try("start.subsystems", Subsystems);
        var integratedSubsystems = new List<IntegratedSubsystem>();
        Try("start.integrated_subsystems", () => { SubsystemManager.GetSubsystems(integratedSubsystems); return integratedSubsystems.Count; });
        Try("start.list_cleared", () =>
        {
            var list = new List<ISubsystem> { null, null };
            SubsystemManager.GetSubsystems(list);
            return list.Count;
        });
    }

    static ProbeDescriptor s_Descriptor;

    static void Registration()
    {
        s_Descriptor = ProbeDescriptor.Make("probe.a", typeof(ProbeProvider), null);
        Try("register.descriptor", () => { SubsystemDescriptorStore.RegisterDescriptor(s_Descriptor); return Descriptors(); });
        var mine = new List<ProbeDescriptor>();
        Try("register.typed", () => { SubsystemManager.GetSubsystemDescriptors(mine); return Ids(mine); });
        Try("register.same_again", () => { SubsystemDescriptorStore.RegisterDescriptor(s_Descriptor); return Descriptors(); });
        Try("register.same_id", () => { SubsystemDescriptorStore.RegisterDescriptor(ProbeDescriptor.Make("probe.a", typeof(ProbeProvider), null)); return Descriptors(); });
        Try("register.null_id", () => { SubsystemDescriptorStore.RegisterDescriptor(ProbeDescriptor.Make(null, typeof(ProbeProvider), null)); return Descriptors(); });
        Try("register.empty_id", () => { SubsystemDescriptorStore.RegisterDescriptor(ProbeDescriptor.Make("", typeof(ProbeProvider), null)); return Descriptors(); });
        Try("register.no_provider", () => { SubsystemDescriptorStore.RegisterDescriptor(ProbeDescriptor.Make("probe.noprovider", null, null)); return Descriptors(); });
        Try("register.wrong_provider", () => { SubsystemDescriptorStore.RegisterDescriptor(ProbeDescriptor.Make("probe.wrongprovider", typeof(string), null)); return Descriptors(); });
        Try("register.wrong_override", () => { SubsystemDescriptorStore.RegisterDescriptor(ProbeDescriptor.Make("probe.wrongoverride", typeof(ProbeProvider), typeof(string))); return Descriptors(); });
        Try("register.null", () => { SubsystemDescriptorStore.RegisterDescriptor(null); return Descriptors(); });
        Try("register.descriptor_fields", () => s_Descriptor.id + "|" + s_Descriptor.Provider + "|" + (s_Descriptor.Override?.Name ?? "null"));
    }

    static void Lifecycle()
    {
        ProbeSubsystem first = null;
        Try("create", () => { first = s_Descriptor.Create(); return first?.GetType().Name; });
        Try("create.state", () => first.running + "|" + (first.subsystemDescriptor == s_Descriptor) + "|" + first.GetProvider()?.GetType().Name + "|" + first.GetProvider()?.running);
        Try("create.subsystems", Subsystems);
        Try("create.again", () => { var again = s_Descriptor.Create(); return ReferenceEquals(again, first) ? "same" : again?.GetType().Name; });
        Try("create.interface", () => { var again = ((ISubsystemDescriptor)s_Descriptor).Create(); return ReferenceEquals(again, first) ? "same" : again?.GetType().Name; });
        Try("create.typed_list", () => { var list = new List<ProbeSubsystem>(); SubsystemManager.GetSubsystems(list); return list.Count; });
        Do("run", () => first.Start());
        Try("run.state", () => first.running + "|" + first.GetProvider().running);
        Do("run.again", () => first.Start());
        Try("run.subsystems", Subsystems);
        Do("stop", () => first.Stop());
        Try("stop.state", () => first.running + "|" + first.GetProvider().running);
        Do("stop.again", () => first.Stop());
        Do("start_then_destroy.start", () => first.Start());
        Do("destroy", () => first.Destroy());
        Try("destroy.state", () => first.running + "|" + first.GetProvider().running);
        Try("destroy.subsystems", Subsystems);
        Do("destroy.again", () => first.Destroy());
        Do("destroy.start", () => first.Start());
        Try("recreate", () => { var again = s_Descriptor.Create(); return ReferenceEquals(again, first) ? "same" : again?.GetType().Name; });
        Try("recreate.subsystems", Subsystems);
    }

    static void Variants()
    {
        var failing = ProbeDescriptor.Make("probe.fail", typeof(ProbeProvider), null);
        SubsystemDescriptorStore.RegisterDescriptor(failing);
        initializes = false;
        Try("fail.create", () => failing.Create()?.GetType().Name);
        initializes = true;
        Try("fail.subsystems", Subsystems);
        Try("fail.create_again", () => failing.Create()?.GetType().Name);

        var derived = ProbeDescriptor.Make("probe.derived", typeof(ProbeProvider), typeof(DerivedProbeSubsystem));
        SubsystemDescriptorStore.RegisterDescriptor(derived);
        Try("derived.create", () => derived.Create()?.GetType().Name);
        Try("derived.subsystems", Subsystems);

        var proxied = ProbeDescriptor.Make("probe.proxy", typeof(ProbeProvider), null);
        SubsystemDescriptorStore.RegisterDescriptor(proxied);
        SubsystemProxy<ProbeSubsystem, ProbeSubsystem.Provider> proxy = null;
        Try("proxy.create", () => { proxy = proxied.CreateProxy(); return proxy?.provider?.GetType().Name; });
        Try("proxy.state", () => proxy.running + "|" + proxy.provider.running);
        Do("proxy.run", () => proxy.running = true);
        Try("proxy.run.state", () => proxy.running + "|" + proxy.provider.running);
        Do("proxy.stop", () => proxy.running = false);
        Try("proxy.subsystems", Subsystems);

        Try("descriptors.end", Descriptors);
    }
}

/// <summary>A subsystem whose callbacks are recorded.</summary>
public class ProbeSubsystem : SubsystemWithProvider<ProbeSubsystem, ProbeDescriptor, ProbeSubsystem.Provider>
{
    public abstract class Provider : SubsystemProvider<ProbeSubsystem> { }

    public ProbeSubsystem() => SubsystemsProbe.calls.Add(GetType().Name + ".ctor");
    protected override void OnCreate() { SubsystemsProbe.calls.Add("OnCreate"); base.OnCreate(); }
    protected override void OnStart() { SubsystemsProbe.calls.Add("OnStart"); base.OnStart(); }
    protected override void OnStop() { SubsystemsProbe.calls.Add("OnStop"); base.OnStop(); }
    protected override void OnDestroy() { SubsystemsProbe.calls.Add("OnDestroy"); base.OnDestroy(); }
}

public class DerivedProbeSubsystem : ProbeSubsystem { }

public class ProbeProvider : ProbeSubsystem.Provider
{
    public ProbeProvider() => SubsystemsProbe.calls.Add("Provider.ctor");
    protected override bool TryInitialize() { SubsystemsProbe.calls.Add("Provider.TryInitialize"); return SubsystemsProbe.initializes; }
    public override void Start() => SubsystemsProbe.calls.Add("Provider.Start");
    public override void Stop() => SubsystemsProbe.calls.Add("Provider.Stop");
    public override void Destroy() => SubsystemsProbe.calls.Add("Provider.Destroy");
}

public class ProbeDescriptor : SubsystemDescriptorWithProvider<ProbeSubsystem, ProbeSubsystem.Provider>
{
    public static ProbeDescriptor Make(string id, Type provider, Type subsystemOverride) =>
        new ProbeDescriptor { id = id, providerType = provider, subsystemTypeOverride = subsystemOverride };

    public string Provider => providerType?.Name ?? "null";
    public Type Override => subsystemTypeOverride;
}
