using UnityEngine;

public sealed class SceneLoadItem : MonoBehaviour
{
    void Awake() => SceneLoadLoop.Event("Awake", gameObject);
    void OnEnable() => SceneLoadLoop.Event("OnEnable", gameObject);
    void Start() => SceneLoadLoop.Event("Start", gameObject);
    void OnRenderObject() => SceneLoadLoop.Render(gameObject);
}
