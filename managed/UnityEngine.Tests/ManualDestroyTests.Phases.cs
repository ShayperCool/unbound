using System;
using Unbound.Runtime;
using Xunit;
using UnityEngine.SceneManagement;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine.Tests
{
    public partial class ManualDestroyTests
    {
        public class AwakeDestroyer : MonoBehaviour
        {
            void Awake() { Calls.Add("Awake"); Destroy(gameObject); }
            void OnEnable() => Calls.Add("OnEnable");
            void OnDisable() => Calls.Add("OnDisable");
            void OnDestroy() => Calls.Add("OnDestroy");
        }

        [Fact]
        public void DestroyInAwakeDisablesBeforeOnEnableCouldRun()
        {
            using var scope = new Scope(measured: true);
            var go = scope.Own(new GameObject("awake"));
            go.AddComponent<AwakeDestroyer>();
            Assert.Equal(new[] { "Awake", "OnDisable" }, Calls);
            RunNode(UpdateDelayed);
            Assert.Equal(new[] { "Awake", "OnDisable", "OnDestroy" }, Calls);
        }

        public class ImmediateAwakeDestroyer : MonoBehaviour
        {
            void Awake() { Calls.Add("Awake"); DestroyImmediate(gameObject); }
            void OnEnable() => Calls.Add("OnEnable");
            void OnDisable() => Calls.Add("OnDisable");
            void OnDestroy() => Calls.Add("OnDestroy");
        }

        [Fact]
        public void ImmediateDestroyInAwakeDisablesBeforeReturning()
        {
            using var scope = new Scope(measured: true);
            var go = scope.Own(new GameObject("awake"));
            go.AddComponent<ImmediateAwakeDestroyer>();
            Assert.True(go == null);
            Assert.Equal(new[] { "Awake", "OnDisable", "OnDestroy" }, Calls);
        }

        public class StartDestroyer : MonoBehaviour
        {
            void Start() { Calls.Add("Start"); Destroy(gameObject); }
            void OnDisable() => Calls.Add("OnDisable");
            void OnDestroy() => Calls.Add("OnDestroy");
        }

        public class CascadeDestroyer : MonoBehaviour
        {
            internal Object Next;
            void OnDestroy() { Calls.Add("cascade"); Destroy(Next); }
        }

        [Fact]
        public void StartRequestWaitsForTheNextScriptingPoint()
        {
            using var scope = new Scope(measured: true);
            var go = scope.Own(new GameObject("start"));
            var script = go.AddComponent<StartDestroyer>();
            RunNode(UpdateDelayed);
            Assert.True(go != null && !script.enabled);
            Assert.Equal(new[] { "Start", "OnDisable" }, Calls);
            RunNode(PostLateDelayed);
            Assert.True(go == null);
            Assert.Equal(new[] { "Start", "OnDisable", "OnDestroy" }, Calls);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void ReentrantRequestWaitsForTheNextDelivery(int node)
        {
            using var scope = new Scope(measured: true);
            var next = scope.Own(new Mesh());
            var go = scope.Own(new GameObject("cascade"));
            go.AddComponent<CascadeDestroyer>().Next = next;
            Object.Destroy(go);
            RunNode(Delivery(node));
            Assert.True(go == null && next != null);
            RunNode(Delivery((node + 1) % 3));
            Assert.True(next == null);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void AssetsAndBuiltinsUseTheSameScriptingDelivery(int node)
        {
            using var scope = new Scope(measured: true);
            var go = scope.Own(new GameObject("keep"));
            var component = go.AddComponent<Light>();
            var asset = scope.Own(new Mesh());
            Object.Destroy(component);
            Object.Destroy(asset);
            Assert.True(component != null && asset != null && go != null);
            RunNode(Delivery(node));
            Assert.True(component == null && asset == null && go != null);
        }

        [Fact]
        public void BuiltinBehaviourDisablesBeforeItsDestructionIsDelivered()
        {
            using var scope = new Scope(measured: true);
            var go = scope.Own(new GameObject("keep"));
            var light = go.AddComponent<Light>();
            Object.Destroy(light);
            Assert.True(light != null && !light.enabled && go != null);
            RunNode(UpdateDelayed);
            Assert.True(light == null && go != null);
        }

        [Fact]
        public void PositiveDelayKeepsScriptsEnabledUntilItsDeadline()
        {
            using var scope = new Scope(measured: true);
            double time = Time.s_Time;
            bool fixedStep = Time.s_InFixedTimeStep;
            try
            {
                Time.s_InFixedTimeStep = false;
                Time.s_Time = 1;
                var script = scope.Subject("delayed");
                Object.Destroy(script.gameObject, .06f);
                Time.s_Time = 1.05;
                RunNode(UpdateDelayed);
                Assert.True(script != null && script.enabled);
                Time.s_Time = 1.07;
                RunNode(FixedDelayed);
                Assert.True(script == null);
                Assert.Contains("delayed.OnDisable", Calls);
                Assert.Contains("delayed.OnDestroy", Calls);
            }
            finally { Time.s_Time = time; Time.s_InFixedTimeStep = fixedStep; }
        }

        [Fact]
        public void RequestDisablesDirectChildrenAndDeliveryDisablesDeeperDescendants()
        {
            using var scope = new Scope(measured: true);
            var parent = scope.Subject("parent");
            var child = scope.Subject("child", parent.transform);
            var grand = scope.Subject("grand", child.transform);
            RunNode(UpdateDelayed);
            Calls.Clear();
            Object.Destroy(parent.gameObject);
            Assert.False(parent.enabled || child.enabled);
            Assert.True(grand.enabled);
            RunNode(typeof(PL.Update.ScriptRunBehaviourUpdate));
            Assert.Contains("grand.Update", Calls);
            RunNode(UpdateDelayed);
            Assert.True(grand == null);
            foreach (string name in new[] { "parent", "child", "grand" })
            {
                Assert.Contains(name + ".OnDisable", Calls);
                Assert.Contains(name + ".OnDestroy", Calls);
                Assert.True(Calls.IndexOf(name + ".OnDisable") < Calls.IndexOf(name + ".OnDestroy"));
            }
            Assert.True(Calls.IndexOf("parent.OnDisable") < Calls.IndexOf("grand.Update"));
            Assert.True(Calls.IndexOf("child.OnDisable") < Calls.IndexOf("grand.Update"));
            Assert.True(Calls.IndexOf("grand.Update") < Calls.IndexOf("grand.OnDisable"));
        }

        [Fact]
        public void ImmediateHierarchyDisablesEveryAwakenedScriptBeforeItsDestroyCallback()
        {
            using var scope = new Scope(measured: true);
            var parent = scope.Subject("parent");
            var child = scope.Subject("child", parent.transform);
            var grand = scope.Subject("grand", child.transform);
            Calls.Clear();
            Object.DestroyImmediate(parent.gameObject);
            Assert.Equal(6, Calls.Count);
            foreach (string name in new[] { "parent", "child", "grand" })
            {
                Assert.Contains(name + ".OnDisable", Calls);
                Assert.Contains(name + ".OnDestroy", Calls);
                Assert.True(Calls.IndexOf(name + ".OnDisable") < Calls.IndexOf(name + ".OnDestroy"));
            }
        }

        [Fact]
        public void AsyncSceneUnloadReturnsBeforeDestroyingItsObjects()
        {
            using var scope = new Scope(measured: true);
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            scenes.Loaded("keep");
            var scene = scenes.Loaded("unload");
            var subject = scope.Subject("subject");
            SceneManager.MoveGameObjectToScene(subject.gameObject, new Scene(scene.Handle));
            Calls.Clear();
            var operation = SceneManager.UnloadSceneAsync(new Scene(scene.Handle));
            operation.completed += _ => { Assert.False(Application.isLoadingLevel); Calls.Add("completed"); };
            Assert.True(Application.isLoadingLevel);
            Assert.False(operation.isDone);
            Assert.True(subject != null && scene.IsLoaded);
            Assert.Empty(Calls);
            SceneManagerInternal.ProcessPending();
            Assert.True(operation.isDone && subject == null && !scene.IsLoaded);
            Assert.Equal(new[] { "subject.OnDisable", "subject.OnDestroy", "completed" }, Calls);
        }

        sealed class DestroyErrors : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Errors = new System.Collections.Generic.List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error) Errors.Add(string.Format(format, args));
            }
            public void LogException(Exception exception, Object context) => throw exception;
        }

        [Fact]
        public void DestroyRefusesPersistentAssetsBeforeQueueing()
        {
            using var scope = new Scope(measured: true);
            var asset = scope.Own(new Mesh());
            asset.IsPersistentAsset = true;
            var logger = Debug.unityLogger.logHandler;
            var capture = new DestroyErrors();
            Debug.unityLogger.logHandler = capture;
            try
            {
                Object.Destroy(asset);
                RunNode(UpdateDelayed);
                Assert.True(asset != null && !asset.m_PendingDestroy);
                Assert.Single(capture.Errors);
                Assert.StartsWith("Destroying assets is not permitted", capture.Errors[0]);
            }
            finally { asset.IsPersistentAsset = false; Debug.unityLogger.logHandler = logger; }
        }

        [Fact]
        public void EndOfFrameResumeFollowsLoopLeavesAndPrecedesFrameCounterAdvance()
        {
            using var scope = new Scope(measured: true);
            int counter = Time.s_RenderedFrameCount;
            int interval = UnityEngine.Rendering.OnDemandRendering.s_FrameInterval;
            bool resumed = false;
            System.Collections.IEnumerator Routine()
            {
                yield return new WaitForEndOfFrame();
                resumed = true;
                Assert.Equal(counter, Time.s_RenderedFrameCount);
            }
            try
            {
                UnityEngine.Rendering.OnDemandRendering.s_FrameInterval = 1;
                scope.Subject("owner").StartCoroutine(Routine());
                RunNode(typeof(PL.PostLateUpdate.TriggerEndOfFrameCallbacks));
                RunNode(typeof(PL.PostLateUpdate.GUIClearEvents));
                Assert.False(resumed);
                Assert.Equal(counter, Time.s_RenderedFrameCount);
                typeof(PlayerLoopDriver).GetMethod("AfterLoop", PrivateStatic).Invoke(null, null);
                Assert.True(resumed);
                Assert.Equal(counter + 1, Time.s_RenderedFrameCount);
            }
            finally
            {
                Time.s_RenderedFrameCount = counter;
                UnityEngine.Rendering.OnDemandRendering.s_FrameInterval = interval;
            }
        }

        [Fact]
        public void PendingQueueDoesNotAllocateWhenItsCapacityIsWarm()
        {
            using var scope = new Scope(measured: true);
            var assets = new Mesh[128];
            for (int i = 0; i < assets.Length; i++) assets[i] = scope.Own(new Mesh());
            Object.Destroy(assets[0]);
            Destruction.BeginDelivery();
            Destruction.DeliverRequested();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 1; i < assets.Length; i++)
            {
                Object.Destroy(assets[i]);
                Destruction.BeginDelivery();
                Destruction.DeliverRequested();
            }
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        }
    }
}
