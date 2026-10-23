using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine.LowLevel
{
    /// <summary>A system of the player loop: engine systems are identified by their type, systems
    /// added by scripts carry an update delegate.</summary>
    public partial struct PlayerLoopSystem
    {
        public delegate void UpdateFunction();

        public Type type;
        public PlayerLoopSystem[] subSystemList;
        public UpdateFunction updateDelegate;
        public IntPtr updateFunction;
        public IntPtr loopConditionFunction;

        public override string ToString() => type?.Name ?? "null";
    }

    public partial class PlayerLoop
    {
        public static PlayerLoopSystem GetDefaultPlayerLoop() => PlayerLoopDriver.DefaultLoop();

        public static PlayerLoopSystem GetCurrentPlayerLoop() => PlayerLoopDriver.CurrentLoop;

        public static void SetPlayerLoop(PlayerLoopSystem loop) => PlayerLoopDriver.CurrentLoop = loop;
    }
}

namespace UnityEngine.PlayerLoop
{
    public partial struct TimeUpdate
    {
        public partial struct WaitForLastPresentationAndUpdateTime { }
    }

    public partial struct Initialization
    {
        public partial struct ProfilerStartFrame { }
        public partial struct UpdateCameraMotionVectors { }
        public partial struct DirectorSampleTime { }
        public partial struct AsyncUploadTimeSlicedUpdate { }
        public partial struct SynchronizeState { }
        public partial struct SynchronizeInputs { }
        public partial struct XREarlyUpdate { }
        [Obsolete] public partial struct PlayerUpdateTime { }
    }

    public partial struct EarlyUpdate
    {
        public partial struct PollPlayerConnection { }
        public partial struct ProfilerStartFrame { }
        public partial struct GpuTimestamp { }
        public partial struct AnalyticsCoreStatsUpdate { }
        public partial struct UnityWebRequestUpdate { }
        public partial struct UpdateStreamingManager { }
        public partial struct ExecuteMainThreadJobs { }
        public partial struct ProcessMouseInWindow { }
        public partial struct ClearIntermediateRenderers { }
        public partial struct ClearLines { }
        public partial struct PresentBeforeUpdate { }
        public partial struct ResetFrameStatsAfterPresent { }
        public partial struct UpdateAsyncReadbackManager { }
        public partial struct UpdateTextureStreamingManager { }
        public partial struct UpdatePreloading { }
        public partial struct RendererNotifyInvisible { }
        public partial struct PlayerCleanupCachedData { }
        public partial struct UpdateMainGameViewRect { }
        public partial struct UpdateCanvasRectTransform { }
        public partial struct XRUpdate { }
        public partial struct UpdateInputManager { }
        public partial struct ProcessRemoteInput { }
        public partial struct ScriptRunDelayedStartupFrame { }
        public partial struct UpdateKinect { }
        public partial struct DeliverIosPlatformEvents { }
        public partial struct ARCoreUpdate { }
        public partial struct DispatchEventQueueEvents { }
        public partial struct PhysicsResetInterpolatedTransformPosition { }
        public partial struct SpriteAtlasManagerUpdate { }
        public partial struct PerformanceAnalyticsUpdate { }
    }

    public partial struct FixedUpdate
    {
        public partial struct ClearLines { }
        public partial struct NewInputFixedUpdate { }
        public partial struct DirectorFixedSampleTime { }
        public partial struct AudioFixedUpdate { }
        public partial struct ScriptRunBehaviourFixedUpdate { }
        public partial struct DirectorFixedUpdate { }
        public partial struct LegacyFixedAnimationUpdate { }
        public partial struct XRFixedUpdate { }
        public partial struct PhysicsFixedUpdate { }
        public partial struct Physics2DFixedUpdate { }
        public partial struct PhysicsClothFixedUpdate { }
        public partial struct DirectorFixedUpdatePostPhysics { }
        public partial struct ScriptRunDelayedFixedFrameRate { }
    }

