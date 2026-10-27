using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// How the 3D joints move bodies: each joint type's defaults, then scenarios (hinges with limits,
/// motors and springs, fixed joints that break, spring joints, character joints, configurable
/// joints with locked, limited and driven axes) simulated in fixed steps; every few steps the
/// bodies' poses and velocities and the joint's readings (angle, velocity, current force and
/// torque) are recorded, and OnJointBreak calls. Writes <c>joints.jsonl</c> into
/// <c>JOINTS_PROBE_OUT</c>, then quits.
/// </summary>
public class JointProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("JointProbe").AddComponent<JointProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string Q(Quaternion q) => $"[{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}]";
    static void Record(string kind, string body) => s_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    static string Body(Rigidbody rb) => $"[{V(rb.position)},{Q(rb.rotation)},{V(rb.linearVelocity)},{V(rb.angularVelocity)}]";

    static string Reading(Joint j)
    {
        if (j == null) return "null";
        string extra = "";
        if (j is HingeJoint h) extra = $",\"angle\":{F(h.angle)},\"velocity\":{F(h.velocity)}";
        return $"{{\"force\":{V(j.currentForce)},\"torque\":{V(j.currentTorque)}{extra}}}";
    }

    /// <summary>Records joint breaks.</summary>
    class BreakRecorder : MonoBehaviour
    {
        internal string Scenario;
        internal int Step;
        void OnJointBreak(float breakForce) =>
            Record("break", $"\"name\":\"{Scenario}\",\"step\":{Step},\"force\":{F(breakForce)},\"joints\":{GetComponents<Joint>().Length}");
    }

    static Rigidbody Bob(string name, Vector3 position, float radius = 0.25f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = Vector3.one * (radius * 2f);
        var rb = go.AddComponent<Rigidbody>();
        return rb;
    }

    static Rigidbody Block(string name, Vector3 position, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = size;
        return go.AddComponent<Rigidbody>();
    }

    IEnumerator Steps(string name, int count, Rigidbody[] bodies, Joint joint, System.Action<int> each = null, int every = 5)
    {
        var recorder = bodies[0].gameObject.GetComponent<BreakRecorder>() ?? bodies[0].gameObject.AddComponent<BreakRecorder>();
        recorder.Scenario = name;
        var steps = new List<string>();
        for (int i = 0; i < count; i++)
        {
            recorder.Step = i;
            each?.Invoke(i);
            yield return new WaitForFixedUpdate();
            if (i % every == every - 1)
                steps.Add($"[{i},[{string.Join(",", bodies.Select(Body))}],{Reading(joint)}]");
        }
        Record("scenario", $"\"name\":\"{name}\",\"steps\":[{string.Join(",", steps)}]");
        foreach (var b in bodies) if (b != null) Destroy(b.gameObject);
        yield return new WaitForFixedUpdate();
    }

    static string Limits(JointLimits l) => $"[{F(l.min)},{F(l.max)},{F(l.bounciness)},{F(l.bounceMinVelocity)},{F(l.contactDistance)}]";
    static string Soft(SoftJointLimit l) => $"[{F(l.limit)},{F(l.bounciness)},{F(l.contactDistance)}]";
    static string SoftSpring(SoftJointLimitSpring s) => $"[{F(s.spring)},{F(s.damper)}]";
    static string Drive(JointDrive d) => $"[{F(d.positionSpring)},{F(d.positionDamper)},{F(d.maximumForce)},{(d.useAcceleration ? 1 : 0)}]";

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("JOINTS_PROBE_OUT") ?? "/tmp/joints";
        Directory.CreateDirectory(dir);
        yield return new WaitForFixedUpdate();

        // Defaults of every joint type on a fresh body.
        {
            var rb = Bob("defaults", new Vector3(0f, 50f, 0f));
            rb.isKinematic = true;
            var go = rb.gameObject;
            var hinge = go.AddComponent<HingeJoint>();
            Record("defaults hinge", $"\"anchor\":{V(hinge.anchor)},\"connectedAnchor\":{V(hinge.connectedAnchor)},\"axis\":{V(hinge.axis)},\"auto\":{hinge.autoConfigureConnectedAnchor.ToString().ToLower()},\"breakForce\":{F(hinge.breakForce)},\"breakTorque\":{F(hinge.breakTorque)},\"enableCollision\":{hinge.enableCollision.ToString().ToLower()},\"enablePreprocessing\":{hinge.enablePreprocessing.ToString().ToLower()},\"massScale\":{F(hinge.massScale)},\"connectedMassScale\":{F(hinge.connectedMassScale)},\"useLimits\":{hinge.useLimits.ToString().ToLower()},\"limits\":{Limits(hinge.limits)},\"useMotor\":{hinge.useMotor.ToString().ToLower()},\"motor\":[{F(hinge.motor.targetVelocity)},{F(hinge.motor.force)},{hinge.motor.freeSpin.ToString().ToLower()}],\"useSpring\":{hinge.useSpring.ToString().ToLower()},\"spring\":[{F(hinge.spring.spring)},{F(hinge.spring.damper)},{F(hinge.spring.targetPosition)}],\"extendedLimits\":{hinge.extendedLimits.ToString().ToLower()},\"useAcceleration\":{hinge.useAcceleration.ToString().ToLower()}");
            Destroy(hinge);
            var spring = go.AddComponent<SpringJoint>();
            Record("defaults spring", $"\"spring\":{F(spring.spring)},\"damper\":{F(spring.damper)},\"minDistance\":{F(spring.minDistance)},\"maxDistance\":{F(spring.maxDistance)},\"tolerance\":{F(spring.tolerance)},\"anchor\":{V(spring.anchor)},\"connectedAnchor\":{V(spring.connectedAnchor)}");
            Destroy(spring);
            var character = go.AddComponent<CharacterJoint>();
            Record("defaults character", $"\"swingAxis\":{V(character.swingAxis)},\"twistLimitSpring\":{SoftSpring(character.twistLimitSpring)},\"lowTwistLimit\":{Soft(character.lowTwistLimit)},\"highTwistLimit\":{Soft(character.highTwistLimit)},\"swingLimitSpring\":{SoftSpring(character.swingLimitSpring)},\"swing1Limit\":{Soft(character.swing1Limit)},\"swing2Limit\":{Soft(character.swing2Limit)},\"enableProjection\":{character.enableProjection.ToString().ToLower()},\"projectionDistance\":{F(character.projectionDistance)},\"projectionAngle\":{F(character.projectionAngle)}");
            Destroy(character);
            var configurable = go.AddComponent<ConfigurableJoint>();
            Record("defaults configurable", $"\"secondaryAxis\":{V(configurable.secondaryAxis)},\"motions\":[{(int)configurable.xMotion},{(int)configurable.yMotion},{(int)configurable.zMotion},{(int)configurable.angularXMotion},{(int)configurable.angularYMotion},{(int)configurable.angularZMotion}],\"linearLimit\":{Soft(configurable.linearLimit)},\"linearLimitSpring\":{SoftSpring(configurable.linearLimitSpring)},\"lowAngularXLimit\":{Soft(configurable.lowAngularXLimit)},\"highAngularXLimit\":{Soft(configurable.highAngularXLimit)},\"angularYLimit\":{Soft(configurable.angularYLimit)},\"angularZLimit\":{Soft(configurable.angularZLimit)},\"xDrive\":{Drive(configurable.xDrive)},\"slerpDrive\":{Drive(configurable.slerpDrive)},\"rotationDriveMode\":{(int)configurable.rotationDriveMode},\"targetPosition\":{V(configurable.targetPosition)},\"targetRotation\":{Q(configurable.targetRotation)},\"projectionMode\":{(int)configurable.projectionMode},\"configuredInWorldSpace\":{configurable.configuredInWorldSpace.ToString().ToLower()},\"swapBodies\":{configurable.swapBodies.ToString().ToLower()}");
            Destroy(configurable);
            var fixedJoint = go.AddComponent<FixedJoint>();
            Record("defaults fixed", $"\"anchor\":{V(fixedJoint.anchor)},\"breakForce\":{F(fixedJoint.breakForce)}");
            Destroy(go);
            yield return new WaitForFixedUpdate();
        }

        // Hinges on the world: a pendulum, limited, motored, sprung.
        {
            var b = Bob("bob", new Vector3(3f, 5f, 0f));
            var h = b.gameObject.AddComponent<HingeJoint>();
            h.autoConfigureConnectedAnchor = false;
            h.anchor = new Vector3(-6f, 0f, 0f); // in the bob's space (scaled by 0.5)
            h.connectedAnchor = new Vector3(0f, 5f, 0f);
            h.axis = Vector3.forward;
            yield return Steps("hinge pendulum", 120, new[] { b }, h);
        }
        {
            var b = Bob("bob", new Vector3(3f, 5f, 0f));
            var h = b.gameObject.AddComponent<HingeJoint>();
            h.autoConfigureConnectedAnchor = false;
            h.anchor = new Vector3(-6f, 0f, 0f);
            h.connectedAnchor = new Vector3(0f, 5f, 0f);
            h.axis = Vector3.forward;
            h.useLimits = true;
            h.limits = new JointLimits { min = -30f, max = 45f, bounciness = 0.3f };
            yield return Steps("hinge limits", 120, new[] { b }, h);
        }
        {
            var b = Block("plate", new Vector3(0f, 5f, 0f), new Vector3(2f, 0.2f, 1f));
            b.useGravity = false;
            var h = b.gameObject.AddComponent<HingeJoint>();
            h.axis = Vector3.up;
            h.useMotor = true;
            h.motor = new JointMotor { targetVelocity = 90f, force = 5f, freeSpin = false };
            yield return Steps("hinge motor", 120, new[] { b }, h);
        }
        {
            var b = Block("plate", new Vector3(0f, 5f, 0f), new Vector3(2f, 0.2f, 1f));
            b.useGravity = false;
            var h = b.gameObject.AddComponent<HingeJoint>();
            h.axis = Vector3.up;
            h.useMotor = true;
            h.motor = new JointMotor { targetVelocity = 360f, force = 50f, freeSpin = true };
            yield return Steps("hinge motor free spin", 60, new[] { b }, h, i => { if (i == 30) { var m = h.motor; m.targetVelocity = 0f; h.motor = m; } });
        }
        {
            var b = Block("plate", new Vector3(0f, 5f, 0f), new Vector3(2f, 0.2f, 1f));
            b.useGravity = false;
            var h = b.gameObject.AddComponent<HingeJoint>();
            h.axis = Vector3.up;
            h.useSpring = true;
            h.spring = new JointSpring { spring = 20f, damper = 0.5f, targetPosition = 60f };
            yield return Steps("hinge spring", 120, new[] { b }, h);
        }
        // A hinge between two bodies, anchors configured automatically.
        {
            var a = Block("door frame", new Vector3(0f, 2f, 0f), new Vector3(0.2f, 2f, 0.2f));
            a.isKinematic = true;
            var b = Block("door", new Vector3(0.6f, 2f, 0f), new Vector3(1f, 2f, 0.1f));
            var h = b.gameObject.AddComponent<HingeJoint>();
            h.connectedBody = a;
            h.anchor = new Vector3(-0.5f, 0f, 0f);
            h.axis = Vector3.up;
            b.AddForceAtPosition(new Vector3(0f, 0f, 20f), b.position + new Vector3(0.5f, 0f, 0f), ForceMode.Impulse);
            yield return Steps("hinge door", 120, new[] { b, a }, h);
        }
        {
            var a = Bob("upper", new Vector3(0f, 4f, 0f));
            var b = Bob("lower", new Vector3(1f, 4f, 0f));
            a.isKinematic = true;
            var h = b.gameObject.AddComponent<HingeJoint>();
            h.connectedBody = a;
            h.anchor = new Vector3(-2f, 0f, 0f);
            h.axis = Vector3.forward;
            yield return Steps("hinge chain", 120, new[] { b, a }, h);
        }

        // Fixed joints: holding, and breaking under a load.
        {
            var a = Block("base", new Vector3(0f, 3f, 0f), Vector3.one);
            a.isKinematic = true;
            var b = Block("arm", new Vector3(1.5f, 3f, 0f), new Vector3(2f, 0.3f, 0.3f));
            var f = b.gameObject.AddComponent<FixedJoint>();
            f.connectedBody = a;
            yield return Steps("fixed hold", 60, new[] { b, a }, f);
        }
        {
            var a = Block("base", new Vector3(0f, 3f, 0f), Vector3.one);
            a.isKinematic = true;
            var b = Block("arm", new Vector3(1.5f, 3f, 0f), new Vector3(2f, 0.3f, 0.3f));
            b.mass = 5f;
            var f = b.gameObject.AddComponent<FixedJoint>();
            f.connectedBody = a;
            f.breakForce = 40f;
            f.breakTorque = 1e9f;
            yield return Steps("fixed break", 60, new[] { b, a }, f, i => { if (i == 20) b.mass = 10f; });
        }

        // Spring joints.
        {
            var b = Bob("bob", new Vector3(0f, 3f, 0f));
            var s = b.gameObject.AddComponent<SpringJoint>();
            s.autoConfigureConnectedAnchor = false;
            s.connectedAnchor = new Vector3(0f, 5f, 0f);
            s.spring = 20f;
            s.damper = 0.5f;
            s.minDistance = 0f;
            s.maxDistance = 1f;
            yield return Steps("spring", 120, new[] { b }, s);
        }
        {
            var b = Bob("bob", new Vector3(2f, 4f, 0f));
            var s = b.gameObject.AddComponent<SpringJoint>();
            s.autoConfigureConnectedAnchor = false;
            s.connectedAnchor = new Vector3(0f, 5f, 0f);
            s.spring = 100f;
            s.damper = 2f;
            s.minDistance = 0.5f;
            s.maxDistance = 1.5f;
            s.tolerance = 0.05f;
            yield return Steps("spring range", 120, new[] { b }, s);
        }
        {
            var a = Bob("a", new Vector3(0f, 5f, 0f));
            a.useGravity = false;
            var b = Bob("b", new Vector3(3f, 5f, 0f));
            b.useGravity = false;
            var s = b.gameObject.AddComponent<SpringJoint>();
            s.connectedBody = a;
            s.spring = 50f;
            s.damper = 1f;
            s.maxDistance = 0f;
            b.linearVelocity = new Vector3(0f, 0f, 2f);
            yield return Steps("spring bodies", 120, new[] { b, a }, s);
        }

        // Character joints.
        {
            var a = Block("torso", new Vector3(0f, 4f, 0f), new Vector3(0.6f, 1f, 0.3f));
            a.isKinematic = true;
            var b = Block("arm", new Vector3(0.8f, 4.4f, 0f), new Vector3(1f, 0.25f, 0.25f));
            var c = b.gameObject.AddComponent<CharacterJoint>();
            c.connectedBody = a;
            c.anchor = new Vector3(-0.5f, 0f, 0f);
            c.axis = Vector3.right;
            c.swingAxis = Vector3.up;
            c.lowTwistLimit = new SoftJointLimit { limit = -20f };
            c.highTwistLimit = new SoftJointLimit { limit = 70f };
            c.swing1Limit = new SoftJointLimit { limit = 40f };
            c.swing2Limit = new SoftJointLimit { limit = 30f };
            b.AddTorque(new Vector3(5f, 0f, 2f), ForceMode.Impulse);
            yield return Steps("character arm", 120, new[] { b, a }, c);
        }

        // Configurable joints.
        {
            var b = Block("slider", new Vector3(0f, 5f, 0f), Vector3.one * 0.5f);
            var c = b.gameObject.AddComponent<ConfigurableJoint>();
            c.xMotion = ConfigurableJointMotion.Free;
            c.yMotion = ConfigurableJointMotion.Locked;
            c.zMotion = ConfigurableJointMotion.Locked;
            c.angularXMotion = c.angularYMotion = c.angularZMotion = ConfigurableJointMotion.Locked;
            c.axis = new Vector3(1f, 1f, 0f);
            yield return Steps("configurable slide", 120, new[] { b }, c);
        }
        {
            var b = Block("slider", new Vector3(0f, 5f, 0f), Vector3.one * 0.5f);
            var c = b.gameObject.AddComponent<ConfigurableJoint>();
            c.xMotion = c.yMotion = c.zMotion = ConfigurableJointMotion.Limited;
            c.angularXMotion = c.angularYMotion = c.angularZMotion = ConfigurableJointMotion.Locked;
            c.linearLimit = new SoftJointLimit { limit = 0.5f, bounciness = 0.5f };
            yield return Steps("configurable limited", 120, new[] { b }, c);
        }
        {
            var b = Block("slider", new Vector3(0f, 5f, 0f), Vector3.one * 0.5f);
            var c = b.gameObject.AddComponent<ConfigurableJoint>();
            c.xMotion = c.yMotion = c.zMotion = ConfigurableJointMotion.Limited;
            c.angularXMotion = c.angularYMotion = c.angularZMotion = ConfigurableJointMotion.Locked;
            c.linearLimit = new SoftJointLimit { limit = 0.5f };
            c.linearLimitSpring = new SoftJointLimitSpring { spring = 200f, damper = 5f };
            yield return Steps("configurable soft limit", 120, new[] { b }, c);
        }
        {
            var b = Block("driven", new Vector3(0f, 5f, 0f), Vector3.one * 0.5f);
            b.useGravity = false;
            var c = b.gameObject.AddComponent<ConfigurableJoint>();
            c.xMotion = c.yMotion = c.zMotion = ConfigurableJointMotion.Free;
            c.angularXMotion = c.angularYMotion = c.angularZMotion = ConfigurableJointMotion.Free;
            c.xDrive = new JointDrive { positionSpring = 50f, positionDamper = 2f, maximumForce = float.MaxValue };
            c.yDrive = new JointDrive { positionSpring = 20f, positionDamper = 1f, maximumForce = 5f };
            c.targetPosition = new Vector3(1f, 0.5f, 0f);
            c.rotationDriveMode = RotationDriveMode.Slerp;
            c.slerpDrive = new JointDrive { positionSpring = 30f, positionDamper = 1f, maximumForce = float.MaxValue };
            c.targetRotation = Quaternion.Euler(0f, 90f, 30f);
            yield return Steps("configurable drives", 120, new[] { b }, c);
        }
        {
            var b = Block("driven", new Vector3(0f, 5f, 0f), Vector3.one * 0.5f);
            b.useGravity = false;
            var c = b.gameObject.AddComponent<ConfigurableJoint>();
            c.xMotion = c.yMotion = c.zMotion = ConfigurableJointMotion.Locked;
            c.angularXMotion = c.angularYMotion = c.angularZMotion = ConfigurableJointMotion.Free;
            c.rotationDriveMode = RotationDriveMode.XYAndZ;
            c.angularXDrive = new JointDrive { positionSpring = 0f, positionDamper = 3f, maximumForce = float.MaxValue };
            c.angularYZDrive = new JointDrive { positionSpring = 40f, positionDamper = 1f, maximumForce = float.MaxValue };
            c.targetAngularVelocity = new Vector3(2f, 0f, 0f);
            c.targetRotation = Quaternion.Euler(0f, 45f, 0f);
            yield return Steps("configurable angular drives", 120, new[] { b }, c);
        }
        {
            var a = Block("a", new Vector3(0f, 3f, 0f), Vector3.one * 0.5f);
            a.isKinematic = true;
            var b = Block("b", new Vector3(0f, 3.4f, 0f), Vector3.one * 0.5f);
            var c = b.gameObject.AddComponent<ConfigurableJoint>();
            c.connectedBody = a;
            c.xMotion = c.zMotion = ConfigurableJointMotion.Locked;
            c.yMotion = ConfigurableJointMotion.Free;
            c.angularXMotion = c.angularYMotion = c.angularZMotion = ConfigurableJointMotion.Locked;
            c.enableCollision = true;
            yield return Steps("configurable collision", 60, new[] { b, a }, c);
        }
        {
            var a = Block("a", new Vector3(0f, 3f, 0f), Vector3.one * 0.5f);
            a.isKinematic = true;
            var b = Block("b", new Vector3(0f, 3.4f, 0f), Vector3.one * 0.5f);
            var c = b.gameObject.AddComponent<ConfigurableJoint>();
            c.connectedBody = a;
            c.xMotion = c.zMotion = ConfigurableJointMotion.Locked;
            c.yMotion = ConfigurableJointMotion.Free;
            c.angularXMotion = c.angularYMotion = c.angularZMotion = ConfigurableJointMotion.Locked;
            c.enableCollision = false;
            yield return Steps("configurable no collision", 60, new[] { b, a }, c);
        }
        {
            var a = Bob("heavy", new Vector3(0f, 5f, 0f));
            a.useGravity = false;
            var b = Bob("light", new Vector3(1f, 5f, 0f));
            b.useGravity = false;
            b.mass = 0.1f;
            var h = b.gameObject.AddComponent<HingeJoint>();
            h.connectedBody = a;
            h.anchor = new Vector3(-2f, 0f, 0f);
            h.axis = Vector3.up;
            h.massScale = 5f;
            h.connectedMassScale = 0.5f;
            b.linearVelocity = new Vector3(0f, 0f, 3f);
            yield return Steps("hinge mass scale", 60, new[] { b, a }, h);
        }

        File.WriteAllText(Path.Combine(dir, "joints.jsonl"), s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
