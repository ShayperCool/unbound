using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    // tests/projects/animatephysics: two identical Unity 6000.0.71f1 captures.
    public class AnimatePhysicsTests
    {
        [Fact]
        public void AnimatePhysicsIsStoredBesideTheUpdateMode()
        {
            var go = new GameObject("animator");
            try
            {
                var animator = go.AddComponent<Animator>();
                Assert.False(animator.animatePhysics);
                animator.animatePhysics = true;
                Assert.Equal(AnimatorUpdateMode.Normal, animator.updateMode);
                animator.updateMode = AnimatorUpdateMode.Fixed;
                Assert.True(animator.animatePhysics);
                animator.animatePhysics = false;
                Assert.Equal(AnimatorUpdateMode.Fixed, animator.updateMode);
                animator.animatePhysics = true;
                var copy = Object.Instantiate(go).GetComponent<Animator>();
                Assert.True(copy.animatePhysics);
                Assert.Equal(AnimatorUpdateMode.Fixed, copy.updateMode);
                Object.DestroyImmediate(copy.gameObject);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void OnlyWritesInsideAnAnimatePhysicsAnimatorsScopeMarkTheTransformAndItsDescendants()
        {
            var root = new GameObject("root");
            var child = new GameObject("child");
            child.transform.SetParent(root.transform, false);
            var on = root.AddComponent<Animator>();
            on.animatePhysics = true;
            var off = new GameObject("off").AddComponent<Animator>();
            try
            {
                int since = AnimatedPhysicsWrites.s_LastStamp;
                child.transform.localPosition = new Vector3(1, 0, 0);
                Assert.False(AnimatedPhysicsWrites.MovedSince(child.transform, since));
                using (AnimatedPhysicsWrites.Begin(off)) root.transform.localPosition = new Vector3(2, 0, 0);
                Assert.False(AnimatedPhysicsWrites.MovedSince(child.transform, since));
                using (AnimatedPhysicsWrites.Begin(on))
                {
                    // Another Animator's writes inside are its own (without animatePhysics: none).
                    using (AnimatedPhysicsWrites.Begin(off)) child.transform.localPosition = new Vector3(3, 0, 0);
                    Assert.False(AnimatedPhysicsWrites.MovedSince(child.transform, since));
                    root.transform.localPosition = new Vector3(4, 0, 0);
                }
                // The parent's write moves the child; writes after the scope do not.
                Assert.True(AnimatedPhysicsWrites.MovedSince(child.transform, since));
                int later = AnimatedPhysicsWrites.s_LastStamp;
                root.transform.localPosition = new Vector3(5, 0, 0);
                Assert.False(AnimatedPhysicsWrites.MovedSince(child.transform, later));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(off.gameObject);
            }
        }
    }
}
