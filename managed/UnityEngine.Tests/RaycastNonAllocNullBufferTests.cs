using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Physics.RaycastNonAlloc with a null results buffer. Documented (6000.0 Scripting API): "If a
    /// null buffer is passed in, no results are returned and no errors or exceptions are thrown."
    /// It returns the number of hits stored, the order of results is undefined, and a full buffer
    /// need not hold the closest hits. So every non-null control below uses a single-hit scene and
    /// never asserts which hits come first. The null cases cover all eight public overloads with
    /// ordinary arguments, and every Unity log report (including Unbound's own diagnostics, which
    /// Application.logMessageReceived does not see) is captured at the logger's handler.
    /// The scene sits in a region no other test uses. The physics settings and log handler that the
    /// tests change are restored in <see cref="Dispose"/>, which also runs when a test fails.
    /// </summary>
    public sealed class RaycastNonAllocNullBufferTests : IDisposable
    {
        static readonly Vector3 Base = new Vector3(-300f, 400f, -300f);
        static readonly Vector3 Forward = Vector3.forward;
        const float Near = 10f;

        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<string> m_Reports = new List<string>();
        readonly bool m_AutoSyncTransforms;
        readonly bool m_QueriesHitTriggers;
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;

        public RaycastNonAllocNullBufferTests()
        {
            m_AutoSyncTransforms = Physics.autoSyncTransforms;
            m_QueriesHitTriggers = Physics.queriesHitTriggers;
            m_PreviousHandler = m_Logger.logHandler;
            m_PreviousLogEnabled = m_Logger.logEnabled;
            m_PreviousFilter = m_Logger.filterLogType;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Reports);
            m_Logger.logEnabled = true;
            m_Logger.filterLogType = LogType.Log;
            Physics.autoSyncTransforms = true;
            Physics.queriesHitTriggers = true;
        }

        public void Dispose()
        {
            try
            {
                foreach (var go in m_Made)
                    if (go != null) Object.DestroyImmediate(go);
            }
            finally
            {
                Physics.autoSyncTransforms = m_AutoSyncTransforms;
                Physics.queriesHitTriggers = m_QueriesHitTriggers;
                m_Logger.logHandler = m_PreviousHandler;
                m_Logger.logEnabled = m_PreviousLogEnabled;
                m_Logger.filterLogType = m_PreviousFilter;
            }
        }

        /// <summary>Records every report other than a plain log and forwards each call unchanged.</summary>
        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Reports;

            internal CaptureHandler(ILogHandler inner, List<string> reports)
            {
                m_Inner = inner;
                m_Reports = reports;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type != LogType.Log)
                {
                    string message = args == null || args.Length == 0 ? format
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                    m_Reports.Add($"{type}: {message}");
                }
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context)
            {
                if (exception != null) m_Reports.Add($"Exception: {exception}");
                m_Inner.LogException(exception, context);
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Calls one of the eight public overloads. Arguments the overload does not take
        /// get its defaults (Mathf.Infinity, DefaultRaycastLayers, UseGlobal).</summary>
        static int Call(int overload, Vector3 origin, Vector3 direction, RaycastHit[] results, float maxDistance, int layerMask, QueryTriggerInteraction triggers) => overload switch
        {
            0 => Physics.RaycastNonAlloc(new Ray(origin, direction), results, maxDistance, layerMask, triggers),
            1 => Physics.RaycastNonAlloc(new Ray(origin, direction), results, maxDistance, layerMask),
            2 => Physics.RaycastNonAlloc(new Ray(origin, direction), results, maxDistance),
            3 => Physics.RaycastNonAlloc(new Ray(origin, direction), results),
            4 => Physics.RaycastNonAlloc(origin, direction, results, maxDistance, layerMask, triggers),
            5 => Physics.RaycastNonAlloc(origin, direction, results, maxDistance, layerMask),
            6 => Physics.RaycastNonAlloc(origin, direction, results, maxDistance),
            7 => Physics.RaycastNonAlloc(origin, direction, results),
            _ => throw new ArgumentOutOfRangeException(nameof(overload)),
        };

        static bool TakesMask(int overload) => overload is 0 or 1 or 4 or 5;
        static bool TakesTriggers(int overload) => overload is 0 or 4;

        /// <summary>One returned value, with every report made while it ran.</summary>
        int Unreported(Func<int> call)
        {
            m_Reports.Clear();
            int n = call();
            Assert.Empty(m_Reports);
            return n;
        }

        /// <summary>A 1 m box collider at offset from <see cref="Base"/>. On the ray from Base along
        /// +z, its front face is at distance offset.z - 0.5.</summary>
        BoxCollider Box(string name, Vector3 offset, int layer = 0, bool trigger = false)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            go.layer = layer;
            go.transform.position = Base + offset;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = trigger;
            return box;
        }

        /// <summary>The region is empty before a test builds its scene, so each count below is the
        /// scene's own.</summary>
        static void AssertRegionEmpty() =>
            Assert.Empty(Physics.RaycastAll(Base, Forward, Mathf.Infinity, Physics.AllLayers, QueryTriggerInteraction.Collide));

        static RaycastHit Sentinel(int i) => new RaycastHit { distance = -7f - i, point = new Vector3(1f, 2f, 3f + i) };

        static RaycastHit[] Filled(int length)
        {
            var results = new RaycastHit[length];
            for (int i = 0; i < length; i++) results[i] = Sentinel(i);
            return results;
        }

        static void AssertSentinel(RaycastHit[] results, int i)
        {
            Assert.Null(results[i].collider);
            Assert.Equal(-7f - i, results[i].distance);
            Assert.Equal(new Vector3(1f, 2f, 3f + i), results[i].point);
        }

        static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.True((expected - actual).magnitude < 1e-3f, $"expected {expected}, got {actual}");
        }

        public static IEnumerable<object[]> Overloads()
        {
            for (int i = 0; i < 8; i++) yield return new object[] { i };
        }

        // ------------------------------------------------------------------ the null buffer

        [Theory]
        [MemberData(nameof(Overloads))]
        public void NullBufferReturnsZeroWithoutReports(int overload)
        {
            AssertRegionEmpty();
            Assert.Equal(0, Unreported(() => Call(overload, Base, Forward, null, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal)));
        }

        [Theory]
        [MemberData(nameof(Overloads))]
        public void NullBufferReturnsZeroWhenTheRayHits(int overload)
        {
            AssertRegionEmpty();
            var target = Box("target", new Vector3(0f, 0f, 5f));
            var probe = new RaycastHit[1];
            Assert.Equal(1, Call(overload, Base, Forward, probe, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
            Assert.Same(target, probe[0].collider);

            Assert.Equal(0, Unreported(() => Call(overload, Base, Forward, null, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal)));
            Assert.Equal(0, Unreported(() => Call(overload, Base, Forward, null, Near, Physics.AllLayers, QueryTriggerInteraction.Collide)));
        }

        /// <summary>
        /// The null call prepares nothing. With autoSyncTransforms set, any query first syncs moved
        /// transforms into the world. With it cleared, the world keeps its last synced poses, and
        /// Collider.bounds and later queries report them. A null call made while a move is
        /// pending must leave the collider at its old pose. A check placed after the query's world
        /// preparation (or in the shared buffer fill) would have synced the move first.
        /// </summary>
        [Theory]
        [MemberData(nameof(Overloads))]
        public void NullBufferLeavesAPendingMoveUnsynced(int overload)
        {
            AssertRegionEmpty();
            var target = Box("target", new Vector3(0f, 0f, 5f));
            var synced = Base + new Vector3(0f, 0f, 5f);
            var moved = Base + new Vector3(20f, 0f, 5f);
            var results = new RaycastHit[4];
            Assert.Equal(1, Physics.RaycastNonAlloc(Base, Forward, results, Near));
            Assert.Same(target, results[0].collider);

            target.transform.position = moved;
            Assert.Equal(0, Unreported(() => Call(overload, Base, Forward, null, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal)));

            Physics.autoSyncTransforms = false;
            AssertNear(synced, target.bounds.center);
            Assert.Equal(1, Physics.RaycastNonAlloc(Base, Forward, results, Near));
            Assert.Same(target, results[0].collider);

            // The move was pending: an explicit sync applies it.
            Physics.SyncTransforms();
            AssertNear(moved, target.bounds.center);
            Assert.Equal(0, Physics.RaycastNonAlloc(Base, Forward, results, Near));
        }

        /// <summary>The documented null result does not depend on the other arguments.</summary>
        [Theory]
        [InlineData(0f, 0f, 0f, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal)]
        [InlineData(0f, 0f, 1f, float.NaN, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal)]
        [InlineData(0f, 0f, 1f, -1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal)]
        [InlineData(0f, 0f, 1f, float.PositiveInfinity, 0, QueryTriggerInteraction.Ignore)]
        [InlineData(0f, 0f, 1f, float.PositiveInfinity, Physics.AllLayers, QueryTriggerInteraction.Collide)]
        public void NullBufferReturnsZeroForAnyArguments(float dx, float dy, float dz, float maxDistance, int layerMask, QueryTriggerInteraction triggers)
        {
            AssertRegionEmpty();
            Box("target", new Vector3(0f, 0f, 5f));
            var direction = new Vector3(dx, dy, dz);
            Assert.Equal(0, Unreported(() => Physics.RaycastNonAlloc(Base, direction, null, maxDistance, layerMask, triggers)));
            Assert.Equal(0, Unreported(() => Physics.RaycastNonAlloc(new Ray(Base, direction), null, maxDistance, layerMask, triggers)));
        }

        // ------------------------------------------------------------------ non-null controls

        [Theory]
        [MemberData(nameof(Overloads))]
        public void ZeroCapacityBufferReturnsZero(int overload)
        {
            AssertRegionEmpty();
            Box("target", new Vector3(0f, 0f, 5f));
            Assert.Equal(0, Unreported(() => Call(overload, Base, Forward, new RaycastHit[0], Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal)));
        }

        [Theory]
        [MemberData(nameof(Overloads))]
        public void HitIsStoredAndTheTailIsLeftAsItWas(int overload)
        {
            AssertRegionEmpty();
            var target = Box("target", new Vector3(0f, 0f, 5f));
            var results = Filled(4);
            Assert.Equal(1, Unreported(() => Call(overload, Base, Forward, results, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal)));
            Assert.Same(target, results[0].collider);
            Assert.Equal(4.5f, results[0].distance, 3);
            AssertNear(Base + new Vector3(0f, 0f, 4.5f), results[0].point);
            for (int i = 1; i < results.Length; i++) AssertSentinel(results, i);
        }

        /// <summary>The six overloads that take maxDistance.</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void MaxDistanceExcludesFartherColliders(int overload)
        {
            AssertRegionEmpty();
            var target = Box("target", new Vector3(0f, 0f, 5f));
            var results = Filled(2);
            Assert.Equal(0, Call(overload, Base, Forward, results, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
            AssertSentinel(results, 0);
            AssertSentinel(results, 1);
            Assert.Equal(1, Call(overload, Base, Forward, results, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
            Assert.Same(target, results[0].collider);
            AssertSentinel(results, 1);
        }

        [Theory]
        [MemberData(nameof(Overloads))]
        public void LayerMaskFiltersColliders(int overload)
        {
            AssertRegionEmpty();
            var target = Box("target", new Vector3(0f, 0f, 5f), layer: 9);
            var results = new RaycastHit[2];
            if (TakesMask(overload))
            {
                Assert.Equal(0, Call(overload, Base, Forward, results, Near, ~(1 << 9), QueryTriggerInteraction.UseGlobal));
                Assert.Equal(1, Call(overload, Base, Forward, results, Near, 1 << 9, QueryTriggerInteraction.UseGlobal));
                Assert.Same(target, results[0].collider);
            }
            // DefaultRaycastLayers, given or forwarded as the overload's default, takes layer 9 and
            // leaves out the Ignore Raycast layer (2).
            Assert.Equal(1, Call(overload, Base, Forward, results, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
            Assert.Same(target, results[0].collider);
            target.gameObject.layer = 2;
            if (TakesMask(overload))
            {
                Assert.Equal(0, Call(overload, Base, Forward, results, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
                Assert.Equal(1, Call(overload, Base, Forward, results, Near, 1 << 2, QueryTriggerInteraction.UseGlobal));
            }
            else
            {
                Assert.Equal(0, Call(overload, Base, Forward, results, Near, 0, QueryTriggerInteraction.UseGlobal));
            }
        }

        [Theory]
        [MemberData(nameof(Overloads))]
        public void TriggerInteractionSelectsTriggers(int overload)
        {
            AssertRegionEmpty();
            var zone = Box("zone", new Vector3(0f, 0f, 5f), trigger: true);
            var results = new RaycastHit[2];
            if (TakesTriggers(overload))
            {
                Physics.queriesHitTriggers = false;
                Assert.Equal(1, Call(overload, Base, Forward, results, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide));
                Assert.Same(zone, results[0].collider);
                Physics.queriesHitTriggers = true;
                Assert.Equal(0, Call(overload, Base, Forward, results, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
            }
            // UseGlobal, given or forwarded, follows Physics.queriesHitTriggers.
            Physics.queriesHitTriggers = true;
            Assert.Equal(1, Call(overload, Base, Forward, results, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
            Assert.Same(zone, results[0].collider);
            Physics.queriesHitTriggers = false;
            Assert.Equal(0, Call(overload, Base, Forward, results, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        }

        /// <summary>Each Ray overload matches its origin/direction overload, including for a
        /// direction that is not unit length.</summary>
        [Theory]
        [InlineData(0, 4)]
        [InlineData(1, 5)]
        [InlineData(2, 6)]
        [InlineData(3, 7)]
        public void RayAndOriginOverloadsAgree(int rayOverload, int originOverload)
        {
            AssertRegionEmpty();
            var target = Box("target", new Vector3(0f, 0f, 5f));
            var direction = new Vector3(0f, 0f, 3f);
            var fromRay = new RaycastHit[2];
            var fromOrigin = new RaycastHit[2];
            int n = Call(rayOverload, Base, direction, fromRay, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
            Assert.Equal(1, n);
            Assert.Equal(n, Call(originOverload, Base, direction, fromOrigin, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
            Assert.Same(target, fromRay[0].collider);
            Assert.Same(fromRay[0].collider, fromOrigin[0].collider);
            Assert.Equal(fromRay[0].distance, fromOrigin[0].distance);
            Assert.Equal(fromRay[0].point, fromOrigin[0].point);
            Assert.Equal(fromRay[0].normal, fromOrigin[0].normal);
        }

        /// <summary>The non-null path still gives its existing result for a zero direction: no
        /// hits, no report, buffer unchanged. This keeps Unbound's unchanged behaviour in place and
        /// is not a measured Unity value.</summary>
        [Fact]
        public void NonNullBufferKeepsItsZeroDirectionResult()
        {
            AssertRegionEmpty();
            Box("target", new Vector3(0f, 0f, 5f));
            var results = Filled(2);
            Assert.Equal(0, Unreported(() => Physics.RaycastNonAlloc(Base, Vector3.zero, results, Near)));
            AssertSentinel(results, 0);
            AssertSentinel(results, 1);
        }
    }
}
