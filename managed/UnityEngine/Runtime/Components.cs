using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>Creating and attaching components with Unity's rules (RequireComponent,
    /// DisallowMultipleComponent, one Transform).</summary>
    internal static class ComponentFactory
    {
        [ThreadStatic] static int t_Creating;

        /// <summary>True while the engine itself constructs a component.</summary>
        internal static bool IsCreating => t_Creating > 0;

        /// <summary>Constructs a component of a type and attaches it, without lifecycle calls.</summary>
        internal static Component Create(Type type, GameObject go)
        {
            t_Creating++;
            Component c;
            try
            {
                c = (Component)Activator.CreateInstance(type, nonPublic: true);
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException ?? e;
            }
            finally
            {
                t_Creating--;
            }
            c.m_GameObject = go;
            c.OnAttached();
            return c;
        }

        /// <summary>GameObject.AddComponent.</summary>
        internal static Component Add(GameObject go, Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), "Type can not be null.");
            if (!typeof(Component).IsAssignableFrom(type))
                throw new ArgumentException($"AddComponent asking for invalid type", nameof(type));
            if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
            {
                Debug.LogError($"Cannot add component of type '{type.Name}' because it is abstract. Add component of type that is derived from '{type.Name}' instead.", go);
                return null;
            }
            if (typeof(Transform).IsAssignableFrom(type))
            {
                if (type == typeof(RectTransform) && !(go.m_Transform is RectTransform))
                    return ReplaceTransform(go);
                Debug.LogError("A GameObject can only contain one 'Transform' component.", go);
                return null;
            }
            if (Disallowed(go, type, out var existing))
            {
                Debug.LogError($"The component '{type.Name}' can't be added to '{go.name}' because it already contains the same component ({existing.GetType().Name}).", go);
                return null;
            }
            foreach (var required in RequiredComponents(type))
            {
                if (go.GetComponent(required) != null) continue;
                if (required.IsAbstract)
                {
                    if (required == typeof(Collider)) Add(go, typeof(BoxCollider));
                    continue;
                }
                Add(go, required);
            }
            var c = Create(type, go);
            go.m_Components.Add(c);
            Lifecycle.OnComponentAdded(c);
            ScriptMessages.Validate(c);
            return c;
        }

        static Component ReplaceTransform(GameObject go)
        {
            var old = go.m_Transform;
            var rect = (RectTransform)Create(typeof(RectTransform), go);
            rect.m_LocalPosition = old.m_LocalPosition;
            rect.m_LocalRotation = old.m_LocalRotation;
            rect.m_LocalScale = old.m_LocalScale;
            var parent = old.m_Parent;
            int index = parent != null ? parent.m_Children.IndexOf(old) : -1;
            if (parent != null) parent.m_Children[index] = rect;
            rect.m_Parent = parent;
            foreach (var child in old.m_Children)
            {
                child.m_Parent = rect;
                rect.m_Children.Add(child);
            }
            old.m_Children.Clear();
            go.m_Components[0] = rect;
            go.m_Transform = rect;
            old.MarkDestroyed();
            rect.MarkChanged();
            return rect;
        }

        static bool Disallowed(GameObject go, Type type, out Component existing)
        {
            existing = null;
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Component); t = t.BaseType)
            {
                if (t.GetCustomAttribute<DisallowMultipleComponent>(inherit: false) == null) continue;
                existing = go.GetComponent(t);
                return existing != null;
            }
            return false;
        }

        internal static IEnumerable<Type> RequiredComponents(Type type)
        {
            foreach (RequireComponent attribute in type.GetCustomAttributes(typeof(RequireComponent), inherit: true))
            {
                if (attribute.m_Type0 != null) yield return attribute.m_Type0;
                if (attribute.m_Type1 != null) yield return attribute.m_Type1;
                if (attribute.m_Type2 != null) yield return attribute.m_Type2;
            }
        }
    }

    internal static class ComponentQueries
    {
        /// <summary>Whether queries that skip inactive objects see this one: active in the hierarchy,
        /// or, for a prefab asset's objects (never in a scene), active by their own flags up the
        /// chain, as they would be once instantiated.</summary>
        static bool Counts(GameObject go)
        {
            if (!go.m_IsPrefabAsset) return go.activeInHierarchy;
            if (go.m_Destroyed) return false;
            for (var t = go.m_Transform; t != null; t = t.m_Parent)
                if (!t.m_GameObject.m_IsActive) return false;
            return true;
        }

        internal static void OnSelf(GameObject go, Type type, List<Component> results)
        {
            foreach (var c in go.m_Components)
                if (!c.m_Destroyed && type.IsInstanceOfType(c))
                    results.Add(c);
        }

        /// <summary>The first match on the object or below it. As Unity's, the object itself is always
        /// searched; below it an inactive object (by its own flag) hides its subtree unless
        /// inactive ones are included. So a deactivated root still finds what its active children have.</summary>
        internal static Component InChildren(GameObject go, Type type, bool includeInactive)
        {
            foreach (var c in go.m_Components)
                if (!c.m_Destroyed && type.IsInstanceOfType(c))
                    return c;
            foreach (var child in go.m_Transform.m_Children)
            {
                var g = child.m_GameObject;
                if (g.m_Destroyed || !includeInactive && !g.m_IsActive) continue;
                var found = InChildren(g, type, includeInactive);
                if (found != null) return found;
            }
            return null;
        }

        internal static void AllInChildren(GameObject go, Type type, bool includeInactive, List<Component> results)
        {
            OnSelf(go, type, results);
            foreach (var child in go.m_Transform.m_Children)
            {
                var g = child.m_GameObject;
                if (g.m_Destroyed || !includeInactive && !g.m_IsActive) continue;
                AllInChildren(g, type, includeInactive, results);
            }
        }

        internal static Component InParent(GameObject go, Type type, bool includeInactive)
        {
            for (var t = go.m_Transform; t != null; t = t.m_Parent)
            {
                var g = t.m_GameObject;
                if (!includeInactive && !Counts(g)) continue;
                foreach (var c in g.m_Components)
                    if (!c.m_Destroyed && type.IsInstanceOfType(c))
                        return c;
            }
            return null;
        }

        internal static void AllInParent(GameObject go, Type type, bool includeInactive, List<Component> results)
        {
            for (var t = go.m_Transform; t != null; t = t.m_Parent)
            {
                var g = t.m_GameObject;
                if (!includeInactive && !Counts(g)) continue;
                OnSelf(g, type, results);
            }
        }
    }

    internal static class Hierarchy
    {
        internal static void SetParent(Transform t, Transform parent, bool worldPositionStays, int siblingIndex)
        {
            var go = t.m_GameObject;
            bool wasActive = go.activeInHierarchy;
            Vector3 worldPosition = default, worldScale = default;
            Quaternion worldRotation = default;
            if (worldPositionStays)
            {
                worldPosition = t.position;
                worldRotation = t.rotation;
                worldScale = t.lossyScale;
            }
            var oldParent = t.m_Parent;
            if (oldParent == null) SceneRegistry.RemoveRoot(go);
            else oldParent.m_Children.Remove(t);

            t.m_Parent = parent;
            if (parent == null)
            {
                var scene = SceneRegistry.Get(go.m_Scene) ?? SceneRegistry.EnsureActive();
                if (siblingIndex >= 0 && siblingIndex < scene.Roots.Count) scene.Roots.Insert(siblingIndex, go);
                else scene.Roots.Add(go);
            }
            else
            {
                if (siblingIndex >= 0 && siblingIndex < parent.m_Children.Count) parent.m_Children.Insert(siblingIndex, t);
                else parent.m_Children.Add(t);
                if (parent.m_GameObject.m_Scene != go.m_Scene)
                    SceneRegistry.SetSceneRecursive(t, parent.m_GameObject.m_Scene);
            }

            if (worldPositionStays) t.KeepWorldPose(worldPosition, worldRotation, worldScale, parent);
            else t.MarkChanged();

            Messaging.SendToHierarchy(t, "OnTransformParentChanged");
            if (oldParent != null) Messaging.SendFast(oldParent.m_GameObject, "OnTransformChildrenChanged");
            if (parent != null) Messaging.SendFast(parent.m_GameObject, "OnTransformChildrenChanged");
            t.OnHierarchyChanged();

            bool isActive = go.activeInHierarchy;
            if (wasActive != isActive)
                Lifecycle.OnHierarchyActiveChanged(go, isActive);
        }

        internal static List<Transform> Siblings(Transform t)
        {
            if (t.m_Parent != null) return t.m_Parent.m_Children;
            var scene = SceneRegistry.Get(t.m_GameObject.m_Scene);
            var list = new List<Transform>();
            if (scene != null)
                foreach (var go in scene.Roots) list.Add(go.m_Transform);
            return list;
        }

        internal static int GetSiblingIndex(Transform t)
        {
            if (t.m_Parent != null) return t.m_Parent.m_Children.IndexOf(t);
            var scene = SceneRegistry.Get(t.m_GameObject.m_Scene);
            return scene != null ? scene.Roots.IndexOf(t.m_GameObject) : 0;
        }

        internal static void SetSiblingIndex(Transform t, int index)
        {
            if (t.m_Parent != null)
            {
                var list = t.m_Parent.m_Children;
                list.Remove(t);
                index = Math.Max(0, Math.Min(index, list.Count));
                list.Insert(index, t);
                Messaging.SendFast(t.m_Parent.m_GameObject, "OnTransformChildrenChanged");
            }
            else
            {
                var scene = SceneRegistry.Get(t.m_GameObject.m_Scene);
                if (scene == null) return;
                scene.Roots.Remove(t.m_GameObject);
                index = Math.Max(0, Math.Min(index, scene.Roots.Count));
                scene.Roots.Insert(index, t.m_GameObject);
            }
            t.OnHierarchyChanged();
        }
    }

    /// <summary>The project's tags (ProjectSettings/TagManager.asset) and the built-in ones.</summary>
    internal static class TagManager
    {
        static readonly HashSet<string> s_Tags = new HashSet<string>
        {
            "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController",
        };

        internal static string[] Layers = { "Default", "TransparentFX", "Ignore Raycast", "", "Water", "UI", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" };

        static readonly string[] s_Builtin = { "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController" };
        static readonly List<string> s_Ordered = new List<string>(s_Builtin);

        /// <summary>The tags in the Editor's order: the built-in ones, then the project's.</summary>
        internal static string[] All => s_Ordered.ToArray();

        /// <summary>The project's tags (<paramref name="replace"/>: instead of the ones it had).</summary>
        internal static void SetProjectTags(IEnumerable<string> tags, bool replace = false)
        {
            if (replace)
            {
                s_Tags.Clear();
                s_Ordered.Clear();
                foreach (var tag in s_Builtin) { s_Tags.Add(tag); s_Ordered.Add(tag); }
            }
            foreach (var tag in tags)
                if (!string.IsNullOrEmpty(tag) && s_Tags.Add(tag)) s_Ordered.Add(tag);
        }

        internal static bool IsDefined(string tag) => tag != null && s_Tags.Contains(tag);
    }
}
