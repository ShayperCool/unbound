using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $JOINTPROJECTION_OUT (tests/value_probe.py rows): chains of bodies whose
/// joints are strained (a heavy last link under a kinematic anchor, swung from the horizontal),
/// with ConfigurableJoint projection and CharacterJoint projection off and on, and a
/// ConfigurableJoint with configuredInWorldSpace and swapBodies on and off. Every fifth physics
/// step for 2 s, each body's position and rotation.
/// </summary>
public class JointProjectionProbe : MonoBehaviour
{
    const string Variable = "JOINTPROJECTION_OUT";
    const int Steps = 100;
    static StreamWriter output;

    readonly List<(string name, Rigidbody[] bodies)> m_Scenarios = new List<(string, Rigidbody[])>();
    int m_Step;

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void Row(string key, string json) => output.Write("{\"k\":\"" + key + "\",\"v\":" + json + "}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("JointProjectionProbe").AddComponent<JointProjectionProbe>();
    }

    static Rigidbody Body(string name, Vector3 at, float mass, bool kinematic = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = at;
        go.transform.localScale = new Vector3(0.6f, 0.2f, 0.2f);
        var body = go.AddComponent<Rigidbody>();
        body.mass = mass;
        body.isKinematic = kinematic;
        return body;
    }

    /// <summary>A kinematic anchor and four links along +x, 1 m apart, the last 200 times as
    /// heavy; each link jointed to the one before at its near end.</summary>
    Rigidbody[] Chain(string name, Vector3 origin, Func<GameObject, Joint> join)
    {
        var bodies = new Rigidbody[5];
        bodies[0] = Body(name + "/anchor", origin, 1f, true);
        for (int i = 1; i < 5; i++)
        {
            bodies[i] = Body(name + "/link" + i, origin + new Vector3(i, 0f, 0f), i == 4 ? 200f : 1f);
            var joint = join(bodies[i].gameObject);
            joint.connectedBody = bodies[i - 1];
            joint.anchor = new Vector3(-0.5f, 0f, 0f);
        }
        m_Scenarios.Add((name, bodies));
        return bodies;
    }

    static ConfigurableJoint Hinge(GameObject go)
    {
        var j = go.AddComponent<ConfigurableJoint>();
        j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
        j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;
        j.angularXMotion = ConfigurableJointMotion.Free;
        j.axis = Vector3.forward;
        return j;
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        float z = 0f;
        foreach (var (name, mode, distance, angle) in new (string, JointProjectionMode, float, float)[]
                 {
                     ("configurable/none", JointProjectionMode.None, 0.1f, 180f),
                     ("configurable/projected", JointProjectionMode.PositionAndRotation, 0.1f, 180f),
                     ("configurable/tight", JointProjectionMode.PositionAndRotation, 0.01f, 5f),
                 })
        {
            Chain(name, new Vector3(0f, 10f, z), go =>
            {
                var j = Hinge(go);
                j.projectionMode = mode;
                j.projectionDistance = distance;
                j.projectionAngle = angle;
                return j;
            });
            z += 5f;
        }
        foreach (var (name, enabled, distance, angle) in new (string, bool, float, float)[]
                 {
                     ("character/none", false, 0.1f, 180f),
                     ("character/projected", true, 0.1f, 180f),
                     ("character/tight", true, 0.01f, 5f),
                 })
        {
            Chain(name, new Vector3(0f, 10f, z), go =>
            {
                var j = go.AddComponent<CharacterJoint>();
                j.axis = Vector3.forward;
                j.swingAxis = Vector3.up;
                j.enableProjection = enabled;
                j.projectionDistance = distance;
                j.projectionAngle = angle;
                return j;
            });
            z += 5f;
        }
        // A configurable joint whose axis and limits are given in world space or in the body's own,
        // on a body turned 90 degrees about y; and one with its bodies swapped.
        foreach (var (name, world, swap) in new[] { ("frame/local", false, false), ("frame/world", true, false), ("frame/swapped", false, true) })
        {
            var anchor = Body(name + "/anchor", new Vector3(0f, 10f, z), 1f, true);
            var body = Body(name + "/body", new Vector3(1f, 10f, z), 2f);
            body.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var j = body.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = anchor;
            j.anchor = new Vector3(0f, 0f, 0f);
            j.axis = Vector3.right;
            j.secondaryAxis = Vector3.up;
            j.xMotion = ConfigurableJointMotion.Limited;
            j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = ConfigurableJointMotion.Free;
            j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;
            j.linearLimit = new SoftJointLimit { limit = 0.3f };
            j.configuredInWorldSpace = world;
            j.swapBodies = swap;
            body.linearVelocity = new Vector3(2f, 1f, 0.5f);
            body.angularVelocity = new Vector3(0f, 0f, 3f);
            m_Scenarios.Add((name, new[] { anchor, body }));
            z += 5f;
        }
        Later(z);
    }

