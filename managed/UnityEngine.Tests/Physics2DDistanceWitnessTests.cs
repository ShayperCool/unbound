using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace UnityEngine.Tests
{
    public class Physics2DDistanceWitnessTests
    {
        readonly ITestOutputHelper output;
        public Physics2DDistanceWitnessTests(ITestOutputHelper output) { this.output = output; }

        static string Reference([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            Path.Combine(Path.GetDirectoryName(source), "..", "..", "tests", "projects", "solverpairorder",
                "Reference", "unity-6000.0.71f1", "review-controls.json");

        // Replay every distance input in Sources/ReviewControls/ProbeEntry.cs, including
        // the two body/fixture orders. Read the existing oracle, without another copy.
        [Fact]
        public void ReviewDistanceWitnessesRetainAllRecordedFields()
        {
            using var reference = JsonDocument.Parse(File.ReadAllText(Reference()));
            var observations = new Dictionary<string, Dictionary<string, double>>();
            var errors = new List<string>();
            var oldMode = Physics2D.simulationMode;
            var oldGravity = Physics2D.gravity;
            float oldOffset = Physics2D.defaultContactOffset;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                Physics2D.gravity = Vector2.zero;
                Physics2D.defaultContactOffset = .01f;
                foreach (var scenario in reference.RootElement.EnumerateObject())
                {
                    if (!scenario.Value.TryGetProperty("distance", out _)) continue;
                    var objects = new List<GameObject>();
                    var values = new Dictionary<string, double>();
                    observations.Add(scenario.Name, values);
                    try
                    {
                        var parts = scenario.Name.Split('/');
                        Collider2D a, b;
                        if (parts[0] == "tips")
                        {
                            bool horizontal = bool.Parse(parts[1]);
                            float angle = Number(parts[2]);
                            var offset = new Vector2(Number(parts[3]), Number(parts[4]));
                            var delta = horizontal ? new Vector2(offset.y, offset.x) : offset;
                            delta = Quaternion.Euler(0, 0, angle) * delta;
                            var ca = Make("target", Vector2.zero, angle, false, objects).AddComponent<CapsuleCollider2D>();
                            var cb = Make("moving", delta, angle, true, objects).AddComponent<CapsuleCollider2D>();
                            ca.direction = cb.direction = horizontal ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
                            ca.size = cb.size = horizontal ? new Vector2(3, 1) : new Vector2(1, 3);
                            a = ca; b = cb;
                        }
                        else
                        {
                            Assert.Equal("collapsed", parts[0]);
                            float length = Number(parts[1]), angle = Number(parts[2]);
                            bool reverse = bool.Parse(parts[3]);
                            var position = Quaternion.Euler(0, 0, angle) * new Vector2(-1.1f, 0);
                            var capsule = Make("capsule", Vector2.zero, angle, reverse, objects).AddComponent<CapsuleCollider2D>();
                            capsule.size = new Vector2(1, length);
                            var box = Make("box", position, angle + 45f, !reverse, objects).AddComponent<BoxCollider2D>();
                            box.size = Vector2.one;
                            a = capsule; b = box;
                        }
                        Physics2D.SyncTransforms();
                        Shape(a.name, a, values); Shape(b.name, b, values);
                        var distance = a.Distance(b);
                        Assert.True(distance.isValid, scenario.Name);
                        values.Add("distance", distance.distance);
                        Vector("distance.normal", distance.normal, values);
                        Vector("distance.pointA", distance.pointA, values);
                        Vector("distance.pointB", distance.pointB, values);
                        foreach (var field in values)
                        {
                            double expected = scenario.Value.GetProperty(field.Key).GetDouble();
                            double delta = field.Value - expected;
                            // public.py's original relative/absolute tolerance, unchanged.
                            double allowed = 1e-3 * System.Math.Max(1, System.Math.Abs(expected));
                            if (!double.IsFinite(field.Value) || System.Math.Abs(delta) > allowed)
                                errors.Add($"{scenario.Name}/{field.Key}: reference={expected:R}, candidate={field.Value:R}, " +
                                    $"delta={delta:R}, absolute_error={System.Math.Abs(delta):R}, allowed={allowed:R}");
                        }
                    }
                    finally
                    {
                        foreach (var go in objects) Object.DestroyImmediate(go);
                    }
                }
            }
            finally
            {
                Physics2D.defaultContactOffset = oldOffset;
                Physics2D.gravity = oldGravity;
                Physics2D.simulationMode = oldMode;
            }
            // Keep successful raw values as well as every failing value in the test log.
            output.WriteLine(JsonSerializer.Serialize(observations));
            Assert.Equal(58, observations.Count);
            int distanceFields = 0;
            foreach (var values in observations.Values)
                foreach (var field in values.Keys)
                    if (field == "distance" || field.StartsWith("distance.", StringComparison.Ordinal)) distanceFields++;
            Assert.Equal(406, distanceFields);
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        }

        static float Number(string value) => float.Parse(value, CultureInfo.InvariantCulture);

        static GameObject Make(string name, Vector2 position, float angle, bool body, List<GameObject> objects)
        {
            var go = new GameObject(name); objects.Add(go);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0, 0, angle);
            go.layer = body ? 9 : 8;
            if (body)
            {
                var rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.useFullKinematicContacts = true;
                rb.constraints = RigidbodyConstraints2D.FreezeAll;
                rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            }
            return go;
        }

        static void Vector(string key, Vector2 value, Dictionary<string, double> values)
        {
            values.Add(key + ".x", value.x); values.Add(key + ".y", value.y);
        }

        static void Shape(string key, Collider2D collider, Dictionary<string, double> values)
        {
            var group = new PhysicsShapeGroup2D(); collider.GetShapes(group);
            var shapes = new List<PhysicsShape2D>(); var vertices = new List<Vector2>();
            group.GetShapeData(shapes, vertices);
            values.Add(key + "/shapes", shapes.Count); values.Add(key + "/vertices", vertices.Count);
            for (int i = 0; i < shapes.Count; i++) values.Add(key + "/radius/" + i, shapes[i].radius);
            for (int i = 0; i < vertices.Count; i++) Vector(key + "/vertex/" + i, vertices[i], values);
        }
    }
}
