using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class Physics2DCastContactTests : IDisposable
    {
        readonly Physics2DMeasuredScope release = new Physics2DMeasuredScope();
        readonly List<GameObject> objects = new List<GameObject>();
        readonly float oldSkin = Physics2D.defaultContactOffset;
        readonly bool oldAuto = Physics2D.autoSyncTransforms;
        readonly Vector2 oldGravity = Physics2D.gravity;
        readonly SimulationMode2D oldMode = Physics2D.simulationMode;
        GameObject Root()
        {
            var root = new GameObject("cast-contact"); objects.Add(root); return root;
        }
        public Physics2DCastContactTests()
        {
            Physics2D.autoSyncTransforms = false; Physics2D.gravity = Vector2.zero;
            Physics2D.simulationMode = SimulationMode2D.Script;
        }
        [Theory]
        [InlineData(0, -1e-7f)]
        [InlineData(0, 1e-7f)]
        [InlineData(1, -1e-7f)]
        [InlineData(1, 1e-7f)]
        [InlineData(2, -.00001f)]
        [InlineData(2, .00001f)]
        public void ExplicitSynchronizationAppliesSmallChangedPoses(int axis, float delta)
        {
            var root = Root(); root.transform.position = new Vector3(.3f, -.2f, 0);
            root.transform.rotation = Quaternion.Euler(0, 0, 23);
            var body = root.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
            var box = root.AddComponent<BoxCollider2D>(); Physics2D.SyncTransforms();
            var position = body.position; float angle = body.rotation; var fixture = box.m_Fixtures[0];
            if (axis == 2) root.transform.rotation = Quaternion.Euler(0, 0, 23 + delta);
            else root.transform.position += axis == 0 ? new Vector3(delta, 0, 0) : new Vector3(0, delta, 0);
            Assert.True(position.Equals(body.position)); Assert.Equal(angle, body.rotation);
            Physics2D.SyncTransforms();
            if (axis == 2) Assert.NotEqual(angle, body.rotation);
            else { Assert.False(position.Equals(body.position)); Assert.True(body.position.Equals((Vector2)root.transform.position)); }
            Assert.Same(fixture, box.m_Fixtures[0]);
        }
        [Theory]
        [InlineData(RigidbodyType2D.Dynamic)]
        [InlineData(RigidbodyType2D.Kinematic)]
        public void SmallSimulatedMotionReachesTheTransform(RigidbodyType2D kind)
        {
            var root = Root(); root.transform.position = new Vector3(.3f, -.2f, 0);
            var body = root.AddComponent<Rigidbody2D>(); body.bodyType = kind;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep; root.AddComponent<CircleCollider2D>(); Physics2D.SyncTransforms();
            float before = body.position.x; body.linearVelocity = new Vector2(5e-6f, 0);
            Assert.True(Physics2D.Simulate(.02f));
            Assert.NotEqual(before, body.position.x); Assert.Equal(body.position.x, root.transform.position.x);
        }
        [Theory]
        [InlineData("6000.0.71f1", true)]
        [InlineData("6000.0.72f1", false)]
        public void ContactClippingSelectsItsMeasuredReleaseAndReportsTheFallback(string version, bool measured)
        {
            const string member = "Physics2D cast contact features";
            var hits = (System.Collections.Concurrent.ConcurrentDictionary<string, int>)typeof(Stubs)
                .GetField("Hits", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            bool hadHit = hits.TryRemove(member, out int previousHit);
            var logger = Debug.unityLogger; var previous = logger.logHandler;
            bool enabled = logger.logEnabled; var filter = logger.filterLogType; var capture = new CastLogs(previous);
            try
            {
                logger.logHandler = capture; logger.logEnabled = true; logger.filterLogType = LogType.Log;
                Physics2DWorld.Reset(); ProjectInfo.UnityVersion = version;
                Physics2D.defaultContactOffset = .01f;
                var box = Root().AddComponent<BoxCollider2D>(); box.size = new Vector2(2, .5f); Physics2D.SyncTransforms();
                Physics2D.defaultContactOffset = .001f;
                var hit = Physics2D.BoxCast(new Vector2(0, 2), new Vector2(1, .4f), .01f, Vector2.down, 2.5f);
                Assert.Same(box, hit.collider);
                Assert.InRange(Mathf.Abs(hit.point.x - (measured ? -.49996546f : -.51096535f)), 0, .00001f);
                Assert.Equal(!measured, capture.Messages.Exists(x => x.Contains(member)));
            }
            finally
            {
                logger.logHandler = previous; logger.logEnabled = enabled; logger.filterLogType = filter;
                hits.TryRemove(member, out _); if (hadHit) hits[member] = previousHit;
            }
        }
        [Theory]
        [InlineData(false, .001f, .01f, -.49996546f)]
        [InlineData(true, .001f, .01f, -.49996546f)]
        [InlineData(false, .001f, .0013f, .000004381f)]
        [InlineData(true, .001f, .0013f, .000004381f)]
        [InlineData(false, .1f, .01f, -.60994893f)]
        [InlineData(true, .1f, .01f, -.60994893f)]
        [InlineData(false, .1f, .0013f, -.6099934f)]
        [InlineData(true, .1f, .0013f, -.6099934f)]
        public void PolygonCastsSelectClippingAndWitnessFromTheirActualRadii(bool polygon, float skin, float angle, float x)
        {
            Physics2D.defaultContactOffset = .01f;
            var root = Root(); Collider2D collider;
            if (polygon)
            {
                var value = root.AddComponent<PolygonCollider2D>();
                value.points = new[] { new Vector2(-1, -.25f), new Vector2(1, -.25f), new Vector2(1, .25f), new Vector2(-1, .25f) };
                collider = value;
            }
            else { var value = root.AddComponent<BoxCollider2D>(); value.size = new Vector2(2, .5f); collider = value; }
            Physics2D.SyncTransforms(); Physics2D.defaultContactOffset = skin;
            var hit = Physics2D.BoxCast(new Vector2(0, 2), new Vector2(1, .4f), angle, Vector2.down, 2.5f);
            Assert.Same(collider, hit.collider); Assert.InRange(Mathf.Abs(hit.point.x - x), 0, .00001f);
        }
        sealed class CastLogs : ILogHandler
        {
            readonly ILogHandler next;
            internal readonly List<string> Messages = new List<string>();
            internal CastLogs(ILogHandler next) { this.next = next; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            { Messages.Add(string.Format(format, args)); next.LogFormat(type, context, format, args); }
            public void LogException(Exception error, Object context) => next.LogException(error, context);
        }
        public void Dispose()
        {
            foreach (var root in objects) if (root) Object.DestroyImmediate(root);
            Physics2D.defaultContactOffset = oldSkin; Physics2D.autoSyncTransforms = oldAuto;
            Physics2D.gravity = oldGravity; Physics2D.simulationMode = oldMode; release.Dispose();
        }
    }
}
