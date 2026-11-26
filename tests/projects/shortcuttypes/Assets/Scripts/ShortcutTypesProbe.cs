using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.ShortcutManagement;
#endif

/// <summary>
/// Writes values.jsonl in $SHORTCUTTYPES_OUT (tests/value_probe.py rows): the Editor's shortcut
/// value types and attributes, as packages' editor code builds and reads them. Only values that do
/// not depend on the Editor's OS are written: a key combination's text without modifiers for keys
/// the OS-specific name tables leave out, its menu hotkey string, key code, hash and equality
/// without the action/control pair, menu hotkey parsing, bindings, the attributes' stored values
/// (read by reflection, as packages with internal access read them), and the event arguments. An
/// exception is written as "throws" with its type and message.
/// </summary>
public class ShortcutTypesProbe : MonoBehaviour
{
    const string Variable = "SHORTCUTTYPES_OUT";
    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value ?? "null") + "\"}\n");

    static void Try(string key, Func<string> action)
    {
        string result;
        try { result = action(); }
        catch (TargetInvocationException e) { result = "throws " + e.InnerException.GetType().FullName + ": " + e.InnerException.Message; }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
    }

    static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("ShortcutTypesProbe").AddComponent<ShortcutTypesProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

#if UNITY_EDITOR
    // Methods carrying each attribute constructor; the probe reads the attributes back and runs
    // three through the entries it makes (their ids are the probe's own).
    [Shortcut("Probe/Plain")] static void Plain() { s_Ran.Add("Plain"); }
    [Shortcut("Probe/Context", typeof(UnityEditor.SceneView))] static void InContext() { }
    [Shortcut("Probe/Global", null, KeyCode.G, ShortcutModifiers.Shift)] static void GlobalKey() { }
    [Shortcut("Probe/Keyed", typeof(UnityEditor.SceneView), KeyCode.K, ShortcutModifiers.Alt | ShortcutModifiers.Shift)] static void Keyed() { }
    [Shortcut("Probe/Tagged", typeof(UnityEditor.SceneView), "probe-tag", KeyCode.T)] static void Tagged() { }
    [Shortcut("Probe/Prioritized", typeof(UnityEditor.SceneView), "probe-tag", KeyCode.P, ShortcutModifiers.Action, 7)] static void Prioritized() { }
    [Shortcut("Probe/KeyOnly", KeyCode.F5)] static void KeyOnly() { }
    [Shortcut("Probe/Named", KeyCode.N, ShortcutModifiers.Action | ShortcutModifiers.Alt, displayName = "Probe Display Name")] static void Named() { }
    [Shortcut("Probe/Twice/A", KeyCode.A), Shortcut("Probe/Twice/B", KeyCode.B)] static void Twice() { }
    [Shortcut("Probe/Arguments", KeyCode.Q)] static void WithArguments(ShortcutArguments arguments) { s_Ran.Add("Arguments:" + arguments.stage + ":" + (arguments.context == null)); }
    [ClutchShortcut("Probe/Clutch")] static void Clutch(ShortcutArguments arguments) { s_Ran.Add("Clutch:" + arguments.stage); }
    [ClutchShortcut("Probe/ClutchKeyed", typeof(UnityEditor.SceneView), KeyCode.C, ShortcutModifiers.Shift)] static void ClutchKeyed(ShortcutArguments arguments) { }
    [ClutchShortcut("Probe/ClutchTagged", typeof(UnityEditor.SceneView), "probe-tag", KeyCode.V)] static void ClutchTagged(ShortcutArguments arguments) { }
    [ClutchShortcut("Probe/ClutchPrioritized", null, "probe-tag", KeyCode.X, ShortcutModifiers.None, 3)] static void ClutchPrioritized(ShortcutArguments arguments) { }
    [ClutchShortcut("Probe/ClutchKeyOnly", KeyCode.Z, ShortcutModifiers.Alt)] static void ClutchKeyOnly(ShortcutArguments arguments) { }
    [Shortcut("Probe/Reserved", KeyCode.R), ReserveModifiers(ShortcutModifiers.Shift | ShortcutModifiers.Alt)] static void Reserved() { }

    static readonly KeyCode[] Keys =
    {
        KeyCode.None, KeyCode.A, KeyCode.Z, KeyCode.Alpha0, KeyCode.Alpha7, KeyCode.Space, KeyCode.Insert,
        KeyCode.Home, KeyCode.End, KeyCode.Tab, KeyCode.Backspace, KeyCode.Return, KeyCode.Escape, KeyCode.Delete,
        KeyCode.LeftArrow, KeyCode.PageUp, KeyCode.F1, KeyCode.F12, KeyCode.F15, KeyCode.F16, KeyCode.F24,
        KeyCode.Keypad0, KeyCode.KeypadEnter, KeyCode.KeypadPlus, KeyCode.Comma, KeyCode.Slash, KeyCode.LeftBracket,
        KeyCode.Backslash, KeyCode.BackQuote, KeyCode.Tilde, KeyCode.Exclaim, KeyCode.Quote, KeyCode.Hash,
        KeyCode.Mouse0, KeyCode.Mouse6, KeyCode.WheelUp, KeyCode.LeftShift, KeyCode.RightAlt,
        KeyCode.Numlock, KeyCode.CapsLock, KeyCode.Print, KeyCode.Menu, KeyCode.JoystickButton0,
        KeyCode.Joystick8Button19, (KeyCode)999, (KeyCode)(-1),
    };

    // OS name tables (macOS glyphs, other platforms' words): text with these keys depends on the OS.
    static readonly HashSet<KeyCode> OsNamedKeys = new HashSet<KeyCode>
    {
        KeyCode.Backspace, KeyCode.Tab, KeyCode.Return, KeyCode.Escape, KeyCode.Delete, KeyCode.UpArrow,
        KeyCode.DownArrow, KeyCode.RightArrow, KeyCode.LeftArrow, KeyCode.Home, KeyCode.End, KeyCode.PageUp,
        KeyCode.PageDown,
    };

    static string Name(KeyCode key) => Enum.IsDefined(typeof(KeyCode), key) ? key.ToString() : I((int)key);

    static string Mods(ShortcutModifiers m) => I((int)m);

    static string Sequence(IEnumerable<KeyCombination> sequence) =>
        sequence == null ? "null" : string.Join(";", sequence.Select(c => Name(c.keyCode) + "+" + Mods(c.modifiers)).ToArray());

    static T Prop<T>(object target, string name) =>
        (T)target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);

    static string ContextName(Type type) => type == null ? "null" : type.FullName;

    static void Run()
    {
        var combination = typeof(KeyCombination);
        var menuString = combination.GetMethod("ToMenuShortcutString", BindingFlags.Instance | BindingFlags.NonPublic);
        var parse = combination.GetMethod("TryParseMenuItemBindingString", BindingFlags.Static | BindingFlags.NonPublic);
        var fromInput = combination.GetMethod("FromKeyboardInput", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(KeyCode), typeof(EventModifiers) }, null);
        var sequenceToMenu = combination.GetMethod("SequenceToMenuString", BindingFlags.Static | BindingFlags.NonPublic);
        Row("reflection", (menuString != null) + "," + (parse != null) + "," + (fromInput != null) + "," + (sequenceToMenu != null));

        // Key codes, plain text, menu strings and hashes of every key with each modifier set.
        foreach (var key in Keys)
        {
            var k = "key." + Name(key);
            var plain = new KeyCombination(key);
            Row(k + ".keyCode", Name(plain.keyCode));
            if (!OsNamedKeys.Contains(key))
                Try(k + ".text", () => plain.ToString());
            for (int m = 0; m < 16; m++)
            {
                var c = new KeyCombination(key, (ShortcutModifiers)m);
                Try(k + ".menu." + m, () => (string)menuString.Invoke(c, null));
                Row(k + ".hash." + m, I(c.GetHashCode()));
            }
        }
        var flags = new KeyCombination(KeyCode.A, (ShortcutModifiers)15);
        Row("flags.15", flags.alt + "," + flags.action + "," + flags.shift + "," + flags.control + "," + Mods(flags.modifiers));
        var odd = new KeyCombination(KeyCode.A, (ShortcutModifiers)48);
        Row("flags.48", odd.alt + "," + odd.action + "," + odd.shift + "," + odd.control + "," + Mods(odd.modifiers));
        Try("key.A.menu.48", () => (string)menuString.Invoke(odd, null));
        KeyCombination defaultCombination = default;
        Row("default.keyCode", Name(defaultCombination.keyCode));
        Row("default.modifiers", Mods(defaultCombination.modifiers));
        Try("default.text", () => defaultCombination.ToString());
        Row("default.hash", I(defaultCombination.GetHashCode()));

        // Equality without the action/control pair (that pair compares by OS).
        var pairs = new (string, KeyCombination, KeyCombination)[]
        {
            ("same", new KeyCombination(KeyCode.A, ShortcutModifiers.Alt), new KeyCombination(KeyCode.A, ShortcutModifiers.Alt)),
            ("key", new KeyCombination(KeyCode.A), new KeyCombination(KeyCode.B)),
            ("altShift", new KeyCombination(KeyCode.A, ShortcutModifiers.Alt), new KeyCombination(KeyCode.A, ShortcutModifiers.Shift)),
            ("actionNone", new KeyCombination(KeyCode.A, ShortcutModifiers.Action), new KeyCombination(KeyCode.A)),
            ("controlShift", new KeyCombination(KeyCode.A, ShortcutModifiers.Control), new KeyCombination(KeyCode.A, ShortcutModifiers.Shift)),
            ("unsupported", new KeyCombination((KeyCode)999), new KeyCombination((KeyCode)998)),
            ("unsupportedNone", new KeyCombination((KeyCode)999), new KeyCombination(KeyCode.None)),
        };
        foreach (var (name, a, b) in pairs)
            Row("equals." + name, a.Equals(b) + "," + a.Equals((object)b) + "," + (a.GetHashCode() == b.GetHashCode()));
        Row("equals.null", new KeyCombination(KeyCode.A).Equals(null).ToString());
        Row("equals.other", new KeyCombination(KeyCode.A).Equals((object)KeyCode.A).ToString());

        // Menu hotkey strings parsed back.
        foreach (var text in new[] { "%a", "#&k", "_g", "a", "", null, "%LEFT", "%left", "^#space", "&F24", "%F25", "%M3", "%ab", "%%a", "%_a", "_", "%", "%1", "%,", "%;", "%A", "%Ä", "#ENTER", "%del" })
        {
            var key = "parse." + (text == null ? "null" : text.Length == 0 ? "empty" : text);
            Try(key, () =>
            {
                var args = new object[] { text, null };
                var ok = (bool)parse.Invoke(null, args);
                var parsed = (KeyCombination)args[1];
                return ok + "," + Name(parsed.keyCode) + "," + Mods(parsed.modifiers);
            });
        }

        // Keyboard input without the OS-dependent control and command modifiers.
        foreach (var m in new[] { EventModifiers.None, EventModifiers.Alt, EventModifiers.Shift, EventModifiers.Alt | EventModifiers.Shift, EventModifiers.FunctionKey | EventModifiers.Numeric | EventModifiers.CapsLock })
            Try("input." + (int)m, () => { var c = (KeyCombination)fromInput.Invoke(null, new object[] { KeyCode.J, m }); return Name(c.keyCode) + "," + Mods(c.modifiers); });

        // Bindings.
        var empty = ShortcutBinding.empty;
        Row("binding.empty.sequence", Sequence(empty.keyCombinationSequence));
        Try("binding.empty.text", () => empty.ToString());
        Row("binding.empty.hash", I(empty.GetHashCode()));
        Row("binding.empty.equalsDefault", empty.Equals(default(ShortcutBinding)) + "," + empty.Equals((object)default(ShortcutBinding)) + "," + empty.Equals(null));
        var one = new ShortcutBinding(new KeyCombination(KeyCode.F, ShortcutModifiers.Alt));
        Row("binding.one.sequence", Sequence(one.keyCombinationSequence));
        Row("binding.one.hash", I(one.GetHashCode()));
        Row("binding.one.equals", one.Equals(new ShortcutBinding(new KeyCombination(KeyCode.F, ShortcutModifiers.Alt))) + "," + one.Equals(empty) + "," + one.Equals((object)KeyCode.F));
        Try("binding.plain.text", () => new ShortcutBinding(new KeyCombination(KeyCode.F)).ToString());
        Try("binding.none.sequence", () => Sequence(new ShortcutBinding(new KeyCombination(KeyCode.None)).keyCombinationSequence));
        var sequenceCtor = typeof(ShortcutBinding).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(IEnumerable<KeyCombination>) }, null);
        Row("binding.sequenceCtor", (sequenceCtor != null).ToString());
        var seq3 = new[] { new KeyCombination(KeyCode.A, ShortcutModifiers.Shift), new KeyCombination(KeyCode.B), new KeyCombination(KeyCode.Alpha3, ShortcutModifiers.Alt) };
        Try("binding.three", () => { var b = (ShortcutBinding)sequenceCtor.Invoke(new object[] { seq3 }); return Sequence(b.keyCombinationSequence) + "|" + I(b.GetHashCode()); });
        Try("binding.three.text", () => ((ShortcutBinding)sequenceCtor.Invoke(new object[] { new[] { new KeyCombination(KeyCode.B), new KeyCombination(KeyCode.Alpha3) } })).ToString());
        Try("binding.three.menu", () => (string)sequenceToMenu.Invoke(null, new object[] { seq3 }));
        Try("binding.single.menu", () => (string)sequenceToMenu.Invoke(null, new object[] { new[] { new KeyCombination(KeyCode.M, ShortcutModifiers.Action) } }));
        Try("binding.zero.menu", () => (string)sequenceToMenu.Invoke(null, new object[] { new KeyCombination[0] }));
        Try("binding.zero", () => { var b = (ShortcutBinding)sequenceCtor.Invoke(new object[] { new KeyCombination[0] }); return Sequence(b.keyCombinationSequence) + "|" + I(b.GetHashCode()) + "|" + b.Equals(empty); });
        Try("binding.null", () => Sequence(((ShortcutBinding)sequenceCtor.Invoke(new object[] { null })).keyCombinationSequence));
        Try("binding.copy", () =>
        {
            var source = new List<KeyCombination> { new KeyCombination(KeyCode.D) };
            var b = (ShortcutBinding)sequenceCtor.Invoke(new object[] { source });
            source.Add(new KeyCombination(KeyCode.E));
            return Sequence(b.keyCombinationSequence);
        });

        // The attributes as their constructors leave them.
        var methods = typeof(ShortcutTypesProbe).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(m => m.GetCustomAttributes(typeof(ShortcutBaseAttribute), false).Length > 0)
            .OrderBy(m => m.Name, StringComparer.Ordinal);
        foreach (var method in methods)
        {
            var attributes = method.GetCustomAttributes(typeof(ShortcutAttribute), false).Cast<ShortcutAttribute>()
                .OrderBy(a => Prop<string>(a, "identifier"), StringComparer.Ordinal).ToArray();
            Row("attribute." + method.Name + ".count", I(attributes.Length));
            for (int i = 0; i < attributes.Length; i++)
            {
                var a = attributes[i];
                var k = "attribute." + method.Name + "." + i;
                Row(k + ".type", a.GetType().Name);
                Row(k + ".identifier", Prop<string>(a, "identifier"));
                Row(k + ".displayName", a.displayName);
                Row(k + ".context", ContextName(Prop<Type>(a, "context")));
                Row(k + ".tag", Prop<string>(a, "tag"));
                Row(k + ".priority", I(Prop<int>(a, "priority")));
                Row(k + ".binding", Sequence(Prop<ShortcutBinding>(a, "defaultBinding").keyCombinationSequence));
                if (a is ClutchShortcutAttribute)
                {
                    var field = typeof(ClutchShortcutAttribute).GetField("clutchActivatedContext", BindingFlags.Instance | BindingFlags.NonPublic);
                    Row(k + ".clutchActivatedContext", field == null ? "missing" : ContextName((Type)field.GetValue(a)));
                }
            }
            var reserved = method.GetCustomAttributes(typeof(ReserveModifiersAttribute), false).Cast<ReserveModifiersAttribute>().ToArray();
            if (reserved.Length > 0)
                Row("attribute." + method.Name + ".reserved", string.Join(",", reserved.Select(r => Mods(r.Modifiers)).ToArray()));
        }
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(typeof(ShortcutAttribute), typeof(AttributeUsageAttribute));
        Row("usage.shortcut", I((int)usage.ValidOn) + "," + usage.AllowMultiple + "," + usage.Inherited);
        usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(typeof(ClutchShortcutAttribute), typeof(AttributeUsageAttribute));
        Row("usage.clutch", I((int)usage.ValidOn) + "," + usage.AllowMultiple + "," + usage.Inherited);
        usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(typeof(ReserveModifiersAttribute), typeof(AttributeUsageAttribute));
        Row("usage.reserve", I((int)usage.ValidOn) + "," + usage.AllowMultiple + "," + usage.Inherited);
        Try("attribute.nullId", () => { var a = new ShortcutAttribute(null); return (Prop<string>(a, "identifier") ?? "null") + "," + (a.displayName ?? "null"); });
        Try("attribute.displayNameSet", () => { var a = new ShortcutAttribute("Probe/Set") { displayName = null }; return (a.displayName ?? "null") + "," + Prop<string>(a, "identifier"); });
        Try("attribute.priorityMax", () => I(Prop<int>(new ShortcutAttribute("Probe/Max", typeof(UnityEditor.SceneView), null, KeyCode.A, ShortcutModifiers.None, int.MaxValue), "priority")));
        Try("attribute.priorityNegative", () => I(Prop<int>(new ShortcutAttribute("Probe/Neg", null, null, KeyCode.A, ShortcutModifiers.None, -5), "priority")));
        Try("attribute.globalContextType", () => ContextName((Type)typeof(ShortcutAttribute).Assembly.GetType("UnityEditor.ShortcutManagement.ContextManager")
            .GetField("globalContextType", BindingFlags.Static | BindingFlags.Public).GetValue(null)));
        Try("attribute.globalContext", () =>
        {
            var global = typeof(ShortcutAttribute).Assembly.GetType("UnityEditor.ShortcutManagement.ContextManager+GlobalContext");
            return ContextName(global) + "," + I(Prop<int>(new ShortcutAttribute("Probe/InGlobal", global), "priority"))
                + "," + I(Prop<int>(new ShortcutAttribute("Probe/InGlobalKeyed", global, KeyCode.A), "priority"));
        });
        Try("attribute.formerlyPrefKey", () =>
        {
            var type = typeof(ShortcutAttribute).Assembly.GetType("UnityEditor.ShortcutManagement.FormerlyPrefKeyAsAttribute");
            var a = Activator.CreateInstance(type, "Probe/Pref", "#k");
            var u = (AttributeUsageAttribute)Attribute.GetCustomAttribute(type, typeof(AttributeUsageAttribute));
            return type.GetField("name").GetValue(a) + "," + type.GetField("defaultValue").GetValue(a) + "," + I((int)u.ValidOn) + "," + u.AllowMultiple;
        });

        // The value types of the shortcut manager's events and actions.
        ShortcutArguments arguments = default;
        Row("arguments.default", (arguments.context == null) + "," + arguments.stage);
        var changed = new ShortcutBindingChangedEventArgs("Probe/Changed", ShortcutBinding.empty, one);
        Row("changed", changed.shortcutId + "," + Sequence(changed.oldBinding.keyCombinationSequence) + "," + Sequence(changed.newBinding.keyCombinationSequence));
        ShortcutBindingChangedEventArgs changedDefault = default;
        Row("changed.default", (changedDefault.shortcutId ?? "null") + "," + Sequence(changedDefault.newBinding.keyCombinationSequence));
        var profile = new ActiveProfileChangedEventArgs("Probe/Before", "Probe/After");
        Row("profile", profile.previousActiveProfileId + "," + profile.currentActiveProfileId);
        Row("modifiers", string.Join(",", Enum.GetValues(typeof(ShortcutModifiers)).Cast<ShortcutModifiers>().Select(m => m + "=" + Mods(m)).ToArray()));
        Row("stages", string.Join(",", Enum.GetValues(typeof(ShortcutStage)).Cast<ShortcutStage>().Select(s => s + "=" + I((int)s)).ToArray()));
        Try("identifier", () =>
        {
            var type = typeof(ShortcutAttribute).Assembly.GetType("UnityEditor.ShortcutManagement.Identifier");
            var id = Activator.CreateInstance(type, "Probe/Path");
            var separator = type.GetField("kPathSeparator", BindingFlags.Static | BindingFlags.Public).GetValue(null);
            return id + "," + separator + "," + Activator.CreateInstance(type);
        });

        Registry();
    }

    // Tool contexts the registry rows register (public, so the context manager can test them).
    public class ProbeTool : IShortcutContext { public bool active { get; set; } }
    public class ProbeSubTool : ProbeTool { }
    [ReserveModifiers(ShortcutModifiers.Shift)]
    public class ProbeReservingTool : IShortcutContext { public bool active => true; }
    public enum ProbeMode { First, Second }

    [Shortcut("Probe/Registry/Tool", typeof(ProbeTool), KeyCode.T)] static void ToolKey() { }
    [Shortcut("Probe/Registry/SubTool", typeof(ProbeSubTool), KeyCode.T)] static void SubToolKey() { }
    [Shortcut("Probe/Registry/GlobalT", KeyCode.T)] static void GlobalT() { }
    [Shortcut("Probe/Registry/TaggedT", null, "probe-registry-tag", KeyCode.T)] static void TaggedT() { }
    [Shortcut("Probe/Registry/Chord", KeyCode.T, ShortcutModifiers.Alt)] static void ChordStart() { }
    [ClutchShortcut("Probe/Registry/ClutchMouse", typeof(ProbeTool), KeyCode.Mouse1)] static void ClutchMouse(ShortcutArguments arguments) { }
    [Shortcut("Probe/Registry/ActionMouse", typeof(ProbeTool), KeyCode.Mouse1)] static void ActionMouse() { }
    [Shortcut("Probe/Registry/Reserving", typeof(ProbeReservingTool), KeyCode.R, ShortcutModifiers.Shift)] static void Reserving() { }
    [Shortcut("Probe/Registry/Unbound")] static void Unbound() { }

    static Type Internal(string name) => typeof(ShortcutAttribute).Assembly.GetType("UnityEditor.ShortcutManagement." + name);

    static object Call(object target, string name, params object[] args)
    {
        var type = target as Type ?? target.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | (target is Type ? BindingFlags.Static : BindingFlags.Instance);
        var method = type.GetMethods(flags).First(m => m.Name == name && m.GetParameters().Length == args.Length
            && m.GetParameters().Zip(args, (p, a) => a == null || p.ParameterType.IsInstanceOfType(a)).All(x => x));
        return method.Invoke(target is Type ? null : target, args);
    }

    static string Entry(object entry)
    {
        if (entry == null) return "null";
        var combinations = (System.Collections.IEnumerable)Prop<object>(entry, "combinations");
        return Prop<object>(entry, "identifier") + "|" + Prop<string>(entry, "displayName") + "|"
            + Sequence(combinations.Cast<KeyCombination>()) + "|" + ContextName(Prop<Type>(entry, "context")) + "|"
            + (Prop<string>(entry, "tag") ?? "null") + "|" + Prop<object>(entry, "type") + "|" + I(Prop<int>(entry, "priority")) + "|"
            + Mods((ShortcutModifiers)entry.GetType().GetField("m_ReservedModifier", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(entry)) + "|"
            + ContextName(Prop<Type>(entry, "clutchContext")) + "|" + Prop<bool>(entry, "overridden");
    }

    static string Ids(System.Collections.IList entries) =>
        string.Join(",", entries.Cast<object>().Select(e => Prop<object>(e, "identifier").ToString()).ToArray());

    /// <summary>The shortcut registry's data: a context manager, the entries the probe's attributes
    /// make and a directory over them, built by reflection as the Editor's shortcut controller
    /// builds them (the probe's own instances, not the Editor's registry).</summary>
    static void Registry()
    {
        Try("registry.isKeyCodeSupported", () => string.Join(",", Keys.Select(k => Name(k) + "=" + Call(Internal("Directory"), "IsKeyCodeSupported", k)).ToArray()));

        var managerType = Internal("ContextManager");
        var manager = Activator.CreateInstance(managerType);
        var global = Internal("ContextManager+GlobalContext");
        Try("context.new", () => I(Prop<int>(manager, "activeContextCount")) + "," + Prop<bool>(manager, "playModeContextIsActive") + "," + Call(manager, "HasAnyPriorityContext"));
        Try("context.globalInstance", () => { var g = Call(manager, "GetContextInstanceOfType", global, true, false); return (g != null) + "," + ReferenceEquals(g, managerType.GetField("globalContext").GetValue(null)); });
        Try("context.nullType", () => (Call(manager, "GetContextInstanceOfType", null, true, false) == null).ToString());
        var tool = new ProbeTool { active = false };
        var sub = new ProbeSubTool { active = true };
        Call(manager, "RegisterToolContext", tool);
        Call(manager, "RegisterToolContext", tool);
        Call(manager, "RegisterToolContext", sub);
        Call(manager, "RegisterToolContext", new object[] { null });
        Try("context.registered", () => I(Prop<int>(manager, "activeContextCount")) + "," + Call(manager, "HasAnyPriorityContext"));
        Try("context.activeOfType", () => Call(manager, "HasActiveContextOfType", typeof(ProbeTool), false) + "," + Call(manager, "HasActiveContextOfType", typeof(ProbeSubTool), false) + "," + Call(manager, "HasActiveContextOfType", typeof(UnityEditor.SceneView), false) + "," + Call(manager, "HasActiveContextOfType", global, false));
        Try("context.instanceUnfiltered", () => { var c = Call(manager, "GetContextInstanceOfType", typeof(UnityEditor.SceneView), false, false); return c == null ? "null" : c.GetType().Name; });
        Try("context.instanceFiltered", () => { var c = Call(manager, "GetContextInstanceOfType", typeof(ProbeTool), true, false); return c == null ? "null" : c.GetType().Name; });
        Try("context.activeContexts", () => string.Join(",", ((List<Type>)managerType.GetInterface("IContextManager").GetMethod("GetActiveContexts").Invoke(manager, null)).Select(t => t.Name).ToArray()));
        tool.active = true;
        Try("context.activated", () => I(Prop<int>(manager, "activeContextCount")) + "," + string.Join(",", ((List<Type>)managerType.GetInterface("IContextManager").GetMethod("GetActiveContexts").Invoke(manager, null)).Select(t => t.Name).ToArray()));
        Call(manager, "DeregisterToolContext", sub);
        Call(manager, "DeregisterToolContext", sub);
        Try("context.deregistered", () => I(Prop<int>(manager, "activeContextCount")));
        Try("context.conflicts", () => string.Join(",", new[]
        {
            Call(manager, "DoContextsConflict", typeof(ProbeTool), typeof(ProbeSubTool)),
            Call(manager, "DoContextsConflict", typeof(ProbeSubTool), typeof(ProbeTool)),
            Call(manager, "DoContextsConflict", typeof(ProbeTool), typeof(ProbeReservingTool)),
            Call(manager, "DoContextsConflict", global, global),
            Call(manager, "DoContextsConflict", typeof(UnityEditor.EditorWindow), typeof(UnityEditor.SceneView)),
        }.Select(o => o.ToString()).ToArray()));
        Try("context.enumTagFormat", () => (string)Call(managerType, "EnumTagFormat", ProbeMode.Second));
        Try("context.tags", () =>
        {
            var steps = new List<string>();
            int changes = 0;
            var onTagChange = managerType.GetField("onTagChange");
            var previous = (Action)onTagChange.GetValue(null);
            onTagChange.SetValue(null, (Action)(() => changes++));
            try
            {
                Func<string> mine = () => string.Join(";", ((List<string>)Call(manager, "GetActiveTags")).Where(t => t.StartsWith("probe-") || t.StartsWith("ProbeMode.")).ToArray());
                Call(manager, "RegisterTag", "probe-a");
                Call(manager, "RegisterTag", "probe-a");
                Call(manager, "RegisterTag", "probe-b");
                steps.Add(mine() + "/" + changes + "/" + Call(manager, "HasTag", "probe-a") + "/" + Call(manager, "HasTag", new object[] { null }));
                Call(manager, "RegisterTag", (Enum)ProbeMode.First);
                Call(manager, "RegisterTag", (Enum)ProbeMode.Second);
                steps.Add(mine() + "/" + changes);
                Call(manager, "UnregisterTag", (Enum)ProbeMode.Second);
                Call(manager, "UnregisterTag", "probe-a");
                Call(manager, "UnregisterTag", "probe-a");
                Call(manager, "UnregisterTag", "probe-b");
                steps.Add(mine() + "/" + changes);
                // A second manager shares the Editor-wide tag set.
                Call(manager, "RegisterTag", "probe-shared");
                steps.Add(Call(Activator.CreateInstance(managerType), "HasTag", "probe-shared").ToString());
                Call(manager, "UnregisterTag", "probe-shared");
            }
            finally { onTagChange.SetValue(null, previous); }
            return string.Join(" ", steps.ToArray());
        });

        // The entries the probe's attributes make.
        var entryType = Internal("ShortcutEntry");
        var methods = typeof(ShortcutTypesProbe).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(m => m.GetCustomAttributes(typeof(ShortcutBaseAttribute), false).Length > 0)
            .OrderBy(m => m.Name, StringComparer.Ordinal);
        var entries = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType));
        var create = typeof(ShortcutBaseAttribute).GetMethod("CreateShortcutEntry", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var method in methods)
            foreach (var attribute in method.GetCustomAttributes(typeof(ShortcutAttribute), false).Cast<ShortcutAttribute>().OrderBy(a => Prop<string>(a, "identifier"), StringComparer.Ordinal))
            {
                var id = Prop<string>(attribute, "identifier");
                Try("entry." + id, () => { var e = create.Invoke(attribute, new object[] { method }); entries.Add(e); return Entry(e); });
            }
        Try("entry.text.Twice", () => entries.Cast<object>().First(e => Prop<object>(e, "identifier").ToString() == "Probe/Twice/A").ToString());
        Try("entry.text.Tagged", () => entries.Cast<object>().First(e => Prop<object>(e, "identifier").ToString() == "Probe/Tagged").ToString());
        Try("entry.text.Unbound", () => entries.Cast<object>().First(e => Prop<object>(e, "identifier").ToString() == "Probe/Registry/Unbound").ToString());
        Try("entry.action", () =>
        {
            s_Ran.Clear();
            foreach (var name in new[] { "Probe/Plain", "Probe/Arguments", "Probe/Clutch" })
            {
                var e = entries.Cast<object>().First(x => Prop<object>(x, "identifier").ToString() == name);
                ((Action<ShortcutArguments>)Prop<object>(e, "action"))(new ShortcutArguments { stage = ShortcutStage.End });
            }
            return string.Join(",", s_Ran.ToArray());
        });
        Try("entry.direct", () =>
        {
            var identifier = Activator.CreateInstance(Internal("Identifier"), "Probe/Direct");
            var shortcutType = Enum.ToObject(Internal("ShortcutType"), 2);
            var ctor = entryType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First(c => c.GetParameters().Length == 7);
            var direct = ctor.Invoke(new object[] { identifier, new[] { new KeyCombination(KeyCode.D) }, null, null, shortcutType, null, int.MaxValue });
            var named = ctor.Invoke(new object[] { identifier, new KeyCombination[0], null, typeof(ProbeReservingTool), shortcutType, "Direct Name", 12 });
            return Entry(direct) + " " + Entry(named);
        });
        Try("entry.startsWith", () =>
        {
            Func<string, object> find = name => entries.Cast<object>().First(x => Prop<object>(x, "identifier").ToString() == name);
            var chord = find("Probe/Registry/Chord");
            var reserving = find("Probe/Registry/Reserving");
            Func<object, KeyCombination[], object, string> starts = (e, prefix, keys) => Call(e, "StartsWith", new List<KeyCombination>(prefix), keys).ToString();
            return string.Join(",", new[]
            {
                starts(chord, new KeyCombination[0], null),
                starts(chord, new[] { new KeyCombination(KeyCode.T, ShortcutModifiers.Alt) }, null),
                starts(chord, new[] { new KeyCombination(KeyCode.T) }, null),
                starts(chord, new[] { new KeyCombination(KeyCode.T, ShortcutModifiers.Alt), new KeyCombination(KeyCode.U) }, null),
                starts(reserving, new[] { new KeyCombination(KeyCode.R) }, null),
                starts(reserving, new[] { new KeyCombination(KeyCode.R, ShortcutModifiers.Shift | ShortcutModifiers.Alt) }, null),
                starts(find("Probe/Registry/ActionMouse"), new[] { new KeyCombination(KeyCode.Mouse1) }, new[] { KeyCode.Mouse1 }),
                starts(chord, new[] { new KeyCombination(KeyCode.T, ShortcutModifiers.Alt) }, new[] { KeyCode.Mouse1 }),
                Call(chord, "FullyMatches", new List<KeyCombination> { new KeyCombination(KeyCode.T, ShortcutModifiers.Alt) }).ToString(),
                Call(find("Probe/Registry/Unbound"), "FullyMatches", new List<KeyCombination>()).ToString(),
            });
        });

        // A directory over them.
        var directory = Activator.CreateInstance(Internal("Directory"), entries);
        Func<System.Collections.IList> list = () => (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType));
        Try("directory.all", () => { var all = list(); Call(directory, "GetAllShortcuts", all); return I(all.Count); });
        Try("directory.find", () => string.Join(";", new[] { "Probe/Keyed", "Probe/Registry/Unbound", "Probe/Missing", "probe/keyed" }
            .Select(id => Entry(Call(directory, "FindShortcutEntry", id))).ToArray()));
        Try("directory.findNull", () => Entry(Call(directory, "FindShortcutEntry", new object[] { null })));
        var lookups = new (string, KeyCombination[], Type[], string[])[]
        {
            ("t.any", new[] { new KeyCombination(KeyCode.T) }, null, null),
            ("t.tool", new[] { new KeyCombination(KeyCode.T) }, new[] { typeof(ProbeTool) }, null),
            ("t.sub", new[] { new KeyCombination(KeyCode.T) }, new[] { typeof(ProbeSubTool) }, null),
            ("t.global", new[] { new KeyCombination(KeyCode.T) }, new[] { global }, null),
            ("t.scene", new[] { new KeyCombination(KeyCode.T) }, new[] { typeof(UnityEditor.SceneView) }, null),
            ("t.globalTag", new[] { new KeyCombination(KeyCode.T) }, new[] { global }, new[] { "probe-registry-tag" }),
            ("t.globalOtherTag", new[] { new KeyCombination(KeyCode.T) }, new[] { global }, new[] { "probe-other" }),
            ("t.globalNoTags", new[] { new KeyCombination(KeyCode.T) }, new[] { global }, new string[0]),
            ("t.sceneTag", new[] { new KeyCombination(KeyCode.T) }, new[] { typeof(UnityEditor.SceneView) }, new[] { "probe-tag" }),
            ("altT", new[] { new KeyCombination(KeyCode.T, ShortcutModifiers.Alt) }, null, null),
            ("mouse", new[] { new KeyCombination(KeyCode.Mouse1) }, new[] { typeof(ProbeTool) }, null),
            ("empty", new KeyCombination[0], null, null),
            ("none", new[] { new KeyCombination(KeyCode.None) }, null, null),
        };
        foreach (var (name, sequence, contexts, tags) in lookups)
            Try("directory.lookup." + name, () => { var found = list(); Call(directory, "FindShortcutEntries", new List<KeyCombination>(sequence), contexts, tags, found); return Ids(found); });
        Try("directory.lookup.manager", () =>
        {
            var found = list();
            Call(directory, "FindShortcutEntries", new List<KeyCombination> { new KeyCombination(KeyCode.T) }, manager, found);
            var withTag = list();
            Call(manager, "RegisterTag", "probe-registry-tag");
            try { Call(directory, "FindShortcutEntries", new List<KeyCombination> { new KeyCombination(KeyCode.T) }, manager, withTag); }
            finally { Call(manager, "UnregisterTag", "probe-registry-tag"); }
            var potential = list();
            Call(directory, "FindPotentialShortcutEntries", manager, potential, false);
            return Ids(found) + " | " + Ids(withTag) + " | " + Ids(potential);
        });
        Try("directory.conflicts", () =>
        {
            var parts = new List<string>();
            foreach (var (context, tag, binding) in new (Type, string, KeyCombination[])[]
            {
                (typeof(ProbeTool), null, new[] { new KeyCombination(KeyCode.T) }),
                (global, null, new[] { new KeyCombination(KeyCode.T) }),
                (global, null, new[] { new KeyCombination(KeyCode.T, ShortcutModifiers.Alt), new KeyCombination(KeyCode.Y) }),
                (global, "probe-registry-tag", new[] { new KeyCombination(KeyCode.T) }),
                (typeof(ProbeTool), null, new[] { new KeyCombination(KeyCode.Mouse1) }),
                (global, null, new KeyCombination[0]),
            })
            {
                var output = list();
                Call(directory, "FindPotentialConflicts", context, tag, new List<KeyCombination>(binding), output, manager);
                parts.Add(Ids(output));
            }
            var withConflicts = list();
            Call(directory, "FindShortcutsWithConflicts", withConflicts, manager);
            parts.Add(string.Join(",", withConflicts.Cast<object>().Select(e => Prop<object>(e, "identifier").ToString()).OrderBy(s => s, StringComparer.Ordinal).ToArray()));
            return string.Join(" | ", parts.ToArray());
        });
        Call(manager, "DeregisterToolContext", tool);
    }

    static readonly List<string> s_Ran = new List<string>();
#else
    static void Run() { }
#endif
}
