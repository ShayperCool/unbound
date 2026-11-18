using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProbeRunner
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Probe").AddComponent<TransparentCullProbe>();
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
    }

    // Records windowed: the overlay pass captures the Game view, sized 256x256 here for the run.
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
        var assembly = typeof(Editor).Assembly;
        var gameViewType = assembly.GetType("UnityEditor.GameView");
        var gameView = EditorWindow.GetWindow(gameViewType);
        var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        var sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance", flags).GetValue(null);
        var groupType = gameViewType.GetProperty("currentSizeGroupType", flags).GetValue(gameView);
        var group = sizesType.GetMethod("GetGroup", flags).Invoke(sizes, new[] { groupType });
        var sizeType = assembly.GetType("UnityEditor.GameViewSize");
        var kindType = assembly.GetType("UnityEditor.GameViewSizeType");
        var size = System.Activator.CreateInstance(sizeType, flags, null, new object[] { System.Enum.ToObject(kindType, 1), 256, 256, "Transparent cull oracle" }, null);
        int index = (int)group.GetType().GetMethod("GetTotalCount", flags).Invoke(group, null);
        group.GetType().GetMethod("AddCustomSize", flags).Invoke(group, new[] { size });
        var selected = gameViewType.GetProperty("selectedSizeIndex", flags);
        int old = (int)selected.GetValue(gameView);
        selected.SetValue(gameView, index);
        EditorApplication.quitting += () =>
        {
            selected.SetValue(gameView, old);
            group.GetType().GetMethod("RemoveCustomSize", flags).Invoke(group, new object[] { index });
        };
        EditorApplication.EnterPlaymode();
    }
}
