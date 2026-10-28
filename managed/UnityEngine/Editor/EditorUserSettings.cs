using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// The project's per-user config values. Values the Editor saved (`UserSettings/EditorUserSettings.asset`)
    /// are read; what the game sets goes to Unbound's own copy (`Library/Unbound/EditorUserSettings.asset`,
    /// the same format), so the user's file is never written.
    /// </summary>
    public sealed partial class EditorUserSettings : UnityEngine.Object
    {
        static Dictionary<string, string> s_Values;
        static Dictionary<string, string> s_Own;

        static string UnityFile => Path.Combine(ProjectInfo.ProjectPath ?? "", "UserSettings", "EditorUserSettings.asset");
        static string OwnFile => Path.Combine(ProjectInfo.ProjectPath ?? "", "Library", "Unbound", "EditorUserSettings.asset");

        static Dictionary<string, string> Values
        {
            get
            {
                if (s_Values != null) return s_Values;
                s_Values = new Dictionary<string, string>();
                s_Own = new Dictionary<string, string>();
                try
                {
                    foreach (var pair in ConfigValues.Read(UnityFile)) s_Values[pair.Key] = pair.Value;
                    foreach (var pair in ConfigValues.Read(OwnFile)) s_Values[pair.Key] = s_Own[pair.Key] = pair.Value;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: could not read EditorUserSettings: {e.Message}");
                }
                return s_Values;
            }
        }

        /// <summary>The value set for a name; null when none is.</summary>
        public static string GetConfigValue(string name) =>
            name != null && Values.TryGetValue(name, out var v) ? v : null;

        // The native side takes the string as a core::string: null is stored as "".
        public static void SetConfigValue(string name, string value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            value ??= "";
            if (Values.TryGetValue(name, out var old) && old == value) return;
            s_Values[name] = value;
            s_Own[name] = value;
            try
            {
                ConfigValues.Write(OwnFile, s_Own);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: could not save EditorUserSettings: {e.Message}");
            }
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>
    /// `EditorUserSettings.asset`'s `m_ConfigSettings`: each value's UTF-8 bytes XORed with a key
    /// stream and written as lowercase hex. Byte i's key is `(Key[i % 72] + i) & 0xff` (measured on
    /// the 6000.0 Editor: a 300-character value, a UTF-8 one and the values it writes itself).
    /// </summary>
    internal static class ConfigValues
    {
        const string Key = "c02ca0c4eeb7d61afff7830950c8fc69bf4fc59519e18130b978b64495c35b241d6b6c95";

        static byte KeyAt(int i) => (byte)(Key[i % Key.Length] + i);

        internal static string Encode(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(((byte)(bytes[i] ^ KeyAt(i))).ToString("x2"));
            return sb.ToString();
        }

        /// <summary>The value, or null when the text is not hex.</summary>
        internal static string Decode(string hex)
        {
            if (hex.Length % 2 != 0) return null;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)) return null;
                bytes[i] = (byte)(b ^ KeyAt(i));
            }
            return Encoding.UTF8.GetString(bytes);
        }

        internal static Dictionary<string, string> Read(string path)
        {
            var values = new Dictionary<string, string>();
            if (!File.Exists(path)) return values;
            foreach (var doc in UnityYamlText.Parse(File.ReadAllText(path)))
            {
                var settings = doc.Body?.Get("m_ConfigSettings");
                if (settings == null) continue;
                for (int i = 0; i < settings.Keys.Count; i++)
                {
                    var value = settings.Values[i].Get("value");
                    var text = value == null ? null : Decode(value.Text);
                    if (text != null) values[settings.Keys[i]] = text;
                }
            }
            return values;
        }

        internal static void Write(string path, Dictionary<string, string> values)
        {
            var settings = YText.Map();
            var names = new List<string>(values.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var entry = YText.Map();
                entry.Add("value", YText.Scalar(Encode(values[name])));
                entry.Add("flags", YText.Scalar("0"));
                settings.Add(name, entry);
            }
            var body = YText.Map();
            body.Add("m_ObjectHideFlags", YText.Scalar("0"));
            body.Add("serializedVersion", YText.Scalar("4"));
            body.Add("m_ConfigSettings", settings);
            var doc = new YTextDocument { Header = "--- !u!162 &1", ClassId = 162, FileId = 1, ClassName = "EditorUserSettings", Body = body };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, UnityYamlText.Write(new List<YTextDocument> { doc }));
        }
    }
}
