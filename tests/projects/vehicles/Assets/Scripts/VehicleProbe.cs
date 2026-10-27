using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// How WheelColliders drive a body: the WheelCollider's defaults and a serialized one, then for
/// each scenario a body with one or four wheels on a flat ground, simulated in fixed steps; after
/// each step the body's pose and velocities and each wheel's state (grounded, rpm, sprung mass,
/// world pose, ground hit) are recorded. Writes <c>vehicles.jsonl</c> into
/// <c>VEHICLES_PROBE_OUT</c>, then quits.
/// </summary>
public class VehicleProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("VehicleProbe").AddComponent<VehicleProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string Q(Quaternion q) => $"[{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}]";
    static string Curve(WheelFrictionCurve c) =>
        $"[{F(c.extremumSlip)},{F(c.extremumValue)},{F(c.asymptoteSlip)},{F(c.asymptoteValue)},{F(c.stiffness)}]";
    void Record(string kind, string body) => m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    static string Properties(WheelCollider w)
    {
        var s = w.suspensionSpring;
        return $"\"center\":{V(w.center)},\"radius\":{F(w.radius)},\"suspensionDistance\":{F(w.suspensionDistance)}," +
            $"\"spring\":[{F(s.spring)},{F(s.damper)},{F(s.targetPosition)}],\"expansionLimited\":{(w.suspensionExpansionLimited ? "true" : "false")}," +
            $"\"forceAppPointDistance\":{F(w.forceAppPointDistance)},\"mass\":{F(w.mass)},\"wheelDampingRate\":{F(w.wheelDampingRate)}," +
            $"\"forward\":{Curve(w.forwardFriction)},\"sideways\":{Curve(w.sidewaysFriction)},\"motorTorque\":{F(w.motorTorque)}," +
            $"\"brakeTorque\":{F(w.brakeTorque)},\"steerAngle\":{F(w.steerAngle)},\"enabled\":{(w.enabled ? "true" : "false")}";
    }

    static string State(WheelCollider w)
    {
        w.GetWorldPose(out var position, out var rotation);
        bool hit = w.GetGroundHit(out var h);
        string ground = hit
            ? $"{{\"collider\":\"{h.collider?.name}\",\"point\":{V(h.point)},\"normal\":{V(h.normal)},\"force\":{F(h.force)},\"forwardDir\":{V(h.forwardDir)},\"sidewaysDir\":{V(h.sidewaysDir)},\"forwardSlip\":{F(h.forwardSlip)},\"sidewaysSlip\":{F(h.sidewaysSlip)}}}"
            : "null";
        return $"{{\"grounded\":{(w.isGrounded ? "true" : "false")},\"rpm\":{F(w.rpm)},\"rotationSpeed\":{F(w.rotationSpeed)},\"sprungMass\":{F(w.sprungMass)}," +
            $"\"pose\":[{V(position)},{Q(rotation)}],\"hit\":{ground}}}";
    }

    static string Body(Rigidbody rb) =>
        $"[{V(rb.position)},{Q(rb.rotation)},{V(rb.linearVelocity)},{V(rb.angularVelocity)}]";

    /// <summary>A scenario: a body, its wheels (local positions), what happens to them, and for
    /// how many fixed steps.</summary>
    class Scenario
    {
        public string Name;
        public Vector3[] Wheels = { Vector3.zero };
        public float Mass = 100f;
        public Vector3 CenterOfMass = Vector3.zero;
        public Vector3 Inertia = new Vector3(50f, 50f, 50f);
        public Vector3 Start = new Vector3(0f, 1f, 0f);
        public Vector3 Velocity = Vector3.zero;
        public bool FreezeRotation = true;
        public bool Gravity = true;
        public int Steps = 100;
        public float Radius = 0.5f, SuspensionDistance = 0.3f;
        public JointSpring? Spring;
        public bool ExpansionLimited;
        public float ForceAppPointDistance;
        public float? WheelMass, WheelDamping;
        public WheelFrictionCurve? Forward, Sideways;
        public float[] Motor, Brake, Steer;
        /// <summary>Called before each step with the step's index.</summary>
        public System.Action<int, WheelCollider[]> Drive;
        public System.Action<Rigidbody, WheelCollider[]> Setup;
        public PhysicsMaterial GroundMaterial;
        public float GroundTilt;
    }

    static float[] All(int n, float v) => Enumerable.Repeat(v, n).ToArray();

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("VEHICLES_PROBE_OUT") ?? "/tmp/vehicles";
        Directory.CreateDirectory(dir);
        Physics.simulationMode = SimulationMode.FixedUpdate;
        yield return new WaitForFixedUpdate();

        // Defaults: a wheel on a body with no other collider (the body's automatic mass
        // properties), before any step and after one.
        {
            var go = new GameObject("defaults");
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            var w = new GameObject("wheel");
            w.transform.SetParent(go.transform, false);
            var wheel = w.AddComponent<WheelCollider>();
            Record("defaults", Properties(wheel) + $",\"state\":{State(wheel)},\"body\":[{F(rb.mass)},{V(rb.centerOfMass)},{V(rb.inertiaTensor)}],\"bounds\":[{V(wheel.bounds.center)},{V(wheel.bounds.size)}]");
            yield return new WaitForFixedUpdate();
            Record("defaults stepped", $"\"state\":{State(wheel)},\"body\":[{F(rb.mass)},{V(rb.centerOfMass)},{V(rb.inertiaTensor)}],\"bounds\":[{V(wheel.bounds.center)},{V(wheel.bounds.size)}]");
            Destroy(go);
            yield return new WaitForFixedUpdate();
        }

        // A wheel the scene serialized with values other than the defaults.
        {
            var saved = GameObject.Find("SavedCar");
            var wheel = saved != null ? saved.GetComponentInChildren<WheelCollider>() : null;
            Record("serialized", wheel != null ? Properties(wheel) : "\"missing\":true");
            if (saved != null) Destroy(saved);
            yield return new WaitForFixedUpdate();
        }

        var car = new[] { new Vector3(-0.8f, -0.3f, 1.3f), new Vector3(0.8f, -0.3f, 1.3f), new Vector3(-0.8f, -0.3f, -1.3f), new Vector3(0.8f, -0.3f, -1.3f) };
        Scenario Car(string name) => new Scenario
        {
            Name = name, Wheels = car, Mass = 1000f, Inertia = new Vector3(700f, 800f, 250f), Start = new Vector3(0f, 0.7f, 0f),
            FreezeRotation = false, Steps = 150, Radius = 0.35f, SuspensionDistance = 0.25f,
        };
        var ice = new PhysicsMaterial("ice") { dynamicFriction = 0.05f, staticFriction = 0.05f };

        var scenarios = new List<Scenario>
        {
            new Scenario { Name = "single drop" },
            new Scenario { Name = "single rest", Start = new Vector3(0f, 0.75f, 0f), Steps = 50 },
            new Scenario { Name = "single stiff", Spring = new JointSpring { spring = 80000f, damper = 1000f, targetPosition = 0.2f } },
            new Scenario { Name = "single soft", Spring = new JointSpring { spring = 5000f, damper = 100f, targetPosition = 0.8f } },
            new Scenario { Name = "single expansion limited", ExpansionLimited = true },
            new Scenario { Name = "single wheel mass", WheelMass = 100f, Start = new Vector3(0f, 0.75f, 0f) },
            new Scenario { Name = "single force point", ForceAppPointDistance = 0.3f, Start = new Vector3(0f, 0.75f, 0f), Steps = 50 },
            new Scenario { Name = "single motor", Start = new Vector3(0f, 0.75f, 0f), Motor = new[] { 100f } },
            new Scenario { Name = "single motor strong", Start = new Vector3(0f, 0.75f, 0f), Motor = new[] { 1000f } },
            new Scenario { Name = "single roll", Start = new Vector3(0f, 0.75f, 0f), Velocity = new Vector3(0f, 0f, 5f) },
            new Scenario { Name = "single brake", Start = new Vector3(0f, 0.75f, 0f), Velocity = new Vector3(0f, 0f, 5f), Brake = new[] { 300f } },
            new Scenario { Name = "single sideways", Start = new Vector3(0f, 0.75f, 0f), Velocity = new Vector3(3f, 0f, 0f) },
            new Scenario { Name = "single steer", Start = new Vector3(0f, 0.75f, 0f), Velocity = new Vector3(0f, 0f, 5f), Steer = new[] { 30f } },
            new Scenario { Name = "single steer stiffness", Start = new Vector3(0f, 0.75f, 0f), Velocity = new Vector3(0f, 0f, 5f), Steer = new[] { 30f },
                Sideways = new WheelFrictionCurve { extremumSlip = 0.2f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.75f, stiffness = 0.5f } },
            new Scenario { Name = "single forward curve", Start = new Vector3(0f, 0.75f, 0f), Motor = new[] { 1000f },
                Forward = new WheelFrictionCurve { extremumSlip = 1f, extremumValue = 2f, asymptoteSlip = 2f, asymptoteValue = 1f, stiffness = 1f } },
            new Scenario { Name = "single ice", Start = new Vector3(0f, 0.75f, 0f), Velocity = new Vector3(0f, 0f, 5f), Steer = new[] { 30f }, GroundMaterial = ice },
            new Scenario { Name = "air motor", Gravity = false, Start = new Vector3(0f, 5f, 0f), Steps = 50,
                Drive = (i, w) => { w[0].motorTorque = i < 25 ? 50f : 0f; } },
            new Scenario { Name = "air brake", Gravity = false, Start = new Vector3(0f, 5f, 0f), Steps = 50,
                Drive = (i, w) => { w[0].motorTorque = i < 25 ? 50f : 0f; w[0].brakeTorque = i < 25 ? 0f : 20f; } },
            new Scenario { Name = "air damping", Gravity = false, Start = new Vector3(0f, 5f, 0f), Steps = 50, WheelDamping = 5f,
                Drive = (i, w) => { w[0].motorTorque = i < 25 ? 50f : 0f; } },
            Car("car settle"),
            new System.Func<Scenario>(() => { var s = Car("car motor"); s.Motor = new[] { 0f, 0f, 300f, 300f }; return s; })(),
            new System.Func<Scenario>(() => { var s = Car("car brake"); s.Velocity = new Vector3(0f, 0f, 10f); s.Brake = All(4, 1000f); return s; })(),
            new System.Func<Scenario>(() => { var s = Car("car steer"); s.Velocity = new Vector3(0f, 0f, 10f); s.Steer = new[] { 15f, 15f, 0f, 0f }; return s; })(),
            new System.Func<Scenario>(() => { var s = Car("car steer force point"); s.Velocity = new Vector3(0f, 0f, 10f); s.Steer = new[] { 15f, 15f, 0f, 0f }; s.ForceAppPointDistance = 0.2f; return s; })(),
            new System.Func<Scenario>(() => { var s = Car("car steer substeps"); s.Velocity = new Vector3(0f, 0f, 10f); s.Steer = new[] { 15f, 15f, 0f, 0f };
                s.Setup = (rb, w) => w[0].ConfigureVehicleSubsteps(5f, 12, 15); return s; })(),
            new System.Func<Scenario>(() => { var s = Car("car slope"); s.GroundTilt = 10f; return s; })(),
            new System.Func<Scenario>(() => { var s = Car("car slope braked"); s.GroundTilt = 6f; s.Start = new Vector3(0f, 0.75f, 0f); s.Brake = All(4, 2000f); return s; })(),
            new System.Func<Scenario>(() => { var s = Car("car drive and turn"); s.Velocity = new Vector3(0f, 0f, 3f); s.Steps = 200;
                s.Drive = (i, w) => { w[2].motorTorque = w[3].motorTorque = 150f; w[0].steerAngle = w[1].steerAngle = i < 60 ? 0f : -20f; }; return s; })(),
            new System.Func<Scenario>(() => { var s = Car("car fast turn"); s.Velocity = new Vector3(0f, 0f, 15f); s.Steps = 100;
                s.Drive = (i, w) => { w[0].steerAngle = w[1].steerAngle = 10f; }; return s; })(),
            new System.Func<Scenario>(() => { var s = Car("car handbrake turn"); s.Velocity = new Vector3(0f, 0f, 8f); s.Steps = 120;
                s.Drive = (i, w) => { w[0].steerAngle = w[1].steerAngle = 25f; w[2].brakeTorque = w[3].brakeTorque = i < 40 ? 3000f : 0f; }; return s; })(),
            new System.Func<Scenario>(() => { var s = Car("car center of mass"); s.CenterOfMass = new Vector3(0f, -0.2f, 0.6f); return s; })(),
            new System.Func<Scenario>(() => { var s = Car("car sprung reset"); s.Steps = 30;
                s.Setup = (rb, w) => { rb.centerOfMass = new Vector3(0.3f, 0f, -0.5f); w[0].ResetSprungMasses(); }; return s; })(),
        };

        foreach (var s in scenarios)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "ground";
            ground.transform.localScale = new Vector3(400f, 1f, 400f);
            ground.transform.rotation = Quaternion.Euler(s.GroundTilt, 0f, 0f);
            ground.transform.position = ground.transform.rotation * new Vector3(0f, -0.5f, 0f);
            if (s.GroundMaterial != null) ground.GetComponent<Collider>().sharedMaterial = s.GroundMaterial;

            var go = new GameObject("body");
            go.transform.position = s.Start;
            var rb = go.AddComponent<Rigidbody>();
            var wheels = new WheelCollider[s.Wheels.Length];
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = new GameObject("wheel" + i);
                w.transform.SetParent(go.transform, false);
                w.transform.localPosition = s.Wheels[i];
                var wheel = wheels[i] = w.AddComponent<WheelCollider>();
                wheel.radius = s.Radius;
                wheel.suspensionDistance = s.SuspensionDistance;
                if (s.Spring.HasValue) wheel.suspensionSpring = s.Spring.Value;
                wheel.suspensionExpansionLimited = s.ExpansionLimited;
                wheel.forceAppPointDistance = s.ForceAppPointDistance;
                if (s.WheelMass.HasValue) wheel.mass = s.WheelMass.Value;
                if (s.WheelDamping.HasValue) wheel.wheelDampingRate = s.WheelDamping.Value;
                if (s.Forward.HasValue) wheel.forwardFriction = s.Forward.Value;
                if (s.Sideways.HasValue) wheel.sidewaysFriction = s.Sideways.Value;
            }
            // Inputs after all wheels exist: adding a wheel clears the others' (measured).
            for (int i = 0; i < wheels.Length; i++)
            {
                if (s.Motor != null) wheels[i].motorTorque = s.Motor[i];
                if (s.Brake != null) wheels[i].brakeTorque = s.Brake[i];
                if (s.Steer != null) wheels[i].steerAngle = s.Steer[i];
            }
            rb.mass = s.Mass;
            rb.centerOfMass = s.CenterOfMass;
            rb.inertiaTensor = s.Inertia;
            rb.inertiaTensorRotation = Quaternion.identity;
            rb.useGravity = s.Gravity;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            if (s.FreezeRotation) rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.linearVelocity = s.Velocity;
            s.Setup?.Invoke(rb, wheels);

            var steps = new List<string>();
            steps.Add($"[{Body(rb)},[{string.Join(",", wheels.Select(State))}]]");
            for (int step = 0; step < s.Steps; step++)
            {
                s.Drive?.Invoke(step, wheels);
                yield return new WaitForFixedUpdate();
                steps.Add($"[{Body(rb)},[{string.Join(",", wheels.Select(State))}]]");
            }
            Record("scenario", $"\"name\":\"{s.Name}\",\"steps\":[{string.Join(",", steps)}]");
            Destroy(go);
            Destroy(ground);
            yield return new WaitForFixedUpdate();
        }

        // Inputs: what happens to a wheel's motor, brake and steer when the vehicle changes, and
        // other set-ups (rest load, turned wheels, centers, dynamic and trigger ground).
        {
            GameObject Ground()
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = "ground";
                g.transform.localScale = new Vector3(400f, 1f, 400f);
                g.transform.position = new Vector3(0f, -0.5f, 0f);
                return g;
            }
            Rigidbody MakeBody(Vector3 start)
            {
                var go = new GameObject("body");
                go.transform.position = start;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 100f;
                rb.centerOfMass = Vector3.zero;
                rb.inertiaTensor = new Vector3(50f, 50f, 50f);
                rb.inertiaTensorRotation = Quaternion.identity;
                rb.linearDamping = 0f;
                rb.angularDamping = 0f;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
                return rb;
            }
            WheelCollider Wheel(Rigidbody rb, string name, Vector3 local)
            {
                var w = new GameObject(name);
                w.transform.SetParent(rb.transform, false);
                w.transform.localPosition = local;
                return w.AddComponent<WheelCollider>();
            }
            string Inputs(WheelCollider w) => $"[{F(w.motorTorque)},{F(w.brakeTorque)},{F(w.steerAngle)}]";
            IEnumerator Steps(string name, Rigidbody rb, WheelCollider[] wheels, int count, string before)
            {
                var steps = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    yield return new WaitForFixedUpdate();
                    steps.Add($"[{Body(rb)},[{string.Join(",", wheels.Select(State))}]]");
                }
                Record("extra", $"\"name\":\"{name}\",\"before\":{before},\"inputs\":[{string.Join(",", wheels.Select(Inputs))}],\"steps\":[{string.Join(",", steps)}]");
            }
            var left = new Vector3(-0.8f, 0f, 0f);
            var right = new Vector3(0.8f, 0f, 0f);

            // A wheel's inputs, then another wheel added.
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", left);
                a.motorTorque = 300f; a.steerAngle = 15f;
                var b = Wheel(rb, "b", right);
                string before = $"[{Inputs(a)},{Inputs(b)}]";
                yield return Steps("added after inputs", rb, new[] { a, b }, 10, before);
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // Inputs set after a step, then another wheel's radius changed.
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", left);
                var b = Wheel(rb, "b", right);
                yield return new WaitForFixedUpdate();
                a.motorTorque = 300f; a.steerAngle = 15f;
                b.radius = 0.45f;
                yield return Steps("other radius changed", rb, new[] { a, b }, 10, $"[{Inputs(a)},{Inputs(b)}]");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // Inputs, then its own radius changed.
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", left);
                var b = Wheel(rb, "b", right);
                yield return new WaitForFixedUpdate();
                a.motorTorque = 300f; a.steerAngle = 15f;
                a.radius = 0.45f;
                yield return Steps("own radius changed", rb, new[] { a, b }, 10, $"[{Inputs(a)},{Inputs(b)}]");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // Inputs, then the body's mass changed.
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", left);
                var b = Wheel(rb, "b", right);
                yield return new WaitForFixedUpdate();
                a.motorTorque = 300f; a.steerAngle = 15f;
                rb.mass = 150f;
                yield return Steps("mass changed", rb, new[] { a, b }, 10, $"[{Inputs(a)},{Inputs(b)}]");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // Inputs, then another wheel disabled.
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", left);
                var b = Wheel(rb, "b", right);
                yield return new WaitForFixedUpdate();
                a.motorTorque = 300f; a.steerAngle = 15f;
                b.enabled = false;
                yield return Steps("other disabled", rb, new[] { a, b }, 10, $"[{Inputs(a)},{Inputs(b)}]");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // A third wheel added after a step with inputs set.
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", left);
                var b = Wheel(rb, "b", right);
                yield return new WaitForFixedUpdate();
                a.motorTorque = 300f; b.brakeTorque = 50f;
                var c = Wheel(rb, "c", new Vector3(0f, 0f, 1.5f));
                yield return Steps("added after a step", rb, new[] { a, b, c }, 10, $"[{Inputs(a)},{Inputs(b)},{Inputs(c)}]");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // Rest load: a heavy wheel rolling.
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", Vector3.zero);
                a.mass = 100f;
                rb.linearVelocity = new Vector3(0f, 0f, 5f);
                yield return Steps("heavy wheel roll", rb, new[] { a }, 40, "null");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // Rest load: a light body rolling.
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                rb.mass = 30f;
                var a = Wheel(rb, "a", Vector3.zero);
                rb.linearVelocity = new Vector3(0f, 0f, 5f);
                yield return Steps("light body roll", rb, new[] { a }, 40, "null");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // A wheel turned about its up axis, the body sliding along x, and a turned body.
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", Vector3.zero);
                a.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                rb.linearVelocity = new Vector3(4f, 0f, 1f);
                yield return Steps("turned wheel", rb, new[] { a }, 30, "null");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                rb.transform.rotation = Quaternion.Euler(0f, 60f, 0f);
                var a = Wheel(rb, "a", Vector3.zero);
                a.motorTorque = 200f;
                rb.linearVelocity = new Vector3(1f, 0f, 3f);
                yield return Steps("turned body", rb, new[] { a }, 30, "null");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // A wheel tilted about its forward axis (suspension not vertical).
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", Vector3.zero);
                a.transform.localRotation = Quaternion.Euler(0f, 0f, 20f);
                yield return Steps("tilted wheel", rb, new[] { a }, 30, "null");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // Center offset, a scaled wheel object, and a wheel below the body's collider.
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", new Vector3(0.3f, 0.1f, -0.2f));
                a.center = new Vector3(0.1f, -0.2f, 0.4f);
                var box = rb.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.4f, 0.2f, 0.4f);
                rb.mass = 100f; rb.centerOfMass = Vector3.zero; rb.inertiaTensor = new Vector3(50f, 50f, 50f);
                yield return Steps("center", rb, new[] { a }, 30, "null");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 1.3f, 0f));
                var holder = new GameObject("scaled");
                holder.transform.SetParent(rb.transform, false);
                holder.transform.localScale = new Vector3(2f, 2f, 2f);
                var w = new GameObject("a");
                w.transform.SetParent(holder.transform, false);
                var a = w.AddComponent<WheelCollider>();
                yield return Steps("scaled", rb, new[] { a }, 30, "null");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // A dynamic box as ground, and a trigger above the ground.
            {
                var ground = Ground();
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "dynamic box";
                box.transform.localScale = new Vector3(2f, 0.5f, 2f);
                box.transform.position = new Vector3(0f, 0.25f, 0f);
                var boxBody = box.AddComponent<Rigidbody>();
                boxBody.mass = 50f;
                var rb = MakeBody(new Vector3(0f, 1.15f, 0f));
                var a = Wheel(rb, "a", Vector3.zero);
                var steps = new List<string>();
                for (int i = 0; i < 30; i++)
                {
                    yield return new WaitForFixedUpdate();
                    steps.Add($"[{Body(rb)},{State(a)},{Body(boxBody)}]");
                }
                Record("extra", $"\"name\":\"dynamic ground\",\"steps\":[{string.Join(",", steps)}]");
                Destroy(rb.gameObject); Destroy(box); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            {
                var ground = Ground();
                var trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
                trigger.name = "trigger";
                trigger.transform.localScale = new Vector3(4f, 0.2f, 4f);
                trigger.transform.position = new Vector3(0f, 0.2f, 0f);
                trigger.GetComponent<Collider>().isTrigger = true;
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", Vector3.zero);
                yield return Steps("trigger ground", rb, new[] { a }, 10, "null");
                Destroy(rb.gameObject); Destroy(trigger); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // Ground on a layer the wheel's layer doesn't collide with.
            {
                var ground = Ground();
                ground.layer = 8;
                Physics.IgnoreLayerCollision(8, 9, true);
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                var a = Wheel(rb, "a", Vector3.zero);
                a.gameObject.layer = 9;
                yield return Steps("ignored layer", rb, new[] { a }, 10, "null");
                Physics.IgnoreLayerCollision(8, 9, false);
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
            // Kinematic body.
            {
                var ground = Ground();
                var rb = MakeBody(new Vector3(0f, 0.65f, 0f));
                rb.isKinematic = true;
                var a = Wheel(rb, "a", Vector3.zero);
                a.motorTorque = 50f;
                yield return Steps("kinematic", rb, new[] { a }, 10, "null");
                Destroy(rb.gameObject); Destroy(ground);
                yield return new WaitForFixedUpdate();
            }
        }

        // Queries: does a ray or an overlap find a wheel?
        {
            var go = new GameObject("query body");
            go.transform.position = new Vector3(0f, 10f, 0f);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var w = new GameObject("query wheel");
            w.transform.SetParent(go.transform, false);
            w.AddComponent<WheelCollider>();
            yield return new WaitForFixedUpdate();
            bool ray = Physics.Raycast(new Vector3(0f, 20f, 0f), Vector3.down, out var hit, 100f);
            var overlap = Physics.OverlapSphere(new Vector3(0f, 10f, 0f), 1f);
            Record("queries", $"\"raycast\":{(ray ? $"\"{hit.collider.name}\"" : "null")},\"overlap\":[{string.Join(",", overlap.Select(c => $"\"{c.name}\""))}]");
            Destroy(go);
        }

        File.WriteAllText(Path.Combine(dir, "vehicles.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
