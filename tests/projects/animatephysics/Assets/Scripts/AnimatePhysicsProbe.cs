using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Animator.animatePhysics: its own stored state beside updateMode, and how an animated kinematic
// body reaches its pose. Physics outcomes are recorded as categories (moving or not, carrying a
// resting box or not), which do not depend on the platform's arithmetic or frame timing.
public class AnimatePhysicsProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("AnimatePhysicsProbe").AddComponent<AnimatePhysicsProbe>(); }
    [Serializable] class Row { public string key; public string value; }

    class Case
    {
        public string name;
        public Rigidbody body;
        public Rigidbody box;
        public float bodyStart, boxStart, fastest;
        public Transform scripted;
    }

    StreamWriter output;
    Case[] cases;
    int steps;
    const int MeasureStep = 100;

    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("ANIMATE_PHYSICS_OUT"); if (string.IsNullOrEmpty(path)) return;
        output = new StreamWriter(path);
        try { RecordState(); BuildCases(); }
        catch (Exception ex) { Debug.LogException(ex); output.Dispose(); EditorApplication.Exit(1); }
    }

    void RecordState()
    {
        var property = typeof(Animator).GetProperty("animatePhysics");
        Put("property.obsolete", property.IsDefined(typeof(ObsoleteAttribute), false));
        foreach (var name in new[] { "AuthoredFixedOff", "AuthoredNormalOn", "LegacyFixed", "LegacyNormal" })
        {
            var authored = GameObject.Find(name).GetComponent<Animator>();
            Put(name + ".animatePhysics", authored.animatePhysics);
            Put(name + ".updateMode", authored.updateMode);
            var copy = Instantiate(authored.gameObject).GetComponent<Animator>();
            Put(name + ".clone.animatePhysics", copy.animatePhysics);
            Put(name + ".clone.updateMode", copy.updateMode);
            Destroy(copy.gameObject);
        }
        var animator = new GameObject("State").AddComponent<Animator>();
        Put("new.animatePhysics", animator.animatePhysics);
        Put("new.updateMode", animator.updateMode);
        animator.animatePhysics = true;
        Put("setOn.animatePhysics", animator.animatePhysics);
        Put("setOn.updateMode", animator.updateMode);
        animator.updateMode = AnimatorUpdateMode.Fixed;
        Put("fixed.animatePhysics", animator.animatePhysics);
        animator.animatePhysics = false;
        Put("setOff.updateMode", animator.updateMode);
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        Put("unscaled.animatePhysics", animator.animatePhysics);
        Destroy(animator.gameObject);
    }

    // The platform's local x goes from 0 to 10 over 10 seconds.
    static AnimationClip Clip(string path)
    {
        var clip = new AnimationClip();
        clip.SetCurve(path, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 10, 10));
        clip.SetCurve(path, typeof(Transform), "localPosition.y", AnimationCurve.Constant(0, 10, 0));
        clip.SetCurve(path, typeof(Transform), "localPosition.z", AnimationCurve.Constant(0, 10, 0));
        return clip;
    }

    static Animator Animate(GameObject go, bool animatePhysics, AnimatorUpdateMode mode, AnimationClip clip)
    {
        var animator = go.AddComponent<Animator>();
        animator.updateMode = mode;
        animator.animatePhysics = animatePhysics;
        var graph = PlayableGraph.Create(go.name);
        // A new graph runs on the DSP clock (its default, tests/projects/playables core.defaults),
        // which follows the audio device in real time; game time keeps the pose in step with the
        // fixed steps however long a frame takes.
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "out", animator);
        if (clip != null) output.SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
        graph.Play();
        return animator;
    }

    static Rigidbody Platform(GameObject go)
    {
        go.AddComponent<BoxCollider>().size = new Vector3(4, 0.5f, 2);
        var body = go.AddComponent<Rigidbody>();
        body.isKinematic = true;
        return body;
    }

    static Rigidbody Box(float z)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.transform.position = new Vector3(0, 0.75f, z);
        return box.AddComponent<Rigidbody>();
    }

    // A static holder places each case at its own z; the clip animates the object under it.
    static GameObject Placed(string name, float z)
    {
        var holder = new GameObject(name + ".holder");
        holder.transform.position = new Vector3(0, 0, z);
        var go = new GameObject(name);
        go.transform.SetParent(holder.transform, false);
        return go;
    }

    Case Root(string name, float z, bool animatePhysics, AnimatorUpdateMode mode)
    {
        var go = Placed(name, z);
        var body = Platform(go);
        Animate(go, animatePhysics, mode, Clip(""));
        return new Case { name = name, body = body, box = Box(z) };
    }

    void BuildCases()
    {
        // A child body below an animated root, a body under an Animator that animates only
        // another object while a script moves it, and a disabled Animator.
        var parent = Placed("child.parent", 40);
        Animate(parent, true, AnimatorUpdateMode.Fixed, Clip(""));
        var child = new GameObject("child");
        child.transform.SetParent(parent.transform, false);
        var childCase = new Case { name = "child", body = Platform(child), box = Box(40) };

        var scripted = Placed("scripted", 50);
        var other = new GameObject("Other");
        other.transform.SetParent(scripted.transform, false);
        var scriptedCase = new Case { name = "scripted", body = Platform(scripted), box = Box(50), scripted = scripted.transform };
        var otherClip = new AnimationClip();
        otherClip.SetCurve("Other", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 10, 10));
        Animate(scripted, true, AnimatorUpdateMode.Fixed, otherClip);

        var disabled = Root("disabled", 60, true, AnimatorUpdateMode.Fixed);
        disabled.body.GetComponent<Animator>().enabled = false;

        cases = new[]
        {
            Root("off.normal", 0, false, AnimatorUpdateMode.Normal),
            Root("on.normal", 10, true, AnimatorUpdateMode.Normal),
            Root("off.fixed", 20, false, AnimatorUpdateMode.Fixed),
            Root("on.fixed", 30, true, AnimatorUpdateMode.Fixed),
            childCase,
            scriptedCase,
            disabled,
        };
        foreach (var c in cases)
        {
            c.bodyStart = c.body.transform.position.x;
            c.boxStart = c.box.position.x;
        }
    }

    void FixedUpdate()
    {
        if (cases == null) return;
        steps++;
        foreach (var c in cases)
            if (c.scripted != null) c.scripted.position += new Vector3(0.02f, 0, 0);
        // In Normal mode a fixed step without an Update before it has no new pose: the fastest of
        // the last ten steps is compared.
        if (steps > MeasureStep - 10)
            foreach (var c in cases) c.fastest = Mathf.Max(c.fastest, c.body.linearVelocity.x);
        if (steps != MeasureStep) return;
        try
        {
            foreach (var c in cases)
            {
                float travel = c.body.position.x - c.bodyStart;
                Put(c.name + ".travelled", travel > 1f);
                Put(c.name + ".moving", c.fastest > 0.5f);
                Put(c.name + ".carried", c.box.position.x - c.boxStart > 0.5f * travel && travel > 1f);
            }
            Put("complete", 1);
            output.Dispose();
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); output.Dispose(); EditorApplication.Exit(1); }
    }
}