    /// <summary>Controls added after the first captures, placed beyond the others: projection by
    /// distance alone and by angle alone; world-space frames with an axis along z and with a turned
    /// connected body; drives toward a target position and rotation in world and local space; a
    /// joint between two dynamic bodies, swapped and not.</summary>
    void Later(float z)
    {
        foreach (var (name, distance, angle) in new[] { ("configurable/distanceonly", 0.01f, 180f), ("configurable/angleonly", 1000f, 1f) })
        {
            Chain(name, new Vector3(0f, 10f, z), go =>
            {
                var j = Hinge(go);
                j.projectionMode = JointProjectionMode.PositionAndRotation;
                j.projectionDistance = distance;
                j.projectionAngle = angle;
                return j;
            });
            z += 5f;
        }
        foreach (var (name, world, turnedAnchor, drive) in new (string, bool, bool, int)[]
                 {
                     ("frame/worldaxis", true, false, 0), ("frame/localaxis", false, false, 0),
                     ("frame/worldturnedanchor", true, true, 0), ("frame/localturnedanchor", false, true, 0),
                     ("frame/worlddrive", true, false, 1), ("frame/localdrive", false, false, 1),
                     ("frame/worldrotdrive", true, false, 2), ("frame/localrotdrive", false, false, 2),
                 })
        {
            var anchor = Body(name + "/anchor", new Vector3(0f, 10f, z), 1f, true);
            if (turnedAnchor) anchor.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var body = Body(name + "/body", new Vector3(1f, 10f, z), 2f);
            if (!turnedAnchor) body.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            body.useGravity = drive == 0;
            var j = body.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = anchor;
            j.anchor = Vector3.zero;
            j.axis = turnedAnchor ? Vector3.right : Vector3.forward;
            j.secondaryAxis = Vector3.up;
            j.xMotion = drive == 1 ? ConfigurableJointMotion.Free : drive == 2 ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
            j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = drive == 2 ? ConfigurableJointMotion.Free : drive == 1 ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Free;
            j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;
            j.linearLimit = new SoftJointLimit { limit = 0.3f };
            if (drive == 1)
            {
                j.xDrive = new JointDrive { positionSpring = 100f, positionDamper = 10f, maximumForce = float.MaxValue };
                j.targetPosition = new Vector3(1f, 0f, 0f);
            }
            if (drive == 2)
            {
                j.angularXDrive = new JointDrive { positionSpring = 50f, positionDamper = 5f, maximumForce = float.MaxValue };
                j.targetRotation = Quaternion.Euler(30f, 0f, 0f);
            }
            j.configuredInWorldSpace = world;
            if (drive == 0)
            {
                body.linearVelocity = new Vector3(2f, 1f, 0.5f);
                body.angularVelocity = new Vector3(0f, 0f, 3f);
            }
            m_Scenarios.Add((name, new[] { anchor, body }));
            z += 5f;
        }
        foreach (var (name, swap) in new[] { ("frame/pair", false), ("frame/pairswapped", true) })
        {
            var first = Body(name + "/first", new Vector3(0f, 10f, z), 1f);
            var second = Body(name + "/second", new Vector3(1f, 10f, z), 3f);
            var j = second.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = first;
            j.anchor = new Vector3(-0.5f, 0f, 0f);
            j.axis = Vector3.forward;
            j.xMotion = ConfigurableJointMotion.Limited;
            j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = ConfigurableJointMotion.Free;
            j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;
            j.linearLimit = new SoftJointLimit { limit = 0.2f };
            j.xDrive = new JointDrive { positionSpring = 20f, positionDamper = 1f, maximumForce = float.MaxValue };
            j.targetPosition = new Vector3(0.1f, 0f, 0f);
            j.swapBodies = swap;
            first.linearVelocity = new Vector3(0f, 2f, 1f);
            second.angularVelocity = new Vector3(0f, 0f, 2f);
            m_Scenarios.Add((name, new Rigidbody[] { first, second }));
            z += 5f;
        }
    }

    void FixedUpdate()
    {
        if (output == null) return;
        if (m_Step % 5 == 0)
        {
            foreach (var (name, bodies) in m_Scenarios)
            {
                var v = new List<string>();
                foreach (var b in bodies)
                {
                    var p = b.position;
                    var q = b.rotation;
                    v.Add(F(p.x)); v.Add(F(p.y)); v.Add(F(p.z));
                    v.Add(F(q.x)); v.Add(F(q.y)); v.Add(F(q.z)); v.Add(F(q.w));
                }
                Row(name + "." + m_Step, "[" + string.Join(",", v) + "]");
            }
        }
        m_Step++;
        if (m_Step <= Steps) return;
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
        output = null;
        EditorApplication.Exit(0);
    }
}
