using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>The transforms of an avatar's nodes below a root, by path hash.</summary>
    internal static class AvatarBinding
    {
        public static UnityEngine.Transform Find(UnityEngine.Transform root, uint hash) => HashedBindings.Find(root, hash);

        /// <summary>Transforms for the human skeleton's nodes and the
        /// hips' ancestors; the other nodes stay unbound.</summary>
        public static UnityEngine.Transform[] Bind(UnityEngine.Transform root, AvatarConstant c)
        {
            var skel = c.AvatarSkeleton;
            var bound = new bool[skel.Count];
            if (c.HumanSkeletonIndexArray.Length != 0)
                for (int i = c.HumanSkeletonIndexArray[0]; i != -1; i = skel.Parent[i]) bound[i] = true;
            foreach (int i in c.HumanSkeletonIndexArray)
                if (i != -1) bound[i] = true;
            var map = new Dictionary<uint, UnityEngine.Transform>();
            Collect(root, "", map);
            var result = new UnityEngine.Transform[skel.Count];
            for (int i = 0; i < skel.Count; i++)
                if (bound[i] && map.TryGetValue(skel.Id[i], out var t)) result[i] = t;
            return result;
        }

        static void Collect(UnityEngine.Transform t, string path, Dictionary<uint, UnityEngine.Transform> map)
        {
            map.TryAdd(HashedBindings.Crc32(path), t);
            foreach (var child in t.m_Children)
                Collect(child, path.Length == 0 ? child.name : path + "/" + child.name, map);
        }
    }
}

namespace UnityEngine
{
    /// <summary>Reads and writes a humanoid hierarchy's pose in muscle space (as the Unity Editor's
    /// HumanPoseHandler: RetargetFrom / RetargetTo over the avatar's human).</summary>
    public partial class HumanPoseHandler : IDisposable
    {
        internal IntPtr m_Ptr;
        AvatarConstant m_Constant;
        Transform m_Root;
        Transform[] m_Bound;
        SkeletonPose m_AvatarPose, m_AvatarGlobal, m_HumanPose, m_W50, m_W58, m_W60, m_W68;

        public HumanPoseHandler(Avatar avatar, Transform root)
        {
            if (root == null) throw new ArgumentNullException("HumanPoseHandler root Transform is null");
            if (avatar == null) throw new ArgumentNullException("HumanPoseHandler avatar is null");
            if (!avatar.isValid) throw new ArgumentException("HumanPoseHandler avatar is invalid");
            if (!avatar.isHuman) throw new ArgumentException("HumanPoseHandler avatar is not human");
            Init(avatar.Constant);
            m_Root = root;
            m_Bound = AvatarBinding.Bind(root, m_Constant);
        }

        /// <summary>A handler without transforms: its avatar pose is read and written as seven floats
        /// per joint (local position, local rotation), the joints found by their paths' hashes among the
        /// avatar's nodes (unknown paths are skipped). The avatar root stays at the origin.</summary>
        public HumanPoseHandler(Avatar avatar, string[] jointPaths)
        {
            if (avatar == null) throw new ArgumentNullException("HumanPoseHandler avatar is null");
            if (jointPaths == null) throw new ArgumentNullException("HumanPoseHandler jointPaths array is null");
            if (!avatar.isValid) throw new ArgumentException("HumanPoseHandler avatar is invalid");
            if (!avatar.isHuman) throw new ArgumentException("HumanPoseHandler avatar is not human");
            Init(avatar.Constant);
            var skel = m_Constant.AvatarSkeleton;
            m_Joints = new int[jointPaths.Length];
            for (int i = 0; i < jointPaths.Length; i++)
                m_Joints[i] = Array.IndexOf(skel.Id, HashedBindings.Crc32(jointPaths[i] ?? ""));
        }

        void Init(AvatarConstant constant)
        {
            m_Constant = constant;
            int n = m_Constant.AvatarSkeleton.Count, h = m_Constant.Human.Skeleton.Count;
            m_AvatarPose = new SkeletonPose(n);
            m_AvatarGlobal = new SkeletonPose(n);
            m_HumanPose = new SkeletonPose(h);
            m_W50 = new SkeletonPose(h);
            m_W58 = new SkeletonPose(h);
            m_W60 = new SkeletonPose(h);
            m_W68 = new SkeletonPose(h);
            m_Ptr = (IntPtr)1;
        }

