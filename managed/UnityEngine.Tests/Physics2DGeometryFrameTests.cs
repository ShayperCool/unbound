using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    public class Physics2DGeometryFrameTests : IDisposable
    {
        readonly List<GameObject> objects = new List<GameObject>();
        readonly bool autoSync = Physics2D.autoSyncTransforms;
        readonly SimulationMode2D mode = Physics2D.simulationMode;

        public Physics2DGeometryFrameTests()
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.autoSyncTransforms = false;
        }

        GameObject Make(string name)
        {
            var go = new GameObject(name);
            objects.Add(go);
            return go;
        }

        public void Dispose()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]);
            Physics2D.autoSyncTransforms = autoSync;
            Physics2D.simulationMode = mode;
        }

        static Vector2 Center(CircleCollider2D collider, out float radius)
        {
            Physics2D.SyncTransforms();
            var group = new PhysicsShapeGroup2D();
            collider.GetShapes(group);
            var shapes = new List<PhysicsShape2D>();
            var vertices = new List<Vector2>();
            group.GetShapeData(shapes, vertices);
            radius = Assert.Single(shapes).radius;
            return Assert.Single(vertices);
        }

        // tests/projects/solverpairorder --transform-inputs circle-scale/12 and /13 (initial) and
        // circle-planar/11: a circle on a scaled, rotated object whose body is on the same object, on a
        // rotated and mirrored parent, and on a parent turned 11 degrees. The arm64 values are the macOS
        // Editor's (fused multiply-adds in the body frame), the x86_64 values the Linux Editor's.
        [Theory]
        [InlineData(0, 0.17f, -0.184f, 0.17f, -0.184f)]
        [InlineData(1, -1.47838914f, -0.8131563f, -1.47838914f, -0.8131564f)]
        [InlineData(2, -0.224795312f, -0.354622453f, -0.224795282f, -0.3546225f)]
        public void BodyFrameFollowsTheHostEditor(int scenario, float armX, float armY, float x86X, float x86Y)
        {
            GameObject go;
            if (scenario == 2)
            {
                var parent = Make("parent");
                go = Make("circle");
                go.transform.position = new Vector3(.3f, -.2f, 2);
                go.transform.localScale = new Vector3(-.9f, 1.7f, -1.1f);
                parent.transform.position = go.transform.position;
                parent.transform.rotation = Quaternion.Euler(0, 0, 11);
                parent.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                go.transform.SetParent(parent.transform, true);
            }
            else
            {
                var parent = Make("parent");
                go = Make("circle");
                parent.transform.position = new Vector3(.7f, -.4f, 1.2f);
                parent.transform.localRotation = Quaternion.Euler(17, -29, 13);
                parent.transform.localScale = new Vector3(-.9f, 1.7f, 1.1f);
                if (scenario == 1)
                {
                    parent.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                    go.transform.SetParent(parent.transform, false);
                }
                else
                    go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                go.transform.localPosition = new Vector3(.23f, -.17f, 2);
                go.transform.localRotation = Quaternion.Euler(0, 0, 13.37f);
                go.transform.localScale = new Vector3(1, .8f, 1);
            }
            var circle = go.AddComponent<CircleCollider2D>();
            circle.radius = .75f;
            circle.offset = new Vector2(.17f, -.23f);
            var center = Center(circle, out _);
            bool arm64 = System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported;
            Assert.Equal(arm64 ? armX : x86X, center.x);
            Assert.Equal(arm64 ? armY : x86Y, center.y);
        }

        // The independent geometryframe2d Native pair records these four displacements.
        // A tolerance smaller than the displacement prevents a zero center from passing.
        [Theory]
        [InlineData(23f, 1e-7f, 0f, 8.940697e-8f, 0f)]
        [InlineData(10f, -1e-7f, 0f, -8.940697e-8f, 0f)]
        [InlineData(23f, 0f, 1e-7f, 0f, 1.04308128e-7f)]
        [InlineData(10f, 0f, -1e-7f, 0f, -1.04308128e-7f)]
        public void ChildShapeRetainsRealSubMicrounitDisplacement(float angle, float dx, float dy, float x, float y)
        {
            var body = Make("body");
            body.transform.position = new Vector3(.3f, -.2f, 2);
            body.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var target = Make("target");
            target.transform.SetParent(body.transform, false);
            target.transform.localPosition = new Vector3(dx, dy, 0);
            target.transform.localRotation = Quaternion.Euler(0, 0, angle);
            target.transform.localScale = new Vector3(1, .8f, 1);
            var circle = target.AddComponent<CircleCollider2D>(); circle.radius = .75f;
            var center = Center(circle, out var radius);
            Assert.InRange(Mathf.Abs(center.x - x), 0, 1e-9f);
            Assert.InRange(Mathf.Abs(center.y - y), 0, 1e-9f);
            Assert.True(center.sqrMagnitude > 0);
            Assert.InRange(Mathf.Abs(radius - .75f), 0, 1e-6f);
        }

        // Native offset/body/circle preserves the scaled offset independently of translation.
        [Theory]
        [InlineData(23f)]
        public void BodyShapeRetainsNonzeroScaledOffset(float angle)
        {
            var target = Make("target");
            target.transform.position = new Vector3(.3f, -.2f, 2);
            target.transform.rotation = Quaternion.Euler(0, 0, angle);
            target.transform.localScale = new Vector3(1, .8f, 1);
            target.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var circle = target.AddComponent<CircleCollider2D>();
            circle.radius = .75f; circle.offset = new Vector2(.2f, -.1f);
            var center = Center(circle, out var radius);
            Assert.InRange(Mathf.Abs(center.x - .2f), 0, 1e-6f);
            Assert.InRange(Mathf.Abs(center.y - -.0800000057f), 0, 1e-6f);
            Assert.InRange(Mathf.Abs(radius - .75f), 0, 1e-6f);
        }
    }
}
