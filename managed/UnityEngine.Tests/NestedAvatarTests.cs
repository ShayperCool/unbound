using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>An Animator above the model its avatar was built from: the avatar root found in its
    /// hierarchy and the curves bound below it (tests/projects/nestedavatar measures the Editor).</summary>
    [Collection("Animation")]
    public sealed class NestedAvatarTests
    {
        /// <summary>Model / Hips / Leg and Model / Prop, with the generic avatar built from it.</summary>
        static GameObject Model(out Avatar avatar)
        {
            var model = new GameObject("Model");
            var hips = new GameObject("Hips").transform;
            hips.SetParent(model.transform, false);
            hips.localPosition = new Vector3(0f, 1f, 0f);
            new GameObject("Leg").transform.SetParent(hips, false);
            new GameObject("Prop").transform.SetParent(model.transform, false);
            avatar = AvatarBuilder.BuildGenericAvatar(model, "");
            return model;
        }

        static Animator Holder(GameObject model, Avatar avatar, params string[] decoys)
        {
            var holder = new GameObject("Holder");
            foreach (var path in decoys)
            {
                var parent = holder.transform;
                foreach (var name in path.Split('/'))
                {
                    var t = parent.Find(name) ?? new GameObject(name).transform;
                    t.SetParent(parent, false);
                    parent = t;
                }
            }
            model.transform.SetParent(holder.transform, false);
            var animator = holder.AddComponent<Animator>();
            animator.avatar = avatar;
            return animator;
        }

        [Fact]
        public void TheAvatarRootIsWhereMostOfTheAvatarsPathsAre()
        {
            var model = Model(out var avatar);
            Assert.Same(model.transform, Holder(model, avatar).avatarRoot);

            model = Model(out avatar);
            model.name = "Renamed";
            Assert.Same(model.transform, Holder(model, avatar).avatarRoot);

            // The holder holds as many top-level names (Hips, Prop) but fewer paths (no Hips/Leg).
            model = Model(out avatar);
            Assert.Same(model.transform, Holder(model, avatar, "Hips", "Prop").avatarRoot);

            // A decoy branch before the model holding Hips/Leg only.
            model = Model(out avatar);
            Assert.Same(model.transform, Holder(model, avatar, "Decoy/Hips/Leg").avatarRoot);

            // As many paths in the holder itself as in the model: the first, the holder, wins.
            model = Model(out avatar);
            var tie = Holder(model, avatar, "Hips/Leg", "Prop");
            Assert.Same(tie.transform, tie.avatarRoot);

            // No avatar, or no path anywhere: the Animator's own transform.
            model = Model(out _);
            var none = Holder(model, null);
            Assert.Same(none.transform, none.avatarRoot);
            Model(out avatar);
            var unrelated = new GameObject("Unrelated").AddComponent<Animator>();
            unrelated.avatar = avatar;
            Assert.Same(unrelated.transform, unrelated.avatarRoot);
        }

        [Fact]
        public void TheAvatarRootIsKeptUntilRebindOrANewAvatar()
        {
            var model = Model(out var avatar);
            var animator = Holder(model, avatar);
            Assert.Same(model.transform, animator.avatarRoot);
            var other = Model(out _);
            other.transform.SetParent(animator.transform, false);
            other.transform.SetAsFirstSibling();
            Assert.Same(model.transform, animator.avatarRoot);
            animator.Rebind();
            Assert.Same(other.transform, animator.avatarRoot);
        }

        [Fact]
        public void CurvesBindBelowTheAvatarRoot()
        {
            var clip = new AnimationClip();
            clip.SetCurve("Hips", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 2f));
            Vector3 Played(Avatar avatar, GameObject model)
            {
                var animator = Holder(model, avatar);
                var graph = PlayableGraph.Create("nested");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
                graph.Evaluate(0.5f);
                var position = model.transform.Find("Hips").localPosition;
                graph.Destroy();
                return position;
            }
            var withAvatar = Model(out var a);
            Assert.Equal(1f, Played(a, withAvatar).x, 4);
            // Control: without the avatar the path is looked for below the holder, and not found.
            var withoutAvatar = Model(out _);
            Assert.Equal(0f, Played(null, withoutAvatar).x, 4);
        }

        [Fact]
        public void LayerMasksTakeTransformPathsBelowTheAvatarRoot()
        {
            var clip = new AnimationClip();
            clip.SetCurve("Hips", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 2f));
            clip.SetCurve("Prop", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 2f));
            var mask = new AvatarMask { transformCount = 2 };
            mask.SetTransformPath(0, "Hips");
            mask.SetTransformActive(0, true);
            mask.SetTransformPath(1, "Prop");
            mask.SetTransformActive(1, false);
            var model = Model(out var avatar);
            var animator = Holder(model, avatar);
            var graph = PlayableGraph.Create("masked");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var layers = AnimationLayerMixerPlayable.Create(graph, 1);
            graph.Connect(AnimationClipPlayable.Create(graph, clip), 0, layers, 0);
            layers.SetInputWeight(0, 1f);
            layers.SetLayerMaskFromAvatarMask(0, mask);
            AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(layers);
            graph.Evaluate(0.5f);
            Assert.Equal(1f, model.transform.Find("Hips").localPosition.x, 4);
            Assert.Equal(0f, model.transform.Find("Prop").localPosition.x, 4);
            graph.Destroy();
        }
    }
}
