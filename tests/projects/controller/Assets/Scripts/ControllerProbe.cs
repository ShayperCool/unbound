using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// How CharacterController moves: its defaults, then scenarios on a ground with a wall, a ceiling,
/// ramps, steps, a trigger and a dynamic box, each a sequence of Move or SimpleMove calls (one per
/// fixed step); after each call the controller's position, isGrounded, collisionFlags and velocity
/// are recorded, with the OnControllerColliderHit and trigger messages of the call. Writes
/// <c>controller.jsonl</c> into <c>CONTROLLER_PROBE_OUT</c>, then quits.
/// </summary>
public class ControllerProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly List<string> s_Hits = new List<string>();
    static readonly List<string> s_Triggers = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("ControllerProbe").AddComponent<ControllerProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static void Record(string kind, string body) => s_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    /// <summary>Records the controller's hits and triggers.</summary>
    class Listener : MonoBehaviour
    {
        void OnControllerColliderHit(ControllerColliderHit hit) =>
            s_Hits.Add($"{{\"collider\":\"{hit.collider.name}\",\"point\":{V(hit.point)},\"normal\":{V(hit.normal)},\"moveDirection\":{V(hit.moveDirection)},\"moveLength\":{F(hit.moveLength)}}}");
        void OnTriggerEnter(Collider other) => s_Triggers.Add($"{{\"triggerEnter\":\"{other.name}\"}}");
        void OnTriggerExit(Collider other) => s_Triggers.Add($"{{\"triggerExit\":\"{other.name}\"}}");
    }

    static GameObject Box(string name, Vector3 position, Vector3 size, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(euler);
        go.transform.localScale = size;
        return go;
    }

    static CharacterController Controller(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var cc = go.AddComponent<CharacterController>();
        go.AddComponent<Listener>();
        return cc;
    }

    static string State(CharacterController cc, CollisionFlags flags) =>
        $"[{V(cc.transform.position)},{(cc.isGrounded ? "true" : "false")},{(int)flags},{(int)cc.collisionFlags},{V(cc.velocity)}]";

    /// <summary>Runs moves: each step's motion from the step index (Move) or a SimpleMove speed. Trigger
    /// messages come from the simulation step before the call.</summary>
    IEnumerator Moves(string name, CharacterController cc, int count, System.Func<int, Vector3> motion, bool simple = false, List<GameObject> extra = null)
    {
        var steps = new List<string>();
        for (int i = 0; i < count; i++)
        {
            s_Triggers.Clear();
            yield return new WaitForFixedUpdate();
            s_Hits.Clear();
            var m = motion(i);
            CollisionFlags flags = CollisionFlags.None;
            if (simple) flags = cc.SimpleMove(m) ? CollisionFlags.Below : CollisionFlags.None;
            else flags = cc.Move(m);
            steps.Add($"[{i},{State(cc, flags)},[{string.Join(",", s_Hits)}],[{string.Join(",", s_Triggers)}]]");
        }
        Record("scenario", $"\"name\":\"{name}\",\"steps\":[{string.Join(",", steps)}]");
        Destroy(cc.gameObject);
        if (extra != null) foreach (var e in extra) Destroy(e);
        yield return new WaitForFixedUpdate();
    }

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("CONTROLLER_PROBE_OUT") ?? "/tmp/controller";
        Directory.CreateDirectory(dir);
        yield return new WaitForFixedUpdate();

        {
            var cc = Controller("defaults", new Vector3(0f, 50f, 0f));
            Record("defaults", $"\"radius\":{F(cc.radius)},\"height\":{F(cc.height)},\"center\":{V(cc.center)},\"slopeLimit\":{F(cc.slopeLimit)},\"stepOffset\":{F(cc.stepOffset)},\"skinWidth\":{F(cc.skinWidth)},\"minMoveDistance\":{F(cc.minMoveDistance)},\"detectCollisions\":{(cc.detectCollisions ? "true" : "false")},\"enableOverlapRecovery\":{(cc.enableOverlapRecovery ? "true" : "false")},\"isGrounded\":{(cc.isGrounded ? "true" : "false")},\"velocity\":{V(cc.velocity)},\"bounds\":[{V(cc.bounds.center)},{V(cc.bounds.size)}]");
            Destroy(cc.gameObject);
            yield return new WaitForFixedUpdate();
        }

        var ground = Box("ground", new Vector3(0f, -0.5f, 0f), new Vector3(100f, 1f, 100f));
        Physics.SyncTransforms();

        // Walking on the flat ground, starting exactly on it and above it.
        yield return Moves("walk", Controller("cc", new Vector3(0f, 1f, 0f)), 20, i => new Vector3(0.1f, 0f, 0.05f));
        yield return Moves("walk with gravity", Controller("cc", new Vector3(0f, 1.2f, 0f)), 30, i => new Vector3(0.1f, -0.2f, 0f));
        {
            float vy = 0f;
            yield return Moves("fall", Controller("cc", new Vector3(0f, 3f, 0f)), 60, i => { vy -= 9.81f * 0.02f; return new Vector3(0f, vy * 0.02f, 0f); });
        }
        {
            var wall = Box("wall", new Vector3(2f, 1f, 0f), new Vector3(0.5f, 2f, 10f));
            Physics.SyncTransforms();
            yield return Moves("wall", Controller("cc", new Vector3(0f, 1.08f, 0f)), 30, i => new Vector3(0.15f, -0.05f, 0.05f), extra: new List<GameObject> { wall });
        }
        {
            var wall = Box("wall", new Vector3(2f, 1f, 0f), new Vector3(0.5f, 2f, 10f), new Vector3(0f, 30f, 0f));
            Physics.SyncTransforms();
            yield return Moves("wall slanted", Controller("cc", new Vector3(0f, 1.08f, 0f)), 30, i => new Vector3(0.15f, -0.05f, 0f), extra: new List<GameObject> { wall });
        }
        {
            var ramp = Box("ramp", new Vector3(3f, 0f, 0f), new Vector3(4f, 0.2f, 4f), new Vector3(0f, 0f, 30f));
            Physics.SyncTransforms();
            yield return Moves("ramp 30", Controller("cc", new Vector3(0f, 1.08f, 0f)), 50, i => new Vector3(0.1f, -0.1f, 0f), extra: new List<GameObject> { ramp });
        }
        {
            var ramp = Box("ramp", new Vector3(3f, 0f, 0f), new Vector3(4f, 0.2f, 4f), new Vector3(0f, 0f, 60f));
            Physics.SyncTransforms();
            yield return Moves("ramp 60", Controller("cc", new Vector3(0f, 1.08f, 0f)), 50, i => new Vector3(0.1f, -0.1f, 0f), extra: new List<GameObject> { ramp });
        }
        {
            var step = Box("step", new Vector3(2.03f, 0.1f, 0f), new Vector3(2f, 0.2f, 4f));
            Physics.SyncTransforms();
            yield return Moves("step low", Controller("cc", new Vector3(0f, 1.08f, 0f)), 30, i => new Vector3(0.1f, -0.1f, 0f), extra: new List<GameObject> { step });
        }
        {
            var step = Box("step", new Vector3(2.05f, 0.25f, 0f), new Vector3(2f, 0.5f, 4f));
            Physics.SyncTransforms();
            yield return Moves("step high", Controller("cc", new Vector3(0f, 1.08f, 0f)), 30, i => new Vector3(0.1f, -0.1f, 0f), extra: new List<GameObject> { step });
        }
        {
            var ceiling = Box("ceiling", new Vector3(0f, 3f, 0f), new Vector3(4f, 0.2f, 4f));
            Physics.SyncTransforms();
            float vy = 6f;
            yield return Moves("ceiling", Controller("cc", new Vector3(0f, 1.08f, 0f)), 40, i => { vy -= 9.81f * 0.02f; return new Vector3(0f, vy * 0.02f, 0f); }, extra: new List<GameObject> { ceiling });
        }
        yield return Moves("simple move", Controller("cc", new Vector3(0f, 2f, 0f)), 60, i => new Vector3(2f, 5f, 1f), simple: true);
        {
            // SimpleMove off a ledge, then with another speed in the air.
            var ledge = Box("ledge", new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f));
            Physics.SyncTransforms();
            yield return Moves("simple move ledge", Controller("cc", new Vector3(0f, 3.08f, 0f)), 50, i => i < 10 ? new Vector3(3f, 0f, 0f) : new Vector3(-1f, 0f, 2f), simple: true, extra: new List<GameObject> { ledge });
        }
        {
            // A Move between SimpleMoves: SimpleMove keeps its own vertical speed.
            var cc = Controller("cc", new Vector3(0f, 4f, 0f));
            yield return Moves("simple move mixed", cc, 30, i => { if (i == 5) cc.Move(new Vector3(0.3f, 0.5f, 0f)); return new Vector3(1f, 0f, 0f); }, simple: true);
        }
        {
            // A scaled controller: the radius follows the larger of x and z, the height y.
            var cc = Controller("cc", new Vector3(0f, 2f, 0f));
            cc.transform.localScale = new Vector3(0.5f, 1.5f, 2f);
            var wall = Box("wall", new Vector3(3f, 1f, 0f), new Vector3(0.5f, 2f, 10f));
            Physics.SyncTransforms();
            yield return Moves("scaled", cc, 30, i => new Vector3(0.1f, -0.1f, 0f), extra: new List<GameObject> { wall });
        }
        {
            var trigger = Box("trigger", new Vector3(1.5f, 1f, 0f), new Vector3(1f, 2f, 2f));
            trigger.GetComponent<Collider>().isTrigger = true;
            Physics.SyncTransforms();
            yield return Moves("trigger", Controller("cc", new Vector3(0f, 1.08f, 0f)), 30, i => new Vector3(0.12f, -0.05f, 0f), extra: new List<GameObject> { trigger });
        }
        {
            var box = Box("crate", new Vector3(2f, 0.5f, 0f), Vector3.one);
            box.AddComponent<Rigidbody>();
            Physics.SyncTransforms();
            yield return Moves("dynamic box", Controller("cc", new Vector3(0f, 1.08f, 0f)), 30, i => new Vector3(0.12f, -0.05f, 0f), extra: new List<GameObject> { box });
        }
        {
            var cc = Controller("small moves", new Vector3(0f, 1.08f, 0f));
            cc.minMoveDistance = 0.05f;
            yield return Moves("min move distance", cc, 20, i => new Vector3(i < 10 ? 0.02f : 0.08f, 0f, 0f));
        }
        {
            var cc = Controller("custom", new Vector3(0f, 1.5f, 0f));
            cc.radius = 0.3f;
            cc.height = 1.2f;
            cc.center = new Vector3(0f, 0.2f, 0f);
            cc.stepOffset = 0.5f;
            cc.slopeLimit = 70f;
            cc.skinWidth = 0.02f;
            var ramp = Box("ramp", new Vector3(3f, 0f, 0f), new Vector3(4f, 0.2f, 4f), new Vector3(0f, 0f, 60f));
            Physics.SyncTransforms();
            yield return Moves("custom shape", cc, 50, i => new Vector3(0.1f, -0.1f, 0f), extra: new List<GameObject> { ramp });
        }
        {
            // The transform moved without a sync: the next move starts from where?
            var cc = Controller("cc", new Vector3(0f, 1.08f, 0f));
            yield return Moves("teleport", cc, 10, i =>
            {
                if (i == 3) cc.transform.position = new Vector3(5f, 1.08f, 0f);
                if (i == 6) { cc.transform.position = new Vector3(-5f, 1.08f, 0f); Physics.SyncTransforms(); }
                return new Vector3(0.1f, -0.1f, 0f);
            });
        }
        {
            // Two controllers: one walks into the other.
            var other = Controller("other", new Vector3(2f, 1.08f, 0f));
            yield return Moves("controllers", Controller("cc", new Vector3(0f, 1.08f, 0f)), 20, i => new Vector3(0.1f, -0.1f, 0f), extra: new List<GameObject> { other.gameObject });
        }
        {
            // A wall the controller ignores.
            var wall = Box("wall", new Vector3(1.5f, 1f, 0f), new Vector3(0.5f, 2f, 10f));
            var cc = Controller("cc", new Vector3(0f, 1.08f, 0f));
            Physics.IgnoreCollision(cc, wall.GetComponent<Collider>());
            Physics.SyncTransforms();
            yield return Moves("ignored", cc, 20, i => new Vector3(0.15f, -0.1f, 0f), extra: new List<GameObject> { wall });
        }
        // Queries find the controller's capsule.
        {
            var cc = Controller("queried", new Vector3(5f, 1.08f, 5f));
            yield return new WaitForFixedUpdate();
            bool ray = Physics.Raycast(new Vector3(5f, 10f, 5f), Vector3.down, out var hit, 20f);
            Record("queries", $"\"raycast\":{(ray ? $"[\"{hit.collider.name}\",{V(hit.point)},{F(hit.distance)}]" : "null")},\"overlap\":[{string.Join(",", Physics.OverlapSphere(new Vector3(5f, 1f, 5f), 0.2f).Select(c => $"\"{c.name}\""))}],\"type\":\"{(Physics.OverlapSphere(new Vector3(5f, 1f, 5f), 0.2f).FirstOrDefault(c => c.name == "queried")?.GetType().Name)}\"");
            Destroy(cc.gameObject);
        }

        Destroy(ground);
        File.WriteAllText(Path.Combine(dir, "controller.jsonl"), s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
