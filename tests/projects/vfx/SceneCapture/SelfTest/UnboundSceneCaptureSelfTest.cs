using System;
using UnityEngine;

/// <summary>
/// The fixtures of scene_capture.py's self-test, copied into the captured copy only: the
/// scene's main camera gets a script that keeps a single instance of itself (as camera
/// controllers often do: a second one destroys its object), and a cube crosses the view, so
/// that two shots of a camera that keeps rendering differ.
/// </summary>
public sealed class UnboundSceneCaptureSelfTest : MonoBehaviour
{
    static UnboundSceneCaptureSelfTest s_Instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UNBOUND_SCENE_CAPTURE"))) return;
        var main = Camera.main;
        if (main == null)
        {
            main = new GameObject("self-test camera").AddComponent<Camera>();
            main.gameObject.tag = "MainCamera";
        }
        main.gameObject.AddComponent<UnboundSceneCaptureSelfTest>();
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "UnboundSceneCaptureSelfTest cube";
        cube.transform.position = new Vector3(-3f, 0f, 10f);
        cube.AddComponent<UnboundSceneCaptureSelfTestMover>();
    }

    void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        s_Instance = this;
    }

    void OnDestroy()
    {
        if (s_Instance == this) s_Instance = null;
    }
}

/// <summary>Moves its object to the right, three units a second.</summary>
public sealed class UnboundSceneCaptureSelfTestMover : MonoBehaviour
{
    void Update() => transform.position += Vector3.right * (3f * Time.deltaTime);
}
