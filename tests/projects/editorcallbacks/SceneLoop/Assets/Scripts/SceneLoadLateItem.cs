using UnityEngine;

public sealed class SceneLoadLateItem : MonoBehaviour
{
    void Start() => SceneLoadLoop.Event("Start", gameObject);
    void LateUpdate() => SceneLoadLoop.Event("LateUpdate", gameObject);
}
