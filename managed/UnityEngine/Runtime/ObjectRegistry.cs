using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// Every live engine object by instance id. Ids of objects loaded from asset files are positive,
    /// those of scene and runtime objects negative, as in Unity.
    /// </summary>
    internal static class ObjectRegistry
    {
        static readonly Dictionary<int, WeakReference<Object>> s_Objects = new Dictionary<int, WeakReference<Object>>();
        static int s_NextRuntimeId = -2;
        static int s_NextAssetId = 2;
        [ThreadStatic] static bool t_RegisterAsAsset;

        internal static int Register(Object obj)
        {
            int id;
            lock (s_Objects)
            {
                if (t_RegisterAsAsset)
                {
                    id = s_NextAssetId;
                    s_NextAssetId += 2;
                }
                else
                {
                    id = s_NextRuntimeId;
                    s_NextRuntimeId -= 2;
                }
                s_Objects[id] = new WeakReference<Object>(obj);
            }
            return id;
        }

        /// <summary>Objects created inside the scope get asset (positive) ids.</summary>
        internal static AssetIdScope AsAssets() => new AssetIdScope(true);

        internal readonly struct AssetIdScope : IDisposable
        {
            readonly bool previous;
            internal AssetIdScope(bool value)
            {
                previous = t_RegisterAsAsset;
                t_RegisterAsAsset = value;
            }
            public void Dispose() => t_RegisterAsAsset = previous;
        }

        internal static void Unregister(Object obj)
        {
            lock (s_Objects) s_Objects.Remove(obj.m_InstanceID);
        }

        internal static Object FindById(int id)
        {
            lock (s_Objects)
                return s_Objects.TryGetValue(id, out var weak) && weak.TryGetTarget(out var obj) && !obj.m_Destroyed ? obj : null;
        }

        /// <summary>Every live object of a type, assets included (Resources.FindObjectsOfTypeAll).</summary>
        internal static List<Object> All(Type type)
        {
            var result = new List<Object>();
            lock (s_Objects)
            {
                foreach (var weak in s_Objects.Values)
                    if (weak.TryGetTarget(out var obj) && !obj.m_Destroyed && type.IsInstanceOfType(obj))
                        result.Add(obj);
            }
            result.Sort((a, b) => a.m_InstanceID.CompareTo(b.m_InstanceID));
            return result;
        }

        /// <summary>Objects of loaded scenes (and runtime objects that are not assets).</summary>
        internal static List<Object> FindSceneObjects(Type type, bool includeInactive, bool sorted)
        {
            var result = new List<Object>();
            if (typeof(Component).IsAssignableFrom(type) || type.IsInterface)
            {
                foreach (var go in SceneRegistry.AllGameObjects(includeInactive))
                {
                    foreach (var c in go.m_Components)
                    {
                        if (!c.m_Destroyed && type.IsInstanceOfType(c))
                            result.Add(c);
                    }
                }
            }
            else if (type == typeof(GameObject) || type == typeof(Object))
            {
                foreach (var go in SceneRegistry.AllGameObjects(includeInactive))
                {
                    result.Add(go);
                    if (type == typeof(Object))
                        foreach (var c in go.m_Components)
                            if (!c.m_Destroyed) result.Add(c);
                }
            }
            else
            {
                // ScriptableObjects and other non-scene objects that are loaded.
                foreach (var obj in All(type))
                    if (!(obj is GameObject) && !(obj is Component))
                        result.Add(obj);
            }
            if (sorted) result.Sort((a, b) => a.m_InstanceID.CompareTo(b.m_InstanceID));
            return result;
        }

        internal static Object FindFirstSceneObject(Type type, bool includeInactive)
        {
            var all = FindSceneObjects(type, includeInactive, sorted: false);
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>Drops entries of collected objects; called on scene unloads.</summary>
        internal static void Compact()
        {
            lock (s_Objects)
            {
                var dead = new List<int>();
                foreach (var pair in s_Objects)
                    if (!pair.Value.TryGetTarget(out _)) dead.Add(pair.Key);
                foreach (var id in dead) s_Objects.Remove(id);
            }
        }
    }
}
