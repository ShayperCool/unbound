using DotRecast.Core.Numerics;
using DotRecast.Detour.Crowd;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public sealed class NavMeshBrakingPlanTests
    {
        static DtCrowdAgent Agent(float acceleration = 8f) => new DtCrowdAgent(0)
        {
            option = new DtCrowdAgentParams { maxAcceleration = acceleration },
            vel = new RcVec3f(2f, 0f, 0f),
            dvel = new RcVec3f(1f, 0.5f, 0f),
            nvel = new RcVec3f(0.5f, 0f, 1f),
            brakingNow = true,
        };

        [Theory]
        [InlineData("6000.0.71f1", true)]
        [InlineData("6000.0.72f1", false)]
        public void BrakingUsesTheSteeringPlanInTheMeasuredRelease(string release, bool measured)
        {
            NavigationTestRelease.At(release, () =>
            {
                var agent = Agent();
                var desired = agent.dvel;
                var sampled = agent.nvel;
                agent.Integrate(0.02f, Compat.NavMeshBrakingPlanMeasured);
                Assert.Equal(measured ? desired : sampled, agent.nvel);
                Assert.Equal(agent.nvel, agent.pvel);
                Assert.Equal(new RcVec3f(0.04f, 0f, 0f), agent.npos);
                agent.Accelerate(0.005f);
                Assert.Equal(measured ? desired : sampled, agent.vel);
            });
        }

        [Fact]
        public void AnUnbrakedAgentKeepsItsAvoidanceSample()
        {
            var agent = Agent();
            var sampled = agent.nvel;
            agent.Integrate(0.02f, true);
            agent.brakingNow = false;
            agent.nvel = sampled;
            agent.Integrate(0.02f, true);
            Assert.Equal(sampled, agent.nvel);
            Assert.False(agent.brakingVelocityPrepared);
        }

        [Fact]
        public void ABrakingSpeedIncreaseIsLimitedBeforeItsPlanIsPublished()
        {
            var agent = Agent(1f);
            agent.vel = new RcVec3f(1f, 0f, 0f);
            agent.dvel = new RcVec3f(4f, 4f, 0f);
            agent.Integrate(0.1f, true);
            Assert.Equal(1.06f, agent.nvel.X, 5);
            Assert.Equal(0.08f, agent.nvel.Y, 5);
            Assert.Equal(0f, agent.nvel.Z);
            // The next frame uses the prepared velocity, even if its duration is shorter.
            var prepared = agent.nvel;
            agent.Accelerate(0.001f);
            Assert.Equal(prepared, agent.vel);
        }

        [Fact]
        public void BrakingCanReduceSpeedAtZeroAcceleration()
        {
            var agent = Agent(0f);
            agent.Integrate(0.02f, true);
            Assert.Equal(agent.dvel, agent.nvel);
            agent.Accelerate(0.005f);
            Assert.Equal(agent.dvel, agent.vel);
        }

        [Fact]
        public void ClearingBrakingRestoresAccelerationTowardsTheCurrentSample()
        {
            var agent = Agent(1f);
            agent.Integrate(0.02f, true);
            agent.vel = RcVec3f.Zero;
            agent.braking = false;
            agent.pvel = new RcVec3f(1f, 0f, 0f);
            agent.Accelerate(0.1f);
            Assert.Equal(new RcVec3f(0.1f, 0f, 0f), agent.vel);
        }
    }
}