    public partial struct PreUpdate
    {
        public partial struct PhysicsUpdate { }
        public partial struct Physics2DUpdate { }
        public partial struct PhysicsClothUpdate { }
        public partial struct CheckTexFieldInput { }
        public partial struct IMGUISendQueuedEvents { }
        public partial struct NewInputUpdate { }
        public partial struct SendMouseEvents { }
        public partial struct AIUpdate { }
        public partial struct WindUpdate { }
        public partial struct UpdateVideo { }
    }

    public partial struct Update
    {
        public partial struct ScriptRunBehaviourUpdate { }
        public partial struct ScriptRunDelayedDynamicFrameRate { }
        public partial struct ScriptRunDelayedTasks { }
        public partial struct DirectorUpdate { }
    }

    public partial struct PreLateUpdate
    {
        public partial struct AIUpdatePostScript { }
        public partial struct DirectorUpdateAnimationBegin { }
        public partial struct LegacyAnimationUpdate { }
        public partial struct DirectorUpdateAnimationEnd { }
        public partial struct DirectorDeferredEvaluate { }
        public partial struct UIElementsUpdatePanels { }
        public partial struct EndGraphicsJobsAfterScriptUpdate { }
        public partial struct ConstraintManagerUpdate { }
        public partial struct ParticleSystemBeginUpdateAll { }
        public partial struct Physics2DLateUpdate { }
        public partial struct PhysicsLateUpdate { }
        public partial struct ScriptRunBehaviourLateUpdate { }
    }

    public partial struct PostLateUpdate
    {
        public partial struct PlayerSendFrameStarted { }
        public partial struct DirectorLateUpdate { }
        public partial struct ScriptRunDelayedDynamicFrameRate { }
        public partial struct PhysicsSkinnedClothBeginUpdate { }
        public partial struct UpdateRectTransform { }
        public partial struct PlayerUpdateCanvases { }
        public partial struct UpdateAudio { }
        public partial struct VFXUpdate { }
        public partial struct ParticleSystemEndUpdateAll { }
        public partial struct EndGraphicsJobsAfterScriptLateUpdate { }
        public partial struct UpdateCustomRenderTextures { }
        public partial struct XRPostLateUpdate { }
        public partial struct UpdateAllRenderers { }
        public partial struct UpdateLightProbeProxyVolumes { }
        public partial struct EnlightenRuntimeUpdate { }
        public partial struct UpdateAllSkinnedMeshes { }
        public partial struct ProcessWebSendMessages { }
        public partial struct SortingGroupsUpdate { }
        public partial struct UpdateVideoTextures { }
        public partial struct UpdateVideo { }
        public partial struct DirectorRenderImage { }
        public partial struct PlayerEmitCanvasGeometry { }
        public partial struct PhysicsSkinnedClothFinishUpdate { }
        public partial struct FinishFrameRendering { }
        public partial struct BatchModeUpdate { }
        public partial struct PlayerSendFrameComplete { }
        public partial struct UpdateCaptureScreenshot { }
        public partial struct PresentAfterDraw { }
        public partial struct ClearImmediateRenderers { }
        public partial struct PlayerSendFramePostPresent { }
        public partial struct UpdateResolution { }
        public partial struct InputEndFrame { }
        public partial struct TriggerEndOfFrameCallbacks { }
        public partial struct GUIClearEvents { }
        public partial struct ShaderHandleErrors { }
        public partial struct ResetInputAxis { }
        public partial struct ThreadedLoadingDebug { }
        public partial struct ProfilerSynchronizeStats { }
        public partial struct MemoryFrameMaintenance { }
        public partial struct ExecuteGameCenterCallbacks { }
        public partial struct XRPreEndFrame { }
        public partial struct ProfilerEndFrame { }
        public partial struct GraphicsWarmupPreloadedShaders { }
        public partial struct ObjectDispatcherPostLateUpdate { }
    }
}
