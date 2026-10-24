using System;
using System.IO;
using UnityEditorInternal;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// An editor singleton: the first <see cref="instance"/> access loads it from its
    /// <see cref="FilePathAttribute"/> file (when it has one and the file exists), else creates it.
    /// <see cref="Save"/> writes it in Unity's text format; files in the project or the Editor's
    /// preferences go to Unbound's own copies (<c>Library/Unbound/EditorFiles</c>, the global cache),
    /// which later loads read first.
    /// </summary>
    public partial class ScriptableSingleton<T> : ScriptableObject where T : ScriptableObject
    {
        static T s_Instance;

        public static T instance
        {
            get
            {
                if (s_Instance == null) CreateAndLoad();
                return s_Instance;
            }
        }

        protected ScriptableSingleton()
        {
            if (s_Instance != null)
                Debug.LogError("ScriptableSingleton already exists. Did you query the singleton in a constructor?");
            else
                s_Instance = this as T;
        }

        static void CreateAndLoad()
        {
            var path = GetFilePath();
            if (!string.IsNullOrEmpty(path)) InternalEditorUtility.LoadSerializedFileAndForget(path);
            if (s_Instance == null)
            {
                var created = CreateInstance<T>();
                created.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
                s_Instance ??= created;
            }
        }

        protected virtual void Save(bool saveAsText)
        {
            if (s_Instance == null)
            {
                Debug.LogError("Cannot save ScriptableSingleton: no instance!");
                return;
            }
            var path = GetFilePath();
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning($"Saving has no effect. Your class '{GetType()}' is missing the FilePathAttribute. Use this attribute to specify where to save your ScriptableSingleton.\nOnly call Save() and use this attribute if you want your state to survive between sessions of Unity.");
                return;
            }
            InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[] { s_Instance }, path, saveAsText);
        }

        protected static string GetFilePath()
        {
            foreach (var attribute in typeof(T).GetCustomAttributes(true))
                if (attribute is FilePathAttribute filePath) return filePath.filepath;
            return string.Empty;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public partial class FilePathAttribute : Attribute
    {
        public enum Location
        {
            PreferencesFolder,
            ProjectFolder,
        }

        string m_FilePath;
        string m_RelativePath;
        Location m_Location;

        public FilePathAttribute(string relativePath, Location location)
        {
            if (string.IsNullOrEmpty(relativePath)) throw new ArgumentException("Invalid relative path (it is empty)");
            m_RelativePath = relativePath;
            m_Location = location;
        }

        internal string filepath
        {
            get
            {
                if (m_FilePath == null && m_RelativePath != null)
                {
                    var relative = m_RelativePath[0] == '/' ? m_RelativePath.Substring(1) : m_RelativePath;
                    m_FilePath = m_Location == Location.PreferencesFolder
                        ? InternalEditorUtility.unityPreferencesFolder + "/" + relative
                        : relative;
                    m_RelativePath = null;
                }
                return m_FilePath;
            }
        }
    }
}

namespace UnityEditorInternal
{
    public partial class InternalEditorUtility
    {
        /// <summary>The Editor's preferences folder on Linux (layouts, game view sizes, overlays).</summary>
        public static string unityPreferencesFolder
        {
            get
            {
                var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(config)) config = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".config");
                return Path.Combine(config, "unity3d", "Preferences", "Editor-5.x");
            }
        }
    }
}
