using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// An Animator's root rotation composed over many root motion steps. The Editor's root
    /// rotations stay unit length while a playable graph's root curves turn and move a humanoid
    /// (6000.0.71f1, tests/projects/humanoid genericOnHuman.graph, recorded on Linux); the
    /// Transform stores each step's rotation normalized, and the next step starts from that. The
    /// control here is a generic Animator on a tilted root.
    /// </summary>
    [Collection("Animation")]
    public sealed class RootMotionRotationTests
    {
        const int Seconds = 30, Rate = 30;

        /// <summary>A clip turning its root about y by 30 degrees a second while moving it 2 units
        /// a second along its z, keyed every second.</summary>
        static AnimationClip TurningClip()
        {
            var clip = new AnimationClip { legacy = true };
            var keys = new Keyframe[4][];
            for (int c = 0; c < 4; c++) keys[c] = new Keyframe[Seconds + 1];
            for (int s = 0; s <= Seconds; s++)
            {
                var q = Quaternion.Euler(0f, 30f * s, 0f);
                keys[0][s] = new Keyframe(s, q.x);
                keys[1][s] = new Keyframe(s, q.y);
                keys[2][s] = new Keyframe(s, q.z);
                keys[3][s] = new Keyframe(s, q.w);
            }
            string[] names = { "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" };
            for (int c = 0; c < 4; c++) clip.SetCurve("", typeof(Transform), names[c], new AnimationCurve(keys[c]));
            clip.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, Seconds, 2f * Seconds));
            clip.legacy = false;
            return clip;
        }

        [Fact]
        public void ATiltedRootStaysAUnitRotationInItsPlaneOverAPlay()
        {
            var start = Quaternion.Euler(-60f, 0f, 20f);
            var root = new GameObject("Turning");
            root.transform.rotation = start;
            var animator = root.AddComponent<Animator>();
            animator.applyRootMotion = true;
            var g = PlayableGraph.Create("root-rotation");
            try
            {
                AnimationPlayableOutput.Create(g, "out", animator).SetSourcePlayable(AnimationClipPlayable.Create(g, TurningClip()));
                g.Evaluate(0f);
                for (int i = 0; i < Seconds * Rate - 1; i++) g.Evaluate(1f / Rate);
                var r = animator.rootRotation;
                float norm = Mathf.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w);
                // Red before the fix: 0.9999957 after this play (each step's product kept its rounding).
                Assert.True(Mathf.Abs(norm - 1f) < 1e-6f, $"root rotation norm {norm:R}");
                // Every step moves along the root's own z, turned about its y: in the plane the
                // start rotation's up is normal to, up to each step's rounding (about 2e-4 over
                // the 60 units travelled).
                var travelled = root.transform.position;
                Assert.True(travelled.magnitude > 30f, $"travelled {travelled}");
                float off = Vector3.Dot(travelled, start * Vector3.up);
                Assert.True(Mathf.Abs(off) < 1e-3f, $"left the plane by {off:R}");
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }
    }
}
