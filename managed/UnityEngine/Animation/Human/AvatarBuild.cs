using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>The avatar's skeleton (every transform of the
    /// hierarchy it was built from, by path hash), its T-pose and the hierarchy's pose at build time,
    /// and the human with the mapping between the two skeletons.</summary>
    internal sealed class AvatarConstant
    {
        public Skeleton AvatarSkeleton;
        /// <summary>The T-pose (the hierarchy's locals with the description's skeleton applied).</summary>
        public SkeletonPose AvatarSkeletonPose;
        /// <summary>The hierarchy's locals when the avatar was built.</summary>
        public SkeletonPose DefaultPose;
        public string[] Paths;
        public Human Human;
        /// <summary>Per human skeleton node, the avatar node.</summary>
        public int[] HumanSkeletonIndexArray;
        /// <summary>Per avatar node, the human skeleton node (or −1).</summary>
        public int[] HumanSkeletonReverseIndexArray;
        public int RootMotionBoneIndex = -1;

        public bool IsHuman => Human != null && Human.Skeleton.Count != 0;

        Dictionary<uint, int> m_NodeByPath;

        /// <summary>The node of a path hash (the first of it), or −1.</summary>
        public int NodeOfPath(uint pathHash)
        {
            if (m_NodeByPath == null)
            {
                var map = new Dictionary<uint, int>(AvatarSkeleton.Count);
                for (int i = 0; i < AvatarSkeleton.Count; i++) map.TryAdd(AvatarSkeleton.Id[i], i);
                m_NodeByPath = map;
            }
            return m_NodeByPath.TryGetValue(pathHash, out int node) ? node : -1;
        }

        /// <summary>The first node below the root whose last path name is <paramref name="name"/>, or −1.</summary>
        public int NodeOfName(string name)
        {
            for (int i = 1; i < Paths.Length; i++)
            {
                var path = Paths[i];
                int start = path.LastIndexOf('/') + 1;
                if (path.Length - start == name.Length && string.CompareOrdinal(path, start, name, 0, name.Length) == 0) return i;
            }
            return -1;
        }

        /// <summary>The human skeleton node of a `HumanBodyBones` bone (−1 when
        /// the avatar does not map it).</summary>
        public int HumanNodeOf(int monoBone)
        {
            if (Human == null) return -1;
            int bone = HumanTrait.GetBoneIndexFromMono(monoBone);
            if (bone < 0) return -1;
            if (bone < Bone.Last) return Human.BoneIndex[bone];
            int finger = bone - Bone.Last;
            if (finger < Bone.FingerBoneCount) return Human.HasLeftHand ? Human.LeftHand.BoneIndex[finger] : -1;
            finger -= Bone.FingerBoneCount;
            if (finger < Bone.FingerBoneCount) return Human.HasRightHand ? Human.RightHand.BoneIndex[finger] : -1;
            return -1;
        }

        /// <summary>The avatar node of a `HumanBodyBones` bone (−1 when not mapped).</summary>
        public int AvatarNodeOf(int monoBone)
        {
            int node = HumanNodeOf(monoBone);
            return node < 0 ? -1 : HumanSkeletonIndexArray[node];
        }
    }

    /// <summary>Builds an AvatarConstant from a hierarchy and a description as AvatarBuilder and the
    /// model importer do in the Unity 6000.0.71f1 Editor (tests/projects/humanoid).</summary>
    internal static class AvatarBuild
    {
        struct NamedTransform
        {
            public string Name, Path;
            public uint Hash;
            public Transform Transform;
        }

        /// <summary>The hierarchy depth first with each transform's path below the
        /// root and its hash; with <paramref name="names"/>, only transforms with those names (their
        /// children are still visited).</summary>
        static void AllChildren(Transform t, string path, List<NamedTransform> list, HashSet<string> names)
        {
            if (names == null || names.Contains(t.name))
                list.Add(new NamedTransform { Name = t.name, Path = path, Hash = HashedBindings.Crc32(path), Transform = t });
            foreach (var child in t.m_Children)
                AllChildren(child, path.Length == 0 ? child.name : path + "/" + child.name, list, names);
        }

        static string[] BodyNames()
        {
            var names = new string[Bone.Last];
            for (int b = 0; b < Bone.Last; b++) names[b] = HumanTrait.BoneName[HumanTrait.GetBoneIndexToMono(b)];
            return names;
        }

        static string FingerName(bool left, int i) => HumanTrait.BoneName[24 + (left ? 0 : Bone.FingerBoneCount) + i];

        static int FindHumanBone(HumanBone[] human, string humanName)
        {
            for (int i = 0; i < human.Length; i++)
                if (human[i].humanName == humanName) return i;
            return -1;
        }

        /// <summary>The description's errors: missing required bones, duplicate human bones or
        /// transforms.</summary>
        static string ValidateDescription(in HumanDescription d)
        {
            var human = d.human ?? Array.Empty<HumanBone>();
            if (human.Length == 0) return "No human bone found. Ensure both rig type match";
            var body = BodyNames();
            for (int b = 0; b < Bone.Last; b++)
            {
                if (!HumanTrait.RequiredBone(HumanTrait.GetBoneIndexToMono(b))) continue;
                if (FindHumanBone(human, body[b]) < 0) return $"Required human bone '{body[b]}' not found";
            }
            for (int i = 0; i < human.Length; i++)
            {
                if (string.IsNullOrEmpty(human[i].humanName)) continue;
                for (int j = i + 1; j < human.Length; j++)
                    if (human[j].humanName == human[i].humanName)
                        return $"Found duplicate human bone '{human[i].humanName}' with transform '{human[i].boneName}' and '{human[j].boneName}'";
            }
            for (int i = 0; i < human.Length; i++)
            {
                if (string.IsNullOrEmpty(human[i].boneName)) continue;
                for (int j = i + 1; j < human.Length; j++)
                    if (human[j].boneName == human[i].boneName)
                        return $"Found duplicate transform '{human[i].boneName}' for human bone '{human[i].humanName}' and '{human[j].humanName}'";
            }
            return null;
        }

        /// <summary>Every mapped transform exists once.</summary>
        static string ValidateHierarchy(in HumanDescription d, List<NamedTransform> transforms)
        {
            foreach (var bone in d.human)
            {
                if (string.IsNullOrEmpty(bone.humanName)) continue;
                int found = -1;
                for (int i = 0; i < transforms.Count; i++)
                {
                    if (transforms[i].Name != bone.boneName) continue;
                    if (found >= 0)
                        return $"Ambiguous Transform '{transforms[found].Path}' and '{transforms[i].Path}' found in hierarchy for human bone '{bone.humanName}'. Transform name mapped to a human bone must be unique.";
                    found = i;
                }
                if (found < 0) return $"Transform '{bone.boneName}' for human bone '{bone.humanName}' not found";
            }
            return null;
        }

        /// <summary>Builds the avatar of <paramref name="go"/>; null with <paramref name="error"/> when
        /// the description does not fit. <paramref name="human"/>: a humanoid avatar;
        /// <paramref name="onlySkeletonNames"/>: keep only the transforms the description's skeleton
        /// names (AvatarBuilder.BuildHumanAvatar with a skeleton); <paramref name="order"/>: the
        /// transforms in the order of the model file the importer built the avatar from (parents
        /// first), instead of the hierarchy's.</summary>
        public static AvatarConstant Build(GameObject go, in HumanDescription d, bool human, bool onlySkeletonNames, out string error,
            Transform[] order = null)
        {
            error = null;
            if (human && (error = ValidateDescription(d)) != null) return null;
            var root = go.transform;
            HashSet<string> names = null;
            if (onlySkeletonNames && d.skeleton != null)
            {
                names = new HashSet<string> { go.name };
                foreach (var s in d.skeleton) names.Add(s.name);
            }
            var transforms = new List<NamedTransform>();
            AllChildren(root, "", transforms, names);
            if (order != null && order.Length == transforms.Count && Array.IndexOf(order, null) < 0)
            {
                var byTransform = new Dictionary<Transform, NamedTransform>(transforms.Count);
                foreach (var t in transforms) byTransform[t.Transform] = t;
                var ordered = new List<NamedTransform>(transforms.Count);
                foreach (var t in order)
                    if (byTransform.TryGetValue(t, out var named)) ordered.Add(named);
                if (ordered.Count == transforms.Count) transforms = ordered;
            }
            if (transforms.Count == 0 || transforms[0].Transform != root)
            {
                error = $"Root '{go.name}' has not been included in avatar transforms.";
                return null;
            }
            if (human)
            {
                var all = new List<NamedTransform>();
                AllChildren(root, "", all, names);
                if ((error = ValidateHierarchy(d, all)) != null) return null;
            }

            // The avatar skeleton: every transform, its parent among them, its path hash.
            int n = transforms.Count;
            var skel = new Skeleton(n, 0);
            var c = new AvatarConstant { AvatarSkeleton = skel, Paths = new string[n] };
            for (int i = 0; i < n; i++)
            {
                var parent = transforms[i].Transform.m_Parent;
                int p = -1;
                if (parent != null)
                    for (int j = i - 1; j >= 0; j--)
                        if (transforms[j].Transform == parent) { p = j; break; }
                skel.Parent[i] = p;
                skel.Id[i] = transforms[i].Hash;
                c.Paths[i] = transforms[i].Path;
            }
            var current = new SkeletonPose(n);
            for (int i = 0; i < n; i++)
            {
                var t = transforms[i].Transform;
                current.X[i] = new Trs(V(t.localPosition), Q(t.localRotation), V(t.localScale));
            }
            var tpose = new SkeletonPose(n);
            tpose.CopyFrom(current);
            if (d.skeleton != null && d.skeleton.Length > 0)
            {
                tpose.X[0] = ToTrs(d.skeleton[0]);
                for (int s = 1; s < d.skeleton.Length; s++)
                {
                    int i = IndexOfName(transforms, d.skeleton[s].name);
                    if (i >= 0) tpose.X[i] = ToTrs(d.skeleton[s]);
                }
            }
            c.AvatarSkeletonPose = tpose;
            c.DefaultPose = current;
            if (human) BuildHuman(c, transforms, d, tpose);
            return c;
        }

        /// <summary>The first transform after the root with that name.</summary>
        static int IndexOfName(List<NamedTransform> transforms, string name)
        {
            for (int i = 1; i < transforms.Count; i++)
                if (transforms[i].Name == name) return i;
            return -1;
        }

        static void BuildHuman(AvatarConstant c, List<NamedTransform> transforms, HumanDescription d, SkeletonPose tpose)
        {
            var skel = c.AvatarSkeleton;
            int n = skel.Count;
            var body = BodyNames();
            var bodyOf = Fill(n);
            var leftOf = Fill(n);
            var rightOf = Fill(n);
            void Map(int[] of, string humanName, int value)
            {
                int h = FindHumanBone(d.human, humanName);
                if (h < 0) return;
                for (int i = 0; i < n; i++)
                    if (transforms[i].Name == d.human[h].boneName) of[i] = value;
            }
            for (int b = 0; b < Bone.Last; b++) Map(bodyOf, body[b], b);
            for (int f = 0; f < Bone.FingerBoneCount; f++) Map(leftOf, FingerName(true, f), f);
            for (int f = 0; f < Bone.FingerBoneCount; f++) Map(rightOf, FingerName(false, f), f);
            int bodyCount = 0, leftCount = 0, rightCount = 0, hips = -1;
            for (int i = 0; i < n; i++)
            {
                if (bodyOf[i] != -1) bodyCount++;
                if (leftOf[i] != -1) leftCount++;
                if (rightOf[i] != -1) rightCount++;
                if (bodyOf[i] == 0) hips = i;
            }

            // The human skeleton: the hips' parent, then each mapped transform and the transforms
            // between it and the hips, in hierarchy order; the mapped ones get axes.
            var flag = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (bodyOf[i] == -1 && leftOf[i] == -1 && rightOf[i] == -1) continue;
                int j = i;
                flag[j] = true;
                while (j != hips)
                {
                    j = skel.Parent[j];
                    if (j < 0) break;
                    flag[j] = true;
                }
            }
            int count = 1;
            foreach (bool f in flag) if (f) count++;
            var hs = new Skeleton(count, bodyCount + leftCount + rightCount);
            hs.Id[0] = skel.Id[skel.Parent[hips]];
            int k = 1, axes = 0;
            for (int i = 0; i < n; i++)
            {
                if (!flag[i]) continue;
                hs.Id[k] = skel.Id[i];
                hs.Parent[k] = hs.FindNode(skel.Id[skel.Parent[i]]);
                hs.AxesId[k] = bodyOf[i] == -1 && leftOf[i] == -1 && rightOf[i] == -1 ? -1 : axes++;
                k++;
            }

            var h = new Human { Skeleton = hs, SkeletonPose = new SkeletonPose(count) };
            HumanOps.DefaultMass(h.BoneMass);
            if (leftCount > 0) h.LeftHand = new Hand();
            if (rightCount > 0) h.RightHand = new Hand();
            h.HasLeftHand = leftCount > 0;
            h.HasRightHand = rightCount > 0;
            for (int i = 0; i < n; i++)
            {
                if (bodyOf[i] != -1) h.BoneIndex[bodyOf[i]] = hs.FindNode(skel.Id[i]);
                if (leftCount > 0 && leftOf[i] != -1) h.LeftHand.BoneIndex[leftOf[i]] = hs.FindNode(skel.Id[i]);
                if (rightCount > 0 && rightOf[i] != -1) h.RightHand.BoneIndex[rightOf[i]] = hs.FindNode(skel.Id[i]);
            }
            h.ArmTwist = d.m_ArmTwist;
            h.ForeArmTwist = d.m_ForeArmTwist;
            h.UpperLegTwist = d.m_UpperLegTwist;
            h.LegTwist = d.m_LegTwist;
            h.ArmStretch = d.m_ArmStretch;
            h.LegStretch = d.m_LegStretch;
            h.FeetSpacing = d.m_FeetSpacing;
            h.HasTDoF = d.m_HasTranslationDoF;

            // The T-pose in avatar space, copied to the human skeleton with an identity node 0.
            var global = new SkeletonPose(n);
            Skel.ComputeGlobal(skel, tpose, global);
            var humanGlobal = new SkeletonPose(count);
            Skel.CopyById(skel, global, hs, humanGlobal);
            humanGlobal.X[0] = Trs.Identity;
            HumanOps.AdjustMass(h);
            HumanOps.SetupAxes(h, humanGlobal);
            if (h.HasLeftHand) HumanOps.HandSetupAxes(h.LeftHand, humanGlobal, hs, true);
            if (h.HasRightHand) HumanOps.HandSetupAxes(h.RightHand, humanGlobal, hs, false);

            // Modified limits replace the default ranges.
            const float Deg2Rad = 0.0174532924f;
            void Limit(string humanName, int node)
            {
                int b = FindHumanBone(d.human, humanName);
                if (b < 0 || node == -1 || d.human[b].limit.useDefaultValues) return;
                int id = hs.AxesId[node];
                if (id == -1) return;
                var l = d.human[b].limit;
                hs.AxesArray[id].Min = new F4(l.min.x * Deg2Rad, l.min.y * Deg2Rad, l.min.z * Deg2Rad, 0);
                hs.AxesArray[id].Max = new F4(l.max.x * Deg2Rad, l.max.y * Deg2Rad, l.max.z * Deg2Rad, 0);
            }
            for (int b = 0; b < Bone.Last; b++) Limit(body[b], h.BoneIndex[b]);
            if (h.HasLeftHand)
                for (int f = 0; f < Bone.FingerBoneCount; f++) Limit(FingerName(true, f), h.LeftHand.BoneIndex[f]);
            if (h.HasRightHand)
                for (int f = 0; f < Bone.FingerBoneCount; f++) Limit(FingerName(false, f), h.RightHand.BoneIndex[f]);

            c.Human = h;
            c.HumanSkeletonIndexArray = new int[count];
            for (int i = 0; i < count; i++) c.HumanSkeletonIndexArray[i] = skel.FindNode(hs.Id[i]);
            c.HumanSkeletonReverseIndexArray = new int[n];
            for (int i = 0; i < n; i++) c.HumanSkeletonReverseIndexArray[i] = hs.FindNode(skel.Id[i]);
            if (!string.IsNullOrEmpty(d.m_RootMotionBoneName))
                Stubs.Ignored("HumanDescription root motion bone", "a humanoid avatar's root motion node is not applied yet");
        }

        static int[] Fill(int n)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = -1;
            return a;
        }

        static Trs ToTrs(in SkeletonBone b) => new Trs(V(b.position), Q(b.rotation), V(b.scale));

        internal static F4 V(Vector3 v) => new F4(v.x, v.y, v.z, 0);

        internal static F4 Q(Quaternion q) => new F4(q.x, q.y, q.z, q.w);
    }
}

