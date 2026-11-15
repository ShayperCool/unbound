using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEditor
{
    public sealed partial class Menu
    {
        static readonly HashSet<string> s_Items = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<string, bool> s_Checked = new Dictionary<string, bool>(StringComparer.Ordinal);
        static object s_Assemblies;
        static int s_Generation = -1;
        static int s_AssemblyCount;
        internal static event Action menuChanged;

        public static void SetChecked(string menuPath, bool isChecked)
        {
            MainThreadCheck.Check("SetChecked");
            EnsureItems();
            if (menuPath != null && s_Items.Contains(menuPath)) s_Checked[menuPath] = isChecked;
        }
        public static bool GetChecked(string menuPath)
        {
            MainThreadCheck.Check("GetChecked");
            EnsureItems();
            if (menuPath == null || !s_Items.Contains(menuPath)) return false;
            if (s_Checked.TryGetValue(menuPath, out bool value)) return value;
            return false;
        }
        static void EnsureItems()
        {
            var assemblies = GameDomain.Assemblies();
            if (ReferenceEquals(assemblies, s_Assemblies) && s_Generation == GameDomain.Generation && s_AssemblyCount == assemblies.Count) return;
            s_Assemblies = assemblies;
            s_Generation = GameDomain.Generation;
            s_AssemblyCount = assemblies.Count;
            s_Items.Clear();
            s_Checked.Clear();
            s_Items.Add("Edit/Play"); s_Items.Add("Edit/Pause"); s_Items.Add("Edit/Step");
            foreach (var method in TypeCache.GetMethodsWithAttribute<MenuItem>())
                foreach (MenuItem item in method.GetCustomAttributes(typeof(MenuItem), true))
                    if (!item.validate && method.IsStatic) s_Items.Add(NormalizeItemPath(item.menuItem));
        }
        internal static string NormalizeItemPath(string name)
        {
            int space = name.LastIndexOf(' ');
            if (space >= 0 && space + 1 < name.Length && "_%#&".IndexOf(name[space + 1]) >= 0)
                return name.Substring(0, space);
            return name;
        }
    }
}
