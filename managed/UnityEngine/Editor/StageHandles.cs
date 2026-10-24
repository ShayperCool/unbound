using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEditor.SceneManagement
{
    /// <summary>A stage of the Editor. Play mode has only the main stage: every loaded scene is in
    /// it (packages ask before collecting objects, as NavMeshSurface does).</summary>
    public partial struct StageHandle : IEquatable<StageHandle>
    {
        bool m_IsMainStage;
        Scene m_CustomScene;

        internal bool isMainStage => m_IsMainStage;

        internal Scene customScene => m_CustomScene;

        internal static StageHandle Main => new StageHandle { m_IsMainStage = true };

        public bool IsValid() => m_IsMainStage || m_CustomScene.IsValid();

        public bool Contains(GameObject gameObject)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            if (m_IsMainStage) return gameObject.scene.IsValid();
            return gameObject.scene == m_CustomScene;
        }

        public T FindComponentOfType<T>() where T : Component
        {
            foreach (var c in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
                if (Contains(c.gameObject)) return c;
            return null;
        }

        public T[] FindComponentsOfType<T>() where T : Component
        {
            var all = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            var self = this;
            return Array.FindAll(all, c => self.Contains(c.gameObject));
        }

        public static bool operator ==(StageHandle s1, StageHandle s2) => s1.Equals(s2);

        public static bool operator !=(StageHandle s1, StageHandle s2) => !s1.Equals(s2);

        public override bool Equals(object other) => other is StageHandle h && Equals(h);

        public bool Equals(StageHandle other) => m_IsMainStage == other.m_IsMainStage && m_CustomScene == other.m_CustomScene;

        public override int GetHashCode() => m_IsMainStage ? 1 : m_CustomScene.GetHashCode();
    }

    public static partial class StageUtility
    {
        public static StageHandle GetMainStageHandle() => StageHandle.Main;

        public static StageHandle GetCurrentStageHandle() => StageHandle.Main;

        public static StageHandle GetStageHandle(GameObject gameObject) =>
            gameObject != null && gameObject.scene.IsValid() ? StageHandle.Main : default;

        public static StageHandle GetStageHandle(Scene scene) => scene.IsValid() ? StageHandle.Main : default;
    }
}
