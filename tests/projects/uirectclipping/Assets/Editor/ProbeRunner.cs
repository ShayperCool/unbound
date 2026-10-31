using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProbeRunner
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Probe").AddComponent<RectClippingProbe>();
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
    }
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        var flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
        var assembly=typeof(Editor).Assembly;
        var gt=assembly.GetType("UnityEditor.GameView");var gv=EditorWindow.GetWindow(gt);
        var st=assembly.GetType("UnityEditor.GameViewSizes");
        var singleton=typeof(ScriptableSingleton<>).MakeGenericType(st).GetProperty("instance",flags).GetValue(null);
        var groupType=gt.GetProperty("currentSizeGroupType",flags).GetValue(gv);
        var group=st.GetMethod("GetGroup",flags).Invoke(singleton,new[]{groupType});
        var sizeType=assembly.GetType("UnityEditor.GameViewSize");var enumType=assembly.GetType("UnityEditor.GameViewSizeType");
        var size=System.Activator.CreateInstance(sizeType,flags,null,new object[]{System.Enum.ToObject(enumType,1),128,128,"Rectangle clipping oracle"},null);
        int index=(int)group.GetType().GetMethod("GetTotalCount",flags).Invoke(group,null);
        group.GetType().GetMethod("AddCustomSize",flags).Invoke(group,new[]{size});
        var selected=gt.GetProperty("selectedSizeIndex",flags);int old=(int)selected.GetValue(gv);selected.SetValue(gv,index);
        EditorApplication.quitting+=()=>{selected.SetValue(gv,old);group.GetType().GetMethod("RemoveCustomSize",flags).Invoke(group,new object[]{index});};
        EditorApplication.EnterPlaymode();
    }
    public static void RecordGamma()
    {
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        AssetDatabase.SaveAssets();
        Record();
    }
    public static void SetLinear()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        AssetDatabase.SaveAssets();
    }
}
