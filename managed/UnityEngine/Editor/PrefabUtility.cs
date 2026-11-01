using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// Prefab queries, answered from the prefab instance links the loaded files keep
    /// (<see cref="PrefabLinks"/>): prefab assets, the nested instances and variants inside them, and
    /// the instances PrefabUtility.InstantiatePrefab makes. Clones made by Object.Instantiate are
    /// plain objects (measured: tests/projects/editorcallbacks).
    /// </summary>
    public sealed partial class PrefabUtility
    {
        static GameObject GameObjectOf(Object o) => o as GameObject ?? (o as Component)?.m_GameObject;

        static bool InPrefabAsset(Object o)
        {
            if (o == null) return false;
            var go = GameObjectOf(o);
            return go != null && go.m_IsPrefabAsset;
        }

        static bool InInstance(Object o) => o != null && PrefabLinks.Of(o) != null;

        static Object Source(Object o) => o == null ? null : PrefabLinks.Source(o);

        static string GuidOf(Object o) => o != null && AssetDatabaseRuntime.TryGetLocation(o, out var guid, out _) ? guid : null;

        static readonly Dictionary<string, PrefabAssetType> s_Kinds = new Dictionary<string, PrefabAssetType>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A prefab asset's kind: a model's, a variant (its root is an instance of another
        /// prefab), or a regular prefab.</summary>
        static PrefabAssetType AssetKind(Object assetObject)
        {
            var guid = GuidOf(assetObject);
            if (guid == null) return PrefabAssetType.NotAPrefab;
            if (s_Kinds.TryGetValue(guid, out var kind)) return kind;
            AssetDatabaseRuntime.AssetInfo info;
            try { info = AssetDatabaseRuntime.Info(guid); }
            catch (Exception) { return PrefabAssetType.NotAPrefab; }
            if (info?.Kind == "model") kind = PrefabAssetType.Model;
            else
            {
                var go = GameObjectOf(assetObject);
                var root = go != null ? go.transform.root.gameObject : null;
                kind = root != null && InInstance(root) ? PrefabAssetType.Variant : PrefabAssetType.Regular;
            }
            return s_Kinds[guid] = kind;
        }

        static bool Exists(string guid)
        {
            if (guid == null) return false;
            if (s_Kinds.ContainsKey(guid)) return true;
            try { return AssetDatabaseRuntime.Info(guid)?.Path is string path && path.Length > 0; }
            catch (Exception) { return false; }
        }

        /// <summary>An instance whose prefab is gone.</summary>
        static bool Missing(Object o)
        {
            var link = PrefabLinks.Of(o);
            if (link == null) return false;
            return !Exists(link.Instance.SourceGuid ?? link.SourceGuid);
        }

        public static bool IsPartOfPrefabAsset(Object componentOrGameObject) => InPrefabAsset(componentOrGameObject);

        public static bool IsPartOfPrefabInstance(Object componentOrGameObject) => InInstance(componentOrGameObject);

        public static bool IsPartOfAnyPrefab(Object componentOrGameObject) =>
            InPrefabAsset(componentOrGameObject) || InInstance(componentOrGameObject);

        public static bool IsPartOfNonAssetPrefabInstance(Object componentOrGameObject) =>
            InInstance(componentOrGameObject) && !InPrefabAsset(componentOrGameObject);

        public static bool IsPartOfRegularPrefab(Object componentOrGameObject) => GetPrefabAssetType(componentOrGameObject) == PrefabAssetType.Regular;

        public static bool IsPartOfModelPrefab(Object componentOrGameObject) => GetPrefabAssetType(componentOrGameObject) == PrefabAssetType.Model;

        public static bool IsPartOfVariantPrefab(Object componentOrGameObject) => GetPrefabAssetType(componentOrGameObject) == PrefabAssetType.Variant;

        /// <summary>A model's prefab, or a prefab of a read-only package.</summary>
        public static bool IsPartOfImmutablePrefab(Object componentOrGameObject)
        {
            var type = GetPrefabAssetType(componentOrGameObject);
            if (type == PrefabAssetType.Model) return true;
            if (type == PrefabAssetType.NotAPrefab || type == PrefabAssetType.MissingAsset) return false;
            var asset = InPrefabAsset(componentOrGameObject) ? componentOrGameObject : Source(componentOrGameObject);
            var package = PackageManager.PackageInfo.FindForAssetPath(AssetDatabase.GetAssetPath(asset));
            return package != null && package.source != PackageManager.PackageSource.Embedded && package.source != PackageManager.PackageSource.Local;
        }

        /// <summary>The prefab of an asset object; of an instance's object, the prefab of its source.</summary>
        public static PrefabAssetType GetPrefabAssetType(Object componentOrGameObject)
        {
            if (InPrefabAsset(componentOrGameObject)) return AssetKind(componentOrGameObject);
            if (!InInstance(componentOrGameObject)) return PrefabAssetType.NotAPrefab;
            var source = Source(componentOrGameObject);
            return source == null ? PrefabAssetType.MissingAsset : AssetKind(source);
        }

        /// <summary>Connected for the objects of a scene's instance; prefab assets are not instances.</summary>
        public static PrefabInstanceStatus GetPrefabInstanceStatus(Object componentOrGameObject)
        {
            if (!IsPartOfNonAssetPrefabInstance(componentOrGameObject)) return PrefabInstanceStatus.NotAPrefab;
            return Missing(componentOrGameObject) ? PrefabInstanceStatus.MissingAsset : PrefabInstanceStatus.Connected;
        }

        public static bool IsPrefabAssetMissing(Object instanceComponentOrGameObject) =>
            InInstance(instanceComponentOrGameObject) && Missing(instanceComponentOrGameObject);

        public static Object GetPrefabInstanceHandle(Object instanceComponentOrGameObject) =>
            PrefabLinks.Of(instanceComponentOrGameObject)?.Instance.Handle;

        public static T GetCorrespondingObjectFromSource<T>(T componentOrGameObject) where T : Object =>
            Source(componentOrGameObject) as T;

        /// <summary>The end of the source chain: an asset object that is no instance answers itself.</summary>
        public static TObject GetCorrespondingObjectFromOriginalSource<TObject>(TObject componentOrGameObject) where TObject : Object
        {
            if (!IsPartOfAnyPrefab(componentOrGameObject)) return null;
            var current = componentOrGameObject;
            for (int depth = 0; depth < 64 && Source(current) is TObject next; depth++) current = next;
            return current;
        }

        /// <summary>The root of the object's own instance: the topmost object of its PrefabInstance.</summary>
        public static GameObject GetOutermostPrefabInstanceRoot(Object componentOrGameObject)
        {
            var link = PrefabLinks.Of(componentOrGameObject);
            var go = GameObjectOf(componentOrGameObject);
            if (link == null || go == null) return null;
            var root = go;
            for (var t = go.transform.parent; t != null; t = t.parent)
            {
                var parentLink = PrefabLinks.Of(t.m_GameObject);
                if (parentLink == null || parentLink.Instance != link.Instance) break;
                root = t.m_GameObject;
            }
            return root;
        }

        /// <summary>The nearest object up the hierarchy that is the root of an instance at any
        /// nesting level.</summary>
        public static GameObject GetNearestPrefabInstanceRoot(Object componentOrGameObject)
        {
            if (!InInstance(componentOrGameObject)) return null;
            for (var t = GameObjectOf(componentOrGameObject)?.transform; t != null; t = t.parent)
            {
                if (!InInstance(t.m_GameObject)) return null;
                if (IsAnyPrefabInstanceRoot(t.m_GameObject)) return t.m_GameObject;
            }
            return null;
        }

        /// <summary>An instance's object whose source chain reaches a prefab's root.</summary>
        public static bool IsAnyPrefabInstanceRoot(GameObject gameObject)
        {
            if (!InInstance(gameObject)) return false;
            Object current = gameObject;
            for (int depth = 0; depth < 64 && (current = Source(current)) is GameObject source; depth++)
                if (source.transform.parent == null) return true;
            return false;
        }

        public static bool IsOutermostPrefabInstanceRoot(GameObject gameObject) =>
            IsAnyPrefabInstanceRoot(gameObject) && GetOutermostPrefabInstanceRoot(gameObject) == gameObject;

        /// <summary>A GameObject an instance does not have from its prefab, under one of its objects.</summary>
        public static bool IsAddedGameObjectOverride(GameObject gameObject)
        {
            if (gameObject == null || InInstance(gameObject)) return false;
            var parent = gameObject.transform.parent;
            return parent != null && InInstance(parent.m_GameObject);
        }

        /// <summary>A component an instance's GameObject does not have from its prefab.</summary>
        public static bool IsAddedComponentOverride(Object component) =>
            component is Component c && !InInstance(c) && InInstance(c.m_GameObject);

        /// <summary>The path of the prefab the object comes from: along the source chain, the first
        /// object of a variant, else the original source (measured: tests/projects/editorcallbacks).</summary>
        public static string GetPrefabAssetPathOfNearestInstanceRoot(Object instanceComponentOrGameObject)
        {
            if (!IsPartOfAnyPrefab(instanceComponentOrGameObject)) return "";
            var current = instanceComponentOrGameObject;
            for (int depth = 0; depth < 64; depth++)
            {
                if (InPrefabAsset(current) && AssetKind(current) == PrefabAssetType.Variant) break;
                var next = Source(current);
                if (next == null) break;
                current = next;
            }
            return AssetDatabase.GetAssetPath(current);
        }

        /// <summary>The obsolete prefab type of an object.</summary>
        [Obsolete("PrefabUtility.GetPrefabType is no longer supported. Use PrefabUtility.GetPrefabAssetType and PrefabUtility.GetPrefabInstanceStatus to get the full picture about Prefab types.")]
        public static PrefabType GetPrefabType(Object target)
        {
            if (target == null) return PrefabType.None;
            var type = GetPrefabAssetType(target);
            if (InPrefabAsset(target)) return type == PrefabAssetType.Model ? PrefabType.ModelPrefab : PrefabType.Prefab;
            if (!InInstance(target)) return PrefabType.None;
            if (type == PrefabAssetType.MissingAsset) return PrefabType.MissingPrefabInstance;
            return type == PrefabAssetType.Model ? PrefabType.ModelPrefabInstance : PrefabType.PrefabInstance;
        }

        [Obsolete("Use PrefabUtility.GetCorrespondingObjectFromSource.")]
        public static Object GetPrefabParent(Object obj) => GetCorrespondingObjectFromSource(obj);

        /// <summary>
        /// A connected instance of a prefab asset: a copy named as the prefab, each object linked to
        /// its source, with the root's name and pose recorded as its overrides (measured:
        /// tests/projects/editorcallbacks). Other objects are copied as Object.Instantiate copies them.
        /// </summary>
        public static Object InstantiatePrefab(Object assetComponentOrGameObject) => Instantiate(assetComponentOrGameObject, null, null);

        public static Object InstantiatePrefab(Object assetComponentOrGameObject, Transform parent) => Instantiate(assetComponentOrGameObject, parent, null);

        public static Object InstantiatePrefab(Object assetComponentOrGameObject, UnityEngine.SceneManagement.Scene destinationScene) =>
            Instantiate(assetComponentOrGameObject, null, destinationScene);

        static Object Instantiate(Object asset, Transform parent, UnityEngine.SceneManagement.Scene? scene)
        {
            var go = asset as GameObject;
            if (go == null || !go.m_IsPrefabAsset || go.transform.parent != null || GuidOf(go) == null)
                return scene.HasValue ? Object.Instantiate(asset, scene.Value) : Object.Instantiate(asset, parent);
            var guid = GuidOf(go);
            var instance = new PrefabInstanceData { SourceGuid = guid };
            void Link(Dictionary<Object, Object> clones)
            {
                foreach (var pair in clones)
                {
                    if (!AssetDatabaseRuntime.TryGetLocation(pair.Key, out var sourceGuid, out var fileId)) continue;
                    PrefabLinks.Set(pair.Value, new PrefabLink { SourceGuid = sourceGuid, SourceFileId = fileId, Instance = instance });
                }
                foreach (var (target, path) in DefaultOverrides(go))
                    if (AssetDatabaseRuntime.TryGetLocation(target, out var targetGuid, out var targetId))
                        instance.Modifications.Add((targetGuid, targetId, path));
            }
            var clone = Instantiation.CloneLinked(go, parent, scene, Link);
            return clone;
        }

        /// <summary>The overrides an instance always has: the root's name and its local pose.</summary>
        static IEnumerable<(Object Target, string Path)> DefaultOverrides(GameObject root)
        {
            yield return (root, "m_Name");
            var t = root.transform;
            foreach (var path in t is RectTransform ? RectTransformDefaults : TransformDefaults)
                yield return (t, path);
        }

        static readonly string[] TransformDefaults =
        {
            "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.w", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z",
            "m_LocalEulerAnglesHint.x", "m_LocalEulerAnglesHint.y", "m_LocalEulerAnglesHint.z",
        };

        static readonly string[] RectTransformDefaults =
        {
            "m_Pivot.x", "m_Pivot.y", "m_AnchorMax.x", "m_AnchorMax.y", "m_AnchorMin.x", "m_AnchorMin.y",
            "m_SizeDelta.x", "m_SizeDelta.y",
            "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.w", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z",
            "m_AnchoredPosition.x", "m_AnchoredPosition.y",
            "m_LocalEulerAnglesHint.x", "m_LocalEulerAnglesHint.y", "m_LocalEulerAnglesHint.z",
        };

        /// <summary>The properties of an instance root an instance always overrides
        /// (<c>SerializedProperty.isDefaultOverride</c>): the name of each of its objects and the
        /// Transform's local pose (measured for Transform: tests/projects/editorcallbacks).</summary>
        internal static bool IsDefaultOverrideProperty(Object target, string path)
        {
            var go = GameObjectOf(target);
            if (go == null || !IsAnyPrefabInstanceRoot(go) || GetOutermostPrefabInstanceRoot(go) != go) return false;
            var top = path.IndexOf('.') is int dot && dot >= 0 ? path.Substring(0, dot) : path;
            if (top == "m_Name") return true;
            if (target is RectTransform)
                return top is "m_LocalPosition" or "m_LocalRotation" or "m_LocalEulerAnglesHint" or "m_RootOrder"
                    or "m_AnchoredPosition" or "m_SizeDelta" or "m_AnchorMin" or "m_AnchorMax" or "m_Pivot";
            if (target is Transform)
                return top is "m_LocalPosition" or "m_LocalRotation" or "m_LocalEulerAnglesHint" or "m_RootOrder";
            return false;
        }

        /// <summary>Whether an instance's object has the property (or a part of it) modified.</summary>
        internal static bool IsOverride(Object target, string path)
        {
            var link = PrefabLinks.Of(target);
            return link != null && link.Instance.Modifies(link.SourceGuid, link.SourceFileId, path);
        }

        public static void RecordPrefabInstancePropertyModifications(Object targetObject) =>
            Stubs.Ignored("PrefabUtility.RecordPrefabInstancePropertyModifications", "is not honoured yet: Play mode edits are not recorded as overrides");
    }
}
