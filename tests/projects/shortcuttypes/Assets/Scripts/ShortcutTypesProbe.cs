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
    // Methods carrying each attribute constructor; the probe reads the attributes back, it never
    // runs them as shortcuts (their ids are the probe's own).
    [Shortcut("Probe/Plain")] static void Plain() { }
    [Shortcut("Probe/Context", typeof(UnityEditor.SceneView))] static void InContext() { }
    [Shortcut("Probe/Global", null, KeyCode.G, ShortcutModifiers.Shift)] static void GlobalKey() { }
    [Shortcut("Probe/Keyed", typeof(UnityEditor.SceneView), KeyCode.K, ShortcutModifiers.Alt | ShortcutModifiers.Shift)] static void Keyed() { }
    [Shortcut("Probe/Tagged", typeof(UnityEditor.SceneView), "probe-tag", KeyCode.T)] static void Tagged() { }
    [Shortcut("Probe/Prioritized", typeof(UnityEditor.SceneView), "probe-tag", KeyCode.P, ShortcutModifiers.Action, 7)] static void Prioritized() { }
    [Shortcut("Probe/KeyOnly", KeyCode.F5)] static void KeyOnly() { }
    [Shortcut("Probe/Named", KeyCode.N, ShortcutModifiers.Action | ShortcutModifiers.Alt, displayName = "Probe Display Name")] static void Named() { }
    [Shortcut("Probe/Twice/A", KeyCode.A), Shortcut("Probe/Twice/B", KeyCode.B)] static void Twice() { }
    [Shortcut("Probe/Arguments", KeyCode.Q)] static void WithArguments(ShortcutArguments arguments) { }
    [ClutchShortcut("Probe/Clutch")] static void Clutch(ShortcutArguments arguments) { }
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
    }
#else
    static void Run() { }
#endif
}
