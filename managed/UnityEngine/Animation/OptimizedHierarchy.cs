using System;
using System.Collections.Generic;
using Unbound.Runtime.Humanoid;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// The pose of an Animator without its transform hierarchy (Optimize Game Objects,
    /// AnimatorUtility.OptimizeTransformHierarchy): its avatar's skeleton, which the transform curves
    /// of the skeleton's paths animate instead of transforms. After a write, the exposed transforms
    /// (the Animator's direct children named as a skeleton node; a skinned renderer's follows its
    /// mesh's root bone instead) take their node's pose relative to the Animator, and skinned meshes
    /// below the Animator skin from the skeleton (Unity 6000.0.71f1 Editor, tests/projects/animoptimize).
    /// </summary>
    internal sealed class AnimatorSkeleton
    {
        internal readonly Animator Animator;
        internal readonly AvatarConstant Avatar;
        readonly SkeletonPose m_Local, m_Global;
        readonly Matrix4x4[] m_Matrices;
        bool m_Dirty = true;
        Transform[] m_Exposed = Array.Empty<Transform>();
        int[] m_ExposedNode = Array.Empty<int>();
        int m_ExposedCount;

        /// <summary>A skeleton starting from <paramref name="pose"/> (locals per node; the avatar's
        /// default pose where it has no value).</summary>
        internal AnimatorSkeleton(Animator animator, AvatarConstant avatar, Trs?[] pose = null)
        {
            Animator = animator;
            Avatar = avatar;
            int n = avatar.AvatarSkeleton.Count;
            m_Local = new SkeletonPose(n);
            m_Local.CopyFrom(avatar.DefaultPose);
            if (pose != null)
                for (int i = 0; i < n && i < pose.Length; i++)
                    if (pose[i] is Trs x) m_Local.X[i] = x;
            // Globals are relative to the Animator: its own transform is the root node's.
            m_Local.X[0] = Trs.Identity;
            m_Global = new SkeletonPose(n);
            m_Matrices = new Matrix4x4[n];
        }

        internal int NodeOfPath(uint pathHash) => Avatar.NodeOfPath(pathHash);

        internal Trs Local(int node) => m_Local.X[node];

        internal void SetLocal(int node, in Trs value)
        {
            m_Local.X[node] = value;
            m_Dirty = true;
        }

        internal void MarkDirty() => m_Dirty = true;

        /// <summary>The node's pose relative to the Animator.</summary>
        internal Matrix4x4 Global(int node)
        {
            Apply();
            return m_Matrices[node];
        }

        /// <summary>The Animator's direct children the skeleton poses: each named as a node (the first
        /// of the name), a skinned renderer's by its mesh's root bone when the skeleton has it.</summary>
        internal void BindExposed()
        {
            m_ExposedCount = 0;
            var root = Animator.m_GameObject != null ? Animator.m_GameObject.m_Transform : null;
            if (root == null) return;
            var children = root.m_Children;
            if (m_Exposed.Length < children.Count)
            {
                m_Exposed = new Transform[children.Count];
                m_ExposedNode = new int[children.Count];
            }
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                int node = Avatar.NodeOfName(child.m_GameObject.m_Name);
                var skinned = child.m_GameObject.GetComponent<SkinnedMeshRenderer>();
                var mesh = skinned != null ? skinned.sharedMesh : null;
                if (mesh != null && mesh.m_RootBoneNameHash != 0)
                {
                    int rootBone = Avatar.NodeOfPath(mesh.m_RootBoneNameHash);
                    if (rootBone >= 0) node = rootBone;
                }
                if (node < 0) continue;
                m_Exposed[m_ExposedCount] = child;
                m_ExposedNode[m_ExposedCount++] = node;
            }
            m_Dirty = true;
        }

        /// <summary>After a write: the globals, then the exposed transforms' poses. An exposed
        /// transform bound to the animation stream (Animator.BindStreamTransform) is the stream's
        /// own from then on: it keeps the value the stream gives it, a job's writes included,
        /// and the skeleton's node no longer poses it (6000.0.71f1, tests/projects/animoptimize
        /// job.RigExposed and jobRead.RigExposed: the exposed hand stays where it was bound while
        /// the take moves the node; with a scene handle alone or no handle it follows,
        /// jobScene.RigExposed and jobNone.RigExposed).</summary>
        internal void Apply()
        {
            if (!m_Dirty) return;
            m_Dirty = false;
            var streamBound = Animator.m_StreamHandles?.StreamBound;
            Skel.ComputeGlobal(Avatar.AvatarSkeleton, m_Local, m_Global);
            for (int i = 0; i < m_Matrices.Length; i++)
            {
                var x = m_Global.X[i];
                m_Matrices[i] = Matrix4x4.TRS(new Vector3(x.t.x, x.t.y, x.t.z), AnimatorHuman.Rotation(x.q), new Vector3(x.s.x, x.s.y, x.s.z));
            }
            for (int i = 0; i < m_ExposedCount; i++)
            {
                var t = m_Exposed[i];
                if (t == null || streamBound != null && streamBound.Contains(t)) continue;
                var x = m_Global.X[m_ExposedNode[i]];
                t.SetLocalPositionAndRotation(new Vector3(x.t.x, x.t.y, x.t.z), AnimatorHuman.Rotation(x.q));
                t.localScale = new Vector3(x.s.x, x.s.y, x.s.z);
            }
        }
    }

    /// <summary>A transform property of a skeleton node (see <see cref="AnimatorSkeleton"/>): what a
    /// transform curve animates on an Animator without its transform hierarchy.</summary>
    internal sealed class SkeletonGroup : PropertyGroup
    {
        internal readonly AnimatorSkeleton Skeleton;
        internal readonly int Node;
        internal readonly TransformProperty Property;

        internal SkeletonGroup(AnimatorSkeleton skeleton, int node, TransformProperty property)
        {
            Skeleton = skeleton;
            Node = node;
            Property = property;
            Target = skeleton.Animator;
            Width = property == TransformProperty.Rotation ? 4 : 3;
            IsQuaternion = property == TransformProperty.Rotation;
            IsScale = property == TransformProperty.Scale;
            SkeletonPath = skeleton.Avatar.Paths[node];
        }

        internal override void Read(float[] values, int offset)
        {
            var x = Skeleton.Local(Node);
            switch (Property)
            {
                case TransformProperty.Position: Put(values, offset, x.t); break;
                case TransformProperty.Rotation:
                    values[offset] = x.q.x; values[offset + 1] = x.q.y; values[offset + 2] = x.q.z; values[offset + 3] = x.q.w;
                    break;
                case TransformProperty.Euler:
                {
                    var e = AnimatorHuman.Rotation(x.q).eulerAngles;
                    values[offset] = e.x; values[offset + 1] = e.y; values[offset + 2] = e.z;
                    break;
                }
                default: Put(values, offset, x.s); break;
            }
        }

        static bool Same(in F4 a, in F4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

        static void Put(float[] values, int offset, F4 v)
        {
            values[offset] = v.x; values[offset + 1] = v.y; values[offset + 2] = v.z;
        }

        internal override bool Write(float[] values, int offset)
        {
            var x = Skeleton.Local(Node);
            var before = x;
            switch (Property)
            {
                case TransformProperty.Position: x.t = new F4(values[offset], values[offset + 1], values[offset + 2], 0); break;
                case TransformProperty.Rotation:
                {
                    float x0 = values[offset], y0 = values[offset + 1], z0 = values[offset + 2], w0 = values[offset + 3];
                    float m = MathF.Sqrt(x0 * x0 + y0 * y0 + z0 * z0 + w0 * w0);
                    if (m < 1e-6f) return false;
                    x.q = new F4(x0 / m, y0 / m, z0 / m, w0 / m);
                    break;
                }
                case TransformProperty.Euler:
                {
                    var q = Quaternion.Euler(values[offset], values[offset + 1], values[offset + 2]);
                    x.q = new F4(q.x, q.y, q.z, q.w);
                    break;
                }
                default: x.s = new F4(values[offset], values[offset + 1], values[offset + 2], 0); break;
            }
            if (Same(x.t, before.t) && Same(x.q, before.q) && Same(x.s, before.s)) return false;
            Skeleton.SetLocal(Node, x);
            return true;
        }
    }

    /// <summary>The transform properties clips animate.</summary>
    internal enum TransformProperty
    {
        Position,
        Rotation,
        Euler,
        Scale,
    }

    /// <summary>
    /// AnimatorUtility.OptimizeTransformHierarchy / DeoptimizeTransformHierarchy, also what the
    /// model importer's Optimize Game Objects does to a model (with its Extra Transforms to Expose).
    /// Matches the Unity 6000.0.71f1 Editor, tests/projects/animoptimize:
    /// <list type="bullet">
    /// <item>Optimizing needs an Animator with a valid avatar and its transform hierarchy. Every
    /// skinned renderer loses its bones; one whose root bone is not exposed moves to the root bone's
    /// pose and loses it too. The transforms of the avatar's skeleton move under the Animator
    /// (keeping their world pose); other transforms stay with their parent. Then only transforms
    /// with components besides their Transform, the exposed ones (found by their last name among
    /// the Animator's children) and their ancestors remain.</item>
    /// <item>Deoptimizing builds the skeleton's transforms again in the avatar's order (an existing
    /// transform of the name is reused), each at its default pose under its parent, and gives the
    /// skinned renderers their bones and root bone back from their mesh's bone paths.</item>
    /// </list>
    /// The Animator keeps its state: its bindings move to the skeleton and back.
    /// </summary>
    internal static class HierarchyOptimizer
    {
        static readonly HashSet<string> s_ReportedImport = new HashSet<string>();

        /// <param name="imported">The importer's optimization of a loaded model: warnings once.</param>
        internal static void Optimize(GameObject go, string[] exposed, bool imported = false)
        {
            if (go == null) return;
            var animator = go.GetComponent<Animator>();
            if (animator == null || !animator.isOptimizable || !animator.hasTransformHierarchy) return;
            var root = animator.avatarRoot;
            var avatar = animator.m_Avatar.Skeleton;
            if (root == null || avatar == null) return;
            exposed ??= Array.Empty<string>();
            var warnings = DuplicateNames(go, root);

            // The bound transforms' skeleton nodes and the skeleton's pose, while the hierarchy is whole.
            var moves = animator.SkeletonBindings(avatar);
            var pose = new Trs?[avatar.AvatarSkeleton.Count];
            for (int i = 1; i < pose.Length; i++)
            {
                var t = HashedBindings.Find(root, avatar.AvatarSkeleton.Id[i]);
                if (t != null) pose[i] = new Trs(AvatarBuild.V(t.localPosition), AvatarBuild.Q(t.localRotation), AvatarBuild.V(t.localScale));
            }

            foreach (var skinned in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var rootBone = skinned.rootBone;
                if (rootBone != null && Array.IndexOf(exposed, PathBelow(rootBone, go.transform)) < 0)
                {
                    SetWorldPoseLossy(skinned.transform, rootBone);
                    skinned.rootBone = null;
                }
                skinned.m_Bones = Array.Empty<Transform>();
            }

            var nonSkeleton = new HashSet<Transform>();
            NonSkeleton(avatar, root, root, nonSkeleton);
            var children = root.m_Children.ToArray();
            foreach (var child in children) Flatten(nonSkeleton, root, child);

            var names = new string[exposed.Length];
            for (int i = 0; i < exposed.Length; i++) names[i] = LastName(exposed[i]);
            RemoveUnnecessary(go, names, imported);

            animator.SetSkeleton(new AnimatorSkeleton(animator, avatar, pose), moves);
            foreach (var w in warnings) Warn(w, imported);
        }

        internal static void Deoptimize(GameObject go)
        {
            if (go == null) return;
            var animator = go.GetComponent<Animator>();
            if (animator == null || !animator.isOptimizable) return;
            var root = animator.avatarRoot;
            var avatar = animator.m_Avatar.Skeleton;
            if (root == null || avatar == null) return;
            var skeleton = avatar.AvatarSkeleton;
            var used = new HashSet<Transform>();
            for (int i = 1; i < skeleton.Count; i++)
            {
                var path = avatar.Paths[i];
                var name = LastName(path);
                var t = FindUnused(root, name, used);
                if (t == null) t = new GameObject(name).transform;
                int slash = path.LastIndexOf('/');
                var parent = slash < 0 ? root : root.Find(path.Substring(0, slash));
                // A transform already under its parent keeps its place among its siblings.
                if (t.m_Parent != parent) t.SetParent(parent, true);
                var x = avatar.DefaultPose.X[i];
                t.SetLocalPositionAndRotation(new Vector3(x.t.x, x.t.y, x.t.z), AnimatorHuman.Rotation(x.q));
                t.localScale = new Vector3(x.s.x, x.s.y, x.s.z);
                used.Add(t);
            }
            foreach (var skinned in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skinned.sharedMesh;
                if (mesh == null) continue;
                skinned.rootBone = mesh.m_RootBoneNameHash != 0 ? NodeTransform(avatar, root, mesh.m_RootBoneNameHash) : null;
                var hashes = mesh.m_BoneNameHashes ?? Array.Empty<uint>();
                var bones = new Transform[hashes.Length];
                for (int b = 0; b < hashes.Length; b++) bones[b] = NodeTransform(avatar, root, hashes[b]);
                skinned.m_Bones = bones;
            }
            animator.ClearSkeleton();
        }

        static Transform NodeTransform(AvatarConstant avatar, Transform root, uint pathHash)
        {
            int node = avatar.NodeOfPath(pathHash);
            if (node < 0) return null;
            return node == 0 ? root : root.Find(avatar.Paths[node]);
        }

        /// <summary>Transforms of the same name: Unity warns about each (paths below the avatar root).</summary>
        static List<string> DuplicateNames(GameObject go, Transform root)
        {
            var warnings = new List<string>();
            var seen = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                var name = t.m_GameObject.m_Name;
                if (seen.TryGetValue(name, out var first))
                    warnings.Add($"Transform '{PathBelow(t, root)}' has the same name ({name}) as transform {PathBelow(first, root)}. " +
                                 "This could lead to undefined behavior if DeoptimizeHierarchy is used on this model.");
                else seen[name] = t;
            }
            return warnings;
        }

        static void Warn(string message, bool imported)
        {
            if (imported && !s_ReportedImport.Add(message)) return;
            Debug.LogWarning(message);
        }

        /// <summary>The transforms below the avatar root outside its skeleton (not their children).</summary>
        static void NonSkeleton(AvatarConstant avatar, Transform root, Transform t, HashSet<Transform> into)
        {
            if (avatar.NodeOfPath(HashedBindings.Crc32(PathBelow(t, root))) < 0)
            {
                into.Add(t);
                return;
            }
            for (int i = 0; i < t.m_Children.Count; i++) NonSkeleton(avatar, root, t.m_Children[i], into);
        }

        /// <summary>Moves the skeleton's transforms below <paramref name="t"/> under the root, keeping
        /// their world pose, depth first.</summary>
        static void Flatten(HashSet<Transform> nonSkeleton, Transform root, Transform t)
        {
            int i = 0;
            while (i < t.m_Children.Count)
            {
                var child = t.m_Children[i];
                if (nonSkeleton.Contains(child))
                {
                    i++;
                    continue;
                }
                child.SetParent(root, true);
                Flatten(nonSkeleton, root, child);
            }
        }

        static void RemoveUnnecessary(GameObject go, string[] exposedNames, bool imported)
        {
            var transforms = go.GetComponentsInChildren<Transform>(true);
            var keep = new Dictionary<Transform, bool>(transforms.Length);
            foreach (var t in transforms) keep[t] = t.m_GameObject.m_Components.Count > 1;
            var top = go.transform;
            foreach (var name in exposedNames)
            {
                var t = top.Find(name);
                if (t != null && keep.ContainsKey(t)) keep[t] = true;
                else Warn($"Unable to optimize missing transform on GameObject '{go.name}'", imported);
            }
            for (int i = transforms.Length - 1; i >= 0; i--)
            {
                var t = transforms[i];
                if (t != top && keep[t] && t.m_Parent != null && keep.ContainsKey(t.m_Parent)) keep[t.m_Parent] = true;
            }
            for (int i = transforms.Length - 1; i >= 0; i--)
                if (!keep[transforms[i]]) Object.DestroyImmediate(transforms[i].m_GameObject, true);
        }

        /// <summary>The first unused transform of the name below the root, depth first.</summary>
        static Transform FindUnused(Transform t, string name, HashSet<Transform> used)
        {
            foreach (var child in t.m_Children)
            {
                if (child.m_GameObject.m_Name == name && !used.Contains(child)) return child;
                var found = FindUnused(child, name, used);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Gives <paramref name="t"/> the world position and rotation of <paramref name="source"/>,
        /// and the local scale closest to its world scale.</summary>
        static void SetWorldPoseLossy(Transform t, Transform source)
        {
            t.SetPositionAndRotation(source.position, source.rotation);
            var world = source.lossyScale;
            var parent = t.m_Parent != null ? t.m_Parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(Div(world.x, parent.x), Div(world.y, parent.y), Div(world.z, parent.z));
        }

        static float Div(float a, float b) => b == 0f ? 0f : a / b;

        internal static string PathBelow(Transform t, Transform root)
        {
            if (t == root) return "";
            var path = t.m_GameObject.m_Name;
            for (var p = t.m_Parent; p != null && p != root; p = p.m_Parent) path = p.m_GameObject.m_Name + "/" + path;
            return path;
        }

        static string LastName(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }
    }
}

namespace UnityEngine
{
    /// <summary>Removes and restores an animated character's transform hierarchy (see
    /// <see cref="Unbound.Runtime.HierarchyOptimizer"/>).</summary>
    public partial class AnimatorUtility
    {
        public static void OptimizeTransformHierarchy(GameObject go, string[] exposedTransforms)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            if (go.IsPersistentAsset) return;
            Unbound.Runtime.HierarchyOptimizer.Optimize(go, exposedTransforms);
        }

        public static void DeoptimizeTransformHierarchy(GameObject go)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            if (go.IsPersistentAsset) return;
            Unbound.Runtime.HierarchyOptimizer.Deoptimize(go);
        }
    }
}
