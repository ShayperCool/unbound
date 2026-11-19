using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    public enum InteractionMode
    {
        AutomatedAction = 0,
        UserAction = 1,
    }

    /// <summary>A property change an edit records for Undo: the value before and after.</summary>
    public partial struct UndoPropertyModification
    {
        public PropertyModification previousValue;
        public PropertyModification currentValue;
    }

    /// <summary>
    /// The prefab queries editor code and packages ask, as the Unity 6000.0.71f1 Editor answers them
    /// (tests/projects/scenesave prefab-api): an instance's root, the instances of a prefab, the
    /// modifications its PrefabInstance holds (any object of an instance answers the instance's
    /// list, in its file's order, each target being the source prefab's object; null for an object
    /// of no instance) and its object overrides (the objects those modifications target, default
    /// overrides only when asked: the instance root's name and pose).
    /// </summary>
    public sealed partial class PrefabUtility
    {
        public delegate void PrefabInstanceUpdated(GameObject instance);

        /// <summary>Called with each instance root a prefab change updated.</summary>
        public static PrefabInstanceUpdated prefabInstanceUpdated;

        /// <summary>An instance's outermost root for an object of a prefab instance, else the object
        /// itself (measured: tests/projects/scenesave findPrefabRoot.*, a plain child answers itself).</summary>
        [Obsolete("Use GetOutermostPrefabInstanceRoot if source is a Prefab instance or source.transform.root.gameObject if source is a Prefab Asset object.")]
        public static GameObject FindPrefabRoot(GameObject source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return IsPartOfPrefabInstance(source) ? GetOutermostPrefabInstanceRoot(source) : source;
        }

        /// <summary>The root of the object's hierarchy.</summary>
        internal static GameObject GetRootGameObject(Object componentOrGameObject) => GameObjectOf(componentOrGameObject)?.transform.root.gameObject;

        public static GameObject[] FindAllInstancesOfPrefab(GameObject prefabRoot) => Instances(prefabRoot, null);

        /// <summary>The instances of the prefab in the scene (measured: an invalid scene throws).</summary>
        public static GameObject[] FindAllInstancesOfPrefab(GameObject prefabRoot, Scene scene)
        {
            CheckPrefabRoot(prefabRoot);
            if (!scene.IsValid()) throw new ArgumentException("Input scene is not valid: Could not be found.");
            return Instances(prefabRoot, scene.Data);
        }

        /// <summary>A prefab asset's root, as FindAllInstancesOfPrefab requires (its errors measured:
        /// tests/projects/scenesave instances.*).</summary>
        static void CheckPrefabRoot(GameObject prefabRoot)
        {
            if (prefabRoot == null) throw new ArgumentNullException(nameof(prefabRoot));
            if (!IsPartOfPrefabAsset(prefabRoot)) throw new ArgumentException("FindAllInstancesOfPrefab must be called with a Prefab asset.");
            if (prefabRoot.transform.parent != null) throw new ArgumentException("FindAllInstancesOfPrefab must be called with a root Prefab GameObject.");
        }

        /// <summary>The instance roots of the prefab in the loaded scenes (or one of them), in
        /// hierarchy order.</summary>
        static GameObject[] Instances(GameObject prefabRoot, SceneData only)
        {
            if (only == null) CheckPrefabRoot(prefabRoot);
            var found = new List<GameObject>();
            if (!AssetDatabaseRuntime.TryGetLocation(prefabRoot, out var guid, out _)) return found.ToArray();
            void Walk(Transform t)
            {
                var go = t.gameObject;
                var link = PrefabLinks.Of(go);
                if (link?.Instance != null && IsAnyPrefabInstanceRoot(go) && string.Equals(link.Instance.SourceGuid, guid, StringComparison.OrdinalIgnoreCase))
                    found.Add(go);
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }
            foreach (var scene in SceneRegistry.Loaded)
            {
                if (!scene.IsLoaded || (only != null && scene != only)) continue;
                foreach (var root in scene.Roots) if (root != null) Walk(root.transform);
            }
            return found.ToArray();
        }

        public static PropertyModification[] GetPropertyModifications(Object targetPrefab)
        {
            var link = PrefabLinks.Of(targetPrefab);
            if (link?.Instance == null) return null;
            var scene = (targetPrefab is Component c ? c.gameObject : targetPrefab as GameObject)?.scene.Data;
            var stored = new Dictionary<(long, string), ObjectRef>();
            foreach (var m in link.Instance.Stored) stored.TryAdd((m.Target.FileId, m.Path), m.Reference);
            var list = new List<PropertyModification>();
            foreach (var m in PrefabInstances.Modifications(link.Instance))
            {
                list.Add(new PropertyModification
                {
                    target = m.Target.IsNull ? null : AssetDatabaseRuntime.Load(m.Target.Guid ?? link.Instance.SourceGuid, m.Target.FileId, typeof(Object)),
                    propertyPath = m.Path,
                    value = m.Value,
                    objectReference = m.Resolved ? m.Reference : Resolve(stored.TryGetValue((m.Target.FileId, m.Path), out var r) ? r : default, scene),
                });
            }
            return list.ToArray();
        }

        /// <summary>A modification's object reference: an object of the instance's scene by its file
        /// id, else an asset.</summary>
        static Object Resolve(ObjectRef reference, SceneData scene)
        {
            if (reference.IsNull) return null;
            if (reference.IsLocal) return scene == null ? null : SceneRegistry.FindByFileId(scene.Path, reference.FileId);
            try { return AssetDatabaseRuntime.Load(reference.Guid, reference.FileId, typeof(Object)); }
            catch (Exception) { return null; }
        }

        public static List<ObjectOverride> GetObjectOverrides(GameObject prefabInstance, bool includeDefaultOverrides = false) =>
            PrefabOverridesUtility.GetObjectOverrides(prefabInstance, includeDefaultOverrides);
    }
}

