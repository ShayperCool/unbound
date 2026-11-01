using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

public static class MatchReviewProbe
{
    static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 value) => "["+F(value.x)+","+F(value.y)+","+F(value.z)+"]";
    static readonly Vector3 Target = new Vector3(3, .5f, 4);
    static readonly Quaternion Turn = Quaternion.Euler(0, 90, 0);
    static readonly MatchTargetWeightMask Mask = new MatchTargetWeightMask(new Vector3(.5f, 0, 1), .75f);

    public static void Run()
    {
        foreach (float initial in new[] { 0f, .4f })
        foreach (bool complete in new[] { false, true })
        foreach (string action in new[] { "control", "interruptFalse", "interruptTrue", "playFull", "playOtherLoop", "rebind", "controller", "transition" })
        foreach (string callback in new[] { "none", "observe", "apply" })
            Lifecycle(initial, complete, action, callback);
        Override(false); Override(true);
        var clip = Resources.LoadAll<AnimationClip>("ReviewHumanA")
            .First(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        bool before = clip.hasRootCurves;
        bool human = clip.humanMotion;
        RootProbe.Record("matchReview.humanGetterOrder", "{\"before\":"+(before?1:0)+",\"human\":"+(human?1:0)+",\"after\":"+(clip.hasRootCurves?1:0)+"}");
    }

    static Animator Make(out GameObject root)
    {
        root = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Mover"));
        root.transform.SetPositionAndRotation(new Vector3(1, 0, -2), Quaternion.Euler(0, 20, 0));
        var animator = root.GetComponent<Animator>();
        if (animator == null) animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("C_MatchReview");
        animator.applyRootMotion = true;
        return animator;
    }

    static void Snapshot(string key, Animator animator, MatchReviewMove observer)
    {
        RootProbe.Record(key, RootProbe.Snapshot(animator, null, true));
        var current = animator.GetCurrentAnimatorStateInfo(0);
        var next = animator.GetNextAnimatorStateInfo(0);
        RootProbe.Record(key+".state", "{\"current\":"+current.fullPathHash+",\"next\":"+next.fullPathHash+",\"nextTime\":"+F(next.normalizedTime)+",\"transition\":"+(animator.IsInTransition(0)?1:0)+"}");
        if (observer != null)
            RootProbe.Record(key+".callback", "{\"count\":"+observer.Count+",\"delta\":"+V(observer.LastDelta)+",\"position\":"+V(observer.LastPosition)+",\"matching\":"+(observer.LastMatching?1:0)+"}");
    }

    static void Lifecycle(float initial, bool complete, string action, string callback)
    {
        string key = "matchReview.life."+F(initial)+"."+complete+"."+action+"."+callback;
        var animator = Make(out var root);
        MatchReviewMove observer = null;
        try
        {
            if (callback != "none") { observer = root.AddComponent<MatchReviewMove>(); observer.Apply = callback == "apply"; }
            animator.Play("Loop", 0, initial); animator.Update(0);
            Snapshot(key+".before", animator, observer);
            animator.MatchTarget(Target, Turn, AvatarTarget.Root, Mask, .2f, .8f, complete);
            Snapshot(key+".queued", animator, observer);
            animator.Update(.5f);
            Snapshot(key+".beforeCall", animator, observer);
            switch (action)
            {
                case "interruptFalse": animator.InterruptMatchTarget(false); break;
                case "interruptTrue": animator.InterruptMatchTarget(true); break;
                case "playFull": animator.Play("Full", 0, 0); break;
                case "playOtherLoop": animator.Play("OtherLoop", 0, 0); break;
                case "rebind": animator.Rebind(); break;
                case "controller": animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("C_Mover_Full"); break;
                case "transition": animator.SetTrigger("Go"); break;
            }
            Snapshot(key+".afterCall", animator, observer);
            animator.Update(0); Snapshot(key+".zero", animator, observer);
            for (int i = 0; i < 20; i++)
            {
                animator.Update(.2f);
                Snapshot(key+".step"+i, animator, observer);
            }
            animator.MatchTarget(new Vector3(-2, 0, 1), Quaternion.identity, AvatarTarget.Root, Mask, .2f, .8f, false);
            Snapshot(key+".secondQueued", animator, observer);
            animator.Update(.2f); Snapshot(key+".secondStep", animator, observer);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void Override(bool originalLoops)
    {
        string key = "matchReview.override."+originalLoops;
        var animator = Make(out var root);
        var controller = new AnimatorOverrideController(animator.runtimeAnimatorController);
        try
        {
            var clips = Resources.LoadAll<AnimationClip>("Mover");
            var original = clips.First(c => c.name == (originalLoops ? "Loop" : "Full"));
            var replacement = clips.First(c => c.name == (originalLoops ? "Full" : "Loop"));
            controller[original] = replacement;
            animator.runtimeAnimatorController = controller;
            animator.Play(originalLoops ? "Loop" : "Full", 0, .5f); animator.Update(0);
            Snapshot(key+".before", animator, null);
            animator.MatchTarget(Target, Turn, AvatarTarget.Root, Mask, .2f, .8f, true);
            Snapshot(key+".queued", animator, null);
            for (int i = 0; i < 12; i++)
            {
                animator.Update(.2f); Snapshot(key+".step"+i, animator, null);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(controller); }
    }
}
