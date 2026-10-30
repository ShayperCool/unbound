using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ProbeRunner
{
    public static void Create()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("Probe").AddComponent<CanvasLifecycleProbe>();
        foreach(var name in new[]{"SerializedWorld","SerializedOverlay","SerializedInactive"}) {
            var go=new GameObject(name,typeof(RectTransform));go.SetActive(false);
            var t=(RectTransform)go.transform;t.localPosition=new Vector3(17,31,6);t.sizeDelta=new Vector2(150,80);t.pivot=new Vector2(.2f,.7f);
            var c=go.AddComponent<Canvas>();c.renderMode=name=="SerializedOverlay"?RenderMode.ScreenSpaceOverlay:RenderMode.WorldSpace;
            if(name!="SerializedInactive")go.SetActive(true);
        }
        var prefab=new GameObject("Lifecycle",typeof(RectTransform));prefab.SetActive(false);
        var rect=(RectTransform)prefab.transform;rect.localPosition=new Vector3(19,23,3);rect.sizeDelta=new Vector2(110,70);rect.anchorMin=new Vector2(.1f,.2f);rect.anchorMax=new Vector2(.8f,.9f);
        prefab.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;prefab.SetActive(true);
        PrefabUtility.SaveAsPrefabAsset(prefab,"Assets/Resources/Lifecycle.prefab");Object.DestroyImmediate(prefab);
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true)};
    }
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        PlayModeWindow.SetCustomRenderingResolution(640,480,"Canvas lifecycle");
        EditorApplication.EnterPlaymode();
    }
}
