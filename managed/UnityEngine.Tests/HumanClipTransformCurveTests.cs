using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The transform curves a Humanoid model clip keeps beside its muscle curves (6000.0.71f1
    /// Editor, tests/projects/modelclips HumanTwistNone, HumanTwistAll, HumanTwistPart): those of
    /// transforms that are not bones of the human skeleton, when the take animates them and the
    /// clip's mask leaves them on; none with mask type None. The Manual's Avatar Mask page says a
    /// mask picks body portions or bones of the Transform hierarchy and can be applied in the
    /// import settings; it does not say what a Humanoid clip without a mask keeps.
    /// </summary>
    [Collection("Animation")]
    public sealed class HumanClipTransformCurveTests : IDisposable
    {
        readonly List<Object> m_Created = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
        }

        /// <summary>A model clip on the test rig with a prop under the left hand: curves of the
        /// left upper leg (a human bone), of the prop and of a transform the avatar does not
        /// have, and the paths the importer lists as kept (null: a clip without the list).</summary>
        AnimationClip Clip(string[] keptPaths)
        {
            var root = AnimationHumanStreamTests.Humanoid(out var animator);
            m_Created.Add(root);
            m_Created.Add(animator.avatar);
            var clip = new AnimationClip { frameRate = 30f };
            m_Created.Add(clip);
            void Rotation(string path, float degrees)
            {
                for (int c = 0; c < 4; c++)
                {
                    var keys = new Keyframe[11];
                    for (int i = 0; i <= 10; i++) keys[i] = new Keyframe(i / 30f, Quaternion.Euler(degrees * i / 10f, 0f, 0f)[c]);
                    clip.m_Curves.Add(new ClipCurve
                    {
                        Binding = new CurveBinding { Path = path, Attribute = "m_LocalRotation." + "xyzw"[c], ClassId = EngineTypes.TransformId },
                        Curve = new AnimationCurve(keys),
                    });
                }
            }
            Rotation("Hips/LeftUpperLeg", 30f);
            Rotation(Prop, 20f);
            Rotation(Elsewhere, 10f);
            clip.m_HumanAvatar = animator.avatar;
            clip.m_StartTime = 0f;
            clip.m_StopTime = 10f / 30f;
            clip.m_HasRange = true;
            if (keptPaths != null) clip.m_HumanTransformPaths = new HashSet<string>(keptPaths, StringComparer.Ordinal);
            clip.ConvertHumanCurves();
            return clip;
        }

        const string Prop = "Hips/Spine/Chest/LeftUpperArm/LeftLowerArm/LeftHand/Prop";
        const string Elsewhere = "Elsewhere/Bone";

        static string[] TransformPaths(AnimationClip clip) =>
            clip.m_Curves.Where(c => c.Binding.ClassId == EngineTypes.TransformId).Select(c => c.Binding.Path).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();

        [Fact]
        public void OnlyTheListedTransformsKeepTheirCurves()
        {
            // No mask (an empty list): muscle curves only.
            var none = Clip(new string[0]);
            Assert.Empty(TransformPaths(none));
            Assert.True(none.m_Curves.Count > 100);

            // Listed transforms keep their curves; a human bone that is listed does not.
            Assert.Equal(new[] { Prop }, TransformPaths(Clip(new[] { Prop, "Hips/LeftUpperLeg" })));
            Assert.Equal(new[] { Elsewhere, Prop }, TransformPaths(Clip(new[] { Prop, Elsewhere })));

            // A clip without a list (not made by the model importer) keeps what is not a human bone.
            Assert.Equal(new[] { Elsewhere, Prop }, TransformPaths(Clip(null)));

            // The muscle curves do not depend on the list.
            float Leg(AnimationClip clip) => clip.m_Curves.First(c => c.Binding.Attribute == "Left Upper Leg Front-Back").Curve[10].value;
            Assert.Equal(Leg(none), Leg(Clip(new[] { Prop })));
            Assert.NotEqual(0f, Leg(none));
        }
    }
}
