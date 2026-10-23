using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>Object.Instantiate: clones a GameObject hierarchy (or a single asset object) the way
    /// Unity does, through serialization: serialized state is copied, references inside the cloned
    /// hierarchy point to the clones, and the clones wake up once they are all built.</summary>
    internal static class Instantiation
    {
        /// <summary>Engine objects that are not GameObjects or components clone through this.</summary>
        internal interface ICloneable
        {
            Object CloneObject();
        }

        internal static Object Clone(Object original, Transform parent, bool instantiateInWorldSpace, Vector3? position, Quaternion? rotation)
        {
            var sourceGo = original as GameObject ?? (original as Component)?.m_GameObject;
            if (sourceGo != null)
                return CloneGameObject(original, sourceGo, parent, instantiateInWorldSpace, position, rotation);

            Object clone;
            if (original is ScriptableObject so)
            {
                clone = ScriptableObject.CreateInstanceWithoutCallbacks(so.GetType());
                new SerializedCopier(o => o).CopyScriptObject(so, clone);
                ((ScriptableObject)clone).RunCreationCallbacks();
            }
            else if (original is ICloneable cloneable)
            {
                clone = cloneable.CloneObject();
            }
            else
            {
                throw new ArgumentException($"Instantiating an object of type {original.GetType().Name} is not supported.");
            }
            clone.m_Name = original.m_Name + "(Clone)";
            return clone;
        }

        static Object CloneGameObject(Object original, GameObject sourceGo, Transform parent, bool instantiateInWorldSpace, Vector3? position, Quaternion? rotation)
        {
            var map = new Dictionary<Object, Object>();
            var pairs = new List<(Component Source, Component Clone)>();
            var source = sourceGo.m_Transform;
            var targetScene = parent != null ? parent.m_GameObject.m_Scene : SceneRegistry.EnsureActive().Handle;
            if (parent == null && SceneManagerInternal.ActiveScene != null) targetScene = SceneManagerInternal.ActiveScene.Handle;

            var rootClone = CloneTree(source, null, targetScene, map, pairs);
            var rootGo = rootClone.m_GameObject;
            rootGo.m_Name = sourceGo.m_Name + "(Clone)";

            // Remap references into the cloned set, then copy component state.
            Object Remap(Object o) => o != null && map.TryGetValue(o, out var clone) ? clone : o;
            var copier = new SerializedCopier(Remap);
            foreach (var (src, dst) in pairs)
            {
                try
                {
                    if (src is MonoBehaviour)
                    {
                        ((Behaviour)dst).m_Enabled = ((Behaviour)src).m_Enabled;
                        copier.CopyScriptObject(src, dst);
                    }
                    else
                    {
                        dst.CopyFrom(src, Remap);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e, src);
                }
            }

            // Placement.
            var sourcePosition = source.position;
            var sourceRotation = source.rotation;
            if (parent == null)
            {
                // A root clone takes the original's world pose (or the one asked for) and keeps
                // its local scale.
                SceneRegistry.Get(targetScene)?.Roots.Add(rootGo);
                rootClone.m_LocalRotation = rotation ?? sourceRotation;
                rootClone.SetLocalPosition(position ?? sourcePosition);
            }
            else
            {
                rootClone.m_Parent = parent;
                parent.m_Children.Add(rootClone);
                if (position.HasValue || rotation.HasValue)
                {
                    rootClone.KeepWorldPose(position ?? sourcePosition, rotation ?? sourceRotation, source.lossyScale, parent);
                    rootClone.m_LocalScale = source.m_LocalScale;
                }
                else if (instantiateInWorldSpace)
                {
                    rootClone.KeepWorldPose(sourcePosition, sourceRotation, source.lossyScale, parent);
                }
            }
            rootClone.MarkChanged();
            foreach (var (_, dst) in pairs)
                if (dst is Transform t) t.OnHierarchyChanged();

            if (rootGo.activeInHierarchy)
            {
                var components = new List<Component>();
                foreach (var (_, dst) in pairs)
                    if (!dst.m_Destroyed && dst.m_GameObject.activeInHierarchy)
                        components.Add(dst);
                Lifecycle.Activate(components);
            }
            if (parent != null)
                Messaging.SendFast(parent.m_GameObject, "OnTransformChildrenChanged");

            return original is GameObject ? rootGo : map[original];
        }

        static Transform CloneTree(Transform source, Transform parentClone, SceneHandle scene, Dictionary<Object, Object> map, List<(Component, Component)> pairs)
        {
            var sourceGo = source.m_GameObject;
            var go = new GameObject(sourceGo.m_Name, bare: true)
            {
                m_IsActive = sourceGo.m_IsActive,
                m_Layer = sourceGo.m_Layer,
                m_Tag = sourceGo.m_Tag,
                m_Scene = scene,
            };
            go.isStatic = sourceGo.isStatic;
            map[sourceGo] = go;
            Transform transformClone = null;
            foreach (var c in sourceGo.m_Components)
            {
                if (c.m_Destroyed) continue;
                var clone = ComponentFactory.Create(c.GetType(), go);
                if (clone is Transform t)
                {
                    transformClone = t;
                    go.m_Transform = t;
                    go.m_Components.Insert(0, t);
                    t.m_LocalPosition = source.m_LocalPosition;
                    t.m_LocalRotation = source.m_LocalRotation;
                    t.m_LocalScale = source.m_LocalScale;
                }
                else
                {
                    go.m_Components.Add(clone);
                }
                map[c] = clone;
                pairs.Add((c, clone));
            }
            if (parentClone != null)
            {
                transformClone.m_Parent = parentClone;
                parentClone.m_Children.Add(transformClone);
            }
            foreach (var child in source.m_Children)
                CloneTree(child, transformClone, scene, map, pairs);
            return transformClone;
        }
    }

    /// <summary>Object.Destroy (at the end of the frame, or after a delay) and DestroyImmediate.</summary>
    internal static class Destruction
    {
        static readonly List<(Object Target, double Time)> s_Delayed = new List<(Object, double)>();
        static readonly List<Object> s_EndOfFrame = new List<Object>();

        internal static void Destroy(Object obj, float delay)
        {
            if (delay > 0f)
            {
                s_Delayed.Add((obj, Time.timeAsDouble + delay));
                return;
            }
            if (obj.m_PendingDestroy) return;
            obj.m_PendingDestroy = true;
            s_EndOfFrame.Add(obj);
        }

        /// <summary>Moves due delayed destructions to the end-of-frame list.</summary>
        internal static void RunDelayed()
        {
            if (s_Delayed.Count == 0) return;
            double now = Time.timeAsDouble;
            for (int i = s_Delayed.Count - 1; i >= 0; i--)
            {
                if (s_Delayed[i].Time > now) continue;
                var target = s_Delayed[i].Target;
                s_Delayed.RemoveAt(i);
                if (!target.m_Destroyed && !target.m_PendingDestroy)
                {
                    target.m_PendingDestroy = true;
                    s_EndOfFrame.Add(target);
                }
            }
        }

        internal static void FlushEndOfFrame()
        {
            while (s_EndOfFrame.Count > 0)
            {
                var batch = s_EndOfFrame.ToArray();
                s_EndOfFrame.Clear();
                foreach (var obj in batch)
                {
                    if (!obj.m_Destroyed) DestroyImmediate(obj);
                }
            }
        }

        internal static void DestroyImmediate(Object obj)
        {
            switch (obj)
            {
                case GameObject go:
                    DestroyGameObject(go);
                    break;
                case Transform t:
                    if (t.m_GameObject != null && !t.m_GameObject.m_Destroyed)
                    {
                        Debug.LogError("Destroying the transform component is not allowed.", t);
                        obj.m_PendingDestroy = false;
                    }
                    break;
                case Component c:
                    DestroyComponent(c);
                    break;
                case ScriptableObject so:
                    so.RunDestroyCallbacks();
                    so.MarkDestroyed();
                    break;
                default:
                    obj.MarkDestroyed();
                    break;
            }
        }

        static void DestroyComponent(Component c)
        {
            var go = c.m_GameObject;
            if (go != null)
            {
                foreach (var other in go.m_Components)
                {
                    if (other == c || other.m_Destroyed) continue;
                    foreach (var required in ComponentFactory.RequiredComponents(other.GetType()))
                    {
                        if (!required.IsInstanceOfType(c)) continue;
                        // Another component of the type could satisfy the requirement.
                        bool replaced = false;
                        foreach (var candidate in go.m_Components)
                            if (candidate != c && !candidate.m_Destroyed && required.IsInstanceOfType(candidate)) replaced = true;
                        if (!replaced)
                        {
                            Debug.LogError($"Can't remove {c.GetType().Name} because {other.GetType().Name} ({other.GetType().Name}) depends on it", c);
                            c.m_PendingDestroy = false;
                            return;
                        }
                    }
                }
            }
            Lifecycle.OnComponentDestroyed(c);
            go?.m_Components.Remove(c);
            c.MarkDestroyed();
        }

        static void DestroyGameObject(GameObject root)
        {
            var all = new List<GameObject>();
            Collect(root.m_Transform, all);
            if (root.activeInHierarchy)
            {
                var active = new List<Component>();
                foreach (var go in all)
                    if (go.activeInHierarchy)
                        foreach (var c in go.m_Components)
                            if (!c.m_Destroyed) active.Add(c);
                Lifecycle.Deactivate(active);
            }
            foreach (var go in all)
                foreach (var c in go.m_Components.ToArray())
                    if (!c.m_Destroyed) Lifecycle.OnComponentDestroyed(c);

            var parent = root.m_Transform.m_Parent;
            if (parent != null)
            {
                parent.m_Children.Remove(root.m_Transform);
                root.m_Transform.m_Parent = null;
                parent.OnHierarchyChanged();
                Messaging.SendFast(parent.m_GameObject, "OnTransformChildrenChanged");
            }
            else
            {
                SceneRegistry.RemoveRoot(root);
            }
            foreach (var go in all)
            {
                foreach (var c in go.m_Components) c.MarkDestroyed();
                go.MarkDestroyed();
            }
        }

        static void Collect(Transform t, List<GameObject> into)
        {
            into.Add(t.m_GameObject);
            foreach (var child in t.m_Children) Collect(child, into);
        }

        internal static void Reset()
        {
            s_Delayed.Clear();
            s_EndOfFrame.Clear();
        }
    }
}
