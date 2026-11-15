using UnityEngine;

namespace UnityEditor.SceneManagement
{
    /// <summary>An editing stage (the main scenes, prefab mode).</summary>
    public abstract partial class Stage : ScriptableObject
    {
    }

    public abstract partial class PreviewSceneStage : Stage
    {
    }

    /// <summary>Prefab mode. Unbound's Editor never opens it.</summary>
    [global::System.Serializable]
    public sealed partial class PrefabStage : PreviewSceneStage
    {
    }

    [Unbound.EditorFaithful("Prefab mode is not open while playing (as the batch-mode Editor)")]
    public static partial class PrefabStageUtility
    {
        public static PrefabStage GetCurrentPrefabStage() => null;

        public static PrefabStage GetPrefabStage(GameObject gameObject) => null;
    }
}
