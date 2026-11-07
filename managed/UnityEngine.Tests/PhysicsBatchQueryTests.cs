using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// 3D batch queries (6000.0 Scripting API: QueryParameters, ColliderHit, OverlapSphereCommand,
    /// OverlapBoxCommand, OverlapCapsuleCommand, ClosestPointCommand and their ScheduleBatch).
    /// Documented and asserted: a command's results start at index N * maxHits, the first unused
    /// result of a command has collider instance ID 0 (no collider), a ClosestPointCommand's result is at index N, the
    /// results are complete once the returned handle is complete, a command searches its own
    /// physicsScene, and QueryParameters' constructor defaults (from its public signature).
    /// Unbound's choices (unmeasured), asserted only where named: the batch checks throw before
    /// any result is written, with the exception types named in the test. The order of several
    /// colliders one command finds is not documented and is not asserted.
    /// Measured controls: tests/projects/physicsbatch, Unity 6000.0.71f1.
    /// </summary>
    public class PhysicsBatchQueryTests : IDisposable
    {
        // Far from the origin, away from anything other tests leave in the default physics scene.
        static readonly Vector3 Origin = new Vector3(700f, 0f, 0f);
        static int s_Created;
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<Scene> m_Scenes = new List<Scene>();
        readonly List<IDisposable> m_Arrays = new List<IDisposable>();
        readonly bool m_HitTriggers;
        readonly bool m_AutoSync;
        readonly SimulationMode m_Mode;
        readonly Scene m_Active;

        public PhysicsBatchQueryTests()
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

        Collider Sphere(string name, Vector3 position, float radius = 0.5f, int layer = 0, bool trigger = false)
        {
            var go = new GameObject(name) { layer = layer };
            m_Made.Add(go);
            go.transform.position = position;
            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = radius;
            sphere.isTrigger = trigger;
            return sphere;
        }

        Collider Box(string name, Vector3 position)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            go.transform.position = position;
            return go.AddComponent<BoxCollider>();
        }

        static QueryParameters Mask(int layerMask, QueryTriggerInteraction triggers = QueryTriggerInteraction.UseGlobal) =>
            new QueryParameters(layerMask, false, triggers, false);

        /// <summary>The colliders of a command's results, up to its first unused result (the
        /// documented end of a command's results).</summary>
        static List<Collider> Hits(NativeArray<ColliderHit> results, int command, int maxHits)
        {
            var hits = new List<Collider>();
            for (int k = 0; k < maxHits; k++)
            {
                var hit = results[command * maxHits + k];
                if (hit.collider == null) break;
                Assert.Equal(hit.collider.GetInstanceID(), hit.instanceID);
                hits.Add(hit.collider);
            }
            return hits;
        }

        // Red on bf016b60: compiles; ScheduleBatch is a stub that writes nothing and ColliderHit.collider
        // a stub returning null, so command 0 reports no colliders and the first Assert.Equal fails.
        [Fact]
        public void OverlapResultsTakeMaxHitsSlotsPerCommand()
        {
            var a = Sphere("A", Origin);
            var b = Sphere("B", Origin + new Vector3(1.5f, 0f, 0f));
            const int maxHits = 3;
            var all = Mask(Physics.AllLayers);
            var commands = Native(
                new OverlapSphereCommand(Origin + new Vector3(0.75f, 0f, 0f), 2f, all), // both
                new OverlapSphereCommand(Origin + new Vector3(0f, 50f, 0f), 1f, all),    // none
                new OverlapSphereCommand(Origin, 0.6f, all));                            // A only
            var results = Native<ColliderHit>(commands.Length * maxHits);

            OverlapSphereCommand.ScheduleBatch(commands, results, 1, maxHits).Complete();

            var first = Hits(results, 0, maxHits);
            Assert.Equal(2, first.Count);
            Assert.Contains(a, first);
            Assert.Contains(b, first);
            Assert.Null(results[2].collider);
            Assert.Empty(Hits(results, 1, maxHits));
            Assert.Null(results[3].collider);
            Assert.Equal(new List<Collider> { a }, Hits(results, 2, maxHits));
            Assert.Null(results[7].collider);
        }

        // Red on bf016b60: compiles; ScheduleBatch writes nothing, so the box command reports no collider.
        [Fact]
        public void OverlapBoxAndCapsuleCommandsUseTheirShapes()
        {
            var a = Sphere("BoxTarget", Origin + new Vector3(0f, 0f, 10f));
            var b = Sphere("CapsuleTarget", Origin + new Vector3(0f, 0f, 20f));
            var all = Mask(Physics.AllLayers);
            var boxes = Native(
                new OverlapBoxCommand(a.transform.position, new Vector3(0.6f, 0.6f, 0.6f), Quaternion.identity, all),
                new OverlapBoxCommand(Origin + new Vector3(0f, 0f, 15f), new Vector3(0.6f, 0.6f, 0.6f), Quaternion.identity, all));
            var boxResults = Native<ColliderHit>(4);
            OverlapBoxCommand.ScheduleBatch(boxes, boxResults, 1, 2).Complete();
            Assert.Equal(new List<Collider> { a }, Hits(boxResults, 0, 2));
            Assert.Empty(Hits(boxResults, 1, 2));

            var capsules = Native(
                new OverlapCapsuleCommand(b.transform.position + Vector3.up * 3f, b.transform.position - Vector3.up * 3f, 0.2f, all),
                new OverlapCapsuleCommand(Origin + new Vector3(3f, 3f, 20f), Origin + new Vector3(3f, -3f, 20f), 0.2f, all));
            var capsuleResults = Native<ColliderHit>(4);
            OverlapCapsuleCommand.ScheduleBatch(capsules, capsuleResults, 1, 2).Complete();
            Assert.Equal(new List<Collider> { b }, Hits(capsuleResults, 0, 2));
            Assert.Empty(Hits(capsuleResults, 1, 2));
        }

        // Red on bf016b60: compiles; ScheduleBatch writes nothing, so the layer-8 command reports no
        // collider and the first Assert.Equal fails.
        [Fact]
        public void LayerMaskAndTriggerParametersFilterOverlaps()
        {
            var solid = Sphere("Solid", Origin + new Vector3(0f, 0f, 30f), layer: 8);
            var trigger = Sphere("Trigger", Origin + new Vector3(0f, 0f, 30f), layer: 0, trigger: true);
            var at = solid.transform.position;
            var commands = Native(
                new OverlapSphereCommand(at, 1f, Mask(1 << 8)),
                new OverlapSphereCommand(at, 1f, Mask(1 << 0, QueryTriggerInteraction.Collide)),
                new OverlapSphereCommand(at, 1f, Mask(Physics.AllLayers, QueryTriggerInteraction.Ignore)),
                new OverlapSphereCommand(at, 1f, Mask(Physics.AllLayers, QueryTriggerInteraction.Collide)),
                new OverlapSphereCommand(at, 1f, Mask(Physics.AllLayers, QueryTriggerInteraction.UseGlobal)));
            var results = Native<ColliderHit>(commands.Length * 2);

            // UseGlobal follows Physics.queriesHitTriggers, as the single queries do.
            Physics.queriesHitTriggers = false;
            OverlapSphereCommand.ScheduleBatch(commands, results, 2, 2).Complete();

            Assert.Equal(new List<Collider> { solid }, Hits(results, 0, 2));
            Assert.Equal(new List<Collider> { trigger }, Hits(results, 1, 2));
            Assert.Equal(new List<Collider> { solid }, Hits(results, 2, 2));
            var both = Hits(results, 3, 2);
            Assert.Equal(2, both.Count);
            Assert.Contains(solid, both);
            Assert.Contains(trigger, both);
            Assert.Equal(new List<Collider> { solid }, Hits(results, 4, 2));
        }

        // Red on bf016b60: compiles; the stub constructor assigns no field, so layerMask is 0, not -5,
        // and the explicit hitBackfaces/hitMultipleFaces values read back false.
        [Fact]
        public void QueryParametersKeepTheirValuesAndSignatureDefaults()
        {
            // The defaults are those of the constructor's public signature.
            var defaults = new QueryParameters(hitTriggers: QueryTriggerInteraction.Ignore);
            Assert.Equal(Physics.DefaultRaycastLayers, defaults.layerMask);
            Assert.False(defaults.hitMultipleFaces);
            Assert.Equal(QueryTriggerInteraction.Ignore, defaults.hitTriggers);
            Assert.False(defaults.hitBackfaces);

            var given = new QueryParameters(1 << 5, true, QueryTriggerInteraction.Collide, true);
            Assert.Equal(1 << 5, given.layerMask);
            Assert.True(given.hitMultipleFaces);
            Assert.Equal(QueryTriggerInteraction.Collide, given.hitTriggers);
            Assert.True(given.hitBackfaces);
        }

        // Red on bf016b60: compiles; ScheduleBatch writes nothing, so results[0] stays (0, 0, 0).
        [Fact]
        public void ClosestPointResultsAreAtTheCommandsIndex()
        {
            var box = Box("ClosestBox", Origin + new Vector3(0f, 0f, 40f));
            var t = box.transform;
            var outside = t.position + new Vector3(5f, 0f, 0f);
            var above = t.position + new Vector3(0f, 3f, 0f);
            var commands = Native(
                new ClosestPointCommand(outside, box.GetInstanceID(), t.position, t.rotation, t.lossyScale),
                new ClosestPointCommand(above, box, t.position, t.rotation, t.lossyScale));
            var results = Native<Vector3>(2);

            ClosestPointCommand.ScheduleBatch(commands, results, 1).Complete();

            Assert.True((results[0] - (t.position + new Vector3(0.5f, 0f, 0f))).magnitude < 1e-3f, $"results[0] = {results[0]}");
            Assert.True((results[1] - (t.position + new Vector3(0f, 0.5f, 0f))).magnitude < 1e-3f, $"results[1] = {results[1]}");
            // The same answer as the single query.
            Assert.True((results[0] - Physics.ClosestPoint(outside, box, t.position, t.rotation)).magnitude < 1e-5f);
        }

        [Fact]
        public void ClosestPointIgnoresCommandScaleAndUsesTheCollidersGeometry()
        {
            var box = Box("ScaleControl",Origin+Vector3.forward*45);
            box.transform.localScale=new Vector3(2,3,4);
            var at=box.transform.position;
            var point=at+Vector3.right*5;
            var commands=Native(
                new ClosestPointCommand(point,box,at,Quaternion.identity,Vector3.zero),
                new ClosestPointCommand(point,box,at,Quaternion.identity,-Vector3.one),
                new ClosestPointCommand(point,box,at,Quaternion.identity,Vector3.one*7));
            var results=Native<Vector3>(3);
            ClosestPointCommand.ScheduleBatch(commands,results,1).Complete();
            var expected=Physics.ClosestPoint(point,box,at,Quaternion.identity);
            for(int i=0;i<3;i++)Assert.Equal(expected,results[i]);
            Assert.Equal(at+Vector3.right,expected);
        }

        [Fact]
        public void MissingClosestPointCollidersDoNotAbortOtherCommands()
        {
            var box=Box("MissingControl",Origin+Vector3.forward*47);
            var at=box.transform.position;
            var point=at+Vector3.right*4;
            var commands=Native(
                new ClosestPointCommand(point,box,at,Quaternion.identity,Vector3.one),
                new ClosestPointCommand(point,0,at,Quaternion.identity,Vector3.one),
                new ClosestPointCommand(point,box,at,Quaternion.identity,Vector3.one));
            var results=Native<Vector3>(3);
            ClosestPointCommand.ScheduleBatch(commands,results,1).Complete();
            Assert.Equal(at+Vector3.right*.5f,results[0]);
            Assert.Equal(point,results[1]);
            Assert.Equal(results[0],results[2]);
        }

        [Fact]
        public void LargeOverlapResultsStreamPastTheFormerTouchCeiling()
        {
            const int count=4105;
            var at=Origin+Vector3.forward*200;
            var expected=new HashSet<Collider>();
            for(int i=0;i<count;i++)expected.Add(Sphere("Crowd"+i,at+Vector3.up*(i%3)*.01f));
            var commands=Native(new OverlapSphereCommand(at,1f,Mask(Physics.AllLayers)));
            var results=Native<ColliderHit>(count+1);
            OverlapSphereCommand.ScheduleBatch(commands,results,1,count+1).Complete();
            var actual=new HashSet<Collider>();
            for(int i=0;i<count;i++)Assert.True(actual.Add(results[i].collider));
            Assert.True(expected.SetEquals(actual));
            Assert.Equal(0,results[count].instanceID);
            Assert.Equal(count,Physics.OverlapSphere(at,1f,Physics.AllLayers).Length);
        }

        // Measured Unity 6000.0.71f1 capacity checks warn and return without running commands.
        [Fact]
        public void WrongBatchesReturnBeforeWritingResults()
        {
            var target = Sphere("Checked", Origin + new Vector3(0f, 0f, 50f));
            var all = Mask(Physics.AllLayers);
            var commands = Native(
                new OverlapSphereCommand(target.transform.position, 1f, all),
                new OverlapSphereCommand(target.transform.position, 1f, all));
            var tooSmall = Native<ColliderHit>(3);

            OverlapSphereCommand.ScheduleBatch(commands, tooSmall, 1, 2).Complete();
            OverlapSphereCommand.ScheduleBatch(commands, tooSmall, 1, 0).Complete();
            OverlapSphereCommand.ScheduleBatch(commands, default, 1, 1).Complete();

            var disposed = new NativeArray<OverlapSphereCommand>(1, Allocator.TempJob);
            disposed.Dispose();
            var deallocated = Assert.Throws<InvalidOperationException>(() => OverlapSphereCommand.ScheduleBatch(disposed, tooSmall, 1, 1));
            Assert.Contains("has been deallocated", deallocated.Message);

            var box = Box("CheckedBox", Origin + new Vector3(0f, 0f, 55f));
            var t = box.transform;
            var points = Native(
                new ClosestPointCommand(t.position + Vector3.right * 5f, box, t.position, t.rotation, t.lossyScale),
                new ClosestPointCommand(t.position + Vector3.up * 5f, box, t.position, t.rotation, t.lossyScale));
            var sentinel = new Vector3(9f, 9f, 9f);
            var shortPoints = Native(sentinel);
            ClosestPointCommand.ScheduleBatch(points, shortPoints, 1).Complete();
            Assert.Equal(sentinel, shortPoints[0]);
        }

        struct WriteOverlapCommand : IJob
        {
            public NativeArray<OverlapSphereCommand> Commands;
            public Vector3 Point;

            public void Execute() =>
                Commands[0] = new OverlapSphereCommand(Point, 1f, new QueryParameters(Physics.AllLayers, false, QueryTriggerInteraction.UseGlobal, false));
        }

        // Red on bf016b60: compiles; ScheduleBatch writes nothing, so results[0] has no collider.
        [Fact]
        public void BatchRunsAfterItsDependency()
        {
            var target = Sphere("Dependency", Origin + new Vector3(0f, 0f, 60f));
            // Zeroed commands: layer mask 0, nothing found until the job writes the real command.
            var commands = Native<OverlapSphereCommand>(1);
            var results = Native<ColliderHit>(1);
            var writer = new WriteOverlapCommand { Commands = commands, Point = target.transform.position }.Schedule();
            try
            {
                var handle = OverlapSphereCommand.ScheduleBatch(commands, results, 1, 1, writer);
                handle.Complete();
                Assert.True(writer.IsCompleted);
                Assert.Equal(target, results[0].collider);
            }
            finally
            {
                writer.Complete();
            }
        }

        // Red on bf016b60: compiles; ScheduleBatch writes nothing, so the local command reports no collider.
        [Fact]
        public void CommandsSearchTheirOwnPhysicsScene()
        {
            var at = Origin + new Vector3(0f, 0f, 70f);
            var scene = SceneManager.CreateScene("BatchLocalPhysics" + s_Created++, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            m_Scenes.Add(scene);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            Collider local;
            try { local = Sphere("LocalTarget", at); }
            finally { SceneManager.SetActiveScene(previous); }
            var shared = Sphere("DefaultTarget", at);
            var physics = scene.GetPhysicsScene();
            Assert.NotEqual(Physics.defaultPhysicsScene, physics);

            var all = Mask(Physics.AllLayers);
            var commands = Native(
                new OverlapSphereCommand(physics, at, 1f, all),
                new OverlapSphereCommand(at, 1f, all));
            var results = Native<ColliderHit>(4);
            OverlapSphereCommand.ScheduleBatch(commands, results, 1, 2).Complete();

            Assert.Equal(new List<Collider> { local }, Hits(results, 0, 2));
            Assert.Equal(new List<Collider> { shared }, Hits(results, 1, 2));
        }
    }
}