        /// <summary>The avatar node of each joint path (−1 when the avatar has none); null for a
        /// handler made from a transform hierarchy.</summary>
        int[] m_Joints;

        /// <summary>The joints' local positions and rotations in the internal avatar pose, seven floats
        /// per joint (as many joints as fit).</summary>
        public void GetInternalAvatarPose(Unity.Collections.NativeArray<float> avatarPose)
        {
            Check();
            if (m_Joints == null)
            {
                Debug.LogError("Trying to get an avatar pose but no joint paths were provided to the HumanPoseHandler");
                return;
            }
            int count = Math.Min(m_Joints.Length, avatarPose.Length / 7);
            for (int i = 0; i < count; i++)
            {
                int node = m_Joints[i];
                if (node < 0) continue;
                var x = m_AvatarPose.X[node];
                int k = i * 7;
                avatarPose[k] = x.t.x; avatarPose[k + 1] = x.t.y; avatarPose[k + 2] = x.t.z;
                avatarPose[k + 3] = x.q.x; avatarPose[k + 4] = x.q.y; avatarPose[k + 5] = x.q.z; avatarPose[k + 6] = x.q.w;
            }
        }

        /// <summary>Writes the joints' local positions and rotations into the internal avatar pose.</summary>
        public void SetInternalAvatarPose(Unity.Collections.NativeArray<float> avatarPose)
        {
            Check();
            if (m_Joints == null)
            {
                Debug.LogError("Trying to set an avatar pose but no joint paths were provided to the HumanPoseHandler");
                return;
            }
            int count = Math.Min(m_Joints.Length, avatarPose.Length / 7);
            for (int i = 0; i < count; i++)
            {
                int node = m_Joints[i];
                if (node < 0) continue;
                int k = i * 7;
                m_AvatarPose.X[node].t = new F4(avatarPose[k], avatarPose[k + 1], avatarPose[k + 2], 0f);
                m_AvatarPose.X[node].q = new F4(avatarPose[k + 3], avatarPose[k + 4], avatarPose[k + 5], avatarPose[k + 6]);
            }
        }

        public void Dispose()
        {
            m_Ptr = IntPtr.Zero;
            m_Constant = null;
            m_Bound = null;
            GC.SuppressFinalize(this);
        }

        void Check()
        {
            if (m_Ptr == IntPtr.Zero) throw new NullReferenceException("HumanPoseHandler is not initialized properly");
        }

        public void GetHumanPose(ref HumanPose humanPose)
        {
            Check();
            humanPose.Init();
            ReadTransforms();
            GetInternal(ref humanPose);
        }

        public void SetHumanPose(ref HumanPose humanPose)
        {
            Check();
            humanPose.Init();
            SetInternal(ref humanPose);
            WriteTransforms();
        }

        public void GetInternalHumanPose(ref HumanPose humanPose)
        {
            Check();
            humanPose.Init();
            GetInternal(ref humanPose);
        }

        public void SetInternalHumanPose(ref HumanPose humanPose)
        {
            Check();
            humanPose.Init();
            SetInternal(ref humanPose);
        }

        /// <summary>The avatar's T-pose with the bound transforms' local positions and rotations.</summary>
        void ReadTransforms()
        {
            if (m_Bound == null) return;
            m_AvatarPose.CopyFrom(m_Constant.AvatarSkeletonPose);
            for (int i = 0; i < m_Bound.Length; i++)
            {
                var t = m_Bound[i];
                if (t == null) continue;
                m_AvatarPose.X[i].t = AvatarBuild.V(t.localPosition);
                m_AvatarPose.X[i].q = AvatarBuild.Q(t.localRotation);
            }
        }

        /// <summary>The bound transforms below the root take the pose's local positions and rotations.</summary>
        void WriteTransforms()
        {
            if (m_Bound == null) return;
            for (int i = 1; i < m_Bound.Length; i++)
            {
                var t = m_Bound[i];
                if (t == null) continue;
                var x = m_AvatarPose.X[i];
                t.localPosition = new Vector3(x.t.x, x.t.y, x.t.z);
                t.localRotation = Rotation(x.q);
            }
        }

