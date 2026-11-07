using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>`Unity -batchmode -projectPath tests/projects/particlecurves -executeMethod ProbeRunner.Record`
/// with <c>PARTICLE_CURVES_OUT</c> set (see run.py): the empty scene in Play mode, where
/// <see cref="ParticleCurveProbe"/> records the values and exits the Editor.
/// <c>ProbeRunner.Create</c> authored Resources/Authored.prefab once.</summary>
public static class ProbeRunner
{
    static AnimationCurve Curve(params float[] timeValue)
    {
        var c = new AnimationCurve();
        for (int i = 0; i + 1 < timeValue.Length; i += 2) c.AddKey(new Keyframe(timeValue[i], timeValue[i + 1]));
        return c;
    }

    /// <summary>Writes one serialized MinMaxCurve: mode, scalar, minScalar and both curves.</summary>
    static void Set(SerializedObject so, string path, int state, float scalar, float minScalar, AnimationCurve max, AnimationCurve min)
    {
        so.FindProperty(path + ".minMaxState").intValue = state;
        so.FindProperty(path + ".scalar").floatValue = scalar;
        so.FindProperty(path + ".minScalar").floatValue = minScalar;
        so.FindProperty(path + ".maxCurve").animationCurveValue = max;
        so.FindProperty(path + ".minCurve").animationCurveValue = min;
    }

    static void Case(Transform root, string name, System.Action<SerializedObject> author)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 777u;
        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 1f;
        var shape = ps.shape;
        shape.enabled = false;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)3) });
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var so = new SerializedObject(ps);
        author(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void Create()
    {
        var root = new GameObject("Authored");
        Case(root.transform, "constkeys", so =>
        {
            Set(so, "InitialModule.startSize", 0, 3f, 0.1f, Curve(0f, 2f, 1f, 0.5f), Curve(0f, 0.1f));
            Set(so, "InitialModule.startSpeed", 0, 1.5f, 0.7f, Curve(0f, 1f), Curve(0f, 1f));
            Set(so, "InitialModule.startLifetime", 0, 2f, 9f, Curve(0f, 1f), Curve(0f, 1f));
            Set(so, "SizeModule.curve", 0, 0.8f, 0.2f, Curve(0f, 0.5f, 1f, 1f), Curve(0f, 1f));
        });
        Case(root.transform, "curvescalar", so =>
        {
            Set(so, "InitialModule.startSize", 1, 2f, 0.4f, Curve(0f, 1.5f, 1f, 0.25f), Curve(0f, 1f));
            Set(so, "InitialModule.startSpeed", 1, 3f, 0f, Curve(0f, 0.5f, 1f, 1f), Curve(0f, 1f));
            Set(so, "InitialModule.gravityModifier", 1, 0f, 0f, Curve(0f, 1f), Curve(0f, 1f));
            Set(so, "SizeModule.curve", 1, 2f, 0.6f, Curve(0f, 1f, 1f, 0.25f), Curve(0f, 1f));
        });
        Case(root.transform, "twocurves", so =>
        {
            Set(so, "InitialModule.startSize", 2, 1.5f, 0.3f, Curve(0f, 1f, 1f, 2f), Curve(0f, 0.2f, 1f, 1f));
            Set(so, "InitialModule.startLifetime", 2, 3f, 0.5f, Curve(0f, 1f, 1f, 0.5f), Curve(0f, 0.5f, 1f, 0.25f));
            Set(so, "SizeModule.curve", 2, 1.25f, 0.5f, Curve(0f, 1f, 1f, 0.5f), Curve(0f, 0.25f, 1f, 1f));
        });
        Case(root.transform, "twoconst", so =>
        {
            Set(so, "InitialModule.startSize", 3, 2.5f, 0.5f, Curve(0f, 7f), Curve(0f, 6f));
            Set(so, "EmissionModule.rateOverTime", 3, 5f, 1f, Curve(0f, 1f), Curve(0f, 1f));
            Set(so, "SizeModule.curve", 3, 0.9f, 0.3f, Curve(0f, 4f), Curve(0f, 5f));
        });
        Case(root.transform, "outofrange", so =>
        {
            Set(so, "InitialModule.startSize", 3, -2f, -3f, Curve(0f, 1f), Curve(0f, 1f));
            Set(so, "InitialModule.startLifetime", 0, 0f, 0f, Curve(0f, 1f), Curve(0f, 1f));
            Set(so, "EmissionModule.rateOverTime", 0, -3f, 0f, Curve(0f, 1f), Curve(0f, 1f));
            Set(so, "CollisionModule.m_Bounce", 0, 5f, 0f, Curve(0f, 1f), Curve(0f, 1f));
            Set(so, "UVModule.frameOverTime", 1, 3f, 0f, Curve(0f, 0f, 1f, 1f), Curve(0f, 1f));
            Set(so, "SizeModule.curve", 1, -2f, 0f, Curve(0f, 1f, 1f, 0.5f), Curve(0f, 1f));
        });
        // Script fields whose constant, multiplier and curves all differ.
        var fields = root.AddComponent<CurveFields>();
        var c = new ParticleSystem.MinMaxCurve(2f);
        c.curveMultiplier = 9f; c.constantMin = -1f; c.curve = Curve(0f, 3f, 1f, 4f);
        fields.constant = c;
        c = new ParticleSystem.MinMaxCurve(3f, Curve(0f, 1f, 1f, 0.5f));
        c.constant = 7f; c.constantMin = 0.5f; c.curveMin = Curve(0f, 0.25f);
        fields.curve = c;
        c = new ParticleSystem.MinMaxCurve(1.5f, Curve(0f, 1f, 1f, 0.5f), Curve(0f, 0.25f, 1f, 2f));
        c.constantMin = -1f; c.constantMax = 8f;
        fields.twoCurves = c;
        c = new ParticleSystem.MinMaxCurve(1f, 4f);
        c.curveMultiplier = 6f; c.curve = Curve(0f, 3f, 0.5f, 1f, 1f, 4f);
        fields.twoConstants = c;
        System.IO.Directory.CreateDirectory("Assets/Resources");
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Authored.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
