using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Edit mode: animation clips made by script and made assets by AssetDatabase.CreateAsset, each
/// with another set of curves, to show how a clip's file orders and groups them: curves set in an
/// order other than their kinds' (script, light and GameObject floats before position, scale and
/// rotation curves, paths out of order), constant and changing curves, a clip of constant curves
/// only, vector components with different key times or some components missing, Euler angles,
/// material, blend shape and sorting order curves, object reference curves on two paths, curves
/// set through AnimationUtility.SetEditorCurve, and an empty clip. Each row records the clip's
/// curve bindings as AnimationUtility lists them; run.py compares the files on disk after the run.
/// </summary>
public class ClipAssetTests
{
    const string Folder = "Assets/Clips";

    static string Binding(EditorCurveBinding b) => b.path + "|" + (b.type == null ? "null" : b.type.Name) + "|" + b.propertyName + (b.isPPtrCurve ? "|pptr" : "") + (b.isDiscreteCurve ? "|discrete" : "");

    static void Create(string key, AnimationClip clip, string path)
    {
        ProbeLog.Try(key + ".result", () => { AssetDatabase.CreateAsset(clip, path); return clip.name; });
        ProbeLog.Row(key + ".bindings", string.Join("; ", AnimationUtility.GetCurveBindings(clip).Select(Binding)));
        ProbeLog.Row(key + ".objectBindings", string.Join("; ", AnimationUtility.GetObjectReferenceCurveBindings(clip).Select(Binding)));
        ProbeLog.Row(key + ".length", clip.length);
        ProbeLog.Row(key + ".genericRoot", clip.hasGenericRootTransform);
        ProbeLog.Snapshot(key + ".file", path);
    }

