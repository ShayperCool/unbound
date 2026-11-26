using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using UnityEngine.Experimental.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The rules tests/projects/defaultcontrols measured in Unity 6000.0.71f1 for queued
    /// creation defaults, each with a control outside the fresh object: values set later, other
    /// contexts, and the path releases without measured answers keep.</summary>
    [Collection("Project release")]
    public sealed class CreationControlsTests : IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly List<GameObject> m_Objects = new List<GameObject>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;

        public CreationControlsTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_PreviousLogEnabled = m_Logger.logEnabled;
            m_PreviousFilter = m_Logger.filterLogType;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Logs);
            m_Logger.logEnabled = true;
            m_Logger.filterLogType = LogType.Log;
        }

        public void Dispose()
        {
            foreach (var g in m_Objects) Object.DestroyImmediate(g);
            m_Logger.logHandler = m_PreviousHandler;
            m_Logger.logEnabled = m_PreviousLogEnabled;
            m_Logger.filterLogType = m_PreviousFilter;
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Logs;
            internal CaptureHandler(ILogHandler inner, List<string> logs) { m_Inner = inner; m_Logs = logs; }
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                m_Logs.Add(logType + ": " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
            public void LogException(Exception exception, Object context) => m_Logs.Add("Exception: " + exception.Message);
        }

        GameObject Make(string name, Vector3 position, bool active = true)
        {
            var g = new GameObject(name);
            m_Objects.Add(g);
            g.transform.position = position;
            if (!active) g.SetActive(false);
            return g;
        }

        [Fact]
        public void FeetPivotIsStateOfAnActiveHierarchy()
        {
            var active = Make("Active", Vector3.zero).AddComponent<Animator>();
            Assert.Equal(1f, active.feetPivotActive);
            active.feetPivotActive = 3f;
            Assert.Equal(3f, active.feetPivotActive);
            active.feetPivotActive = -1f;
            Assert.Equal(-1f, active.feetPivotActive);

            var inactive = Make("Inactive", Vector3.zero, active: false).AddComponent<Animator>();
            Assert.Equal(0f, inactive.feetPivotActive);
            inactive.feetPivotActive = 0.5f;
            Assert.Equal(0f, inactive.feetPivotActive);
            inactive.gameObject.SetActive(true);
            Assert.Equal(1f, inactive.feetPivotActive);

            var deactivated = Make("Deactivated", Vector3.zero).AddComponent<Animator>();
            deactivated.feetPivotActive = 0.5f;
            deactivated.gameObject.SetActive(false);
            Assert.Equal(0f, deactivated.feetPivotActive);
            deactivated.feetPivotActive = 0.25f;
            deactivated.gameObject.SetActive(true);
            Assert.Equal(1f, deactivated.feetPivotActive);

            var disabled = Make("Disabled", Vector3.zero).AddComponent<Animator>();
            disabled.enabled = false;
            disabled.feetPivotActive = 0.5f;
            Assert.Equal(0.5f, disabled.feetPivotActive);
            Assert.Empty(m_Logs);
        }

        [Fact]
        public void AnimatorWithoutRecordingReadsMinusOne()
        {
            var animator = Make("Recorder", Vector3.zero).AddComponent<Animator>();
            animator.recorderStartTime = 2f;
            animator.recorderStopTime = 3f;
            animator.playbackTime = 1f;
            Assert.Equal(-1f, animator.recorderStartTime);
            Assert.Equal(-1f, animator.recorderStopTime);
            Assert.Empty(m_Logs);
            Assert.Equal(-1f, animator.playbackTime);
            Assert.Equal(new[] { "Warning: Can't call GetPlaybackTime while not in playback mode. You must call StartPlayback before." }, m_Logs);
            Assert.Equal(Quaternion.identity, animator.targetRotation);
            Assert.Equal(Vector3.zero, animator.targetPosition);
        }

        [Fact]
        public void AgentOffTheNavMeshReadsNoPath()
        {
            var agent = Make("OffMesh", new Vector3(5000f, 0f, 5000f)).AddComponent<NavMeshAgent>();
            // Placing it warns (at AddComponent, or at its first query when a NavMesh exists).
            Assert.False(agent.isOnNavMesh);
            m_Logs.Clear();
            Assert.Equal(Vector3.positiveInfinity, agent.destination);
            Assert.Equal(NavMeshPathStatus.PathInvalid, agent.pathStatus);
            Assert.Empty(m_Logs);
            Assert.Equal(float.PositiveInfinity, agent.remainingDistance);
            Assert.Equal(new[] { "Error: \"GetRemainingDistance\" can only be called on an active agent that has been placed on a NavMesh." }, m_Logs);

            var disabled = Make("Disabled", new Vector3(5010f, 0f, 5000f)).AddComponent<NavMeshAgent>();
            disabled.enabled = false;
            m_Logs.Clear();
            Assert.Equal(float.PositiveInfinity, disabled.remainingDistance);
            Assert.Single(m_Logs);
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                m_Logs.Clear();
                Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
                Assert.Equal(0f, agent.remainingDistance);
                Assert.Empty(m_Logs);
            }
        }

        [Fact]
        public void TrailWithoutPointsSpansTheOriginAndTheObject()
        {
            var away = Make("Away", new Vector3(-10f, 5f, 2f)).AddComponent<TrailRenderer>();
            Assert.Equal(new Vector3(-5f, 2.5f, 1f), away.bounds.center);
            Assert.Equal(new Vector3(5.5f, 3f, 1.5f), away.bounds.extents);
            Assert.Equal(new Vector3(5f, -2.5f, -1f), away.localBounds.center);

            var wide = Make("Wide", new Vector3(10f, 0f, 0f)).AddComponent<TrailRenderer>();
            wide.widthMultiplier = 3f;
            Assert.Equal(new Vector3(6.5f, 1.5f, 1.5f), wide.bounds.extents);

            var scaled = Make("Scaled", new Vector3(0f, 0f, 10f)).AddComponent<TrailRenderer>();
            scaled.transform.localScale = new Vector3(2f, 2f, 2f);
            Assert.Equal(new Vector3(0.5f, 0.5f, 5.5f), scaled.bounds.extents);
            Assert.Equal(new Vector3(0.25f, 0.25f, 2.75f), scaled.localBounds.extents);

            var set = Make("Set", new Vector3(3f, 0f, 0f)).AddComponent<TrailRenderer>();
            var custom = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 4f, 4f));
            set.localBounds = custom;
            Assert.Equal(custom, set.localBounds);
            Assert.NotEqual(new Vector3(1.5f, 0f, 0f), set.bounds.center);
        }

        [Fact]
        public void WorldSpaceCanvasDisplaySizeIsItsRectangle()
        {
            var world = Make("World", Vector3.zero).AddComponent<Canvas>();
            world.renderMode = RenderMode.WorldSpace;
            ((RectTransform)world.transform).sizeDelta = new Vector2(200f, 50f);
            Assert.Equal(new Vector2(200f, 50f), world.renderingDisplaySize);
            var overlay = Make("Overlay", Vector3.zero).AddComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            Assert.Equal(new Vector2(Screen.width, Screen.height), overlay.renderingDisplaySize);
        }

        [Fact]
        public void LightLayerDistancesAreANewZeroArray()
        {
            var light = Make("Light", Vector3.zero).AddComponent<Light>();
            var distances = light.layerShadowCullDistances;
            Assert.Equal(new float[32], distances);
            distances[0] = 5f;
            Assert.Equal(0f, light.layerShadowCullDistances[0]);
        }

        [Fact]
        public void InstantiateCopiesProbePositions()
        {
            var source = Make("Probes", Vector3.zero);
            var group = source.AddComponent<LightProbeGroup>();
            group.probePositions = new[] { new Vector3(7f, 8f, 9f) };
            var copy = Object.Instantiate(source);
            m_Objects.Add(copy);
            Assert.Equal(new[] { new Vector3(7f, 8f, 9f) }, copy.GetComponent<LightProbeGroup>().probePositions);
            group.probePositions = new Vector3[0];
            var empty = Object.Instantiate(source);
            m_Objects.Add(empty);
            Assert.Empty(empty.GetComponent<LightProbeGroup>().probePositions);
        }

        [Fact]
        public void RenderTextureFormatOfFormatsWithoutOne()
        {
            Assert.Equal((RenderTextureFormat)29, GraphicsFormatUtility.GetRenderTextureFormat(GraphicsFormat.None));
            Assert.Equal((RenderTextureFormat)29, GraphicsFormatUtility.GetRenderTextureFormat(GraphicsFormat.D24_UNorm_S8_UInt));
            Assert.Equal((RenderTextureFormat)29, GraphicsFormatUtility.GetRenderTextureFormat(GraphicsFormat.R8G8B8A8_SNorm));
            Assert.Equal((RenderTextureFormat)21, GraphicsFormatUtility.GetRenderTextureFormat(GraphicsFormat.YUV2));
            Assert.Equal(RenderTextureFormat.R8, GraphicsFormatUtility.GetRenderTextureFormat(GraphicsFormat.R8_SRGB));
            Assert.Equal(RenderTextureFormat.ARGB2101010, GraphicsFormatUtility.GetRenderTextureFormat(GraphicsFormat.A2R10G10B10_UNormPack32));
            Assert.Equal(RenderTextureFormat.ARGB32, GraphicsFormatUtility.GetRenderTextureFormat(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.Equal((RenderTextureFormat)29, new UnityEngine.Rendering.AttachmentDescriptor(GraphicsFormat.None).format);
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                Assert.Equal(RenderTextureFormat.Depth, GraphicsFormatUtility.GetRenderTextureFormat(GraphicsFormat.None));
                Assert.Equal(RenderTextureFormat.ARGB32, GraphicsFormatUtility.GetRenderTextureFormat(GraphicsFormat.R8G8B8A8_SNorm));
            }
        }
    }
}
