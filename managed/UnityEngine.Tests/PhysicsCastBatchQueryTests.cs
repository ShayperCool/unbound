using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// 3D cast batch queries and the RaycastHit they write (6000.0 Scripting API: RaycastHit,
    /// RaycastCommand, SpherecastCommand, BoxcastCommand, CapsulecastCommand, their ScheduleBatch,
    /// QueryParameters).
    /// Documented and asserted: a command's results start at index N * maxHits; the ScheduleBatch
    /// overload without maxHits uses maxHits 1; for RaycastCommand the first invalid result has a
    /// null collider; results are complete once the returned handle is complete; a command searches
    /// its own physicsScene; RaycastHit.collider is the Collider that was hit, rigidbody its attached
    /// Rigidbody and transform the Transform of that rigidbody or collider.
    /// Unbound's choices (unmeasured), asserted only where a test names them: RaycastHit holds the
    /// collider's instance ID and reads null once that collider is destroyed; hitBackfaces on any
    /// command and hitMultipleFaces on RaycastCommand are refused with NotSupportedException before
    /// any result is written; the shape casts answer hitMultipleFaces as without it (the
    /// QueryParameters Remarks limit multiple Mesh faces to RaycastCommand; inferred, unmeasured); the
    /// other checks throw before any result is written, with the exception types named; a
    /// one-slot command answers what the single PhysicsScene cast with an out hit answers; the
    /// obsolete signatures put their layer mask into QueryParameters.
    /// The order of several hits one command finds is not documented and is not asserted.
    /// No Editor reference measures these APIs: the expectations are per documentation, unmeasured.
    ///
    /// Red base 29030429: the file compiles there (every API it uses is a public stub with the same
    /// signature). Each case names what fails. "Allocation" means new NativeArray&lt;RaycastHit&gt;
    /// throws InvalidOperationException because RaycastHit holds a managed reference. At 24802055
    /// (RaycastHit made unmanaged, commands still stubs) the cast cases fail on their first
    /// assertion instead, since the stub ScheduleBatch writes nothing.
    /// </summary>
    public class PhysicsCastBatchQueryTests : IDisposable
    {
        // Far from the origin, away from anything other tests leave in the default physics scene.
        static readonly Vector3 Origin = new Vector3(900f, 0f, 0f);
        const float Reach = 20f;
        static int s_Created;
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<Scene> m_Scenes = new List<Scene>();
        readonly List<IDisposable> m_Arrays = new List<IDisposable>();
        readonly bool m_HitTriggers;
        readonly bool m_AutoSync;
        readonly SimulationMode m_Mode;
        readonly Scene m_Active;

        public PhysicsCastBatchQueryTests()
        {
            m_HitTriggers = Physics.queriesHitTriggers;
            m_AutoSync = Physics.autoSyncTransforms;
            m_Mode = Physics.simulationMode;
            Physics.queriesHitTriggers = true;
            Physics.autoSyncTransforms = true;
            Physics.simulationMode = SimulationMode.Script;
            m_Active = SceneManager.GetActiveScene();
        }

        public void Dispose()
        {
            foreach (var array in m_Arrays) array.Dispose();
            if (m_Active.isLoaded && SceneManager.GetActiveScene() != m_Active) SceneManager.SetActiveScene(m_Active);
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            foreach (var scene in m_Scenes)
                if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            Physics.queriesHitTriggers = m_HitTriggers;
            Physics.autoSyncTransforms = m_AutoSync;
            Physics.simulationMode = m_Mode;
        }

        NativeArray<T> Native<T>(int length) where T : struct
        {
            var array = new NativeArray<T>(length, Allocator.TempJob);
            m_Arrays.Add(array);
            return array;
        }

        NativeArray<T> Native<T>(params T[] values) where T : struct
        {
            var array = new NativeArray<T>(values, Allocator.TempJob);
            m_Arrays.Add(array);
            return array;
        }

        BoxCollider Box(string name, Vector3 position, int layer = 0, bool trigger = false)
        {
            var go = new GameObject(name) { layer = layer };
            m_Made.Add(go);
            go.transform.position = position;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = trigger;
            return box;
        }

        static QueryParameters Mask(int layerMask, QueryTriggerInteraction triggers = QueryTriggerInteraction.UseGlobal) =>
            new QueryParameters(layerMask, false, triggers, false);

        static readonly QueryParameters All = Mask(Physics.AllLayers);

        /// <summary>A ray from 10 m in front of a point (along -z) towards it.</summary>
        static Vector3 Before(Vector3 at) => at - new Vector3(0f, 0f, 10f);

        /// <summary>The colliders of a command's results, up to its first invalid result (the
        /// documented end of a command's results).</summary>
        static List<Collider> Hits(NativeArray<RaycastHit> results, int command, int maxHits)
        {
            var hits = new List<Collider>();
            for (int k = 0; k < maxHits; k++)
            {
                var hit = results[command * maxHits + k];
                if (hit.collider == null) break;
                Assert.Equal(hit.collider.GetInstanceID(), hit.colliderInstanceID);
                hits.Add(hit.collider);
            }
            return hits;
        }

        static void SameHit(RaycastHit expected, RaycastHit actual)
        {
            Assert.Same(expected.collider, actual.collider);
            Assert.Equal(expected.colliderInstanceID, actual.colliderInstanceID);
            Assert.Equal(expected.point, actual.point);
            Assert.Equal(expected.normal, actual.normal);
            Assert.Equal(expected.distance, actual.distance);
        }

        static RaycastHit Sentinel(int i) => new RaycastHit { distance = -7f - i, point = new Vector3(1f, 2f, 3f + i) };

        static void StillSentinels(NativeArray<RaycastHit> results)
        {
            for (int i = 0; i < results.Length; i++)
            {
                Assert.Equal(-7f - i, results[i].distance);
                Assert.Equal(new Vector3(1f, 2f, 3f + i), results[i].point);
                Assert.Null(results[i].collider);
            }
        }

        // Red on 29030429: IsUnmanaged<RaycastHit> is false (the hit holds a Collider reference), so
        // the first Assert.True fails. Green on 24802055.
        [Fact]
        public void RaycastHitIsUnmanagedAndCopiesThroughNativeContainers()
        {
            Assert.True(UnsafeUtility.IsUnmanaged<RaycastHit>());
            var target = Box("Copied", Origin);
            Assert.True(Physics.Raycast(Before(Origin), Vector3.forward, out var hit, Reach, Physics.AllLayers));

            var array = Native<RaycastHit>(3);
            array[1] = hit;
            var copy = new NativeArray<RaycastHit>(array, Allocator.TempJob);
            m_Arrays.Add(copy);
            var managed = array.ToArray();
            var sub = array.GetSubArray(1, 2);
            var slice = new NativeSlice<RaycastHit>(array, 1, 1);

            SameHit(hit, array[1]);
            SameHit(hit, copy[1]);
            SameHit(hit, managed[1]);
            SameHit(hit, sub[0]);
            SameHit(hit, slice[0]);
            Assert.Same(target, copy[1].collider);
            // A zeroed element is an empty hit.
            Assert.Null(array[0].collider);
            Assert.Equal(0, array[0].colliderInstanceID);
            Assert.Null(sub[1].collider);
        }

        // Red on 29030429: allocation. Green on 24802055.
        [Fact]
        public void HitAccessorsFollowTheColliderThroughTheInstanceID()
        {
            var body = new GameObject("Body");
            m_Made.Add(body);
            body.transform.position = Origin + new Vector3(0f, 0f, 20f);
            var rigidbody = body.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            var child = new GameObject("Child");
            m_Made.Add(child);
            child.transform.SetParent(body.transform, false);
            child.transform.localPosition = new Vector3(0f, 1f, 0f);
            var attached = child.AddComponent<BoxCollider>();
            var loose = Box("Loose", Origin + new Vector3(0f, 0f, 25f));

            var stored = Native<RaycastHit>(2);
            Assert.True(Physics.Raycast(Before(attached.transform.position), Vector3.forward, out var onBody, Reach, Physics.AllLayers));
            Assert.True(Physics.Raycast(Before(loose.transform.position), Vector3.forward, out var onLoose, Reach, Physics.AllLayers));
            stored[0] = onBody;
            stored[1] = onLoose;

            var a = stored[0];
            Assert.Same(attached, a.collider);
            Assert.Equal(attached.GetInstanceID(), a.colliderInstanceID);
            Assert.Same(rigidbody, a.rigidbody);
            Assert.Same(body.transform, a.transform);
            // Not a MeshCollider: no texture coordinates; no Renderer: the lightmap coordinate is
            // the second channel's.
            Assert.Equal(Vector2.zero, a.textureCoord);
            Assert.Equal(Vector2.zero, a.textureCoord2);
            Assert.Equal(Vector2.zero, a.lightmapCoord);

            var b = stored[1];
            Assert.Same(loose, b.collider);
            Assert.Null(b.rigidbody);
            Assert.Same(loose.transform, b.transform);
        }

        // Unbound's choice (unmeasured), asserted: a hit on a collider destroyed since reads null.
        // Red on 29030429: compiles and needs no NativeArray; collider returns the destroyed
        // Collider object (a non-null reference), so Assert.Null(hit.collider) fails. Green on
        // 24802055.
        [Fact]
        public void EmptyAndDestroyedCollidersReadNull()
        {
            RaycastHit empty = default;
            Assert.Null(empty.collider);
            Assert.Equal(0, empty.colliderInstanceID);
            Assert.Null(empty.transform);
            Assert.Null(empty.rigidbody);
            Assert.Equal(Vector2.zero, empty.textureCoord);
            // Kept as before: an empty hit's lightmap coordinate throws.
            Assert.Throws<NullReferenceException>(() => empty.lightmapCoord);

            var body = new GameObject("Doomed");
            m_Made.Add(body);
            body.transform.position = Origin + new Vector3(0f, 0f, 30f);
            body.AddComponent<Rigidbody>().isKinematic = true;
            var doomed = body.AddComponent<BoxCollider>();
            int id = doomed.GetInstanceID();
            Assert.True(Physics.Raycast(Before(body.transform.position), Vector3.forward, out var hit, Reach, Physics.AllLayers));
            Assert.Same(doomed, hit.collider);

            Object.DestroyImmediate(doomed);

            Assert.Null(hit.collider);
            Assert.Equal(id, hit.colliderInstanceID);
            Assert.Null(hit.transform);
            Assert.Null(hit.rigidbody);
            Assert.Throws<NullReferenceException>(() => hit.lightmapCoord);
        }

        // Red on 29030429: allocation. On 24802055: the stub writes nothing, so command 0 has no
        // hits and the first Assert.Equal fails.
        [Fact]
        public void RaycastResultsTakeMaxHitsSlotsPerCommand()
        {
            var at = Origin + new Vector3(0f, 0f, 40f);
            var near = Box("Near", at);
            var far = Box("Far", at + new Vector3(0f, 0f, 3f));
            const int maxHits = 3;
            var commands = Native(
                new RaycastCommand(Before(at), Vector3.forward, All, Reach),                    // both
                new RaycastCommand(at + new Vector3(0f, 5f, 0f), Vector3.up, All, Reach),       // none
                new RaycastCommand(Before(at), Vector3.forward, All, 10f));                     // near only
            var results = Native<RaycastHit>(commands.Length * maxHits);

            RaycastCommand.ScheduleBatch(commands, results, 1, maxHits).Complete();

            var first = Hits(results, 0, maxHits);
            Assert.Equal(2, first.Count);
            Assert.Contains(near, first);
            Assert.Contains(far, first);
            Assert.Null(results[2].collider);
            Assert.Empty(Hits(results, 1, maxHits));
            Assert.Null(results[3].collider);
            Assert.Equal(new List<Collider> { near }, Hits(results, 2, maxHits));
            Assert.Null(results[7].collider);
            // Each hit is the one the single all-hits query reports for that collider.
            foreach (var single in Physics.RaycastAll(Before(at), Vector3.forward, Reach, Physics.AllLayers))
                for (int k = 0; k < 2; k++)
                    if (results[k].collider == single.collider) SameHit(single, results[k]);

            // Documented: the overload without maxHits uses maxHits 1, so command N is at index N.
            // Unbound's choice (unmeasured), asserted: that slot is what the single PhysicsScene
            // cast with an out hit answers (the closest hit).
            var one = Native<RaycastHit>(commands.Length);
            RaycastCommand.ScheduleBatch(commands, one, 1).Complete();
            Assert.True(Physics.defaultPhysicsScene.Raycast(Before(at), Vector3.forward, out var closest, Reach, Physics.AllLayers));
            SameHit(closest, one[0]);
            Assert.Same(near, one[0].collider);
            Assert.Null(one[1].collider);
            Assert.Same(near, one[2].collider);
        }

        /// <summary>A unit box target and a lane 0.8 m beside it: a ray along the lane misses the
        /// box, a cast shape of radius or half size above 0.3 m hits it.</summary>
        (BoxCollider Target, Vector3 Lane, Vector3 Away) ShapeLane(string name, float z)
        {
            var at = Origin + new Vector3(0f, 0f, z);
            var target = Box(name, at);
            var lane = Before(at) + new Vector3(0.8f, 0f, 0f);
            Assert.False(Physics.Raycast(lane, Vector3.forward, Reach, Physics.AllLayers));
            return (target, lane, Before(at) + new Vector3(3f, 0f, 0f));
        }

        // Red on 29030429: allocation. On 24802055: the stub writes nothing, so the first
        // Assert.Equal fails.
        [Fact]
        public void SpherecastCommandsCastTheirSphere()
        {
            var (target, lane, away) = ShapeLane("SphereTarget", 50f);
            var commands = Native(
                new SpherecastCommand(lane, 0.5f, Vector3.forward, All, Reach),
                new SpherecastCommand(away, 0.5f, Vector3.forward, All, Reach));
            var results = Native<RaycastHit>(4);
            SpherecastCommand.ScheduleBatch(commands, results, 1, 2).Complete();
            Assert.Equal(new List<Collider> { target }, Hits(results, 0, 2));
            Assert.Empty(Hits(results, 1, 2));

            var one = Native<RaycastHit>(2);
            SpherecastCommand.ScheduleBatch(commands, one, 1).Complete();
            Assert.True(Physics.defaultPhysicsScene.SphereCast(lane, 0.5f, Vector3.forward, out var single, Reach, Physics.AllLayers));
            SameHit(single, one[0]);
            Assert.Null(one[1].collider);
        }

        // Red on 29030429: allocation. On 24802055: the stub writes nothing, so the first
        // Assert.Equal fails.
        [Fact]
        public void BoxcastCommandsCastTheirOrientedBox()
        {
            var (target, lane, away) = ShapeLane("BoxTarget", 60f);
            // Long along z it misses at 0.8 m (0.05 m half width); turned 90 degrees about y the
            // same box is 1 m wide on x and reaches the target.
            var thin = new Vector3(0.05f, 0.05f, 1f);
            var turned = Quaternion.Euler(0f, 90f, 0f);
            var commands = Native(
                new BoxcastCommand(lane, new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, Vector3.forward, All, Reach),
                new BoxcastCommand(away, new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, Vector3.forward, All, Reach),
                new BoxcastCommand(lane, thin, Quaternion.identity, Vector3.forward, All, Reach),
                new BoxcastCommand(lane, thin, turned, Vector3.forward, All, Reach));
            var results = Native<RaycastHit>(8);
            BoxcastCommand.ScheduleBatch(commands, results, 2, 2).Complete();
            Assert.Equal(new List<Collider> { target }, Hits(results, 0, 2));
            Assert.Empty(Hits(results, 1, 2));
            Assert.Empty(Hits(results, 2, 2));
            Assert.Equal(new List<Collider> { target }, Hits(results, 3, 2));

            var one = Native<RaycastHit>(4);
            BoxcastCommand.ScheduleBatch(commands, one, 1).Complete();
            Assert.True(Physics.defaultPhysicsScene.BoxCast(lane, thin, Vector3.forward, out var single, turned, Reach, Physics.AllLayers));
            SameHit(single, one[3]);
        }

        // Red on 29030429: allocation. On 24802055: the stub writes nothing, so the first
        // Assert.Equal fails.
        [Fact]
        public void CapsulecastCommandsCastTheirCapsule()
        {
            var (target, lane, away) = ShapeLane("CapsuleTarget", 70f);
            var up = new Vector3(0f, 1f, 0f);
            var commands = Native(
                new CapsulecastCommand(lane + up, lane - up, 0.4f, Vector3.forward, All, Reach),
                new CapsulecastCommand(away + up, away - up, 0.4f, Vector3.forward, All, Reach));
            var results = Native<RaycastHit>(4);
            CapsulecastCommand.ScheduleBatch(commands, results, 1, 2).Complete();
            Assert.Equal(new List<Collider> { target }, Hits(results, 0, 2));
            Assert.Empty(Hits(results, 1, 2));

            var one = Native<RaycastHit>(2);
            CapsulecastCommand.ScheduleBatch(commands, one, 1).Complete();
            Assert.True(Physics.defaultPhysicsScene.CapsuleCast(lane + up, lane - up, 0.4f, Vector3.forward, out var single, Reach, Physics.AllLayers));
            SameHit(single, one[0]);
            Assert.Null(one[1].collider);
        }

        // Red on 29030429: allocation. On 24802055: the stub writes nothing, so the layer-8 command
        // reports no collider.
        [Fact]
        public void LayerMaskAndTriggerParametersFilterCasts()
        {
            var at = Origin + new Vector3(0f, 0f, 80f);
            var trigger = Box("CastTrigger", at, layer: 0, trigger: true);
            var solid = Box("CastSolid", at + new Vector3(0f, 0f, 3f), layer: 8);
            var from = Before(at);
            var commands = Native(
                new RaycastCommand(from, Vector3.forward, Mask(1 << 8), Reach),
                new RaycastCommand(from, Vector3.forward, Mask(1 << 0, QueryTriggerInteraction.Collide), Reach),
                new RaycastCommand(from, Vector3.forward, Mask(Physics.AllLayers, QueryTriggerInteraction.Ignore), Reach),
                new RaycastCommand(from, Vector3.forward, Mask(Physics.AllLayers, QueryTriggerInteraction.Collide), Reach),
                new RaycastCommand(from, Vector3.forward, Mask(Physics.AllLayers, QueryTriggerInteraction.UseGlobal), Reach));
            var results = Native<RaycastHit>(commands.Length * 2);

            // UseGlobal follows Physics.queriesHitTriggers, as the single queries do.
            Physics.queriesHitTriggers = false;
            RaycastCommand.ScheduleBatch(commands, results, 2, 2).Complete();

            Assert.Equal(new List<Collider> { solid }, Hits(results, 0, 2));
            Assert.Equal(new List<Collider> { trigger }, Hits(results, 1, 2));
            Assert.Equal(new List<Collider> { solid }, Hits(results, 2, 2));
            var both = Hits(results, 3, 2);
            Assert.Equal(2, both.Count);
            Assert.Contains(solid, both);
            Assert.Contains(trigger, both);
            Assert.Equal(new List<Collider> { solid }, Hits(results, 4, 2));

            var spheres = Native(new SpherecastCommand(from, 0.3f, Vector3.forward, Mask(1 << 8), Reach));
            var sphereResults = Native<RaycastHit>(2);
            SpherecastCommand.ScheduleBatch(spheres, sphereResults, 1, 2).Complete();
            Assert.Equal(new List<Collider> { solid }, Hits(sphereResults, 0, 2));
        }

        struct WriteRaycastCommand : IJob
        {
            public NativeArray<RaycastCommand> Commands;
            public Vector3 From;

            public void Execute() =>
                Commands[0] = new RaycastCommand(From, Vector3.forward, new QueryParameters(Physics.AllLayers, false, QueryTriggerInteraction.UseGlobal, false), Reach);
        }

        // Red on 29030429: allocation. On 24802055: the stub writes nothing, so results[0] has no
        // collider.
        [Fact]
        public void CastBatchRunsAfterItsDependency()
        {
            var at = Origin + new Vector3(0f, 0f, 90f);
            var target = Box("CastDependency", at);
            // A zeroed command (no direction, layer mask 0) finds nothing until the job writes it.
            var commands = Native<RaycastCommand>(1);
            var results = Native<RaycastHit>(1);
            var writer = new WriteRaycastCommand { Commands = commands, From = Before(at) }.Schedule();
            try
            {
                var handle = RaycastCommand.ScheduleBatch(commands, results, 1, writer);
                handle.Complete();
                Assert.True(writer.IsCompleted);
                Assert.Same(target, results[0].collider);
            }
            finally
            {
                writer.Complete();
            }
        }

        // Red on 29030429: allocation. On 24802055: the stub writes nothing, so the local command
        // reports no collider.
        [Fact]
        public void CastCommandsSearchTheirOwnPhysicsScene()
        {
            var at = Origin + new Vector3(0f, 0f, 100f);
            var scene = SceneManager.CreateScene("CastLocalPhysics" + s_Created++, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            m_Scenes.Add(scene);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            Collider local;
            try { local = Box("LocalCastTarget", at); }
            finally { SceneManager.SetActiveScene(previous); }
            var shared = Box("DefaultCastTarget", at);
            var physics = scene.GetPhysicsScene();
            Assert.NotEqual(Physics.defaultPhysicsScene, physics);

            var rays = Native(
                new RaycastCommand(physics, Before(at), Vector3.forward, All, Reach),
                new RaycastCommand(Before(at), Vector3.forward, All, Reach));
            var rayResults = Native<RaycastHit>(4);
            RaycastCommand.ScheduleBatch(rays, rayResults, 1, 2).Complete();
            Assert.Equal(new List<Collider> { local }, Hits(rayResults, 0, 2));
            Assert.Equal(new List<Collider> { shared }, Hits(rayResults, 1, 2));

            var boxes = Native(
                new BoxcastCommand(physics, Before(at), new Vector3(0.2f, 0.2f, 0.2f), Quaternion.identity, Vector3.forward, All, Reach),
                new BoxcastCommand(Before(at), new Vector3(0.2f, 0.2f, 0.2f), Quaternion.identity, Vector3.forward, All, Reach));
            var boxResults = Native<RaycastHit>(2);
            BoxcastCommand.ScheduleBatch(boxes, boxResults, 1).Complete();
            Assert.Same(local, boxResults[0].collider);
            Assert.Same(shared, boxResults[1].collider);
        }

        // Unbound's choice (unmeasured), asserted: hitBackfaces and hitMultipleFaces are not
        // supported by the managed casts, and a batch asking for hitBackfaces on any command, or
        // hitMultipleFaces on a RaycastCommand, throws NotSupportedException before writing
        // anything, every time. The shape casts accept hitMultipleFaces (see
        // ShapeCastsAnswerHitMultipleFacesAsWithoutIt), but not together with hitBackfaces.
        // Red on 29030429: allocation. On 24802055: the stubs throw nothing, so the first
        // Assert.Throws fails. Green on 916304b3 and the head (a regression control for them).
        [Fact]
        public void FaceOptionsAreRefusedBeforeAnyResult()
        {
            var at = Origin + new Vector3(0f, 0f, 110f);
            Box("FaceTarget", at);
            var from = Before(at);
            var backfaces = new QueryParameters(Physics.AllLayers, false, QueryTriggerInteraction.UseGlobal, true);
            var multiple = new QueryParameters(Physics.AllLayers, true, QueryTriggerInteraction.UseGlobal, false);
            var half = new Vector3(0.2f, 0.2f, 0.2f);

            void Refused(string option, Func<NativeArray<RaycastHit>, JobHandle> schedule)
            {
                var results = Native<RaycastHit>(4);
                for (int i = 0; i < results.Length; i++) results[i] = Sentinel(i);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    var error = Assert.Throws<NotSupportedException>(() => schedule(results));
                    Assert.Contains(option, error.Message);
                    Assert.Contains("command 1", error.Message);
                }
                StillSentinels(results);
            }

            // The flagged command comes second: the valid first command is not answered either.
            var rays = Native(new RaycastCommand(from, Vector3.forward, All, Reach), new RaycastCommand(from, Vector3.forward, backfaces, Reach));
            Refused("hitBackfaces", r => RaycastCommand.ScheduleBatch(rays, r, 1, 2));
            var multiRays = Native(new RaycastCommand(from, Vector3.forward, All, Reach), new RaycastCommand(from, Vector3.forward, multiple, Reach));
            Refused("hitMultipleFaces", r => RaycastCommand.ScheduleBatch(multiRays, r, 1, 2));
            Refused("hitMultipleFaces", r => RaycastCommand.ScheduleBatch(multiRays, r, 1));

            var spheres = Native(new SpherecastCommand(from, 0.2f, Vector3.forward, All, Reach), new SpherecastCommand(from, 0.2f, Vector3.forward, backfaces, Reach));
            Refused("hitBackfaces", r => SpherecastCommand.ScheduleBatch(spheres, r, 1, 2));
            var boxes = Native(new BoxcastCommand(from, half, Quaternion.identity, Vector3.forward, All, Reach), new BoxcastCommand(from, half, Quaternion.identity, Vector3.forward, backfaces, Reach));
            Refused("hitBackfaces", r => BoxcastCommand.ScheduleBatch(boxes, r, 1, 2));
            var up = new Vector3(0f, 0.2f, 0f);
            var capsules = Native(new CapsulecastCommand(from + up, from - up, 0.2f, Vector3.forward, All, Reach), new CapsulecastCommand(from + up, from - up, 0.2f, Vector3.forward, backfaces, Reach));
            Refused("hitBackfaces", r => CapsulecastCommand.ScheduleBatch(capsules, r, 1, 2));

            // hitBackfaces stays refused on the shape casts when hitMultipleFaces, which they
            // accept, is set as well.
            var both = new QueryParameters(Physics.AllLayers, true, QueryTriggerInteraction.UseGlobal, true);
            var bothSpheres = Native(new SpherecastCommand(from, 0.2f, Vector3.forward, All, Reach), new SpherecastCommand(from, 0.2f, Vector3.forward, both, Reach));
            Refused("hitBackfaces", r => SpherecastCommand.ScheduleBatch(bothSpheres, r, 1, 2));
            var bothBoxes = Native(new BoxcastCommand(from, half, Quaternion.identity, Vector3.forward, All, Reach), new BoxcastCommand(from, half, Quaternion.identity, Vector3.forward, both, Reach));
            Refused("hitBackfaces", r => BoxcastCommand.ScheduleBatch(bothBoxes, r, 1, 2));
            var bothCapsules = Native(new CapsulecastCommand(from + up, from - up, 0.2f, Vector3.forward, All, Reach), new CapsulecastCommand(from + up, from - up, 0.2f, Vector3.forward, both, Reach));
            Refused("hitBackfaces", r => CapsulecastCommand.ScheduleBatch(bothCapsules, r, 1, 2));
        }

        // Documented basis (6000.0 QueryParameters Remarks): "Only RaycastCommand supports hitting
        // multiple Mesh faces". Inferred from that API scope, not a measured Unity result: the
        // shape casts accept hitMultipleFaces and answer each command as they answer it without
        // the flag. Each batch pairs a flag-free command with the same command flagged, on a lane
        // that hits the target and on one that hits nothing, with maxHits 1 and 2; one target per
        // lane, so no hit order is involved.
        // Red on 916304b3: the first SpherecastCommand.ScheduleBatch throws NotSupportedException
        // (hitMultipleFaces, command 1). Red on 29030429: allocation. On 24802055: the stub writes
        // nothing, so the first Assert.Same fails.
        [Fact]
        public void ShapeCastsAnswerHitMultipleFacesAsWithoutIt()
        {
            var (target, lane, away) = ShapeLane("MultipleFacesTarget", 150f);
            var multiple = new QueryParameters(Physics.AllLayers, true, QueryTriggerInteraction.UseGlobal, false);

            void AnsweredAsWithout(Func<NativeArray<RaycastHit>, int, JobHandle> schedule)
            {
                foreach (int maxHits in new[] { 1, 2 })
                {
                    // Commands: lane, lane flagged, away, away flagged.
                    var results = Native<RaycastHit>(4 * maxHits);
                    schedule(results, maxHits).Complete();
                    Assert.Same(target, results[0].collider);
                    Assert.Null(results[2 * maxHits].collider);
                    for (int pair = 0; pair < 2; pair++)
                        for (int k = 0; k < maxHits; k++)
                            SameHit(results[2 * pair * maxHits + k], results[(2 * pair + 1) * maxHits + k]);
                }
            }

            var spheres = Native(
                new SpherecastCommand(lane, 0.5f, Vector3.forward, All, Reach),
                new SpherecastCommand(lane, 0.5f, Vector3.forward, multiple, Reach),
                new SpherecastCommand(away, 0.5f, Vector3.forward, All, Reach),
                new SpherecastCommand(away, 0.5f, Vector3.forward, multiple, Reach));
            AnsweredAsWithout((r, maxHits) => SpherecastCommand.ScheduleBatch(spheres, r, 1, maxHits));

            var half = new Vector3(0.5f, 0.5f, 0.5f);
            var boxes = Native(
                new BoxcastCommand(lane, half, Quaternion.identity, Vector3.forward, All, Reach),
                new BoxcastCommand(lane, half, Quaternion.identity, Vector3.forward, multiple, Reach),
                new BoxcastCommand(away, half, Quaternion.identity, Vector3.forward, All, Reach),
                new BoxcastCommand(away, half, Quaternion.identity, Vector3.forward, multiple, Reach));
            AnsweredAsWithout((r, maxHits) => BoxcastCommand.ScheduleBatch(boxes, r, 1, maxHits));

            var up = new Vector3(0f, 1f, 0f);
            var capsules = Native(
                new CapsulecastCommand(lane + up, lane - up, 0.4f, Vector3.forward, All, Reach),
                new CapsulecastCommand(lane + up, lane - up, 0.4f, Vector3.forward, multiple, Reach),
                new CapsulecastCommand(away + up, away - up, 0.4f, Vector3.forward, All, Reach),
                new CapsulecastCommand(away + up, away - up, 0.4f, Vector3.forward, multiple, Reach));
            AnsweredAsWithout((r, maxHits) => CapsulecastCommand.ScheduleBatch(capsules, r, 1, maxHits));
        }

        // The documented requirement is that a wrong batch is not run; the exception types and the
        // "nothing written" property are Unbound's choice (unmeasured), as for the overlap commands.
        // Red on 29030429: allocation. On 24802055: the stubs throw nothing, so the first
        // Assert.Throws fails.
        [Fact]
        public void WrongCastBatchesThrowBeforeWritingResults()
        {
            var at = Origin + new Vector3(0f, 0f, 120f);
            Box("WrongTarget", at);
            var commands = Native(
                new RaycastCommand(Before(at), Vector3.forward, All, Reach),
                new RaycastCommand(Before(at), Vector3.forward, All, Reach));
            var tooSmall = Native<RaycastHit>(3);
            for (int i = 0; i < tooSmall.Length; i++) tooSmall[i] = Sentinel(i);

            Assert.Throws<ArgumentException>(() => RaycastCommand.ScheduleBatch(commands, tooSmall, 1, 2));
            Assert.ThrowsAny<ArgumentException>(() => RaycastCommand.ScheduleBatch(commands, tooSmall, 1, 0));
            var shapes = Native(new SpherecastCommand(Before(at), 0.2f, Vector3.forward, All, Reach), new SpherecastCommand(Before(at), 0.2f, Vector3.forward, All, Reach));
            var oneSlot = Native<RaycastHit>(1);
            oneSlot[0] = Sentinel(0);
            Assert.Throws<ArgumentException>(() => SpherecastCommand.ScheduleBatch(shapes, oneSlot, 1));
            Assert.Equal(-7f, oneSlot[0].distance);
            StillSentinels(tooSmall);

            var unassigned = Assert.Throws<InvalidOperationException>(() => RaycastCommand.ScheduleBatch(commands, default, 1, 1));
            Assert.Contains("has not been assigned or constructed", unassigned.Message);
            var disposed = new NativeArray<RaycastCommand>(1, Allocator.TempJob);
            disposed.Dispose();
            var deallocated = Assert.Throws<InvalidOperationException>(() => RaycastCommand.ScheduleBatch(disposed, tooSmall, 1, 1));
            Assert.Contains("has been deallocated", deallocated.Message);
        }

        // Unbound's choice (unmeasured), asserted: the obsolete signatures put their layer mask
        // into QueryParameters and keep every other QueryParameters constructor default; the
        // obsolete per-command maxHits is kept but ScheduleBatch uses its own maxHits argument.
        // Red on 29030429 and 24802055: the stub constructors assign nothing and the stub
        // layerMask getter returns 0, so the first Assert.Equal fails.
        [Fact]
        public void ObsoleteSignaturesKeepTheirLayerMask()
        {
#pragma warning disable CS0618
            var at = Origin + new Vector3(0f, 0f, 130f);
            var ray = new RaycastCommand(Before(at), Vector3.forward, Reach, 1 << 8, 4);
            Assert.Equal(1 << 8, ray.layerMask);
            Assert.Equal(1 << 8, ray.queryParameters.layerMask);
            Assert.Equal(QueryTriggerInteraction.UseGlobal, ray.queryParameters.hitTriggers);
            Assert.False(ray.queryParameters.hitBackfaces);
            Assert.False(ray.queryParameters.hitMultipleFaces);
            Assert.Equal(4, ray.maxHits);
            Assert.Equal(Reach, ray.distance);
            Assert.Equal(Physics.defaultPhysicsScene, ray.physicsScene);
            ray.layerMask = 1 << 3;
            Assert.Equal(1 << 3, ray.queryParameters.layerMask);

            var sphere = new SpherecastCommand(Before(at), 0.5f, Vector3.forward, Reach, 1 << 9);
            Assert.Equal(1 << 9, sphere.queryParameters.layerMask);
            var box = new BoxcastCommand(Before(at), Vector3.one, Quaternion.identity, Vector3.forward, Reach, 1 << 10);
            Assert.Equal(1 << 10, box.queryParameters.layerMask);
            var capsule = new CapsulecastCommand(Before(at), Before(at) + Vector3.up, 0.5f, Vector3.forward, Reach, 1 << 11);
            Assert.Equal(1 << 11, capsule.queryParameters.layerMask);
            Assert.Equal(Before(at), capsule.point1);
            Assert.Equal(Before(at) + Vector3.up, capsule.point2);

            // Documented signature default: the full distance.
            Assert.Equal(float.MaxValue, new RaycastCommand(Before(at), Vector3.forward).distance);

            var target = Box("ObsoleteTarget", at);
            var commands = Native(new RaycastCommand(Before(at), Vector3.forward, Reach, Physics.AllLayers, 4));
            var results = Native<RaycastHit>(1);
            RaycastCommand.ScheduleBatch(commands, results, 1).Complete();
            Assert.Same(target, results[0].collider);
#pragma warning restore CS0618
        }

        // A regression control, not a red: green on 29030429, 24802055 and the head. The single
        // queries keep their hits, identity and geometry, and the overlap command keeps its result.
        [Fact]
        public void ExistingQueriesAreUnchanged()
        {
            var at = Origin + new Vector3(0f, 0f, 140f);
            var target = Box("Control", at);
            var from = Before(at);
            Assert.True(Physics.Raycast(from, Vector3.forward, out var hit, Reach, Physics.AllLayers));
            Assert.Same(target, hit.collider);
            Assert.Equal(target.GetInstanceID(), hit.colliderInstanceID);
            Assert.Same(target.transform, hit.transform);
            Assert.True(Mathf.Abs(hit.distance - 9.5f) < 1e-4f, $"distance {hit.distance}");
            Assert.True((hit.point - (at - new Vector3(0f, 0f, 0.5f))).magnitude < 1e-4f, $"point {hit.point}");
            Assert.True((hit.normal - Vector3.back).magnitude < 1e-4f, $"normal {hit.normal}");

            var all = Physics.RaycastAll(from, Vector3.forward, Reach, Physics.AllLayers);
            Assert.Single(all);
            Assert.Same(target, all[0].collider);
            var buffer = new RaycastHit[2];
            Assert.Equal(1, Physics.RaycastNonAlloc(from, Vector3.forward, buffer, Reach, Physics.AllLayers));
            Assert.Same(target, buffer[0].collider);
            Assert.True(Physics.SphereCast(from, 0.2f, Vector3.forward, out var sphereHit, Reach, Physics.AllLayers));
            Assert.Same(target, sphereHit.collider);

            var overlaps = Native(new OverlapSphereCommand(at, 1f, All));
            var overlapResults = Native<ColliderHit>(1);
            OverlapSphereCommand.ScheduleBatch(overlaps, overlapResults, 1, 1).Complete();
            Assert.Same(target, overlapResults[0].collider);
            Assert.Equal(target.GetInstanceID(), overlapResults[0].instanceID);
        }
    }
}
