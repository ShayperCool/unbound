using System.Collections.Generic;
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

    public class LifecycleTests
    {
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