namespace UnityEditor.SceneManagement
{
    internal partial class PrefabOverridesUtility
    {
        /// <summary>The objects of the instance (in hierarchy order: a GameObject, then its components)
        /// that its modifications target; an object whose modifications are all default overrides
        /// only with <paramref name="includeDefaultOverrides"/>.</summary>
        public static List<ObjectOverride> GetObjectOverrides(GameObject prefabInstance, bool includeDefaultOverrides = false)
        {
            // Measured: tests/projects/scenesave overrides.null, overrides.plain.
            if (prefabInstance == null) throw new ArgumentNullException(nameof(prefabInstance));
            if (!PrefabUtility.IsPartOfPrefabInstance(prefabInstance)) throw new ArgumentException("Provided GameObject is not a Prefab instance");
            var overrides = new List<ObjectOverride>();
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(prefabInstance);
            var instance = root == null ? null : PrefabLinks.Of(root)?.Instance;
            if (instance == null) return overrides;
            var modifications = PrefabInstances.Modifications(instance);
            void Add(Object o)
            {
                var link = PrefabLinks.Of(o);
                if (link == null || link.Instance != instance) return;
                foreach (var m in modifications)
                {
                    if (m.Target.IsNull || m.Target.FileId != link.SourceFileId || !string.Equals(m.Target.Guid ?? instance.SourceGuid, link.SourceGuid, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!includeDefaultOverrides && PrefabUtility.IsDefaultOverrideProperty(o, m.Path)) continue;
                    overrides.Add(new ObjectOverride { instanceObject = o });
                    return;
                }
            }
            void Walk(Transform t)
            {
                Add(t.gameObject);
                foreach (var c in t.gameObject.GetComponents<Component>()) if (c != null) Add(c);
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }
            Walk(root.transform);
            return overrides;
        }
    }

    /// <summary>An override of a prefab instance.</summary>
    public abstract partial class PrefabOverride
    {
        public abstract void Apply(string prefabAssetPath, InteractionMode mode);

        public abstract void Revert(InteractionMode mode);

        public abstract Object GetAssetObject();

        internal abstract Object GetObject();
    }

    public partial class ObjectOverride : PrefabOverride
    {
        public ObjectOverride() { }

        public Object instanceObject { get; set; }

        public PrefabOverride coupledOverride { get; set; }

        public override Object GetAssetObject() => PrefabLinks.Source(instanceObject);

        internal override Object GetObject() => instanceObject;
    }
}
