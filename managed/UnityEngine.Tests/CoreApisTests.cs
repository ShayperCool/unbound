using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unity.Collections;
using Xunit;

namespace UnityEngine.Tests
{
    public class RecordsActivation : MonoBehaviour
    {
        public static readonly List<string> Log = new List<string>();
        void Awake() => Log.Add("Awake " + name);
        void OnEnable() => Log.Add("OnEnable " + name);
        void OnDisable() => Log.Add("OnDisable " + name);
    }

    public class LazySample : ScriptableObject
    {
        public LazyLoadReference<Texture2D> mat;
        public LazyLoadReference<Texture2D> none;
        public LazyLoadReference<Material> other;
    }

    /// <summary>Pose, LazyLoadReference and the batch GameObject calls, as
    /// tests/projects/coreapis measured them.</summary>
    [Collection("Frames")]
    public class CoreApisTests
    {
        [Fact]
        public void PoseComposesThroughQuaternionOperators()
        {
            var a = new Pose(new Vector3(1, 2, 3), new Quaternion(0.1f, 0.2f, 0.3f, 0.9273618f));
            var lhs = new Pose(new Vector3(-1, 0.5f, 4), new Quaternion(-0.5f, 0.5f, -0.5f, -0.5f));
            var by = a.GetTransformedBy(lhs);
            Assert.Equal(lhs.position + lhs.rotation * a.position, by.position);
            Assert.Equal(lhs.rotation * a.rotation, by.rotation);
            Assert.Equal(a.rotation * Vector3.forward, a.forward);
            Assert.Equal("((1.00, 2.00, 3.00), (0.10000, 0.20000, 0.30000, 0.92736))", a.ToString());
            Assert.Equal("((1.0, 2.0, 3.0), (0.1, 0.2, 0.3, 0.9))", a.ToString("F1"));
            // == compares the position approximately and the rotation exactly; Equals is exact.
            var near = new Pose(a.position + new Vector3(1e-6f, 0, 0), a.rotation);
            Assert.True(a == near);
            Assert.False(a.Equals(near));
            Assert.False(a == new Pose(a.position, new Quaternion(-0.1f, -0.2f, -0.3f, -0.9273618f)));
            Assert.False(Pose.identity == default);
            Assert.Equal(1065353216, Pose.identity.GetHashCode());
        }

        [Fact]
        public void LazyReferencesHoldIdsAndAcceptOnlyPersistentAssets()
        {
            var runtime = new Texture2D(1, 1);
            Assert.Throws<ArgumentException>(() => new LazyLoadReference<Texture2D>(runtime));
            LazyLoadReference<Texture2D> byId = runtime.GetInstanceID();
            Assert.True(byId.isSet);
            Assert.False(byId.isBroken);
            Assert.Same(runtime, byId.asset);
            LazyLoadReference<Material> wrong = runtime.GetInstanceID();
            Assert.Throws<InvalidCastException>(() => wrong.asset);
            Object.DestroyImmediate(runtime);
            Assert.True(byId.isBroken);
            Assert.Null(byId.asset);
            var unset = new LazyLoadReference<Material>(0);
            Assert.False(unset.isSet);
            Assert.False(unset.isBroken);
            Assert.False(new LazyLoadReference<Material>((Material)null).isSet);
        }

        sealed class OneAsset : IReferenceResolver
        {
            internal Object Asset;
            public Object Resolve(ObjectRef reference, Type expected) => reference.Guid == "aa" ? Asset : null;
        }

        [Fact]
        public void LazyReferencesReadAsObjectReferences()
        {
            var material = new Texture2D(1, 1);
            var sample = ScriptableObject.CreateInstance<LazySample>();
            new YamlReader(new OneAsset { Asset = material }).ReadScriptObject(sample, TestYaml.DocumentBody(@"
MonoBehaviour:
  mat: {fileID: 2100000, guid: aa, type: 2}
  none: {fileID: 0}
  other: {fileID: 2100000, guid: bb, type: 2}
"));
            Assert.Equal(material.GetInstanceID(), sample.mat.instanceID);
            Assert.False(sample.none.isSet);
            // A reference to a missing asset is set and broken.
            Assert.True(sample.other.isSet);
            Assert.True(sample.other.isBroken);
            Assert.Null(sample.other.asset);
            Assert.Equal("{\"mat\":{\"instanceID\":" + material.GetInstanceID() + "},\"none\":{\"instanceID\":0},\"other\":{\"instanceID\":" + sample.other.instanceID + "}}",
                JsonUtility.ToJson(sample));
            Object.DestroyImmediate(sample);
            Object.DestroyImmediate(material);
        }

        [Fact]
        public void SetGameObjectsActiveSetsEachInOrderAndSkipsOtherIds()
        {
            var a = new GameObject("batch-a");
            a.AddComponent<RecordsActivation>();
            var b = new GameObject("batch-b");
            b.AddComponent<RecordsActivation>();
            RecordsActivation.Log.Clear();
            GameObject.SetGameObjectsActive(new[] { b.GetInstanceID(), 0, a.transform.GetInstanceID(), a.GetInstanceID(), a.GetInstanceID() }, false);
            Assert.Equal(new[] { "OnDisable batch-b", "OnDisable batch-a" }, RecordsActivation.Log);
            Assert.Throws<ArgumentException>(() => GameObject.SetGameObjectsActive(default(NativeArray<int>), true));
            using (var ids = new NativeArray<int>(new[] { a.GetInstanceID() }, Allocator.Persistent))
                GameObject.SetGameObjectsActive(ids, true);
            Assert.True(a.activeSelf);
            Assert.False(b.activeSelf);
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        [Fact]
        public void InstantiateGameObjectsWakesEachCopyInTurn()
        {
            var parent = new GameObject("batch-parent");
            parent.transform.localScale = new Vector3(2, 2, 2);
            var source = new GameObject("batch-src");
            source.transform.SetParent(parent.transform, false);
            source.transform.localPosition = new Vector3(1, 0, 0);
            source.AddComponent<RecordsActivation>();
            var child = new GameObject("batch-child");
            child.transform.SetParent(source.transform, false);
            child.AddComponent<RecordsActivation>();
            RecordsActivation.Log.Clear();
            var ids = new NativeArray<int>(2, Allocator.Persistent);
            var transforms = new NativeArray<int>(2, Allocator.Persistent);
            GameObject.InstantiateGameObjects(source.GetInstanceID(), 2, ids, transforms);
            Assert.Equal(new[] { "Awake batch-src(Clone)", "OnEnable batch-src(Clone)", "Awake batch-child", "OnEnable batch-child",
                                 "Awake batch-src(Clone)", "OnEnable batch-src(Clone)", "Awake batch-child", "OnEnable batch-child" }, RecordsActivation.Log);
            var firstCopy = (GameObject)Resources.InstanceIDToObject(ids[0]);
            var copy = (GameObject)Resources.InstanceIDToObject(ids[1]);
            Assert.Same(copy.transform, Resources.InstanceIDToObject(transforms[1]));
            Assert.Null(copy.transform.parent);
            Assert.Equal(new Vector3(1, 0, 0), copy.transform.position);
            Assert.Throws<ArgumentException>(() => GameObject.InstantiateGameObjects(source.GetInstanceID(), 3, ids, transforms));
            GameObject.InstantiateGameObjects(source.transform.GetInstanceID(), 2, ids, transforms);
            Assert.Equal(0, ids[0]);
            Assert.Equal(0, transforms[1]);
            ids.Dispose();
            transforms.Dispose();
            Object.DestroyImmediate(firstCopy);
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(parent);
        }
    }
}
