using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>Values kept for the editor session (in memory: a play session is the session).</summary>
    public partial class SessionState
    {
        static readonly Dictionary<string, object> s_Values = new Dictionary<string, object>();

        static T Get<T>(string key, T defaultValue) => key != null && s_Values.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
        static void Set(string key, object value) { if (key != null) s_Values[key] = value; }
        static void Erase(string key) { if (key != null) s_Values.Remove(key); }

        public static void SetBool(string key, bool value) => Set(key, value);
        public static bool GetBool(string key, bool defaultValue) => Get(key, defaultValue);
        public static void EraseBool(string key) => Erase(key);
        public static void SetFloat(string key, float value) => Set(key, value);
        public static float GetFloat(string key, float defaultValue) => Get(key, defaultValue);
        public static void EraseFloat(string key) => Erase(key);
        public static void SetInt(string key, int value) => Set(key, value);
        public static int GetInt(string key, int defaultValue) => Get(key, defaultValue);
        public static void EraseInt(string key) => Erase(key);
        public static void SetString(string key, string value) => Set(key, value);
        public static string GetString(string key, string defaultValue) => Get(key, defaultValue);
        public static void EraseString(string key) => Erase(key);
        public static void SetVector3(string key, Vector3 value) => Set(key, value);
        public static Vector3 GetVector3(string key, Vector3 defaultValue) => Get(key, defaultValue);
        public static void EraseVector3(string key) => Erase(key);
        public static void EraseIntArray(string key) => Erase(key);
        public static void SetIntArray(string key, int[] value) => Set(key, value);
        public static int[] GetIntArray(string key, int[] defaultValue) => Get(key, defaultValue);
    }
}
