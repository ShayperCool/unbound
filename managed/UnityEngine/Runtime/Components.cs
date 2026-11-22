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
        [ThreadStatic] static int t_Creating, t_Deserializing;

        /// <summary>True while the engine itself constructs a component.</summary>
        internal static bool IsCreating => t_Creating > 0;

        /// <summary>True while a component read from a scene or prefab is attached (its own
        /// serialized state replaces what <see cref="Component.OnAttached"/> sets).</summary>
        internal static bool IsDeserializing => t_Deserializing > 0;

        /// <summary>Constructs a component of a type and attaches it, without lifecycle calls.</summary>
        internal static Component Create(Type type, GameObject go, bool deserializing = false)
        {
            t_Creating++;
            Component c;
            try
            {
                if (Compat.UncreatedObjectWrappersMeasured)
                    c = (Component)ObjectConstruction.CreateWithBacking(type, deserializing ? null : go?.m_Name);
                else
                {
                    using var construction = MainThreadCheck.Construct(typeof(MonoBehaviour).IsAssignableFrom(type) ? type : null,
                        deserializing ? null : go?.m_Name);
                    c = (Component)ObjectConstruction.Create(type);
                }
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
            if (deserializing) t_Deserializing++;
            try { c.OnAttached(); }
            finally
            {
                if (deserializing) t_Deserializing--;
            }
            return c;
        }

        /// <summary>GameObject.AddComponent. On the measured release (Unity 6000.0.71f1,
        /// tests/projects/objecterrors and componentlogs) a type that cannot be added is refused
        /// with the engine's diagnostic and no component; other releases keep the earlier,
        /// unmeasured exceptions and error logs.</summary>
        internal static Component Add(GameObject go, Type type)
        {
            bool measured = Compat.ComponentFactoryEligibilityMeasured;
            if (type == null)
            {
                if (!measured) throw new ArgumentNullException(nameof(type), "Type can not be null.");
                Log.Engine(LogType.Warning, "AddComponent asking for invalid type", go);
                return null;
            }
            if (!typeof(Component).IsAssignableFrom(type))
            {
                if (!measured) throw new ArgumentException($"AddComponent asking for invalid type", nameof(type));
                if (!typeof(UnityEngine.Object).IsAssignableFrom(type))
                    Log.Engine(LogType.Warning, $"AddComponent asking for \"{type.Name}\" which is not a Unity engine type.", go);
                else if (type.Assembly != typeof(UnityEngine.Object).Assembly)
                    Log.Engine(LogType.Warning, "AddComponent asking for invalid type using [ExtensionOfNativeClass] in its ancestor but cannot be found in the assembly", go);
                else
                    Log.Engine(LogType.Log, $"Can't add component because '{type.Name}' is not derived from Component.", go);
                return null;
            }
            if (measured && type == typeof(MonoBehaviour))
            {
                Debug.LogError("AddComponent with MonoBehaviour is not allowed. Create a class that derives from MonoBehaviour and add it instead.", go);
                return null;
            }
            // A base class the object already has a component of (measured for Component,
            // Behaviour and Transform), or a second engine component of a kind an object holds
            // one of (tests/projects/uniquecomponents); other classes keep their earlier diagnostics.
            if (measured && (type == typeof(Component) || type == typeof(Behaviour) || type == typeof(Transform)
                    || Compat.NativeComponentIsUnique(type))
                && go.FindComponent(type) != null)
            {
                Log.Engine(LogType.Log, $"Can't add component '{type.Name}' to {go.name} because such a component is already added to the game object!", go);
                return null;
            }
            if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters || Compat.NativeComponentIsAbstract(type))
            {
                string message = $"Cannot add component of type '{type.Name}' because it is abstract. Add component of type that is derived from '{type.Name}' instead.";
                if (measured) Log.Engine(LogType.Log, message, go);
                else Debug.LogError(message, go);
                return null;
            }
            if (typeof(Transform).IsAssignableFrom(type))
            {
                if (type == typeof(RectTransform) && !(go.m_Transform is RectTransform))
                {
                    // A loaded prefab asset keeps its Transform (Unity 6000.0.71f1,
                    // tests/projects/gameinterop hier.rect_transform).
                    if (Hierarchy.IsLoadedPrefabAsset(go.m_Transform))
                    {
                        Log.Engine(LogType.Log, $"Changing Transform on a Prefab instance ({go.name}) is not allowed.", go);
                        return null;
                    }
                    return ReplaceTransform(go);
                }
                Debug.LogError("A GameObject can only contain one 'Transform' component.", go);
                return null;
            }
            if (Disallowed(go, type, out var existing))
            {
                // A second 2D effector conflicts with the first (tests/projects/uniquecomponents).
                string message = Compat.ComponentFactoryEligibilityMeasured && typeof(Effector2D).IsAssignableFrom(type)
                    ? $"Can't add component '{type.Name}' to {go.name} because it conflicts with the existing '{existing.GetType().Name}' derived component!"
                    : $"The component '{type.Name}' can't be added to '{go.name}' because it already contains the same component ({existing.GetType().Name}).";
                // Component rejection reaches Application callbacks independently of the public logger.
                // Measured on 6000.0.71f1 by tests/projects/componentlogs.
                if (Compat.ComponentFactoryEligibilityMeasured)
                    Log.Engine(LogType.Log, message, go);
                else
                    Debug.LogError(message, go);
                return null;
            }
            foreach (var required in RequiredComponents(type))
            {
                if ((Compat.ComponentFactoryEligibilityMeasured && typeof(Component).IsAssignableFrom(required) && required.IsAssignableFrom(type))
                    || go.FindComponent(required) != null) continue;
                if (Compat.ComponentFactoryEligibilityMeasured
                    && (required.IsAbstract || Compat.NativeComponentIsAbstract(required))) return null;
                if (required.IsAbstract)
                {
                    if (required == typeof(Collider)) Add(go, typeof(BoxCollider));
                    continue;
                }
                var dependency = Add(go, required);
                if (Compat.ComponentFactoryEligibilityMeasured && ReferenceEquals(dependency, null)) return null;
            }
            var c = Create(type, go);
            go.m_Components.Add(c);
            var companionType = c.AddedCompanion;
            Component companion = null;
            if (companionType != null && go.FindComponent(companionType) == null)
            {
                companion = Create(companionType, go);
                go.m_Components.Add(companion);
            }
            if (Compat.AudioComponentActivationMeasured)
            {
                AudioFilterChain.ComponentAdded(go, c);
                if (companion != null) AudioFilterChain.ComponentAdded(go, companion);
            }
            Lifecycle.OnComponentAdded(c);
            if (companion != null) Lifecycle.OnComponentAdded(companion);
            if (!c.m_Destroyed) c.OnAddedByScript();
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
            // ConstantForce's native limit is independent of DisallowMultipleComponent metadata.
            if (typeof(ConstantForce).IsAssignableFrom(type))
            {
                existing = go.FindComponent<ConstantForce>();
                return existing != null;
            }
            // Unity 6000.0.71f1, audiopitch: AddComponent permits one of each audio filter type per object,
            // including a disabled instance. Removing it permits a replacement.
            if (type == typeof(AudioDistortionFilter)
                || Compat.ComponentFactoryEligibilityMeasured && AudioFilterChain.IsFilterType(type))
            {
                existing = go.FindComponent(type);
                return existing != null;
            }
            if (typeof(Effector2D).IsAssignableFrom(type))
            {
                existing = go.FindComponent<Effector2D>();
                return existing != null;
            }
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Component); t = t.BaseType)
            {
                if (t.GetCustomAttribute<DisallowMultipleComponent>(inherit: false) == null) continue;
                existing = go.FindComponent(t);
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
            if (!go.m_IsPrefabAsset) return go.ActiveInHierarchyNoThrow;
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
        internal const string AssetParentingRefused = "Setting the parent of a transform which resides in a Prefab Asset is disabled to prevent data corruption";
        const string AssetTransformRefused = "Transform resides in a Prefab asset and cannot be set to prevent data corruption";

        /// <summary>A transform of a prefab asset as loaded (not one an importer is building).</summary>
        internal static bool IsLoadedPrefabAsset(Transform t) =>
            t.m_GameObject.m_IsPrefabAsset && !ScriptedImports.Importing && !UnityEditor.AssetImporter.AnyImporting;

        /// <summary>Scripts cannot change the hierarchy of a loaded prefab asset: a parent inside an
        /// asset is refused with one error, an asset's transform moved out of it (to the scene or to
        /// no parent) with another, and nothing changes (Unity 6000.0.71f1,
        /// tests/projects/gameinterop hier.*).</summary>
        internal static bool RefusesAssetParenting(Transform t, Transform parent)
        {
            if (parent != null && IsLoadedPrefabAsset(parent))
            {
                Debug.LogError(AssetParentingRefused, t);
                return true;
            }
            if (IsLoadedPrefabAsset(t))
            {
                Debug.LogError(AssetTransformRefused, t);
                return true;
            }
            return false;
        }

        internal static void SetParent(Transform t, Transform parent, bool worldPositionStays, int siblingIndex)
        {
            var go = t.m_GameObject;
            Vector3 worldPosition = default, worldScale = default;
            Quaternion worldRotation = default;
            if (worldPositionStays)
            {
                worldPosition = t.position;
                worldRotation = t.rotation;
                worldScale = t.lossyScale;
            }
            bool sceneChanged = false;
            // The whole subtree (inactive objects too) hears of it before the relinking (measured:
            // tests/projects/messages).
            Messaging.SendToHierarchy(t, "OnBeforeTransformParentChanged");
            if (t.m_Destroyed || (parent != null && parent.m_Destroyed)) return;
            // A script that hears it may have moved the transform already: it leaves its parent (and
            // activity) of now, and the request still decides where it ends up (Unity 6000.0.71f1,
            // tests/projects/gameinterop reentrant.before).
            var oldParent = t.m_Parent;
            bool wasActive = go.activeInHierarchy;
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
                {
                    SceneRegistry.SetSceneRecursive(t, parent.m_GameObject.m_Scene);
                    sceneChanged = true;
                }
            }

            if (worldPositionStays) t.KeepWorldPose(worldPosition, worldRotation, worldScale, parent);
            else t.MarkChanged();
            // At its kept world pose, before its messages, its 2D bodies and colliders go to the new
            // scene's physics scene (the 3D ones move at the next physics access).
            if (sceneChanged) Unbound.Runtime.Physics2DWorld.SceneMembershipChanged();

            // Renderers that changed canvas lose their depth at once (tests/projects/uidepth).
            Unbound.Runtime.CanvasSystem.ReleaseDepths(t);
            // The subtree's activity follows its new parent before the parent messages: OnDisable
            // comes before OnTransformParentChanged under an inactive parent, in the new scene
            // (matches the Unity 6000.0.71f1 Editor, tests/projects/localphysics reparent.*).
            bool isActive = go.ActiveInHierarchyNoThrow;
            if (wasActive != isActive)
                Lifecycle.OnHierarchyActiveChanged(go, isActive);
            // A script there may have destroyed the subtree: its parents still hear of it.
            if (!t.m_Destroyed) Messaging.SendToHierarchy(t, "OnTransformParentChanged");
            if (oldParent != null && !oldParent.m_Destroyed) Messaging.SendFast(oldParent.m_GameObject, "OnTransformChildrenChanged");
            if (parent != null && !parent.m_Destroyed) Messaging.SendFast(parent.m_GameObject, "OnTransformChildrenChanged");
            if (!t.m_Destroyed) t.OnHierarchyChanged();
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
