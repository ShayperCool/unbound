using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// An editor singleton: the first <see cref="instance"/> access creates it (hidden, not saved).
    /// Unity also loads it from its <c>FilePathAttribute</c> file; play mode under Unbound starts
    /// from a fresh instance and does not save it (that would write editor state into the
    /// project).
    /// </summary>
    public partial class ScriptableSingleton<T> : ScriptableObject where T : ScriptableObject
    {
        static T s_Instance;

        public static T instance
        {
            get
            {
                if (s_Instance == null)
                {
                    var created = CreateInstance<T>();
                    created.hideFlags = HideFlags.HideAndDontSave;
                    s_Instance ??= created;
                }
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

        protected virtual void Save(bool saveAsText) { }

        protected static string GetFilePath() => null;
    }
}
