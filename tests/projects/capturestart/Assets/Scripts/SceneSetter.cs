using UnityEngine;

// An object of the scene: its Awake, OnEnable and Start as points to set the capture rate at.
public class SceneSetter : MonoBehaviour
{
    void Awake() => CaptureStartProbe.At("sceneAwake");
    void OnEnable() => CaptureStartProbe.At("sceneEnable");
    void Start() => CaptureStartProbe.At("sceneStart");
}
