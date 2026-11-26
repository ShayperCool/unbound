using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Joint projection and the configurable joint's frame in world space, as the Unity 6000.0.71f1
    /// Editor has them (tests/projects/jointprojection): a heavy chain projected with a tight
    /// tolerance keeps its links in place where the chain without projection stretches by metres;
    /// configuredInWorldSpace takes the axes as world directions.
    /// </summary>
    public class JointProjectionTests : IDisposable
    {
        const float Dt = 0.02f;
        // Far from the origin, away from anything other tests leave in the default physics scene.
        static readonly Vector3 Origin = new Vector3(0f, 10f, 1300f);

        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly Vector3 m_Gravity = Physics.gravity;
        readonly SimulationMode m_Mode = Physics.simulationMode;

        public JointProjectionTests()
        {
            Physics.simulationMode = SimulationMode.Script;
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
        }

        public void Dispose()
        {
            foreach (var go in m_Made) Object.DestroyImmediate(go);
            Physics.gravity = m_Gravity;
            Physics.simulationMode = m_Mode;
        }

        Rigidbody Body(Vector3 at, float mass, bool kinematic = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Made.Add(go);
            go.transform.position = at;
            go.transform.localScale = new Vector3(0.6f, 0.2f, 0.2f);
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.isKinematic = kinematic;
            return body;
        }

        /// <summary>The probe's chain: a kinematic anchor and four links 1 m apart along +x, the
        /// last 200 times as heavy, hinged about z.</summary>
        Rigidbody[] Chain(Vector3 origin, Action<ConfigurableJoint> configure)
        {
            var bodies = new Rigidbody[5];
            bodies[0] = Body(origin, 1f, true);
            for (int i = 1; i < 5; i++)
            {
                bodies[i] = Body(origin + new Vector3(i, 0f, 0f), i == 4 ? 200f : 1f);
                var j = bodies[i].gameObject.AddComponent<ConfigurableJoint>();
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
                j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;
                j.angularXMotion = ConfigurableJointMotion.Free;
                j.axis = Vector3.forward;
                configure(j);
                j.connectedBody = bodies[i - 1];
                j.anchor = new Vector3(-0.5f, 0f, 0f);
            }
            return bodies;
        }

        [Fact]
        public void ATightlyProjectedChainKeepsItsLinksWhereAChainWithoutProjectionStretches()
        {
            var loose = Chain(Origin, j => j.projectionMode = JointProjectionMode.None);
            var tight = Chain(Origin + new Vector3(0f, 0f, 5f), j =>
            {
                j.projectionMode = JointProjectionMode.PositionAndRotation;
                j.projectionDistance = 0.01f;
                j.projectionAngle = 5f;
            });
            for (int i = 0; i < 50; i++) Physics.Simulate(Dt);
            // The Editor at step 50: 1.006 m between the last two links with the tight projection;
            // without projection the strained chain is chaotic (2.243 m in the Editor), only
            // clearly longer is asserted.
            float looseGap = Vector3.Distance(loose[3].position, loose[4].position);
            float tightGap = Vector3.Distance(tight[3].position, tight[4].position);
            Assert.True(Mathf.Abs(tightGap - 1.006f) < 0.01f, $"with projection {tightGap}");
            Assert.True(looseGap > tightGap + 0.05f, $"without projection {looseGap}, with {tightGap}");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ConfiguredInWorldSpaceTakesTheAxesAsWorldDirections(bool world)
        {
            // A body turned 90 degrees about y, free to move 0.3 m along the joint's x axis, pushed
            // along world x and z: the Editor's body ends 0.3 m along world z (its own x) or, in
            // world space, along world x.
            var at = Origin + new Vector3(0f, 0f, 10f + (world ? 5f : 0f));
            var anchor = Body(at, 1f, true);
            var body = Body(at + Vector3.right, 2f);
            body.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var j = body.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = anchor;
            j.anchor = Vector3.zero;
            j.axis = Vector3.right;
            j.secondaryAxis = Vector3.up;
            j.xMotion = ConfigurableJointMotion.Limited;
            j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = ConfigurableJointMotion.Free;
            j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;
            j.linearLimit = new SoftJointLimit { limit = 0.3f };
            j.configuredInWorldSpace = world;
            body.linearVelocity = new Vector3(2f, 1f, 0.5f);
            body.angularVelocity = new Vector3(0f, 0f, 3f);
            for (int i = 0; i < 50; i++) Physics.Simulate(Dt);
            var moved = body.position - (at + Vector3.right);
            var expected = world ? new Vector3(0.3f, 0f, 0f) : new Vector3(0f, 0f, 0.3f);
            Assert.True((moved - expected).magnitude < 2e-3f, $"moved {moved}, expected {expected}");
        }
    }
}