        static Quaternion Rotation(F4 q)
        {
            float a = q.y * q.y + q.x * q.x, b = q.w * q.w + q.z * q.z;
            if (b + a <= 1e-30f) return Quaternion.identity;
            var n = M.QNormalize(q);
            return new Quaternion(n.x, n.y, n.z, n.w);
        }

        void GetInternal(ref HumanPose humanPose)
        {
            var c = m_Constant;
            int hipsParent = c.HumanSkeletonIndexArray[0];
            Skel.ComputeGlobal(c.AvatarSkeleton, m_AvatarPose, m_AvatarGlobal, hipsParent, 0);
            Skel.CopyById(c.AvatarSkeleton, m_AvatarPose, c.Human.Skeleton, m_HumanPose);
            m_HumanPose.X[0] = m_AvatarGlobal.X[hipsParent];
            var pose = new Unbound.Runtime.Humanoid.HumanPose();
            HumanOps.RetargetFrom(c.Human, m_HumanPose, pose, m_W50, m_W58, m_W60, m_W68, null);
            humanPose.bodyPosition = new Vector3(pose.RootX.t.x, pose.RootX.t.y, pose.RootX.t.z);
            humanPose.bodyRotation = Rotation(pose.RootX.q);
            ToMuscles(pose, humanPose.muscles);
        }

        void SetInternal(ref HumanPose humanPose)
        {
            var c = m_Constant;
            var human = c.Human;
            int hipsParent = c.HumanSkeletonIndexArray[0];
            var rootX = Trs.Identity;
            if (m_Root != null)
                rootX = new Trs(AvatarBuild.V(m_Root.position), AvatarBuild.Q(m_Root.rotation), AvatarBuild.V(m_Root.lossyScale));
            var pose = new Unbound.Runtime.Humanoid.HumanPose();
            pose.RootX.t = AvatarBuild.V(humanPose.bodyPosition);
            pose.RootX.q = AvatarBuild.Q(humanPose.bodyRotation);
            FromMuscles(humanPose.muscles, pose);
            var output = new Unbound.Runtime.Humanoid.HumanPose();
            HumanOps.RetargetTo(human, pose, null, rootX, output, m_HumanPose, m_W50, true);
            m_W50.CopyFrom(m_HumanPose);
            HumanOps.TwistSolve(human, m_W50, m_W58);
            m_AvatarPose.CopyFrom(c.AvatarSkeletonPose);
            m_AvatarPose.X[0] = rootX;
            Skel.ComputeGlobal(c.AvatarSkeleton, m_AvatarPose, m_AvatarPose, hipsParent, 0);
            Skel.ComputeGlobal(human.Skeleton, m_W50, m_W50, 1, 1);
            m_W50.X[0] = m_AvatarPose.X[hipsParent];
            Skel.ComputeLocal(c.AvatarSkeleton, m_AvatarPose, m_AvatarPose, hipsParent, 0);
            Skel.ComputeLocal(human.Skeleton, m_W50, m_W50, 1, 1);
            m_W50.X[0] = m_AvatarPose.X[hipsParent];
            for (int i = 0; i < c.HumanSkeletonIndexArray.Length; i++)
            {
                int a = c.HumanSkeletonIndexArray[i];
                if (a >= 0) m_AvatarPose.X[a] = m_W50.X[i];
            }
        }

        /// <summary>HumanPose.muscles: the 55 body DoFs, then the left and the right hand's 20.</summary>
        static void ToMuscles(Unbound.Runtime.Humanoid.HumanPose pose, float[] muscles)
        {
            Array.Copy(pose.DoF, 0, muscles, 0, Bone.DoFCount);
            Array.Copy(pose.LeftHand.DoF, 0, muscles, Bone.DoFCount, Bone.HandDoFCount);
            Array.Copy(pose.RightHand.DoF, 0, muscles, Bone.DoFCount + Bone.HandDoFCount, Bone.HandDoFCount);
        }

        static void FromMuscles(float[] muscles, Unbound.Runtime.Humanoid.HumanPose pose)
        {
            Array.Copy(muscles, 0, pose.DoF, 0, Bone.DoFCount);
            Array.Copy(muscles, Bone.DoFCount, pose.LeftHand.DoF, 0, Bone.HandDoFCount);
            Array.Copy(muscles, Bone.DoFCount + Bone.HandDoFCount, pose.RightHand.DoF, 0, Bone.HandDoFCount);
        }
    }
}
