using System;
using Unbound.Runtime;
using Unity.Collections;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    /// <summary>The batch calls by instance id (tests/projects/coreapis).</summary>
    [global::UnityEngine.@ExcludeFromPresetAttribute] public sealed partial class GameObject
    {
        public static void SetGameObjectsActive(NativeArray<int> instanceIDs, bool active)
        {
            if (!instanceIDs.IsCreated)
                throw new ArgumentException("NativeArray is uninitialized", nameof(instanceIDs));
            if (instanceIDs.Length == 0) return;
            SetGameObjectsActive(instanceIDs.AsReadOnlySpan(), active);
        }

        /// <summary>SetActive on each object in order, its messages before the next one's. Ids of
        /// no live GameObject (0, unknown, destroyed, a component's) are skipped without an error.</summary>
        public static void SetGameObjectsActive(ReadOnlySpan<int> instanceIDs, bool active)
        {
            foreach (int id in instanceIDs)
                if (ObjectRegistry.FindById(id) is GameObject go)
                    go.SetActive(active);
        }

        /// <summary>Copies of a GameObject named <c>&lt;name&gt;(Clone)</c>, at the root of the
        /// destination scene (else the active one) with the original's local pose. Each copy wakes
        /// (Awake, OnEnable through its hierarchy) before the next is made. A source id of no live
        /// GameObject makes nothing and writes 0 ids, without an error.</summary>
        public static void InstantiateGameObjects(int sourceInstanceID, int count, NativeArray<int> newInstanceIDs, NativeArray<int> newTransformInstanceIDs, Scene destinationScene = default)
        {
            if (!newInstanceIDs.IsCreated)
                throw new ArgumentException("NativeArray is uninitialized", nameof(newInstanceIDs));
            if (!newTransformInstanceIDs.IsCreated)
                throw new ArgumentException("NativeArray is uninitialized", nameof(newTransformInstanceIDs));
            if (count == 0) return;
            if (count != newInstanceIDs.Length || count != newTransformInstanceIDs.Length)
                throw new ArgumentException("Size mismatch! Both arrays must already be the size of count.");

            var source = ObjectRegistry.FindById(sourceInstanceID) as GameObject;
            var scene = Instantiation.TargetScene(null, destinationScene);
            for (int i = 0; i < count; i++)
            {
                if (source == null || source.m_Destroyed)
                {
                    newInstanceIDs[i] = 0;
                    newTransformInstanceIDs[i] = 0;
                    continue;
                }
                var clone = Instantiation.Prepare(source, source, scene, "(Clone)");
                Instantiation.Place(clone, null, scene, false, false, null, null);
                Instantiation.Activate(new[] { clone });
                newInstanceIDs[i] = clone.RootGo.GetInstanceID();
                newTransformInstanceIDs[i] = clone.Root.GetInstanceID();
            }
        }
    }
}
