using Unbound.Runtime;

namespace UnityEngine.Tests
{
    /// <summary>The player loop's automatic Animator update for tests that drive it without the
    /// loop: both leaves (<see cref="AnimationPhases"/>), so the poses are written.</summary>
    internal static class AnimationTestLoop
    {
        /// <summary>The Begin (or DirectorFixedUpdate) leaf with its graph pass, then the End
        /// (or DirectorFixedUpdatePostPhysics) leaf.</summary>
        internal static void Update(bool fixedStep)
        {
            AnimationSystem.UpdateAnimators(fixedStep);
            AnimationPhases.WritePoses();
        }

        /// <summary>The Animators' own update in both leaves, without the graph pass.</summary>
        internal static void Tick()
        {
            AnimationSystem.TickAnimators(fixedStep: false);
            AnimationPhases.DeliverMovement();
            AnimationPhases.WritePoses();
        }
    }
}
