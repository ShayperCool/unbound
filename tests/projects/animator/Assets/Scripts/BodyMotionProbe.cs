using UnityEngine;

public partial class AnimatorProbe
{
    static string BodySnapshot(Animator animator, Rigidbody body, CharacterController controller) =>
        "{\"pose\":" + Snapshot(animator) + ",\"body_position\":" + V(body != null ? body.position : Vector3.zero)
        + ",\"body_rotation\":" + Q(body != null ? body.rotation : Quaternion.identity)
        + ",\"body_velocity\":" + V(body != null ? body.linearVelocity : Vector3.zero)
        + ",\"body_angular\":" + V(body != null ? body.angularVelocity : Vector3.zero)
        + ",\"controller_velocity\":" + V(controller != null ? controller.velocity : Vector3.zero) + "}";

    static void BodyMotion()
    {
        var simulation = Physics.simulationMode;
        bool autoSync = Physics.autoSyncTransforms;
        Physics.simulationMode = SimulationMode.Script;
        Physics.autoSyncTransforms = false;
        Value("root.body.clock", V(new Vector3(Time.deltaTime, Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime)));
        foreach (bool fixedMode in new[] { false, true })
            foreach (int kind in new[] { 0,1,2,3,4,5 })
                foreach (int mode in new[] { -1,1,3 })
                {
                    string key = "root.body." + B(fixedMode) + "." + kind + "." + mode;
                    var root = new GameObject("Body Root");
                    root.transform.position = new Vector3(0,1,0);
                    new GameObject("Child").transform.SetParent(root.transform, false);
                    Rigidbody body = null;
                    CharacterController controller = null;
                    if (kind == 1 || kind == 2 || kind == 4 || kind == 5)
                    {
                        root.AddComponent<SphereCollider>().radius = 0.2f;
                        body = root.AddComponent<Rigidbody>();
                        body.isKinematic = kind == 2;
                        body.useGravity = kind == 4;
                        if (kind == 4 || kind == 5)
                        {
                            body.linearVelocity = new Vector3(1,3,2);
                            body.angularVelocity = new Vector3(0.5f,1,0.25f);
                            body.linearDamping = 0.5f;
                        }
                    }
                    if (kind == 3) controller = root.AddComponent<CharacterController>();
                    var animator = root.AddComponent<Animator>();
                    animator.updateMode = fixedMode ? AnimatorUpdateMode.Fixed : AnimatorUpdateMode.Normal;
                    animator.applyRootMotion = true;
                    if (mode >= 0) root.AddComponent<RootObserver>().Mode = mode;
                    animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("RootMotionPlain");
                    Physics.SyncTransforms();
                    animator.Update(0f);
                    for (int step = 0; step < 3; step++)
                    {
                        s_Events.Clear();
                        animator.Update(0.2f);
                        Value(key + ".animation" + step, BodySnapshot(animator, body, controller));
                        Physics.Simulate(0.2f);
                        Value(key + ".physics" + step, BodySnapshot(animator, body, controller));
                    }
                    DestroyImmediate(root);
                }
        foreach (bool kinematic in new[] { false, true })
        {
            var root = new GameObject("Repeated Body");
            new GameObject("Child").transform.SetParent(root.transform, false);
            var body = root.AddComponent<Rigidbody>(); body.isKinematic = kinematic; body.useGravity = false;
            var animator = root.AddComponent<Animator>(); animator.applyRootMotion = true;
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("RootMotionPlain");
            Physics.SyncTransforms(); animator.Update(0f);
            for (int step = 0; step < 3; step++)
            {
                s_Events.Clear(); animator.Update(0.2f);
                Value("root.body.repeated." + B(kinematic) + "." + step, BodySnapshot(animator, body, null));
            }
            Physics.Simulate(0.2f);
            Value("root.body.repeated." + B(kinematic) + ".physics", BodySnapshot(animator, body, null));
            DestroyImmediate(root);
        }
        Physics.simulationMode = simulation;
        Physics.autoSyncTransforms = autoSync;
    }
}
