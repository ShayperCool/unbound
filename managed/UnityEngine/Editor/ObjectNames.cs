using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>The Editor's naming helpers.</summary>
    public sealed partial class ObjectNames
    {
        // A component type's title is the last part of its own [AddComponentMenu] path (none for
        // an empty path or one that ends with a slash), kept per type while the game's code stays.
        static readonly Dictionary<Type, string> s_MenuTitles = new Dictionary<Type, string>();
        static int s_MenuTitlesGeneration = -1;

        static bool TryGetMenuTitle(Type type, out string title)
        {
            if (s_MenuTitlesGeneration != Unbound.Runtime.GameDomain.Generation)
            {
                s_MenuTitles.Clear();
                s_MenuTitlesGeneration = Unbound.Runtime.GameDomain.Generation;
            }
            if (!s_MenuTitles.TryGetValue(type, out title))
            {
                string path = (Attribute.GetCustomAttribute(type, typeof(AddComponentMenu), false) as AddComponentMenu)?.componentMenu?.Trim();
                if (!string.IsNullOrEmpty(path))
                {
                    int slash = path.LastIndexOf('/');
                    title = slash < 0 ? path : slash < path.Length - 1 ? path.Substring(slash + 1) : null;
                }
                s_MenuTitles[type] = title;
            }
            return title != null;
        }

        internal static bool IsScriptObject(Object obj) => obj is MonoBehaviour || obj is ScriptableObject;

        /// <summary>The engine class of an object: "MonoBehaviour" for every script object.</summary>
        public static string GetClassName(Object obj)
        {
            if ((object)obj == null) throw new ArgumentNullException(nameof(obj));
            obj.ThrowIfDestroyed();
            return IsScriptObject(obj) ? "MonoBehaviour" : obj.GetType().Name;
        }

        static string ObjectTypeName(Object o, bool multiObjectEditing)
        {
            if (o is GameObject) return o.name;
            if (o is Component)
            {
                if (o is MonoBehaviour) return o.GetType().Name + " (Script)";
                if (o is MeshFilter filter)
                {
                    if (multiObjectEditing) return "MeshFilter";
                    var mesh = filter.sharedMesh;
                    return (mesh ? mesh.name : "[none]") + " (MeshFilter)";
                }
                return o.GetType().Name;
            }
            if (o is AssetImporter)
            {
                // An importer of a script is titled after the script; not done.
                if (o is MonoImporter) Unbound.Runtime.Stubs.NotImplemented("UnityEditor.ObjectNames.GetInspectorTitle (MonoImporter)");
                // A scripted importer is a script's class.
                return o is UnityEditor.AssetImporters.ScriptedImporter ? o.GetType().Name + " (Script)" : o.GetType().Name;
            }
            return o.name + " (" + o.GetType().Name + ")";
        }

        /// <summary>An object's title in the Inspector (tests/projects/editorpreview names.*): a
        /// GameObject's name, a component's menu name or its type in words (a script's with
        /// " (Script)"), another object's name and type.</summary>
        public static string GetInspectorTitle(Object obj, bool multiObjectEditing)
        {
            if (obj == null && (object)obj != null && IsScriptObject(obj)) return " (Script)";
            if (obj == null) return "Nothing Selected";
            if (!TryGetMenuTitle(obj.GetType(), out string title)) title = NicifyVariableName(ObjectTypeName(obj, multiObjectEditing));
            if (Attribute.IsDefined(obj.GetType(), typeof(ObsoleteAttribute))) title += " (Deprecated)";
            return title;
        }

        public static string GetInspectorTitle(Object obj) => GetInspectorTitle(obj, false);

        /// <summary>The kind of object for titles of several: its type, or what its file is.</summary>
        internal static string GetTypeName(Object obj)
        {
            if (obj == null) return "Object";
            string path = AssetDatabase.GetAssetPath(obj);
            string lower = path.ToLower();
            if (lower.EndsWith(".unity")) return "Scene";
            if (lower.EndsWith(".guiskin")) return "GUI Skin";
            if (System.IO.Directory.Exists(path)) return "Folder";
            if (obj.GetType() == typeof(Object)) return System.IO.Path.GetExtension(lower) + " File";
            return obj.GetType().Name;
        }

        [Obsolete("Please use NicifyVariableName instead")]
        public static string MangleVariableName(string name) => NicifyVariableName(name);

        [Obsolete("Please use GetInspectorTitle instead")]
        public static string GetPropertyEditorTitle(Object obj) => GetInspectorTitle(obj);

        /// <summary>
        /// The Inspector's label for a field name: an `m_` prefix, a leading `_`, a `k` constant
        /// prefix (before an upper-case letter) and compiler auto-property wrappers are stripped,
        /// the first letter is capitalised, and words are separated at lowercase-to-uppercase/digit
        /// transitions and acronym/digit-to-word transitions ("m_myHTMLParser2" → "My HTML Parser 2",
        /// "u32" → "U32", "u32Value" → "U32 Value").
        /// </summary>
        public static string NicifyVariableName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            int start = 0;
            if (name.Length >= 2 && name[0] == 'm' && name[1] == '_') start = 2;
            else if (name[0] == '_') start = 1;
            if (start + 1 < name.Length && name[start] == 'k' && IsAsciiUpper(name[start + 1])) start++;
            if (start >= name.Length) return "";

            var sb = new StringBuilder(name.Length + 4);
            bool previousWasCapital = false;
            bool previousWasLowercase = false;
            bool previousWasNumber = false;
            bool capitalizeNext = false;
            bool insideAutoProperty = false;
            int i = start;

            if (IsAsciiLower(name[i]))
            {
                sb.Append((char)(name[i] - 32));
                previousWasCapital = true;
                i++;
            }

            while (i < name.Length)
            {
                char c = name[i];
                bool isUpper = IsAsciiUpper(c);
                bool isDigit = IsAsciiDigit(c);
                if (isUpper || isDigit)
                {
                    if (previousWasLowercase)
                    {
                        bool isDimension = isDigit && i - start >= 2 && name[i - 1] == 'x' && IsAsciiDigit(name[i - 2]);
                        if (!isDimension) sb.Append(' ');
                    }
                    else if (previousWasCapital && i + 1 < name.Length && IsAsciiLower(name[i + 1]))
                    {
                        if (!(previousWasNumber && isDigit)) sb.Append(' ');
                    }

                    capitalizeNext = false;
                    sb.Append(c);
                    previousWasCapital = true;
                    previousWasLowercase = false;
                    previousWasNumber = isDigit;
                    i++;
                    continue;
                }

                if (c == '<')
                {
                    capitalizeNext = true;
                    insideAutoProperty = true;
                    i++;
                    continue;
                }

                if (insideAutoProperty && c == '>')
                {
                    const string backingField = "k__BackingField";
                    if (i + 1 + backingField.Length <= name.Length &&
                        string.CompareOrdinal(name, i + 1, backingField, 0, backingField.Length) == 0)
                    {
                        i += 1 + backingField.Length;
                    }
                    else
                    {
                        sb.Append(' ');
                        i++;
                    }
                    continue;
                }

                bool isLower = IsAsciiLower(c);
                if (capitalizeNext)
                {
                    capitalizeNext = false;
                    if (isLower)
                    {
                        sb.Append((char)(c - 32));
                        previousWasCapital = true;
                        previousWasLowercase = false;
                    }
                    else
                    {
                        sb.Append(c);
                        previousWasCapital = false;
                        previousWasLowercase = false;
                    }
                }
                else
                {
                    sb.Append(c);
                    previousWasCapital = false;
                    previousWasLowercase = isLower;
                }
                previousWasNumber = false;
                i++;
            }

            return sb.ToString();
        }

        static bool IsAsciiUpper(char c) => c >= 'A' && c <= 'Z';
        static bool IsAsciiLower(char c) => c >= 'a' && c <= 'z';
        static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    }
}
