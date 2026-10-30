using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class BlendSetup
{
    public static void Create()
    {
        var clips = new AnimationClip[9];
        for (int i = 0; i < clips.Length; i++)
        {
            var clip = clips[i] = new AnimationClip { name = "Weight" + i };
            for (int channel = 0; channel < 9; channel++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(BlendWeightTarget), "w" + channel),
                    AnimationCurve.Linear(0, channel == i ? 1 : 0, 1, channel == i ? 1 : 0));
            AssetDatabase.CreateAsset(clip, "Assets/Resources/Weight" + i + ".anim");
        }
        var points = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.left, Vector2.down,
            Vector2.right * 2, Vector2.up * 2, Vector2.left * 2, Vector2.down * 2 };
        foreach (string name in new[] { "Simple", "Directional", "Cartesian", "Direct", "DirectNormalized" })
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Resources/Blend" + name + ".controller");
            controller.AddParameter("x", AnimatorControllerParameterType.Float);
            controller.AddParameter("y", AnimatorControllerParameterType.Float);
            for (int i = 0; i < 9; i++) controller.AddParameter("d" + i, AnimatorControllerParameterType.Float);
            var tree = new BlendTree { name = name, blendParameter = "x", blendParameterY = "y", useAutomaticThresholds = false,
                blendType = name == "Simple" ? BlendTreeType.SimpleDirectional2D : name == "Directional" ? BlendTreeType.FreeformDirectional2D
                    : name == "Cartesian" ? BlendTreeType.FreeformCartesian2D : BlendTreeType.Direct };
            AssetDatabase.AddObjectToAsset(tree, controller);
            int count = name == "Simple" ? 5 : 9;
            var children = new ChildMotion[count];
            for (int i = 0; i < count; i++) children[i] = new ChildMotion { motion = clips[i], position = points[i], timeScale = 1, directBlendParameter = "d" + i };
            tree.children = children;
            var serialized = new SerializedObject(tree);
            serialized.FindProperty("m_NormalizedBlendValues").boolValue = name == "DirectNormalized";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var machine = controller.layers[0].stateMachine;
            var state = machine.AddState("Blend");
            state.motion = tree;
            machine.defaultState = state;
        }
        AssetDatabase.SaveAssets();
    }
    public static void SimpleVariants()
    {
        foreach (string name in new[] { "SimpleNoCenter", "SimpleSkew", "SimpleAxis" })
        {
            var points = name == "SimpleNoCenter" ? new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down }
                : name == "SimpleAxis" ? new[] { Vector2.zero, Vector2.right, Vector2.left }
                : new[] { Vector2.zero, new Vector2(2,0), new Vector2(0.5f,1), new Vector2(-1,0.25f), new Vector2(-0.2f,-1.5f) };
            var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Resources/Blend" + name + ".controller");
            controller.AddParameter("x", AnimatorControllerParameterType.Float);
            controller.AddParameter("y", AnimatorControllerParameterType.Float);
            var tree = new BlendTree { name = name, blendParameter = "x", blendParameterY = "y", blendType = BlendTreeType.SimpleDirectional2D,
                useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller);
            var children = new ChildMotion[points.Length];
            for (int i = 0; i < children.Length; i++) children[i] = new ChildMotion
            {
                motion = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resources/Weight" + i + ".anim"),
                position = points[i], timeScale = 1,
            };
            tree.children = children;
            var machine = controller.layers[0].stateMachine;
            var state = machine.AddState("Blend"); state.motion = tree; machine.defaultState = state;
        }
        AssetDatabase.SaveAssets();
    }

    public static void Bundle()
    {
        System.IO.Directory.CreateDirectory("Reference/blend-bundle");
        var names = new[] { "Simple", "SimpleNoCenter", "SimpleSkew", "SimpleAxis", "Directional", "Cartesian", "Direct", "DirectNormalized" };
        var paths = Array.ConvertAll(names, name => "Assets/Resources/Blend" + name + ".controller");
        BuildPipeline.BuildAssetBundles("Reference/blend-bundle", new[] { new AssetBundleBuild { assetBundleName = "blends", assetNames = paths } },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DeterministicAssetBundle, BuildTarget.StandaloneLinux64);
    }

}
