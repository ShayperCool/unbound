using UnityEngine;

// Records OnDisable, OnEnable and OnTransformParentChanged with the scene the object is in at that
// moment and whether that scene's 3D and 2D physics scenes hold anything.
public class SceneEvents : MonoBehaviour
{
    void Add(string kind)
    {
        var scene = gameObject.scene;
        LocalPhysicsProbe.Events.Add(kind + "@" + scene.name + "|empty3d:" + (scene.GetPhysicsScene().IsEmpty() ? "true" : "false")
            + "|empty2d:" + (scene.GetPhysicsScene2D().IsEmpty() ? "true" : "false"));
    }
    void OnDisable() => Add("OnDisable");
    void OnEnable() => Add("OnEnable");
    void OnTransformParentChanged() => Add("OnTransformParentChanged");
}
