using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// PlayerPrefs, stored where and as the Unity Editor of this OS stores them — the game sees the
    /// same values in Unbound and in Unity: on Linux `~/.config/unity3d/&lt;company&gt;/&lt;product&gt;/prefs`,
    /// an XML file with ints, floats and base64 strings; on macOS the preferences domain
    /// `unity.&lt;company&gt;.&lt;product&gt;` (<see cref="MacPreferences"/>).
    /// </summary>
    public partial class PlayerPrefs
    {
        static Dictionary<string, object> s_Values;
        static bool s_Dirty;

        /// <summary>On macOS the Editor keeps them in a preferences domain; a file the run was
        /// given is Unity's XML file on every OS.</summary>
        static bool InMacDomain => OperatingSystem.IsMacOS() && string.IsNullOrEmpty(ProjectInfo.PlayerPrefsPath);

        static string FilePath => !string.IsNullOrEmpty(ProjectInfo.PlayerPrefsPath)
            ? ProjectInfo.PlayerPrefsPath
            : InMacDomain
                ? $"unity.{ProjectInfo.CompanyName}.{ProjectInfo.ProductName}"
                : Path.Combine(ProjectInfo.PersistentDataPath, "prefs");

        static Dictionary<string, object> Values
        {
            get
            {
                if (s_Values != null) return s_Values;
                s_Values = new Dictionary<string, object>();
                try
                {
                    if (InMacDomain || File.Exists(FilePath)) Load();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: could not read PlayerPrefs from {FilePath}: {e.Message}");
                }
                return s_Values;
            }
        }

        static void Load()
        {
            var values = InMacDomain ? MacPreferences.Read(FilePath) : UnityPrefsXml.Read(FilePath);
            foreach (var pair in values) s_Values[pair.Key] = pair.Value;
        }

        public static void Save()
        {
            if (!s_Dirty || s_Values == null) return;
            try
            {
                if (InMacDomain) MacPreferences.Write(FilePath, s_Values);
                else UnityPrefsXml.Write(FilePath, s_Values);
                s_Dirty = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: could not save PlayerPrefs to {FilePath}: {e.Message}");
            }
        }

        static void Set(string key, object value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            Values[key] = value;
            s_Dirty = true;
        }

        public static void SetInt(string key, int value) => Set(key, value);
        public static void SetFloat(string key, float value) => Set(key, value);
        public static void SetString(string key, string value) => Set(key, value ?? "");

        public static int GetInt(string key, int defaultValue) => Values.TryGetValue(key, out var v) && v is int i ? i : defaultValue;
        public static int GetInt(string key) => GetInt(key, 0);
        public static float GetFloat(string key, float defaultValue) => Values.TryGetValue(key, out var v) && v is float f ? f : defaultValue;
        public static float GetFloat(string key) => GetFloat(key, 0f);
        public static string GetString(string key, string defaultValue) => Values.TryGetValue(key, out var v) && v is string s ? s : defaultValue;
        public static string GetString(string key) => GetString(key, "");

        public static bool HasKey(string key) => Values.ContainsKey(key);

        public static void DeleteKey(string key)
        {
            if (Values.Remove(key)) s_Dirty = true;
        }

        public static void DeleteAll()
        {
            Values.Clear();
            s_Dirty = true;
        }

        internal static void SaveOnQuit() => Save();
    }

    /// <summary>Unity's prefs file format on Linux: `&lt;unity_prefs&gt;` with int, float and base64
    /// string entries (PlayerPrefs and EditorPrefs use it alike).</summary>
    internal static class UnityPrefsXml
    {
        internal static Dictionary<string, object> Read(string path)
        {
            var values = new Dictionary<string, object>();
            if (!File.Exists(path)) return values;
            var doc = new XmlDocument();
            doc.Load(path);
            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.Name != "pref") continue;
                var name = node.Attributes?["name"]?.Value;
                var type = node.Attributes?["type"]?.Value;
                if (name == null) continue;
                try
                {
                    switch (type)
                    {
                        case "int":
                            values[name] = int.Parse(node.InnerText, CultureInfo.InvariantCulture);
                            break;
                        case "float":
                            values[name] = float.Parse(node.InnerText, CultureInfo.InvariantCulture);
                            break;
                        default:
                            values[name] = Encoding.UTF8.GetString(Convert.FromBase64String(node.InnerText));
                            break;
                    }
                }
                catch (FormatException)
                {
                }
            }
            return values;
        }

        internal static void Write(string path, Dictionary<string, object> values)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            sb.Append("<unity_prefs version_major=\"1\" version_minor=\"1\">\n");
            foreach (var pair in values)
            {
                var name = System.Security.SecurityElement.Escape(pair.Key);
                switch (pair.Value)
                {
                    case int i:
                        sb.Append($"\t<pref name=\"{name}\" type=\"int\">{i.ToString(CultureInfo.InvariantCulture)}</pref>\n");
                        break;
                    case float f:
                        sb.Append($"\t<pref name=\"{name}\" type=\"float\">{f.ToString("R", CultureInfo.InvariantCulture)}</pref>\n");
                        break;
                    case string str:
                        sb.Append($"\t<pref name=\"{name}\" type=\"string\">{Convert.ToBase64String(Encoding.UTF8.GetBytes(str))}</pref>\n");
                        break;
                }
            }
            sb.Append("</unity_prefs>\n");
            var temp = path + ".tmp";
            File.WriteAllText(temp, sb.ToString());
            File.Move(temp, path, overwrite: true);
        }
    }

    public sealed partial class PlayerPrefsException : Exception
    {
        public PlayerPrefsException(string error) : base(error) { }
    }
}
