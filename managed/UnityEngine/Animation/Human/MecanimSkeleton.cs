using System;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>A bone's muscle frame (): the rotations taking the bone's local rotation
    /// into muscle space, the sign of each axis, the limits in radians, the length and the
    /// decomposition (0 qtan, 1 ZYRoll, 2 RollZY, 3 Euler).</summary>
    internal sealed class Axes
    {
        public F4 PreQ = F4.QIdentity, PostQ = F4.QIdentity, Sgn = F4.One3, Min, Max;
        public float Length = 1f;
        public int Type;

        public Axes Clone() => (Axes)MemberwiseClone();
    }

    /// <summary>Nodes in parent-first order with their parent, axes
    /// and name hash.</summary>
    internal sealed class Skeleton
    {
        public int[] Parent;
        public int[] AxesId;
        public uint[] Id;
        public Axes[] AxesArray;

        public int Count => Parent.Length;

        public Skeleton(int count, int axes)
        {
            Parent = new int[count];
            AxesId = new int[count];
            Id = new uint[count];
            AxesArray = new Axes[axes];
            for (int i = 0; i < axes; i++) AxesArray[i] = new Axes();
            for (int i = 0; i < count; i++) { Parent[i] = -1; AxesId[i] = -1; }
        }

        public int FindNode(uint id)
        {
            for (int i = 0; i < Id.Length; i++)
                if (Id[i] == id) return i;
            return -1;
        }
    }

    /// <summary>A skeleton's transforms (local or global).</summary>
    internal sealed class SkeletonPose
    {
        public readonly Trs[] X;

        public SkeletonPose(int count)
        {
            X = new Trs[count];
            for (int i = 0; i < count; i++) X[i] = Trs.Identity;
        }

        public void CopyFrom(SkeletonPose other) => Array.Copy(other.X, X, X.Length);
    }

    /// <summary>Skeleton pose and muscle axes operations.</summary>
    internal static class Skel
    {
        /// <summary>Globals of every node: node 0 keeps its local, the others are parent * local.</summary>
        public static void ComputeGlobal(Skeleton s, SkeletonPose local, SkeletonPose global)
        {
            global.X[0] = local.X[0];
            for (int i = 1; i < s.Count; i++) global.X[i] = M.Mul(global.X[s.Parent[i]], local.X[i]);
        }

        /// <summary>The globals from <paramref name="index"/> up the parents to <paramref name="stop"/>
        /// (whose parent's global is used as it is).</summary>
        public static void ComputeGlobal(Skeleton s, SkeletonPose local, SkeletonPose global, int index, int stop)
        {
            int parent = s.Parent[index];
            if (parent == -1)
            {
                global.X[index] = local.X[index];
                return;
            }
            if (index != stop) ComputeGlobal(s, local, global, parent, stop);
            global.X[index] = M.Mul(global.X[parent], local.X[index]);
        }

        /// <summary>Locals of every node (from the last, so the poses may be the same).</summary>
        public static void ComputeLocal(Skeleton s, SkeletonPose global, SkeletonPose local)
        {
            for (int i = s.Count - 1; i > 0; i--) local.X[i] = M.InvMul(global.X[s.Parent[i]], global.X[i]);
            local.X[0] = global.X[0];
        }

        /// <summary>The locals from <paramref name="index"/> up to <paramref name="stop"/>.</summary>
        public static void ComputeLocal(Skeleton s, SkeletonPose global, SkeletonPose local, int index, int stop)
        {
            int i = index;
            while (true)
            {
                int parent = s.Parent[i];
                if (parent == -1)
                {
                    local.X[i] = global.X[i];
                    return;
                }
                local.X[i] = M.InvMul(global.X[parent], global.X[i]);
                if (i == stop) return;
                i = parent;
            }
        }

        public static void ComputeGlobalQ(Skeleton s, SkeletonPose local, SkeletonPose global, int index, int stop)
        {
            int parent = s.Parent[index];
            if (parent == -1)
            {
                global.X[index].q = local.X[index].q;
                return;
            }
            if (index != stop) ComputeGlobalQ(s, local, global, parent, stop);
            global.X[index].q = M.QNormalize(M.QMul(global.X[parent].q, local.X[index].q));
        }

        public static void ComputeLocalQ(Skeleton s, SkeletonPose global, SkeletonPose local)
        {
            for (int i = s.Count - 1; i > 0; i--)
                local.X[i].q = M.QNormalize(M.QMul(M.QConj(global.X[s.Parent[i]].q), global.X[i].q));
            local.X[0].q = global.X[0].q;
        }

        public static void ComputeLocalQ(Skeleton s, SkeletonPose global, SkeletonPose local, int index, int stop)
        {
            int i = index;
            while (true)
            {
                int parent = s.Parent[i];
                if (parent == -1)
                {
                    local.X[i].q = global.X[i].q;
                    return;
                }
                local.X[i].q = M.QNormalize(M.QMul(M.QConj(global.X[parent].q), global.X[i].q));
                if (i == stop) return;
                i = parent;
            }
        }

        /// <summary>Copies between skeletons: each node of the destination
        /// takes the source node with the same id (nodes without one keep their value).</summary>
        public static void CopyById(Skeleton src, SkeletonPose srcPose, Skeleton dst, SkeletonPose dstPose)
        {
            for (int i = 0; i < dst.Count; i++)
            {
                int j = src.FindNode(dst.Id[i]);
                if (j >= 0) dstPose.X[i] = srcPose.X[j];
            }
        }

        /// <summary>A node's local rotation in muscle space (ToAxes), or its qtan
        /// when it has no axes.</summary>
        public static F4 GetDoF(Skeleton s, SkeletonPose local, int index)
        {
            var q = local.X[index].q;
            int id = s.AxesId[index];
            if (id == -1) return M.Quat2Qtan(q).Xyz0;
            return ToAxes(s.AxesArray[id], q);
        }

        public static F4 ToAxes(Axes a, F4 q)
        {
            var qp = M.QNormalize(M.QMul(M.QConj(a.PreQ), M.QMul(q, a.PostQ)));
            F4 v;
            switch (a.Type)
            {
                case 3:
                    v = Euler.QuatToEulerXYZ(qp);
                    return Project(a, v);
                case 1: v = M.Quat2ZYRoll(qp); break;
                case 2: v = M.Quat2RollZY(qp); break;
                default: v = M.Quat2Qtan(qp); break;
            }
            v = CopySign(v, a.Sgn);
            return Project(a, M.DoubleAtan(v));
        }

        /// <summary>The value with its sign flipped where the axis sign is negative.</summary>
        static F4 CopySign(F4 v, F4 sgn) => new F4(
            sgn.x < 0f || (sgn.x == 0f && float.IsNegative(sgn.x)) ? -v.x : v.x,
            sgn.y < 0f || (sgn.y == 0f && float.IsNegative(sgn.y)) ? -v.y : v.y,
            sgn.z < 0f || (sgn.z == 0f && float.IsNegative(sgn.z)) ? -v.z : v.z, 0);

        static F4 Project(Axes a, F4 v) => new F4(
            M.LimitProject(a.Min.x, a.Max.x, v.x), M.LimitProject(a.Min.y, a.Max.y, v.y), M.LimitProject(a.Min.z, a.Max.z, v.z), 0);

        /// <summary>Sets a node's local rotation from muscle space (FromAxes), or from a
        /// qtan when it has no axes.</summary>
        public static void SetDoF(Skeleton s, SkeletonPose local, F4 dof, int index)
        {
            int id = s.AxesId[index];
            local.X[index].q = id == -1 ? M.Qtan2Quat(dof) : FromAxes(s.AxesArray[id], dof);
        }

        public static F4 FromAxes(Axes a, F4 dof)
        {
            var v = new F4(M.LimitUnproject(a.Min.x, a.Max.x, dof.x), M.LimitUnproject(a.Min.y, a.Max.y, dof.y),
                M.LimitUnproject(a.Min.z, a.Max.z, dof.z), 0);
            F4 q;
            if (a.Type == 3) q = Euler.EulerXYZToQuat(v);
            else
            {
                var t = M.HalfTan(CopySign(v, a.Sgn));
                q = a.Type switch
                {
                    1 => M.ZYRoll2Quat(t),
                    2 => M.RollZY2Quat(t),
                    _ => M.Qtan2Quat(t),
                };
            }
            return M.QNormalize(M.QMul(a.PreQ, M.QMul(q, M.QConj(a.PostQ))));
        }

        /// <summary>Q turned so its axis (q * postQ applied to x)
        /// points along the reference's.</summary>
        public static F4 Align(F4 refQ, F4 q, F4 postQ)
        {
            var u = XAxis(M.QNormalize(M.QMul(refQ, postQ)));
            var v = XAxis(M.QNormalize(M.QMul(q, postQ)));
            var c = M.Cross(v, u);
            float lu = u.x * u.x + u.y * u.y + u.z * u.z, lv = v.x * v.x + v.y * v.y + v.z * v.z;
            float w = MathF.Sqrt(lv * lu) + M.Dot3(u, v);
            var r = M.QNormalize(new F4(c.x, c.y, c.z, w));
            return M.QMul(r, q);
        }

        static F4 XAxis(F4 q) => new F4(
            2f * ((q.w * q.w - 0.5f) + q.x * q.x),
            2f * (q.w * q.z + q.x * q.y),
            2f * (q.x * q.z - q.w * q.y), 0);

        /// <summary>Aligns a global rotation with the
        /// reference's along the node's axis.</summary>
        public static void Align(Skeleton s, SkeletonPose reference, SkeletonPose pose, int index)
        {
            int id = s.AxesId[index];
            if (id == -1) return;
            pose.X[index].q = Align(reference.X[index].q, pose.X[index].q, s.AxesArray[id].PostQ);
        }

        /// <summary>A bone's default frame, limits (degrees), sign and type.</summary>
        internal struct SetupAxesInfo
        {
            public F4 PreQ, MainAxis, Min, Max, Sgn;
            public int Type, ForceAxis;

            public SetupAxesInfo(F4 preQ, F4 mainAxis, F4 min, F4 max, F4 sgn, int type, int forceAxis)
            {
                PreQ = preQ; MainAxis = mainAxis; Min = min; Max = max; Sgn = sgn; Type = type; ForceAxis = forceAxis;
            }
        }

        const float Deg2Rad = 0.0174532924f;

        /// <summary>A bone's axes from its global pose, the direction to the
        /// axis node scaled by <paramref name="len"/>, and the info's main axis.</summary>
        public static void SetupAxes(Skeleton s, SkeletonPose global, in SetupAxesInfo info, int bone, int axisNode, bool left, float len)
        {
            int id = s.AxesId[bone];
            if (id == -1) return;
            int parent = s.Parent[bone];
            var axes = s.AxesArray[id];
            var boneX = global.X[bone];
            axes.Min = new F4(info.Min.x * Deg2Rad, info.Min.y * Deg2Rad, info.Min.z * Deg2Rad, 0);
            axes.Max = new F4(info.Max.x * Deg2Rad, info.Max.y * Deg2Rad, info.Max.z * Deg2Rad, 0);
            var sgnSide = left ? new F4(1, 1, 1, 0) : new F4(-1, 1, -1, 0);
            axes.Sgn = new F4(sgnSide.x * info.Sgn.x, sgnSide.y * info.Sgn.y, sgnSide.z * info.Sgn.z, 0);
            var preSide = left ? F4.One : new F4(1, 1, 1, -1);
            axes.Type = info.Type;
            axes.Length = 1f;
            var m = info.MainAxis;

            // The frame (u, v, w): u along the bone, v across it and the main axis, w = u x v.
            F4 u = new F4(1, 0, 0, 0), v = new F4(0, 0, 1, 0), w = new F4(0, 1, 0, 0);
            float length = 1f;
            if (axisNode != -1)
            {
                var d = (global.X[axisNode].t - boneX.t) * len;
                float l = M.Length3(d);
                if (l > M.Epsilon)
                {
                    var c = new F4(d.z * m.y - m.z * d.y, d.x * m.z - m.x * d.z, d.y * m.x - m.y * d.x, 0);
                    float lc = M.Length3(c);
                    if (lc > M.Epsilon)
                    {
                        u = new F4(d.x / l, d.y / l, d.z / l, 0);
                        v = new F4(c.x / lc, c.y / lc, c.z / lc, 0);
                        w = new F4(v.z * u.y - u.z * v.y, v.x * u.z - u.x * v.z, v.y * u.x - u.y * v.x, 0);
                        length = l;
                    }
                }
            }
            if (info.ForceAxis != 0)
            {
                F4 a = info.ForceAxis switch
                {
                    2 => new F4(0, 1, 0, 0),
                    3 => new F4(0, 0, 1, 0),
                    -2 => new F4(0, -1, 0, 0),
                    -1 => new F4(-1, 0, 0, 0),
                    _ => new F4(1, 0, 0, 0),
                };
                var c = new F4(m.y * a.z - m.z * a.y, m.z * a.x - m.x * a.z, m.x * a.y - m.y * a.x, 0);
                float lc = M.Length3(c);
                if (lc > M.Epsilon)
                {
                    u = a;
                    v = new F4(c.x / lc, c.y / lc, c.z / lc, 0);
                    w = new F4(v.z * a.y - a.z * v.y, v.x * a.z - a.x * v.z, v.y * a.x - a.y * v.x, 0);
                }
            }
            axes.Length = MathF.Abs(len) * length;
            var q = M.QFromColumns(u, v, w);
            var parentQ = parent != -1 ? M.QConj(global.X[parent].q) : F4.QIdentity;
            axes.PostQ = M.QNormalize(M.QMul(M.QConj(boneX.q), q));
            var pre = new F4(preSide.x * info.PreQ.x, preSide.y * info.PreQ.y, preSide.z * info.PreQ.z, preSide.w * info.PreQ.w);
            axes.PreQ = M.QNormalize(M.QMul(parentQ, M.QMul(pre, q)));
        }
    }
}
