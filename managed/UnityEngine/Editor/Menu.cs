using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;

namespace UnityEditor
{
    public sealed partial class Menu
    {
        sealed class Item
        {
            internal Action Execute;
            internal Func<bool> Validate;
            internal bool Checked;
            internal bool Supported = true;
            internal bool Builtin;
        }
        static Dictionary<string, Item> s_Items = new Dictionary<string, Item>(StringComparer.Ordinal);
        static object s_Assemblies;
        static int s_Generation = -1;
        static int s_AssemblyCount;
        internal static event Action menuChanged;

        public static void SetChecked(string menuPath, bool isChecked)
        {
            MainThreadCheck.Check("SetChecked");
            EnsureItems();
            if (menuPath != null && s_Items.TryGetValue(menuPath, out var item)) item.Checked = isChecked;
        }
        public static bool GetChecked(string menuPath)
        {
            MainThreadCheck.Check("GetChecked");
            EnsureItems();
            return menuPath != null && s_Items.TryGetValue(menuPath, out var item) && item.Checked;
        }
        public static bool GetEnabled(string menuPath)
        {
            MainThreadCheck.Check("GetEnabled");
            EnsureItems();
            if (menuPath == null || !s_Items.TryGetValue(menuPath, out var item)) return false;
            return Enabled(item);
        }
        static bool Enabled(Item item)
        {
            if (item.Builtin)
            {
                if (Compat.EditorPlayMenusMeasured && EditorSession.IsPlaying) return true;
                Stubs.NotImplemented("Built-in menu availability outside the measured Play-mode profile");
                return false;
            }
            if (!item.Supported)
            {
                Stubs.NotImplemented("Menu commands with this method signature");
                return false;
            }
            try { return item.Validate?.Invoke() ?? true; }
            catch (TargetInvocationException error) { UnityEngine.Debug.LogException(error.InnerException ?? error); return false; }
            catch (Exception error) { UnityEngine.Debug.LogException(error); return false; }
        }
        internal static bool TryExecute(string path, out bool result)
        {
            EnsureItems();
            result = false;
            if (path == null || !s_Items.TryGetValue(path, out var item) || item.Execute == null) return false;
            if (!Enabled(item)) return true;
            item.Execute();
            result = true;
            return true;
        }
        internal static void AddMenuItem(string name, string shortcut, bool @checked, int priority, Action execute, Func<bool> validate)
        {
            MainThreadCheck.Check("AddMenuItem");
            EnsureItems();
            if (string.IsNullOrEmpty(name)) return;
            if (s_Items.TryGetValue(name, out var existing)) { existing.Checked = @checked; return; }
            AddParents(name);
            s_Items[name] = new Item { Execute = execute, Validate = validate, Checked = @checked };
        }
        internal static void RemoveMenuItem(string name)
        {
            MainThreadCheck.Check("RemoveMenuItem");
            EnsureItems();
            if (name == null) return;
            var removed = new List<string>();
            foreach (var path in s_Items.Keys)
                if (path == name || path.StartsWith(name + "/", StringComparison.Ordinal)) removed.Add(path);
            foreach (var path in removed) s_Items.Remove(path);
        }
        static void AddParents(string path, Dictionary<string, Item> items = null)
        {
            items ??= s_Items;
            int slash = path.IndexOf('/');
            while (slash >= 0)
            {
                string parent = path.Substring(0, slash);
                if (!items.ContainsKey(parent)) items[parent] = new Item();
                slash = path.IndexOf('/', slash + 1);
            }
        }
        static void EnsureItems()
        {
            var assemblies = GameDomain.Assemblies();
            if (ReferenceEquals(assemblies, s_Assemblies) && s_Generation == GameDomain.Generation && s_AssemblyCount == assemblies.Count) return;
            var items = new Dictionary<string, Item>(StringComparer.Ordinal);
            AddParents("Edit/Play", items);
            items.Add("Edit/Play", new Item { Builtin = true });
            items.Add("Edit/Pause", new Item { Builtin = true });
            items.Add("Edit/Step", new Item { Builtin = true });
            var validators = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
            var commands = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
            foreach (var method in TypeCache.GetMethodsWithAttribute<MenuItem>())
                foreach (MenuItem annotation in method.GetCustomAttributes(typeof(MenuItem), true))
                {
                    if (!method.IsStatic || method.ContainsGenericParameters || annotation.menuItem == null) continue;
                    var destination = annotation.validate ? validators : commands;
                    if (!destination.ContainsKey(annotation.menuItem)) destination.Add(annotation.menuItem, method);
                }
            foreach (var command in commands)
            {
                string path = NormalizeItemPath(command.Key);
                AddParents(path, items);
                var item = new Item();
                var method = command.Value;
                item.Supported = TakesCommand(method, out bool withCommand);
                if (item.Supported)
                {
                    try
                    {
                        if (withCommand)
                        {
                            if (method.ReturnType == typeof(void))
                            {
                                var run = method.CreateDelegate<Action<MenuCommand>>();
                                item.Execute = () => run(PathCommand());
                            }
                            else item.Execute = () => method.Invoke(null, new object[] { PathCommand() });
                        }
                        else item.Execute = method.ReturnType == typeof(void) ? method.CreateDelegate<Action>() : () => method.Invoke(null, null);
                    }
                    catch (ArgumentException error) { item.Supported = false; UnityEngine.Debug.LogWarning(error.Message); }
                }
                if (validators.TryGetValue(command.Key, out var validator)
                    && TakesCommand(validator, out bool validatorWithCommand) && validator.ReturnType == typeof(bool))
                {
                    try
                    {
                        if (validatorWithCommand)
                        {
                            var validate = validator.CreateDelegate<Func<MenuCommand, bool>>();
                            item.Validate = () => validate(PathCommand());
                        }
                        else item.Validate = validator.CreateDelegate<Func<bool>>();
                    }
                    catch (ArgumentException error) { item.Supported = false; UnityEngine.Debug.LogWarning(error.Message); }
                }
                items[path] = item;
            }
            s_Items = items;
            s_Assemblies = assemblies;
            s_Generation = GameDomain.Generation;
            s_AssemblyCount = assemblies.Count;
        }
        /// <summary>Whether a menu method has a signature the registry runs: no parameters, or one
        /// MenuCommand (<paramref name="withCommand"/>).</summary>
        static bool TakesCommand(MethodInfo method, out bool withCommand)
        {
            var parameters = method.GetParameters();
            withCommand = parameters.Length == 1 && parameters[0].ParameterType == typeof(MenuCommand);
            return parameters.Length == 0 || withCommand;
        }

        /// <summary>The command a method run by its menu path receives: no context, user data 0
        /// (tests/projects/menucommand), a new one each call.</summary>
        static MenuCommand PathCommand() => new MenuCommand(null, 0);

        internal static string NormalizeItemPath(string name)
        {
            int space = name.LastIndexOf(' ');
            if (space >= 0 && space + 1 < name.Length && "_%#&".IndexOf(name[space + 1]) >= 0)
                return name.Substring(0, space);
            return name;
        }
    }
}
