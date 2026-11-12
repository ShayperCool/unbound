using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class DeactivatesOnEnable : MonoBehaviour
    {
        void OnEnable() => gameObject.SetActive(false);
    }

    public class RecordsEnableDisable : MonoBehaviour
    {
        public readonly List<string> log = new List<string>();
        void OnEnable() => log.Add("enable");
        void OnDisable() => log.Add("disable");
    }

    public class RecordsQuit : MonoBehaviour
    {
        public int calls;
        void OnApplicationQuit() => calls++;
    }

    public class LifecycleTests
    {
        [Fact]
        public void QuitReachesDisabledScriptsOnActiveObjectsOnly()
        {
            // Unity 6000.0.71f1: editorcallbacks leave-play controls, with and without domain reload.
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            scenes.Loaded("Quit");
            var active = new GameObject("active").AddComponent<RecordsQuit>();
            var disabled = new GameObject("disabled").AddComponent<RecordsQuit>();
            disabled.enabled = false;
            var inactive = new GameObject("inactive").AddComponent<RecordsQuit>();
            inactive.gameObject.SetActive(false);
            try
            {
                Lifecycle.Broadcast(m => m.OnApplicationQuit, null);
                Assert.Equal(1, active.calls);
                Assert.Equal(1, disabled.calls);
                Assert.Equal(0, inactive.calls);
            }
            finally
            {
                Object.DestroyImmediate(active.gameObject);
                Object.DestroyImmediate(disabled.gameObject);
                Object.DestroyImmediate(inactive.gameObject);
            }
        }

        [Fact]
        public void BehavioursGetOnDisableOnlyAfterOnEnable()
        {
            var go = new GameObject("lifecycle");
            go.SetActive(false);
            go.AddComponent<DeactivatesOnEnable>();
            var recorder = go.AddComponent<RecordsEnableDisable>();
            // The first script deactivates the object before the second one's turn: the second gets
            // neither OnEnable nor OnDisable.
            go.SetActive(true);
            Assert.Empty(recorder.log);
            Object.DestroyImmediate(go);
            Assert.Empty(recorder.log);
        }

        [Fact]
        public void DisablingAnEnabledBehaviourSendsOnDisableOnce()
        {
            var go = new GameObject("lifecycle");
            var recorder = go.AddComponent<RecordsEnableDisable>();
            recorder.enabled = false;
            go.SetActive(false);
            recorder.enabled = true;
            go.SetActive(true);
            Object.DestroyImmediate(go);
            Assert.Equal(new[] { "enable", "disable", "enable", "disable" }, recorder.log);
        }
    }
}
