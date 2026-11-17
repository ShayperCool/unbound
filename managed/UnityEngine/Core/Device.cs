// UnityEngine.Device: the Device Simulator-aware facade of Application, Screen and SystemInfo.
// Outside the Device Simulator (in Play mode in the Game view, as Unbound runs a project, and in
// builds) every member answers exactly what its UnityEngine counterpart answers, and setters and
// events act on that counterpart. Matches the Unity 6000.0.71f1 Editor, tests/projects/devicefacade.
// https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Device.Screen.html
#pragma warning disable CS0618 // obsolete members forward to their obsolete counterparts
namespace UnityEngine.Device
{
    public static partial class Application
    {
        public static bool CanStreamedLevelBeLoaded(int levelIndex) => global::UnityEngine.Application.CanStreamedLevelBeLoaded(levelIndex);
        public static bool CanStreamedLevelBeLoaded(string levelName) => global::UnityEngine.Application.CanStreamedLevelBeLoaded(levelName);
        [global::System.Obsolete("Application.GetBuildTags is no longer supported and will be removed.", false)] public static string[] GetBuildTags() => global::UnityEngine.Application.GetBuildTags();
        public static global::UnityEngine.StackTraceLogType GetStackTraceLogType(global::UnityEngine.LogType logType) => global::UnityEngine.Application.GetStackTraceLogType(logType);
        public static bool HasProLicense() => global::UnityEngine.Application.HasProLicense();
        public static bool HasUserAuthorization(global::UnityEngine.UserAuthorization mode) => global::UnityEngine.Application.HasUserAuthorization(mode);
        public static bool IsPlaying(global::UnityEngine.Object obj) => global::UnityEngine.Application.IsPlaying(obj);
        public static void OpenURL(string url) => global::UnityEngine.Application.OpenURL(url);
        public static void Quit() => global::UnityEngine.Application.Quit();
        public static void Quit(int exitCode) => global::UnityEngine.Application.Quit(exitCode);
        public static bool RequestAdvertisingIdentifierAsync(global::UnityEngine.Application.AdvertisingIdentifierCallback delegateMethod) => global::UnityEngine.Application.RequestAdvertisingIdentifierAsync(delegateMethod);
        public static global::UnityEngine.AsyncOperation RequestUserAuthorization(global::UnityEngine.UserAuthorization mode) => global::UnityEngine.Application.RequestUserAuthorization(mode);
        [global::System.Obsolete("Application.SetBuildTags is no longer supported and will be removed.", false)] public static void SetBuildTags(string[] buildTags) => global::UnityEngine.Application.SetBuildTags(buildTags);
        public static void SetStackTraceLogType(global::UnityEngine.LogType logType, global::UnityEngine.StackTraceLogType stackTraceType) => global::UnityEngine.Application.SetStackTraceLogType(logType, stackTraceType);
        public static void Unload() => global::UnityEngine.Application.Unload();
        public static string absoluteURL { get => global::UnityEngine.Application.absoluteURL; }
        public static global::UnityEngine.ThreadPriority backgroundLoadingPriority { get => global::UnityEngine.Application.backgroundLoadingPriority; set => global::UnityEngine.Application.backgroundLoadingPriority = value; }
        public static string buildGUID { get => global::UnityEngine.Application.buildGUID; }
        public static string cloudProjectId { get => global::UnityEngine.Application.cloudProjectId; }
        public static string companyName { get => global::UnityEngine.Application.companyName; }
        public static string consoleLogPath { get => global::UnityEngine.Application.consoleLogPath; }
        public static string dataPath { get => global::UnityEngine.Application.dataPath; }
        public static bool genuine { get => global::UnityEngine.Application.genuine; }
        public static bool genuineCheckAvailable { get => global::UnityEngine.Application.genuineCheckAvailable; }
        public static string identifier { get => global::UnityEngine.Application.identifier; }
        public static string installerName { get => global::UnityEngine.Application.installerName; }
        public static global::UnityEngine.ApplicationInstallMode installMode { get => global::UnityEngine.Application.installMode; }
        public static global::UnityEngine.NetworkReachability internetReachability { get => global::UnityEngine.Application.internetReachability; }
        public static bool isBatchMode { get => global::UnityEngine.Application.isBatchMode; }
        public static bool isConsolePlatform { get => global::UnityEngine.Application.isConsolePlatform; }
        public static bool isEditor { get => global::UnityEngine.Application.isEditor; }
        public static bool isFocused { get => global::UnityEngine.Application.isFocused; }
        public static bool isMobilePlatform { get => global::UnityEngine.Application.isMobilePlatform; }
        public static bool isPlaying { get => global::UnityEngine.Application.isPlaying; }
        public static string persistentDataPath { get => global::UnityEngine.Application.persistentDataPath; }
        public static global::UnityEngine.RuntimePlatform platform { get => global::UnityEngine.Application.platform; }
        public static string productName { get => global::UnityEngine.Application.productName; }
        public static bool runInBackground { get => global::UnityEngine.Application.runInBackground; set => global::UnityEngine.Application.runInBackground = value; }
        public static global::UnityEngine.ApplicationSandboxType sandboxType { get => global::UnityEngine.Application.sandboxType; }
        public static string streamingAssetsPath { get => global::UnityEngine.Application.streamingAssetsPath; }
        public static global::UnityEngine.SystemLanguage systemLanguage { get => global::UnityEngine.Application.systemLanguage; }
        public static int targetFrameRate { get => global::UnityEngine.Application.targetFrameRate; set => global::UnityEngine.Application.targetFrameRate = value; }
        public static string temporaryCachePath { get => global::UnityEngine.Application.temporaryCachePath; }
        public static string unityVersion { get => global::UnityEngine.Application.unityVersion; }
        public static string version { get => global::UnityEngine.Application.version; }
        public static global::System.Threading.CancellationToken exitCancellationToken { get => global::UnityEngine.Application.exitCancellationToken; }
        public static event global::System.Action<string> deepLinkActivated { add => global::UnityEngine.Application.deepLinkActivated += value; remove => global::UnityEngine.Application.deepLinkActivated -= value; }
        public static event global::System.Action<bool> focusChanged { add => global::UnityEngine.Application.focusChanged += value; remove => global::UnityEngine.Application.focusChanged -= value; }
        public static event global::UnityEngine.Application.LogCallback logMessageReceived { add => global::UnityEngine.Application.logMessageReceived += value; remove => global::UnityEngine.Application.logMessageReceived -= value; }
        public static event global::UnityEngine.Application.LogCallback logMessageReceivedThreaded { add => global::UnityEngine.Application.logMessageReceivedThreaded += value; remove => global::UnityEngine.Application.logMessageReceivedThreaded -= value; }
        public static event global::UnityEngine.Application.LowMemoryCallback lowMemory { add => global::UnityEngine.Application.lowMemory += value; remove => global::UnityEngine.Application.lowMemory -= value; }
        public static event global::UnityEngine.Application.MemoryUsageChangedCallback memoryUsageChanged { add => global::UnityEngine.Application.memoryUsageChanged += value; remove => global::UnityEngine.Application.memoryUsageChanged -= value; }
        public static event global::UnityEngine.Events.UnityAction onBeforeRender { add => global::UnityEngine.Application.onBeforeRender += value; remove => global::UnityEngine.Application.onBeforeRender -= value; }
        public static event global::System.Action quitting { add => global::UnityEngine.Application.quitting += value; remove => global::UnityEngine.Application.quitting -= value; }
        public static event global::System.Func<bool> wantsToQuit { add => global::UnityEngine.Application.wantsToQuit += value; remove => global::UnityEngine.Application.wantsToQuit -= value; }
        public static event global::System.Action unloading { add => global::UnityEngine.Application.unloading += value; remove => global::UnityEngine.Application.unloading -= value; }
    }
    public static partial class Screen
    {
        public static void SetResolution(int width, int height, global::UnityEngine.FullScreenMode fullscreenMode, global::UnityEngine.RefreshRate preferredRefreshRate) => global::UnityEngine.Screen.SetResolution(width, height, fullscreenMode, preferredRefreshRate);
        [global::System.Obsolete("SetResolution(int, int, FullScreenMode, int) is obsolete. Use SetResolution(int, int, FullScreenMode, RefreshRate) instead.")] public static void SetResolution(int width, int height, global::UnityEngine.FullScreenMode fullscreenMode, int preferredRefreshRate) => global::UnityEngine.Screen.SetResolution(width, height, fullscreenMode, preferredRefreshRate);
        public static void SetResolution(int width, int height, global::UnityEngine.FullScreenMode fullscreenMode) => global::UnityEngine.Screen.SetResolution(width, height, fullscreenMode);
        [global::System.Obsolete("SetResolution(int, int, bool, int) is obsolete. Use SetResolution(int, int, FullScreenMode, RefreshRate) instead.")] public static void SetResolution(int width, int height, bool fullscreen, int preferredRefreshRate) => global::UnityEngine.Screen.SetResolution(width, height, fullscreen, preferredRefreshRate);
        public static void SetResolution(int width, int height, bool fullscreen) => global::UnityEngine.Screen.SetResolution(width, height, fullscreen);
        public static void GetDisplayLayout(global::System.Collections.Generic.List<global::UnityEngine.DisplayInfo> displayLayout) => global::UnityEngine.Screen.GetDisplayLayout(displayLayout);
        public static global::UnityEngine.AsyncOperation MoveMainWindowTo(in global::UnityEngine.DisplayInfo display, global::UnityEngine.Vector2Int position) => global::UnityEngine.Screen.MoveMainWindowTo(in display, position);
        public static bool autorotateToLandscapeLeft { get => global::UnityEngine.Screen.autorotateToLandscapeLeft; set => global::UnityEngine.Screen.autorotateToLandscapeLeft = value; }
        public static bool autorotateToLandscapeRight { get => global::UnityEngine.Screen.autorotateToLandscapeRight; set => global::UnityEngine.Screen.autorotateToLandscapeRight = value; }
        public static bool autorotateToPortrait { get => global::UnityEngine.Screen.autorotateToPortrait; set => global::UnityEngine.Screen.autorotateToPortrait = value; }
        public static bool autorotateToPortraitUpsideDown { get => global::UnityEngine.Screen.autorotateToPortraitUpsideDown; set => global::UnityEngine.Screen.autorotateToPortraitUpsideDown = value; }
        public static global::UnityEngine.Resolution currentResolution { get => global::UnityEngine.Screen.currentResolution; }
        public static global::UnityEngine.Rect[] cutouts { get => global::UnityEngine.Screen.cutouts; }
        public static float dpi { get => global::UnityEngine.Screen.dpi; }
        public static bool fullScreen { get => global::UnityEngine.Screen.fullScreen; set => global::UnityEngine.Screen.fullScreen = value; }
        public static global::UnityEngine.FullScreenMode fullScreenMode { get => global::UnityEngine.Screen.fullScreenMode; set => global::UnityEngine.Screen.fullScreenMode = value; }
        public static int height { get => global::UnityEngine.Screen.height; }
        public static int width { get => global::UnityEngine.Screen.width; }
        public static global::UnityEngine.ScreenOrientation orientation { get => global::UnityEngine.Screen.orientation; set => global::UnityEngine.Screen.orientation = value; }
        public static global::UnityEngine.Resolution[] resolutions { get => global::UnityEngine.Screen.resolutions; }
        public static global::UnityEngine.Rect safeArea { get => global::UnityEngine.Screen.safeArea; }
        public static int sleepTimeout { get => global::UnityEngine.Screen.sleepTimeout; set => global::UnityEngine.Screen.sleepTimeout = value; }
        public static float brightness { get => global::UnityEngine.Screen.brightness; set => global::UnityEngine.Screen.brightness = value; }
        public static global::UnityEngine.Vector2Int mainWindowPosition { get => global::UnityEngine.Screen.mainWindowPosition; }
        public static global::UnityEngine.DisplayInfo mainWindowDisplayInfo { get => global::UnityEngine.Screen.mainWindowDisplayInfo; }
        public static int msaaSamples { get => global::UnityEngine.Screen.msaaSamples; }
    }
    public static partial class SystemInfo
    {
        public const string unsupportedIdentifier = "n/a";
        public static bool SupportsRenderTextureFormat(global::UnityEngine.RenderTextureFormat format) => global::UnityEngine.SystemInfo.SupportsRenderTextureFormat(format);
        public static bool SupportsBlendingOnRenderTextureFormat(global::UnityEngine.RenderTextureFormat format) => global::UnityEngine.SystemInfo.SupportsBlendingOnRenderTextureFormat(format);
        public static bool SupportsTextureFormat(global::UnityEngine.TextureFormat format) => global::UnityEngine.SystemInfo.SupportsTextureFormat(format);
        [global::System.Obsolete("Use overload with a GraphicsFormatUsage parameter instead", false)] public static bool IsFormatSupported(global::UnityEngine.Experimental.Rendering.GraphicsFormat format, global::UnityEngine.Experimental.Rendering.FormatUsage usage) => global::UnityEngine.SystemInfo.IsFormatSupported(format, usage);
        public static bool IsFormatSupported(global::UnityEngine.Experimental.Rendering.GraphicsFormat format, global::UnityEngine.Experimental.Rendering.GraphicsFormatUsage usage) => global::UnityEngine.SystemInfo.IsFormatSupported(format, usage);
        [global::System.Obsolete("Use overload with a GraphicsFormatUsage parameter instead", false)] public static global::UnityEngine.Experimental.Rendering.GraphicsFormat GetCompatibleFormat(global::UnityEngine.Experimental.Rendering.GraphicsFormat format, global::UnityEngine.Experimental.Rendering.FormatUsage usage) => global::UnityEngine.SystemInfo.GetCompatibleFormat(format, usage);
        public static global::UnityEngine.Experimental.Rendering.GraphicsFormat GetCompatibleFormat(global::UnityEngine.Experimental.Rendering.GraphicsFormat format, global::UnityEngine.Experimental.Rendering.GraphicsFormatUsage usage) => global::UnityEngine.SystemInfo.GetCompatibleFormat(format, usage);
        public static global::UnityEngine.Experimental.Rendering.GraphicsFormat GetGraphicsFormat(global::UnityEngine.Experimental.Rendering.DefaultFormat format) => global::UnityEngine.SystemInfo.GetGraphicsFormat(format);
        public static int GetRenderTextureSupportedMSAASampleCount(global::UnityEngine.RenderTextureDescriptor desc) => global::UnityEngine.SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc);
        public static bool SupportsRandomWriteOnRenderTextureFormat(global::UnityEngine.RenderTextureFormat format) => global::UnityEngine.SystemInfo.SupportsRandomWriteOnRenderTextureFormat(format);
        public static int GetTiledRenderTargetStorageSize(global::UnityEngine.Experimental.Rendering.GraphicsFormat format, int sampleCount) => global::UnityEngine.SystemInfo.GetTiledRenderTargetStorageSize(format, sampleCount);
        public static float batteryLevel { get => global::UnityEngine.SystemInfo.batteryLevel; }
        public static global::UnityEngine.BatteryStatus batteryStatus { get => global::UnityEngine.SystemInfo.batteryStatus; }
        public static string operatingSystem { get => global::UnityEngine.SystemInfo.operatingSystem; }
        public static global::UnityEngine.OperatingSystemFamily operatingSystemFamily { get => global::UnityEngine.SystemInfo.operatingSystemFamily; }
        public static string processorType { get => global::UnityEngine.SystemInfo.processorType; }
        public static string processorModel { get => global::UnityEngine.SystemInfo.processorModel; }
        public static string processorManufacturer { get => global::UnityEngine.SystemInfo.processorManufacturer; }
        public static int processorFrequency { get => global::UnityEngine.SystemInfo.processorFrequency; }
        public static int processorCount { get => global::UnityEngine.SystemInfo.processorCount; }
        public static int systemMemorySize { get => global::UnityEngine.SystemInfo.systemMemorySize; }
        public static string deviceUniqueIdentifier { get => global::UnityEngine.SystemInfo.deviceUniqueIdentifier; }
        public static string deviceName { get => global::UnityEngine.SystemInfo.deviceName; }
        public static string deviceModel { get => global::UnityEngine.SystemInfo.deviceModel; }
        public static bool supportsAccelerometer { get => global::UnityEngine.SystemInfo.supportsAccelerometer; }
        public static bool supportsGyroscope { get => global::UnityEngine.SystemInfo.supportsGyroscope; }
        public static bool supportsLocationService { get => global::UnityEngine.SystemInfo.supportsLocationService; }
        public static bool supportsVibration { get => global::UnityEngine.SystemInfo.supportsVibration; }
        public static bool supportsAudio { get => global::UnityEngine.SystemInfo.supportsAudio; }
        public static global::UnityEngine.DeviceType deviceType { get => global::UnityEngine.SystemInfo.deviceType; }
        public static int graphicsMemorySize { get => global::UnityEngine.SystemInfo.graphicsMemorySize; }
        public static string graphicsDeviceName { get => global::UnityEngine.SystemInfo.graphicsDeviceName; }
        public static string graphicsDeviceVendor { get => global::UnityEngine.SystemInfo.graphicsDeviceVendor; }
        public static int graphicsDeviceID { get => global::UnityEngine.SystemInfo.graphicsDeviceID; }
        public static int graphicsDeviceVendorID { get => global::UnityEngine.SystemInfo.graphicsDeviceVendorID; }
        public static global::UnityEngine.Rendering.GraphicsDeviceType graphicsDeviceType { get => global::UnityEngine.SystemInfo.graphicsDeviceType; }
        public static bool graphicsUVStartsAtTop { get => global::UnityEngine.SystemInfo.graphicsUVStartsAtTop; }
        public static string graphicsDeviceVersion { get => global::UnityEngine.SystemInfo.graphicsDeviceVersion; }
        public static int graphicsShaderLevel { get => global::UnityEngine.SystemInfo.graphicsShaderLevel; }
        public static bool graphicsMultiThreaded { get => global::UnityEngine.SystemInfo.graphicsMultiThreaded; }
        public static global::UnityEngine.Rendering.RenderingThreadingMode renderingThreadingMode { get => global::UnityEngine.SystemInfo.renderingThreadingMode; }
        public static global::UnityEngine.Rendering.FoveatedRenderingCaps foveatedRenderingCaps { get => global::UnityEngine.SystemInfo.foveatedRenderingCaps; }
        public static int maxTiledPixelStorageSize { get => global::UnityEngine.SystemInfo.maxTiledPixelStorageSize; }
        public static bool hasHiddenSurfaceRemovalOnGPU { get => global::UnityEngine.SystemInfo.hasHiddenSurfaceRemovalOnGPU; }
        public static bool hasDynamicUniformArrayIndexingInFragmentShaders { get => global::UnityEngine.SystemInfo.hasDynamicUniformArrayIndexingInFragmentShaders; }
        public static bool supportsShadows { get => global::UnityEngine.SystemInfo.supportsShadows; }
        public static bool supportsRawShadowDepthSampling { get => global::UnityEngine.SystemInfo.supportsRawShadowDepthSampling; }
        public static bool supportsMotionVectors { get => global::UnityEngine.SystemInfo.supportsMotionVectors; }
        public static bool supports3DTextures { get => global::UnityEngine.SystemInfo.supports3DTextures; }
        public static bool supportsCompressed3DTextures { get => global::UnityEngine.SystemInfo.supportsCompressed3DTextures; }
        public static bool supports2DArrayTextures { get => global::UnityEngine.SystemInfo.supports2DArrayTextures; }
        public static bool supports3DRenderTextures { get => global::UnityEngine.SystemInfo.supports3DRenderTextures; }
        public static bool supportsCubemapArrayTextures { get => global::UnityEngine.SystemInfo.supportsCubemapArrayTextures; }
        public static bool supportsAnisotropicFilter { get => global::UnityEngine.SystemInfo.supportsAnisotropicFilter; }
        public static global::UnityEngine.Rendering.CopyTextureSupport copyTextureSupport { get => global::UnityEngine.SystemInfo.copyTextureSupport; }
        public static bool supportsComputeShaders { get => global::UnityEngine.SystemInfo.supportsComputeShaders; }
        public static bool supportsGeometryShaders { get => global::UnityEngine.SystemInfo.supportsGeometryShaders; }
        public static bool supportsTessellationShaders { get => global::UnityEngine.SystemInfo.supportsTessellationShaders; }
        public static bool supportsRenderTargetArrayIndexFromVertexShader { get => global::UnityEngine.SystemInfo.supportsRenderTargetArrayIndexFromVertexShader; }
        public static bool supportsInstancing { get => global::UnityEngine.SystemInfo.supportsInstancing; }
        public static bool supportsHardwareQuadTopology { get => global::UnityEngine.SystemInfo.supportsHardwareQuadTopology; }
        public static bool supports32bitsIndexBuffer { get => global::UnityEngine.SystemInfo.supports32bitsIndexBuffer; }
        public static bool supportsSparseTextures { get => global::UnityEngine.SystemInfo.supportsSparseTextures; }
        public static int supportedRenderTargetCount { get => global::UnityEngine.SystemInfo.supportedRenderTargetCount; }
        public static bool supportsSeparatedRenderTargetsBlend { get => global::UnityEngine.SystemInfo.supportsSeparatedRenderTargetsBlend; }
        public static int supportedRandomWriteTargetCount { get => global::UnityEngine.SystemInfo.supportedRandomWriteTargetCount; }
        public static int supportsMultisampledTextures { get => global::UnityEngine.SystemInfo.supportsMultisampledTextures; }
        public static bool supportsMultisampled2DArrayTextures { get => global::UnityEngine.SystemInfo.supportsMultisampled2DArrayTextures; }
        public static bool supportsMultisampledBackBuffer { get => global::UnityEngine.SystemInfo.supportsMultisampledBackBuffer; }
        public static bool supportsMultisampleAutoResolve { get => global::UnityEngine.SystemInfo.supportsMultisampleAutoResolve; }
        public static int supportsTextureWrapMirrorOnce { get => global::UnityEngine.SystemInfo.supportsTextureWrapMirrorOnce; }
        public static bool usesReversedZBuffer { get => global::UnityEngine.SystemInfo.usesReversedZBuffer; }
        public static global::UnityEngine.NPOTSupport npotSupport { get => global::UnityEngine.SystemInfo.npotSupport; }
        public static int maxTextureSize { get => global::UnityEngine.SystemInfo.maxTextureSize; }
        public static int maxTexture3DSize { get => global::UnityEngine.SystemInfo.maxTexture3DSize; }
        public static int maxTextureArraySlices { get => global::UnityEngine.SystemInfo.maxTextureArraySlices; }
        public static int maxCubemapSize { get => global::UnityEngine.SystemInfo.maxCubemapSize; }
        public static int maxAnisotropyLevel { get => global::UnityEngine.SystemInfo.maxAnisotropyLevel; }
        public static int maxComputeBufferInputsVertex { get => global::UnityEngine.SystemInfo.maxComputeBufferInputsVertex; }
        public static int maxComputeBufferInputsFragment { get => global::UnityEngine.SystemInfo.maxComputeBufferInputsFragment; }
        public static int maxComputeBufferInputsGeometry { get => global::UnityEngine.SystemInfo.maxComputeBufferInputsGeometry; }
        public static int maxComputeBufferInputsDomain { get => global::UnityEngine.SystemInfo.maxComputeBufferInputsDomain; }
        public static int maxComputeBufferInputsHull { get => global::UnityEngine.SystemInfo.maxComputeBufferInputsHull; }
        public static int maxComputeBufferInputsCompute { get => global::UnityEngine.SystemInfo.maxComputeBufferInputsCompute; }
        public static int maxComputeWorkGroupSize { get => global::UnityEngine.SystemInfo.maxComputeWorkGroupSize; }
        public static int maxComputeWorkGroupSizeX { get => global::UnityEngine.SystemInfo.maxComputeWorkGroupSizeX; }
        public static int maxComputeWorkGroupSizeY { get => global::UnityEngine.SystemInfo.maxComputeWorkGroupSizeY; }
        public static int maxComputeWorkGroupSizeZ { get => global::UnityEngine.SystemInfo.maxComputeWorkGroupSizeZ; }
        public static int computeSubGroupSize { get => global::UnityEngine.SystemInfo.computeSubGroupSize; }
        public static bool supportsAsyncCompute { get => global::UnityEngine.SystemInfo.supportsAsyncCompute; }
        public static bool supportsGraphicsFence { get => global::UnityEngine.SystemInfo.supportsGraphicsFence; }
        public static bool supportsAsyncGPUReadback { get => global::UnityEngine.SystemInfo.supportsAsyncGPUReadback; }
        public static bool supportsParallelPSOCreation { get => global::UnityEngine.SystemInfo.supportsParallelPSOCreation; }
        public static bool supportsRayTracingShaders { get => global::UnityEngine.SystemInfo.supportsRayTracingShaders; }
        public static bool supportsRayTracing { get => global::UnityEngine.SystemInfo.supportsRayTracing; }
        public static bool supportsInlineRayTracing { get => global::UnityEngine.SystemInfo.supportsInlineRayTracing; }
        public static bool supportsSetConstantBuffer { get => global::UnityEngine.SystemInfo.supportsSetConstantBuffer; }
        public static int constantBufferOffsetAlignment { get => global::UnityEngine.SystemInfo.constantBufferOffsetAlignment; }
        public static int maxConstantBufferSize { get => global::UnityEngine.SystemInfo.maxConstantBufferSize; }
        public static long maxGraphicsBufferSize { get => global::UnityEngine.SystemInfo.maxGraphicsBufferSize; }
        public static bool hasMipMaxLevel { get => global::UnityEngine.SystemInfo.hasMipMaxLevel; }
        public static bool supportsMipStreaming { get => global::UnityEngine.SystemInfo.supportsMipStreaming; }
        public static bool usesLoadStoreActions { get => global::UnityEngine.SystemInfo.usesLoadStoreActions; }
        public static global::UnityEngine.HDRDisplaySupportFlags hdrDisplaySupportFlags { get => global::UnityEngine.SystemInfo.hdrDisplaySupportFlags; }
        public static bool supportsConservativeRaster { get => global::UnityEngine.SystemInfo.supportsConservativeRaster; }
        public static bool supportsMultiview { get => global::UnityEngine.SystemInfo.supportsMultiview; }
        public static bool supportsStoreAndResolveAction { get => global::UnityEngine.SystemInfo.supportsStoreAndResolveAction; }
        public static bool supportsMultisampleResolveDepth { get => global::UnityEngine.SystemInfo.supportsMultisampleResolveDepth; }
        public static bool supportsMultisampleResolveStencil { get => global::UnityEngine.SystemInfo.supportsMultisampleResolveStencil; }
        public static bool supportsIndirectArgumentsBuffer { get => global::UnityEngine.SystemInfo.supportsIndirectArgumentsBuffer; }
        public static bool supportsDepthFetchInRenderPass { get => global::UnityEngine.SystemInfo.supportsDepthFetchInRenderPass; }
        public static bool supportsDynamicResolution { get => global::UnityEngine.SystemInfo.supportsDynamicResolution; }
    }
}