    [Test]
    public void ClipAssets()
    {
        AssetDatabase.CreateFolder("Assets", "Clips");
        var matA = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MatEdit.mat");
        var matB = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MatQuit.mat");

        // Kinds set in reverse: floats of other classes first, then transforms, paths out of order.
        var order = new AnimationClip { frameRate = 24f };
        order.SetCurve("", typeof(Holder), "ratio", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        order.SetCurve("B", typeof(Light), "m_Intensity", AnimationCurve.Linear(0f, 1f, 2f, 3f));
        order.SetCurve("A", typeof(GameObject), "m_IsActive", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        order.SetCurve("B", typeof(Transform), "localScale.x", AnimationCurve.Linear(0f, 1f, 1f, 2f));
        order.SetCurve("B", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        order.SetCurve("A", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 2f));
        order.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 1f, 3f));
        order.SetCurve("A/C", typeof(Transform), "localRotation.y", AnimationCurve.Linear(0f, 0f, 1f, 0.5f));
        order.SetCurve("A/C", typeof(Transform), "localRotation.w", AnimationCurve.Linear(0f, 1f, 1f, 0.8660254f));
        order.SetCurve("", typeof(Holder), "count", AnimationCurve.Constant(0f, 1f, 4f));
        AnimationUtility.SetObjectReferenceCurve(order, EditorCurveBinding.PPtrCurve("B", typeof(MeshRenderer), "m_Materials.Array.data[0]"),
            new[] { new ObjectReferenceKeyframe { time = 0f, value = matB } });
        AnimationUtility.SetObjectReferenceCurve(order, EditorCurveBinding.PPtrCurve("A", typeof(MeshRenderer), "m_Materials.Array.data[0]"),
            new[] { new ObjectReferenceKeyframe { time = 0f, value = matA }, new ObjectReferenceKeyframe { time = 0.5f, value = matB } });
        Create("order", order, Folder + "/Order.anim");

        // Constant curves only, and one changing curve set last.
        var constants = new AnimationClip();
        constants.SetCurve("", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f, 1f));
        constants.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 2f));
        constants.SetCurve("", typeof(Light), "m_Intensity", AnimationCurve.Constant(0f, 1f, 0.5f));
        constants.SetCurve("", typeof(Light), "m_Range", AnimationCurve.Linear(0f, 1f, 1f, 5f));
        Create("constants", constants, Folder + "/Constants.anim");

        // Vector components with different key times; a rotation with two components.
        var merged = new AnimationClip();
        merged.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        merged.SetCurve("", typeof(Transform), "localPosition.y", new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 2f), new Keyframe(2f, 1f)));
        merged.SetCurve("", typeof(Transform), "localRotation.x", AnimationCurve.Linear(0.25f, 0f, 1f, 0.5f));
        merged.SetCurve("", typeof(Transform), "localRotation.w", AnimationCurve.Linear(0f, 1f, 1f, 0.8660254f));
        merged.SetCurve("Child", typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Linear(0f, 0f, 1f, 90f));
        Create("merged", merged, Folder + "/Merged.anim");

        // Curves set through the editor's curve API: Euler angles, a material property, a blend
        // shape, a sorting order, a position component on a child.
        var editor = new AnimationClip();
        AnimationUtility.SetEditorCurve(editor, EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.y"), AnimationCurve.Linear(0f, 0f, 1f, 180f));
        AnimationUtility.SetEditorCurve(editor, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.z"), AnimationCurve.Linear(0f, 0f, 1f, 4f));
        AnimationUtility.SetEditorCurve(editor, EditorCurveBinding.FloatCurve("", typeof(MeshRenderer), "material._Color.r"), AnimationCurve.Linear(0f, 1f, 1f, 0f));
        AnimationUtility.SetEditorCurve(editor, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Smile"), AnimationCurve.Linear(0f, 0f, 1f, 100f));
        AnimationUtility.SetEditorCurve(editor, EditorCurveBinding.DiscreteCurve("", typeof(SpriteRenderer), "m_SortingOrder"), AnimationCurve.Constant(0f, 1f, 3f));
        Create("editor", editor, Folder + "/Editor.anim");

        // Every kind in reverse, constant transforms and floats among changing ones, then built by
        // an object reference curve.
        var kinds = new AnimationClip();
        kinds.SetCurve("", typeof(Holder), "count", AnimationCurve.Constant(0f, 1f, 2f));
        kinds.SetCurve("", typeof(Light), "m_Intensity", AnimationCurve.Linear(0f, 1f, 1f, 2f));
        kinds.SetCurve("S", typeof(Transform), "localScale.x", AnimationCurve.Linear(0f, 1f, 1f, 2f));
        kinds.SetCurve("E", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0f, 0f, 1f, 45f));
        kinds.SetCurve("R", typeof(Transform), "localRotation.x", AnimationCurve.Linear(0f, 0f, 1f, 0.5f));
        kinds.SetCurve("R", typeof(Transform), "localRotation.w", AnimationCurve.Linear(0f, 1f, 1f, 0.8660254f));
        kinds.SetCurve("Q", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 5f));
        kinds.SetCurve("P", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        kinds.SetCurve("", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f, 1f));
        kinds.SetCurve("", typeof(Holder), "ratio", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        AnimationUtility.SetObjectReferenceCurve(kinds, EditorCurveBinding.PPtrCurve("", typeof(MeshRenderer), "m_Materials.Array.data[0]"),
            new[] { new ObjectReferenceKeyframe { time = 0f, value = matA } });
        Create("kinds", kinds, Folder + "/Kinds.anim");

        // Curves set by SetCurve only, then clip settings or events set through AnimationUtility.
        var settings = new AnimationClip();
        settings.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        settings.SetCurve("", typeof(Holder), "ratio", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        var clipSettings = AnimationUtility.GetAnimationClipSettings(settings);
        clipSettings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(settings, clipSettings);
        Create("settings", settings, Folder + "/Settings.anim");
        var events = new AnimationClip();
        events.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        events.SetCurve("", typeof(Holder), "ratio", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        AnimationUtility.SetAnimationEvents(events, new[] { new AnimationEvent { time = 0.25f, functionName = "OnHit" } });
        Create("events", events, Folder + "/Events.anim");

        // Common properties of other classes, built by a sprite reference curve.
        var common = new AnimationClip();
        common.SetCurve("UI", typeof(RectTransform), "m_AnchoredPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 10f));
        common.SetCurve("UI", typeof(CanvasGroup), "m_Alpha", AnimationCurve.Linear(0f, 1f, 1f, 0f));
        common.SetCurve("", typeof(SpriteRenderer), "m_Color.a", AnimationCurve.Linear(0f, 1f, 1f, 0f));
        common.SetCurve("", typeof(MeshRenderer), "m_Enabled", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        common.SetCurve("", typeof(AudioSource), "m_Volume", AnimationCurve.Linear(0f, 1f, 1f, 0f));
        common.SetCurve("", typeof(Camera), "field of view", AnimationCurve.Linear(0f, 60f, 1f, 30f));
        common.SetCurve("", typeof(Light), "m_Color.r", AnimationCurve.Linear(0f, 1f, 1f, 0f));
        common.SetCurve("", typeof(Light), "m_Range", AnimationCurve.Linear(0f, 1f, 1f, 5f));
        common.SetCurve("", typeof(Holder), "offset.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        AnimationUtility.SetObjectReferenceCurve(common, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"),
            new[] { new ObjectReferenceKeyframe { time = 0f, value = null }, new ObjectReferenceKeyframe { time = 0.5f, value = null } });
        Create("common", common, Folder + "/Common.anim");

        var empty = new AnimationClip();
        Create("empty", empty, Folder + "/Empty.anim");
    }
}
