using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Records every 3D contact and trigger message its GameObject gets, with the other
    /// collider's name ("CE:wall", "TS:visitor", ...).</summary>
    public class RebuildRecorder3D : MonoBehaviour
    {
        public readonly List<string> log = new List<string>();
        public Action whenEntered;
        void OnCollisionEnter(Collision c) { log.Add("CE:" + c.collider.name); }
        void OnCollisionStay(Collision c) { log.Add("CS:" + c.collider.name); }
        void OnCollisionExit(Collision c) { log.Add("CX:" + c.collider.name); }
        void OnTriggerEnter(Collider other) { log.Add("TE:" + other.name); whenEntered?.Invoke(); }
        void OnTriggerStay(Collider other) { log.Add("TS:" + other.name); }
        void OnTriggerExit(Collider other) { log.Add("TX:" + other.name); }
    }

    /// <summary>
    /// A collider whose shapes are destroyed and recreated while it stays in the simulation (a
    /// geometry, trigger or frame change, another attached Rigidbody) keeps one contact/trigger pair
    /// per collider pair. Unity 6000.0 documents Enter when colliders begin touching, Stay while they
    /// touch and Exit when they stop (MonoBehaviour.OnCollisionEnter/Stay/Exit, OnTriggerEnter/Stay/
    /// Exit), and no Exit for a deactivated or destroyed collider. A property change that keeps them
    /// touching is not a new touch, and one that ends the touch is an Exit, as BoxCollider's in-place
    /// changes already behave (the box rows and PhysicsTests' trigger conversion are those controls).
    /// What a Rigidbody attachment change sends while still touching is not measured: those tests
    /// check that the pair ends with its Exit and no further Stay once the colliders separate, and
    /// that its Enters and Exits are equal in number (derived from the documented Enter/Exit
    /// pairing, not measured; it does not tell continuity from an Exit and a new Enter).
    /// </summary>
    public class PhysicsShapeRebuildTests : IDisposable
    {
        const float Dt = 0.02f;
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<Object> m_Assets = new List<Object>();

        public PhysicsShapeRebuildTests()
        {
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            Physics.queriesHitTriggers = true;
            Physics.autoSyncTransforms = true;
            Physics.invokeCollisionCallbacks = true;
            for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
                Physics.IgnoreLayerCollision(i, j, false);
            Physics.simulationMode = SimulationMode.Script;
        }

        public void Dispose()
        {
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            foreach (var asset in m_Assets)
                if (asset != null) Object.DestroyImmediate(asset, true);
            Physics.Simulate(Dt);
        }

        GameObject Make(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            m_Made.Add(go);
            return go;
        }

        /// <summary>A dynamic body that neither moves nor sleeps.</summary>
        static Rigidbody Hold(GameObject go)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
            rb.sleepThreshold = 0f;
            return rb;
        }

        /// <summary>A held sphere of radius 0.5 that overlaps a unit collider at the origin by 0.2.</summary>
        GameObject Visitor(Vector3 position, bool trigger = false)
        {
            var go = Make("visitor", position);
            go.AddComponent<SphereCollider>().isTrigger = trigger;
            Hold(go);
            return go;
        }

        /// <summary>A unit cube, its faces wound so the cross product of their edges points out.</summary>
        Mesh Cube()
        {
            var mesh = new Mesh();
            m_Assets.Add(mesh);
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++) v[i] = new Vector3((i & 1) != 0 ? .5f : -.5f, (i & 2) != 0 ? .5f : -.5f, (i & 4) != 0 ? .5f : -.5f);
            mesh.vertices = v;
            mesh.triangles = new[] { 1, 3, 7, 1, 7, 5, 0, 4, 6, 0, 6, 2, 2, 6, 7, 2, 7, 3, 0, 1, 5, 0, 5, 4, 4, 5, 7, 4, 7, 6, 0, 2, 3, 0, 3, 1 };
            return mesh;
        }

        /// <summary>A unit collider of the given kind at the origin, and a change that keeps its size
        /// class (each call alternates between the authored and a slightly larger shape).</summary>
        Collider Target(string kind, string name, bool trigger, out Action change)
        {
            var go = Make(name, Vector3.zero);
            Collider collider;
            switch (kind)
            {
                case "box":
                {
                    var box = go.AddComponent<BoxCollider>();
                    change = () => box.size = box.size.x > 1f ? Vector3.one : new Vector3(1.2f, 1f, 1f);
                    collider = box;
                    break;
                }
                case "sphere":
                {
                    var sphere = go.AddComponent<SphereCollider>();
                    change = () => sphere.radius = sphere.radius > .5f ? .5f : .6f;
                    collider = sphere;
                    break;
                }
                case "capsule":
                {
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.height = 2f;
                    change = () => capsule.radius = capsule.radius > .5f ? .5f : .6f;
                    collider = capsule;
                    break;
                }
                case "convex":
                case "mesh":
                {
                    var meshCollider = go.AddComponent<MeshCollider>();
                    meshCollider.convex = kind == "convex";
                    var mesh = Cube();
                    meshCollider.sharedMesh = mesh;
                    change = () => meshCollider.sharedMesh = mesh;
                    collider = meshCollider;
                    break;
                }
                default: throw new ArgumentException(kind);
            }
            collider.isTrigger = trigger;
            return collider;
        }

        /// <summary>One simulation step: the messages the recorder got in it.</summary>
        static List<string> Step(RebuildRecorder3D recorder)
        {
            recorder.log.Clear();
            Physics.Simulate(Dt);
            return new List<string>(recorder.log);
        }

        static List<string> WithoutStays(List<string> log) => log.Where(m => !m.StartsWith("CS:")).ToList();

        static List<string> Sorted(List<string> log)
        {
            var copy = new List<string>(log);
            copy.Sort(StringComparer.Ordinal);
            return copy;
        }

        // ------------------------------------------------------------------ geometry changes

        [Theory]
        [InlineData("box")]
        [InlineData("sphere")]
        [InlineData("capsule")]
        [InlineData("convex")]
        public void ATriggerChangedWhileOverlappingStaysOneOverlapUntilTheColliderLeaves(string kind)
        {
            Target(kind, "zone", true, out var change);
            var visitor = Visitor(new Vector3(.8f, 0f, 0f));
            var recorder = visitor.AddComponent<RebuildRecorder3D>();

            Assert.Equal(new[] { "TE:zone", "TS:zone" }, Step(recorder));
            change();
            Assert.Equal(new[] { "TS:zone" }, Step(recorder));
            change();
            Assert.Equal(new[] { "TS:zone" }, Step(recorder));
            visitor.transform.position = new Vector3(5f, 0f, 0f);
            Assert.Equal(new[] { "TX:zone" }, Step(recorder));
            Assert.Empty(Step(recorder));
        }

        [Theory]
        [InlineData("box")]
        [InlineData("sphere")]
        [InlineData("capsule")]
        [InlineData("convex")]
        [InlineData("mesh")]
        public void ACollisionChangedWhileTouchingEndsWithOneExitWhenTheyPart(string kind)
        {
            Target(kind, "target", false, out var change);
            var visitor = Visitor(new Vector3(.8f, 0f, 0f));
            var recorder = visitor.AddComponent<RebuildRecorder3D>();

            // The Enter step's Stay is engine-specific and stays unasserted. While they keep
            // touching, the held body never sleeps, so Stay comes once per step (OnCollisionStay).
            Assert.Equal(new[] { "CE:target" }, WithoutStays(Step(recorder)));
            change();
            Assert.Equal(new[] { "CS:target" }, Step(recorder));
            change();
            Assert.Equal(new[] { "CS:target" }, Step(recorder));
            visitor.transform.position = new Vector3(5f, 0f, 0f);
            Assert.Equal(new[] { "CX:target" }, Step(recorder));
            Assert.Empty(Step(recorder));
        }

        [Theory]
        [InlineData("sphere")]
        [InlineData("capsule")]
        public void AShapeShrunkOutOfTheOverlapExitsAtTheNextStep(string kind)
        {
            var zone = Target(kind, "zone", true, out _);
            var visitor = Visitor(new Vector3(.8f, 0f, 0f));
            var recorder = visitor.AddComponent<RebuildRecorder3D>();

            Assert.Equal(new[] { "TE:zone", "TS:zone" }, Step(recorder));
            if (zone is SphereCollider sphere) sphere.radius = .2f;
            else ((CapsuleCollider)zone).radius = .2f;
            Assert.Equal(new[] { "TX:zone" }, Step(recorder));
            Assert.Empty(Step(recorder));
        }

        [Fact]
        public void ARadiusGrownDuringTriggerEnterKeepsTheOverlap()
        {
            // A radius change during OnTriggerEnter, as in the SphereCollider.radius Scripting API
            // example (there the collider that enters a trigger grows; here the trigger itself grows).
            var zone = Make("zone", Vector3.zero).AddComponent<SphereCollider>();
            zone.isTrigger = true;
            var zoneRecorder = zone.gameObject.AddComponent<RebuildRecorder3D>();
            bool grown = false;
            zoneRecorder.whenEntered = () => { if (!grown) { grown = true; zone.radius += .1f; } };
            var visitor = Visitor(new Vector3(.8f, 0f, 0f));

            Assert.Equal(new[] { "TE:visitor", "TS:visitor" }, Step(zoneRecorder));
            Assert.True(grown && zone.radius > .55f);
            Assert.Equal(new[] { "TS:visitor" }, Step(zoneRecorder));
            visitor.transform.position = new Vector3(5f, 0f, 0f);
            Assert.Equal(new[] { "TX:visitor" }, Step(zoneRecorder));
            Assert.Empty(Step(zoneRecorder));
        }

        [Fact]
        public void AShapeChangedWhileApartChangesNothingUntilTheyTouch()
        {
            var zone = Target("sphere", "zone", true, out var change);
            var visitor = Visitor(new Vector3(3f, 0f, 0f));
            var recorder = visitor.AddComponent<RebuildRecorder3D>();

            Assert.Empty(Step(recorder));
            change();
            Assert.Empty(Step(recorder));
            change();
            Assert.Empty(Step(recorder));
            visitor.transform.position = new Vector3(.8f, 0f, 0f);
            Assert.Equal(new[] { "TE:zone", "TS:zone" }, Step(recorder));
            Assert.Equal(new[] { "TS:zone" }, Step(recorder));
            visitor.transform.position = new Vector3(5f, 0f, 0f);
            Assert.Equal(new[] { "TX:zone" }, Step(recorder));
            Assert.Empty(Step(recorder));
        }

        // ------------------------------------------------------------------ trigger conversion

        /// <summary>PhysicsTests.TriggerConversionsKeepBothMessageKindsAndTriggerPairsReenter's
        /// sequence: a rebuilt shape converts as the in-place box does.</summary>
        [Theory]
        [InlineData("box")]
        [InlineData("sphere")]
        [InlineData("capsule")]
        public void ConvertingARebuiltShapeSendsTheMessagesOfTheBoxConversion(string kind)
        {
            var a = Target(kind, "a", false, out _);
            var b = Target(kind, "b", false, out _);
            b.transform.position = new Vector3(.8f, 0f, 0f);
            Hold(a.gameObject); Hold(b.gameObject);
            var ar = a.gameObject.AddComponent<RebuildRecorder3D>();
            var br = b.gameObject.AddComponent<RebuildRecorder3D>();
            void Expect(params string[] codes)
            {
                ar.log.Clear(); br.log.Clear();
                Physics.SyncTransforms(); Physics.Simulate(Dt);
                Assert.Equal(codes.Select(c => c + ":b"), ar.log);
                Assert.Equal(codes.Select(c => c + ":a"), br.log);
            }

            Expect("CE");
            a.isTrigger = true; Expect("TE", "TS", "CX");
            b.isTrigger = true; Expect("TS");
            a.isTrigger = false; Expect("TS");
            b.transform.position = new Vector3(3f, 0f, 0f); Expect("TX");
            b.transform.position = new Vector3(.8f, 0f, 0f); Expect("TE", "TS");
            // Extrapolated, not measured for this state: physicsscenesettings measures TX then CE
            // for the same flag change on a carried-over pair (flags dd/ss-again) and for a
            // re-entered trigger/trigger pair made solid (trigger-pairs dd/from-tt).
            b.isTrigger = false; Expect("TX", "CE");
            b.transform.position = new Vector3(3f, 0f, 0f); Expect("CX");
            Expect();
        }

        // ------------------------------------------------------------------ transform frames

        [Theory]
        [InlineData("box")]
        [InlineData("sphere")]
        public void AChildColliderMovedWithinItsBodyKeepsOnePair(string kind)
        {
            var body = Make("body", Vector3.zero);
            Hold(body);
            var part = Make("part", Vector3.zero);
            part.transform.SetParent(body.transform, false);
            if (kind == "box") part.AddComponent<BoxCollider>();
            else part.AddComponent<SphereCollider>();
            var wall = Make("wall", new Vector3(.8f, 0f, 0f));
            wall.AddComponent<BoxCollider>();
            var recorder = wall.AddComponent<RebuildRecorder3D>();

            // As above: the held body keeps the documented Stay while the part touches the wall.
            Assert.Equal(new[] { "CE:part" }, WithoutStays(Step(recorder)));
            part.transform.localPosition = new Vector3(.05f, 0f, 0f);
            Assert.Equal(new[] { "CS:part" }, Step(recorder));
            part.transform.localPosition = Vector3.zero;
            Assert.Equal(new[] { "CS:part" }, Step(recorder));
            wall.transform.position = new Vector3(5f, 0f, 0f);
            Assert.Equal(new[] { "CX:part" }, Step(recorder));
            Assert.Empty(Step(recorder));
        }

        // ------------------------------------------------------------------ several shapes

        [Fact]
        public void CollidersOfOneBodyKeepTheirOwnPairsThroughARebuild()
        {
            var body = Make("body", Vector3.zero);
            Hold(body);
            SphereCollider Part(string name, float y)
            {
                var go = Make(name, Vector3.zero);
                go.transform.SetParent(body.transform, false);
                go.transform.localPosition = new Vector3(0f, y, 0f);
                var sphere = go.AddComponent<SphereCollider>();
                sphere.radius = .3f;
                return sphere;
            }
            var upper = Part("upper", .3f);
            Part("lower", -.3f);
            var zone = Make("zone", new Vector3(.5f, 0f, 0f));
            var box = zone.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 2f, 1f);
            box.isTrigger = true;
            var recorder = zone.AddComponent<RebuildRecorder3D>();

            Assert.Equal(new[] { "TE:lower", "TE:upper", "TS:lower", "TS:upper" }, Sorted(Step(recorder)));
            upper.radius = .35f;
            Assert.Equal(new[] { "TS:lower", "TS:upper" }, Sorted(Step(recorder)));
            body.transform.position = new Vector3(10f, 0f, 0f);
            Assert.Equal(new[] { "TX:lower", "TX:upper" }, Sorted(Step(recorder)));
            Assert.Empty(Step(recorder));
        }

        [Fact]
        public void ATerrainsTreeShapesKeepOneCountedPairThroughARebuild()
        {
            var prefab = Make("tree", Vector3.zero);
            prefab.SetActive(false);
            var trunk = prefab.AddComponent<CapsuleCollider>();
            trunk.radius = .5f;
            trunk.height = 2f;
            var data = new TerrainData();
            m_Assets.Add(data);
            data.heightmapResolution = 33;
            data.size = new Vector3(32f, 10f, 32f);
            data.treePrototypes = new[] { new TreePrototype { prefab = prefab } };
            TreeInstance Tree(float x) => new TreeInstance
            {
                position = new Vector3(x / 32f, 0f, .5f), widthScale = 1f, heightScale = 1f, prototypeIndex = 0,
            };
            data.treeInstances = new[] { Tree(15.4f), Tree(16.6f) };
            var terrain = Make("terrain", Vector3.zero);
            terrain.AddComponent<TerrainCollider>().terrainData = data;
            // A trigger sphere of radius 1 over both trunks and clear of the flat ground.
            var visitor = Make("visitor", new Vector3(16f, 1.2f, 16f));
            var sphere = visitor.AddComponent<SphereCollider>();
            sphere.radius = 1f;
            sphere.isTrigger = true;
            Hold(visitor);
            var recorder = visitor.AddComponent<RebuildRecorder3D>();
            void Rebuild() => data.SetHeights(0, 0, new float[1, 1]);

            Assert.Equal(new[] { "TE:terrain", "TS:terrain" }, Step(recorder));
            Rebuild();
            Assert.Equal(new[] { "TS:terrain" }, Step(recorder));
            // Leaving one trunk only: the other still overlaps (no rebuild).
            visitor.transform.position = new Vector3(17.2f, 1.2f, 16f);
            Assert.Equal(new[] { "TS:terrain" }, Step(recorder));
            Rebuild();
            Assert.Equal(new[] { "TS:terrain" }, Step(recorder));
            visitor.transform.position = new Vector3(16f, 1.2f, 28f);
            Assert.Equal(new[] { "TX:terrain" }, Step(recorder));
            Assert.Empty(Step(recorder));
        }

        // ------------------------------------------------------------------ attachment changes

        [Fact]
        public void AddingARigidbodyToATouchedColliderStillEndsItsPair()
        {
            var post = Make("post", Vector3.zero);
            post.AddComponent<SphereCollider>();
            var visitor = Visitor(new Vector3(.8f, 0f, 0f));
            var recorder = visitor.AddComponent<RebuildRecorder3D>();
            var all = new List<string>();

            all.AddRange(Step(recorder));
            Assert.Equal(new[] { "CE:post" }, WithoutStays(all));
            Hold(post);
            all.AddRange(Step(recorder));
            all.AddRange(Step(recorder));
            visitor.transform.position = new Vector3(5f, 0f, 0f);
            var parting = Step(recorder);
            all.AddRange(parting);
            Assert.Equal(new[] { "CX:post" }, parting);
            Assert.Empty(Step(recorder));
            Assert.Equal(all.Count(m => m == "CE:post"), all.Count(m => m == "CX:post"));
        }

        [Fact]
        public void RemovingTheRigidbodyOfATouchingColliderStillEndsItsPair()
        {
            var crate = Make("crate", Vector3.zero);
            crate.AddComponent<SphereCollider>();
            var body = Hold(crate);
            var visitor = Visitor(new Vector3(.8f, 0f, 0f));
            var recorder = visitor.AddComponent<RebuildRecorder3D>();
            var all = new List<string>();

            all.AddRange(Step(recorder));
            Assert.Equal(new[] { "CE:crate" }, WithoutStays(all));
            Object.DestroyImmediate(body);
            all.AddRange(Step(recorder));
            all.AddRange(Step(recorder));
            visitor.transform.position = new Vector3(5f, 0f, 0f);
            var parting = Step(recorder);
            all.AddRange(parting);
            Assert.Equal(new[] { "CX:crate" }, parting);
            Assert.Empty(Step(recorder));
            Assert.Equal(all.Count(m => m == "CE:crate"), all.Count(m => m == "CX:crate"));
        }

        // ------------------------------------------------------------------ leaving the simulation

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ARebuiltColliderLeavingTheSimulationStillSendsNoExit(bool destroy)
        {
            var zone = Target("sphere", "zone", true, out var change);
            var visitor = Visitor(new Vector3(.8f, 0f, 0f));
            var recorder = visitor.AddComponent<RebuildRecorder3D>();

            Assert.Equal(new[] { "TE:zone", "TS:zone" }, Step(recorder));
            change();
            Physics.SyncTransforms(); // the new shape exists before the collider goes
            if (destroy) Object.DestroyImmediate(zone);
            else zone.enabled = false;
            Assert.Empty(Step(recorder));
            Assert.Empty(Step(recorder));
            if (destroy) return;
            zone.enabled = true;
            Assert.Equal(new[] { "TE:zone", "TS:zone" }, Step(recorder));
            visitor.transform.position = new Vector3(5f, 0f, 0f);
            Assert.Equal(new[] { "TX:zone" }, Step(recorder));
        }

        [Fact]
        public void DestroyingTheOtherObjectAfterARebuildSendsNoExit()
        {
            var zone = Target("sphere", "zone", true, out var change);
            var zoneRecorder = zone.gameObject.AddComponent<RebuildRecorder3D>();
            var visitor = Visitor(new Vector3(.8f, 0f, 0f));

            Assert.Equal(new[] { "TE:visitor", "TS:visitor" }, Step(zoneRecorder));
            change();
            Physics.SyncTransforms();
            Object.DestroyImmediate(visitor);
            Assert.Empty(Step(zoneRecorder));
            Assert.Empty(Step(zoneRecorder));
        }
    }
}
