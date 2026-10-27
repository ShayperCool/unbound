using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/vehicles -executeMethod ProbeRunner.Record`
/// with <c>VEHICLES_PROBE_OUT</c> set: a new empty scene in Play mode, where
/// <see cref="VehicleProbe"/> records the vehicles and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // A wheel with values other than the defaults, serialized in the scene.
        var car = new UnityEngine.GameObject("SavedCar");
        car.AddComponent<UnityEngine.Rigidbody>().isKinematic = true;
        var wheel = new UnityEngine.GameObject("SavedWheel");
        wheel.transform.SetParent(car.transform, false);
        var w = wheel.AddComponent<UnityEngine.WheelCollider>();
        w.center = new UnityEngine.Vector3(0.1f, 0.2f, 0.3f);
        w.radius = 0.42f;
        w.suspensionDistance = 0.17f;
        w.suspensionSpring = new UnityEngine.JointSpring { spring = 12345f, damper = 678f, targetPosition = 0.3f };
        w.suspensionExpansionLimited = true;
        w.forceAppPointDistance = 0.11f;
        w.mass = 33f;
        w.wheelDampingRate = 0.7f;
        w.forwardFriction = new UnityEngine.WheelFrictionCurve { extremumSlip = 0.5f, extremumValue = 1.5f, asymptoteSlip = 0.9f, asymptoteValue = 0.6f, stiffness = 1.2f };
        w.sidewaysFriction = new UnityEngine.WheelFrictionCurve { extremumSlip = 0.25f, extremumValue = 1.1f, asymptoteSlip = 0.55f, asymptoteValue = 0.7f, stiffness = 0.9f };
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