namespace UnityEngine
{
    using Unbound.Runtime.Humanoid;

    /// <summary>Builds avatars at run time from a hierarchy (and a human description).</summary>
    public partial class AvatarBuilder
    {
        public AvatarBuilder() { }

        public static Avatar BuildHumanAvatar(GameObject go, HumanDescription humanDescription)
        {
            if (go == null) throw new NullReferenceException();
            var avatar = Avatar.Create();
            avatar.m_Description = humanDescription.Copy();
            avatar.m_Human = true;
            var c = AvatarBuild.Build(go, humanDescription, true, humanDescription.skeleton != null && humanDescription.skeleton.Length != 0, out string error);
            if (c == null) Debug.LogError($"AvatarBuilder '{go.name}': {error}");
            avatar.SetConstant(c);
            return avatar;
        }

        public static Avatar BuildGenericAvatar(GameObject go, string rootMotionTransformName)
        {
            if (go == null) throw new NullReferenceException();
            var avatar = Avatar.Create();
            var d = new HumanDescription { m_RootMotionBoneName = rootMotionTransformName, human = Array.Empty<HumanBone>(), skeleton = Array.Empty<SkeletonBone>() };
            avatar.m_Description = d;
            var c = AvatarBuild.Build(go, d, false, false, out string error);
            if (c != null && !string.IsNullOrEmpty(rootMotionTransformName))
            {
                int node = Array.IndexOf(c.Paths, rootMotionTransformName);
                if (node < 0)
                    foreach (var path in c.Paths)
                        if (path == rootMotionTransformName || path.EndsWith("/" + rootMotionTransformName, StringComparison.Ordinal)) { node = Array.IndexOf(c.Paths, path); break; }
                if (node < 0)
                {
                    c = null;
                    error = $"Cannot find root motion transform '{rootMotionTransformName}' in the hierarchy";
                }
                else c.RootMotionBoneIndex = node;
            }
            if (c == null) Debug.LogError($"AvatarBuilder '{go.name}': {error}");
            avatar.SetConstant(c);
            return avatar;
        }
    }
}
