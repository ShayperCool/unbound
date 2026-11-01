using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace UnityEngine.Tests
{
    public class RecordsAwake : MonoBehaviour
    {
        public static readonly List<string> Log = new List<string>();
        void Awake() => Log.Add("Awake " + name);
        void OnEnable() => Log.Add("OnEnable " + name);
    }

    /// <summary>InstantiateAsync's operation, without the player loop (tests/projects/instantiateasync
    /// measures its frames). Shares the frame collection: pending operations are global.</summary>
    [Collection("Frames")]
    public class AsyncInstantiateTests
    {
        static GameObject Original(string name)
        {
            var go = new GameObject(name);
            go.transform.localPosition = new Vector3(1, 2, 3);
            go.AddComponent<RecordsAwake>();
            new GameObject("Child").transform.SetParent(go.transform, false);
            return go;
        }

        [Fact]
        public void CopiesWaitUntilIntegratedThenWakeInOrder()
        {
            var original = Original("async-order");
            RecordsAwake.Log.Clear();
            var op = Object.InstantiateAsync(original, 2, new[] { new Vector3(5, 0, 0), new Vector3(6, 0, 0) }, ReadOnlySpan<Quaternion>.Empty);
            original.name = "renamed";
            Assert.False(op.isDone);
            Assert.Null(op.Result);
            Assert.Equal(0.9f, op.progress);
            Assert.Empty(RecordsAwake.Log);
            bool completedAfterAwake = false;
            op.completed += _ => completedAfterAwake = RecordsAwake.Log.Count == 4;
            op.WaitForCompletion();
            Assert.True(op.isDone);
            Assert.True(completedAfterAwake);
            Assert.Equal(new[] { "Awake async-order (Clone)", "OnEnable async-order (Clone)", "Awake async-order (Clone)", "OnEnable async-order (Clone)" }, RecordsAwake.Log);
            Assert.Equal(2, op.Result.Length);
            Assert.Equal(new Vector3(6, 0, 0), op.Result[1].transform.position);
            // An awaiter gives the result once, as Unity's does.
            var awaiter = op.GetAwaiter();
            Assert.Equal(2, awaiter.GetResult().Length);
            Assert.Equal("Awaitable is in detached state", Assert.Throws<InvalidOperationException>(() => awaiter.GetResult()).Message);
            foreach (var clone in op.Result) Object.DestroyImmediate(clone);
            Object.DestroyImmediate(original);
        }

        [Fact]
        public void CancelledAndHeldOperations()
        {
            var original = Original("async-cancel");
            RecordsAwake.Log.Clear();
            var cancelled = Object.InstantiateAsync(original);
            cancelled.Cancel();
            var held = Object.InstantiateAsync(original);
            held.allowSceneActivation = false;
            Assert.True(held.IsWaitingForSceneActivation());
            Unbound.Runtime.AsyncInstantiation.Run();
            Assert.True(cancelled.isDone);
            Assert.Null(cancelled.Result);
            Assert.False(held.isDone);
            held.Cancel();
            Assert.False(held.IsWaitingForSceneActivation());
            held.allowSceneActivation = true;
            Assert.True(held.isDone);
            Assert.Null(held.Result);
            Assert.Empty(RecordsAwake.Log);

            var source = new CancellationTokenSource();
            source.Cancel();
            var token = Object.InstantiateAsync(original, 1, null, Vector3.zero, Quaternion.identity, source.Token);
            token.WaitForCompletion();
            Assert.Null(token.Result);
            Assert.Equal("Async operation canceled", Assert.Throws<OperationCanceledException>(() => token.GetAwaiter().GetResult()).Message);
            Object.DestroyImmediate(original);
        }

        [Fact]
        public void ArgumentsAreCheckedAsUnityChecksThem()
        {
            Assert.Equal("The Object you want to instantiate is null.",
                Assert.Throws<ArgumentException>(() => Object.InstantiateAsync<GameObject>(null, 0)).Message);
            var original = Original("async-errors");
            Assert.Throws<ArgumentException>(() => Object.InstantiateAsync(original, 0));
            Assert.Throws<ArgumentException>(() => Object.InstantiateAsync(ScriptableObject.CreateInstance<ScriptableObject>()));
            Assert.Throws<NullReferenceException>(() => Object.InstantiateAsync(new Texture2D(2, 2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => AsyncInstantiateOperation.SetIntegrationTimeMS(0f));
            AsyncInstantiateOperation.SetIntegrationTimeMS(float.NaN);
            Assert.Equal(2f, AsyncInstantiateOperation.GetIntegrationTimeMS());
            Object.DestroyImmediate(original);
        }

        [Fact]
        public void InstantiateParametersPlaceInTheParentsSpaceUnlessWorldSpace()
        {
            var original = Original("params");
            var parent = new GameObject("params-parent").transform;
            parent.position = new Vector3(10, 0, 0);
            var local = Object.Instantiate(original, new Vector3(1, 0, 0), Quaternion.identity, new InstantiateParameters { parent = parent });
            Assert.Equal(new Vector3(11, 0, 0), local.transform.position);
            var world = Object.Instantiate(original, new Vector3(1, 0, 0), Quaternion.identity, new InstantiateParameters { parent = parent, worldSpace = true });
            Assert.Equal(new Vector3(1, 0, 0), world.transform.position);
            Assert.Equal("params(Clone)", world.name);
            Object.DestroyImmediate(parent.gameObject);
            Object.DestroyImmediate(original);
        }
    }
}
