using System;
using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    public sealed partial class GameObjectUtility
    {
        /// <summary>"Gets the StaticEditorFlags of the GameObject specified" (Scripting API 6000.0):
        /// the flags its scene or prefab file gives it (m_StaticEditorFlags).</summary>
        public static StaticEditorFlags GetStaticEditorFlags(GameObject go)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            return (StaticEditorFlags)go.m_StaticEditorFlags;
        }

        /// <summary>"Unique name for a new GameObject" among the children of
        /// <paramref name="parent"/> (null: the active scene's roots), Scripting API 6000.0: the
        /// name itself when no sibling has it, else the name with the lowest free incremental suffix
        /// of the project's naming scheme ("Prefab (1)", "Prefab (2)"; EditorSettings). A name that
        /// already ends in such a suffix is numbered from its base (unmeasured, as are case
        /// differences: names compare ordinally).</summary>
        public static string GetUniqueNameForSibling(Transform parent, string name) =>
            UniqueName(SiblingNames(parent, parent == null ? UnityEngine.SceneManagement.SceneManager.GetActiveScene() : default, null), name ?? "");

        /// <summary>Renames the GameObject, when a sibling has its name, as
        /// <see cref="GetUniqueNameForSibling"/> names a new one.</summary>
        public static void EnsureUniqueNameForSibling(GameObject self)
        {
            if (self == null) throw new ArgumentNullException(nameof(self));
            var unique = UniqueName(SiblingNames(self.transform.parent, self.scene, self), self.name);
            if (unique != self.name) self.name = unique;
        }

        static HashSet<string> SiblingNames(Transform parent, UnityEngine.SceneManagement.Scene scene, GameObject except)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i).gameObject;
                    if (child != except) names.Add(child.name);
                }
            }
            else if (scene.IsValid())
            {
                foreach (var root in scene.GetRootGameObjects())
                    if (root != except) names.Add(root.name);
            }
            return names;
        }

        static string UniqueName(HashSet<string> taken, string name)
        {
            if (!taken.Contains(name)) return name;
            var scheme = EditorSettings.gameObjectNamingScheme;
            int digits = Math.Max(1, EditorSettings.gameObjectNamingDigits);
            var stem = StemOf(name, scheme);
            for (int n = 1; ; n++)
            {
                var candidate = Numbered(stem, n, scheme, digits);
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        static string Numbered(string stem, int n, EditorSettings.NamingScheme scheme, int digits)
        {
            var number = n.ToString("D" + digits, CultureInfo.InvariantCulture);
            return scheme switch
            {
                EditorSettings.NamingScheme.Dot => stem + "." + number,
                EditorSettings.NamingScheme.Underscore => stem + "_" + number,
                _ => stem + " (" + number + ")",
            };
        }

        /// <summary>The name without a trailing number of the scheme.</summary>
        static string StemOf(string name, EditorSettings.NamingScheme scheme)
        {
            int end = name.Length;
            if (scheme == EditorSettings.NamingScheme.SpaceParenthesis)
            {
                if (end < 4 || name[end - 1] != ')') return name;
                end--;
            }
            int digitsStart = end;
            while (digitsStart > 0 && char.IsAsciiDigit(name[digitsStart - 1])) digitsStart--;
            if (digitsStart == end) return name;
            string separator = scheme switch
            {
                EditorSettings.NamingScheme.Dot => ".",
                EditorSettings.NamingScheme.Underscore => "_",
                _ => " (",
            };
            return digitsStart >= separator.Length && string.CompareOrdinal(name, digitsStart - separator.Length, separator, 0, separator.Length) == 0
                ? name.Substring(0, digitsStart - separator.Length)
                : name;
        }
    }

    public sealed partial class EditorSettings
    {
        public enum NamingScheme
        {
            SpaceParenthesis = 0,
            Dot = 1,
            Underscore = 2,
        }

        /// <summary>The project's naming scheme for duplicated GameObjects (EditorSettings.asset
        /// m_GameObjectNamingScheme; SpaceParenthesis without one), saved there as the Editor saves
        /// settings.</summary>
        public static NamingScheme gameObjectNamingScheme
        {
            get => int.TryParse(SettingsAssets.Value("EditorSettings", "m_GameObjectNamingScheme"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? (NamingScheme)value : NamingScheme.SpaceParenthesis;
            set => SettingsAssets.Edit("EditorSettings", body => body.Set("m_GameObjectNamingScheme", YText.Scalar(((int)value).ToString(CultureInfo.InvariantCulture))));
        }

        /// <summary>The digits of the number (EditorSettings.asset m_GameObjectNamingDigits; 1
        /// without one).</summary>
        public static int gameObjectNamingDigits
        {
            get => int.TryParse(SettingsAssets.Value("EditorSettings", "m_GameObjectNamingDigits"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value : 1;
            set => SettingsAssets.Edit("EditorSettings", body => body.Set("m_GameObjectNamingDigits", YText.Scalar(value.ToString(CultureInfo.InvariantCulture))));
        }
    }
}
