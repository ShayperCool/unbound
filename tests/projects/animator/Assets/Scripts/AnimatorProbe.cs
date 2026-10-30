using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public partial class AnimatorProbe : MonoBehaviour
{
    static readonly List<string> s_Events = new List<string>();
    static readonly StringBuilder s_Output = new StringBuilder();
    public static string Number(float value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
    public static void Event(string message) => s_Events.Add(message);
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
    static string S(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static void Value(string key, string value) => s_Output.Append("{\"k\":").Append(S(key)).Append(",\"v\":").Append(value).Append("}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("AnimatorProbe").AddComponent<AnimatorProbe>();

    static string Snapshot(Animator animator)
    {
        var t = animator.transform;
        return "{\"position\":" + V(t.position) + ",\"rotation\":" + Q(t.rotation)
            + ",\"child\":" + V(t.Find("Child").localPosition) + ",\"delta\":" + V(animator.deltaPosition)
            + ",\"delta_rotation\":" + Q(animator.deltaRotation) + ",\"velocity\":" + V(animator.velocity)
            + ",\"angular_velocity\":" + V(animator.angularVelocity) + ",\"root_position\":" + V(animator.rootPosition)
            + ",\"root_rotation\":" + Q(animator.rootRotation) + ",\"time\":" + F(animator.GetCurrentAnimatorStateInfo(0).normalizedTime)
            + ",\"events\":[" + string.Join(",", s_Events.Select(S)) + "]}";
    }

    IEnumerator Start()
    {
        var uninitialized = new GameObject("Uninitialized");
        new GameObject("Child").transform.SetParent(uninitialized.transform, false);
        uninitialized.transform.position = new Vector3(1,2,3);
        var empty = uninitialized.AddComponent<Animator>();
        Value("root.uninitialized.position", V(empty.rootPosition));
        Value("root.uninitialized.rotation", Q(empty.rootRotation));
        empty.rootPosition = new Vector3(4,5,6);
        empty.rootRotation = Quaternion.Euler(0,45,0);
        Value("root.uninitialized.after_set.position", V(empty.rootPosition));
        Value("root.uninitialized.after_set.transform", V(empty.transform.position));
        empty.transform.position = new Vector3(7,8,9);
        Value("root.uninitialized.after_transform.position", V(empty.rootPosition));
        DestroyImmediate(uninitialized);
        foreach (bool stateMove in new[] { false, true })
        foreach (bool apply in new[] { false, true })
            foreach (int mode in new[] { -1, 0, 1, 2, 3, 4, 5, 6, 7 })
                foreach (bool parented in new[] { false, true })
                {
                    string key = (stateMove ? "root." : "plain.") + B(apply) + "." + mode + "." + B(parented);
                    var parent = new GameObject("Parent");
                    if (parented)
                    {
                        parent.transform.position = new Vector3(2, 0, 3);
                        parent.transform.rotation = Quaternion.Euler(20, 30, 10);
                        parent.transform.localScale = new Vector3(2, 1, 3);
                    }
                    var root = new GameObject("Root");
                    root.transform.SetParent(parent.transform, false);
                    var child = new GameObject("Child");
                    child.transform.SetParent(root.transform, false);
                    var animator = root.AddComponent<Animator>();
                    animator.applyRootMotion = apply;
                    if (mode >= 0)
                    {
                        var observer = root.AddComponent<RootObserver>();
                        observer.Mode = mode;
                        if (mode == 4) observer.enabled = false;
                    }
                    animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(stateMove ? "RootMotion" : "RootMotionPlain");
                    animator.Update(0f);
                    Value(key + ".has_root_motion", B(animator.hasRootMotion));
                    int step = 0;
                    foreach (float delta in new[] { 0f, 0.2f, 0.2f, 0f, 0.4f, 0.4f, 2.5f, -0.3f })
                    {
                        s_Events.Clear();
                        animator.Update(delta);
                        if (mode == 5) { animator.ApplyBuiltinRootMotion(); animator.ApplyBuiltinRootMotion(); }
                        if (mode == 7) { animator.rootPosition = new Vector3(9,2,3); animator.rootRotation = Quaternion.Euler(0,45,0); }
                        Value(key + ".step" + step++, Snapshot(animator));
                    }
                    DestroyImmediate(parent);
                }
        {
            var parent = new GameObject("Scale Parent");
            var root = new GameObject("Scale Root");
            root.transform.SetParent(parent.transform, false);
            new GameObject("Child").transform.SetParent(root.transform, false);
            var animator = root.AddComponent<Animator>();
            animator.applyRootMotion = true;
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("RootMotionPlain");
            animator.Update(0f);
            for (int step = 0; step < 6; step++)
            {
                if (step == 1) root.transform.localScale = new Vector3(2,3,4);
                if (step == 2) { parent.transform.localScale = new Vector3(3,2,1); parent.transform.rotation = Quaternion.Euler(20,30,10); }
                if (step == 3)
                {
                    var grandparent = new GameObject("Scale Grandparent");
                    grandparent.transform.localScale = new Vector3(2,1,3);
                    grandparent.transform.rotation = Quaternion.Euler(10,40,20);
                    parent.transform.SetParent(grandparent.transform, false);
                }
                if (step == 4) parent.transform.localScale = new Vector3(-3,2,1);
                s_Events.Clear();
                animator.Update(0.2f);
                Value("root.scaling.step" + step, Snapshot(animator));
            }
            var top = parent.transform.parent != null ? parent.transform.parent.gameObject : parent;
            DestroyImmediate(top);
        }
        BlendTrees();
        float previousCapture = Time.captureDeltaTime;
        Time.captureDeltaTime = 0.04f;
        yield return null;
        BodyMotion();
        Time.captureDeltaTime = previousCapture;
        string output = Environment.GetEnvironmentVariable("ANIMATOR_PROBE_OUT") ?? "/tmp/animator";
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "values.jsonl"), s_Output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
