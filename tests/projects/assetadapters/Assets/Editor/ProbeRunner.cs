using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ProbeRunner
{
    public static void Record()
    {
        AssetDatabase.CreateFolder("Assets", "Resources");
        AssetDatabase.CreateAsset(new PhysicsMaterial("payload3d") { dynamicFriction = .125f, staticFriction = .375f, bounciness = .625f, frictionCombine = PhysicsMaterialCombine.Multiply, bounceCombine = PhysicsMaterialCombine.Maximum }, "Assets/Resources/Payload3D.physicMaterial");
        AssetDatabase.CreateAsset(new PhysicsMaterial2D("payload2d") { friction = .25f, bounciness = .75f, frictionCombine = PhysicsMaterialCombine2D.Minimum, bounceCombine = PhysicsMaterialCombine2D.Average }, "Assets/Resources/Payload2D.physicsMaterial2D");
        AssetDatabase.CreateAsset(new TextAsset("caf\u00e9\nasset \u03bb") { name = "payload-text" }, "Assets/Resources/PayloadText.asset");
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
