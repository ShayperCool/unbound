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
            var bound = Nodes(c);
            var map = new Dictionary<uint, UnityEngine.Transform>();
            Collect(root, "", map);
            var result = new UnityEngine.Transform[skel.Count];
            for (int i = 0; i < skel.Count; i++)
                if (bound[i] && map.TryGetValue(skel.Id[i], out var t)) result[i] = t;
            return result;
        }

        /// <summary>The avatar nodes a human pose writes: the human skeleton's and the hips' ancestors.</summary>
        public static bool[] Nodes(AvatarConstant c)
        {
            var skel = c.AvatarSkeleton;
            var bound = new bool[skel.Count];
            if (c.HumanSkeletonIndexArray.Length != 0)
                for (int i = c.HumanSkeletonIndexArray[0]; i != -1; i = skel.Parent[i]) bound[i] = true;
            foreach (int i in c.HumanSkeletonIndexArray)
                if (i != -1) bound[i] = true;
            return bound;
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
            m_Constant = avatar.Constant;
            m_Root = root;
            m_Bound = AvatarBinding.Bind(root, m_Constant);
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

        public HumanPoseHandler(Avatar avatar, string[] jointPaths) =>
            throw new NotSupportedException("HumanPoseHandler(Avatar, string[] jointPaths) (an avatar pose without transforms) is not supported yet");

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
