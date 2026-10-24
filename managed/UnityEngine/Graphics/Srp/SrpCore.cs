using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unbound.Runtime;

namespace UnityEngine.Rendering
{
    /// <summary>A scriptable render pipeline's asset: makes the pipeline that renders the cameras.</summary>
    public abstract partial class RenderPipelineAsset : ScriptableObject
    {
        protected RenderPipelineAsset() { }

        public virtual int terrainBrushPassIndex => 2500;
        public virtual Material defaultMaterial => null;
        public virtual Shader autodeskInteractiveShader => null;
        public virtual Shader autodeskInteractiveTransparentShader => null;
        public virtual Shader autodeskInteractiveMaskedShader => null;
        public virtual Shader terrainDetailLitShader => null;
        public virtual Shader terrainDetailGrassShader => null;
        public virtual Shader terrainDetailGrassBillboardShader => null;
        public virtual Material defaultParticleMaterial => null;
        public virtual Material defaultLineMaterial => null;
        public virtual Material defaultTerrainMaterial => null;
        public virtual Material defaultUIMaterial => null;
        public virtual Material defaultUIOverdrawMaterial => null;
        public virtual Material defaultUIETC1SupportedMaterial => null;
        public virtual Material default2DMaterial => null;
        public virtual Material default2DMaskMaterial => null;
        public virtual Shader defaultShader => null;
        public virtual Shader defaultSpeedTree7Shader => null;
        public virtual Shader defaultSpeedTree8Shader => null;
        public virtual Shader defaultSpeedTree9Shader => null;

        public virtual string renderPipelineShaderTag
        {
            get
            {
                Debug.LogWarning("The property renderPipelineShaderTag has not been overridden. At build time, any shader variants that use any RenderPipeline tag will be stripped.");
                return string.Empty;
            }
        }

        public virtual Type pipelineType
        {
            get
            {
                Debug.LogWarning("You must either inherit from RenderPipelineAsset<TRenderPipeline> or override pipelineType property.");
                return null;
            }
        }

        internal string pipelineTypeFullName => pipelineType?.FullName ?? string.Empty;

        protected internal virtual bool requiresCompatibleRenderPipelineGlobalSettings { get; } = false;

        [Obsolete("This property is obsolete. Use pipelineType instead. #from(23.2)", false)]
        protected internal virtual Type renderPipelineType
        {
            get
            {
                Debug.LogWarning("You must either inherit from RenderPipelineAsset<TRenderPipeline> or override renderPipelineType property");
                return null;
            }
        }

        [Obsolete("This property is obsolete. Use RenderingLayerMask API and Tags & Layers project settings instead. #from(23.3)", false)]
        public virtual string[] renderingLayerMaskNames => null;

        [Obsolete("This property is obsolete. Use RenderingLayerMask API and Tags & Layers project settings instead. #from(23.3)", false)]
        public virtual string[] prefixedRenderingLayerMaskNames => null;

        /// <summary>The pipeline, or null (the exception logged) when making it throws.</summary>
        internal RenderPipeline InternalCreatePipeline()
        {
            RenderPipeline pipeline = null;
            try
            {
                EnsureGlobalSettings();
                pipeline = CreatePipeline();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (pipelineType != null && pipeline != null && pipelineType != pipeline.GetType())
                Debug.LogWarning($"{GetType()}.CreatePipeline. Type error: {pipelineType} is different from {pipeline.GetType()}. The property pipelineType must return the same type");
            return pipeline;
        }

        protected abstract RenderPipeline CreatePipeline();

        protected virtual void EnsureGlobalSettings() { }

        protected virtual void OnValidate() => RenderPipelineManager.RecreateCurrentPipeline(this);

        protected virtual void OnDisable() => RenderPipelineManager.CleanupRenderPipeline();
    }

    public abstract partial class RenderPipelineAsset<TRenderPipeline> : RenderPipelineAsset where TRenderPipeline : RenderPipeline
    {
        protected RenderPipelineAsset() { }

        public sealed override Type pipelineType => typeof(TRenderPipeline);

        public override string renderPipelineShaderTag => typeof(TRenderPipeline).Name;

        [Obsolete("This property is obsolete. Use pipelineType instead. #from(23.2)", false)]
        protected internal sealed override Type renderPipelineType => typeof(TRenderPipeline);
    }

    /// <summary>A scriptable render pipeline: renders the cameras each frame.</summary>
    public abstract partial class RenderPipeline
    {
        public class StandardRequest
        {
            public RenderTexture destination;
            public int mipLevel;
            public CubemapFace face = CubemapFace.Unknown;
            public int slice;
        }

        protected RenderPipeline() { }

        public bool disposed { get; private set; }

        public virtual RenderPipelineGlobalSettings defaultSettings => null;

        protected abstract void Render(ScriptableRenderContext context, Camera[] cameras);

        protected virtual void Render(ScriptableRenderContext context, List<Camera> cameras) => Render(context, cameras.ToArray());

        protected virtual void ProcessRenderRequests<RequestData>(ScriptableRenderContext context, Camera camera, RequestData renderRequest) { }

        protected internal virtual bool IsRenderRequestSupported<RequestData>(Camera camera, RequestData data) => false;

        protected static void BeginFrameRendering(ScriptableRenderContext context, Camera[] cameras) => RenderPipelineManager.BeginContextRendering(context, new List<Camera>(cameras));
        protected static void BeginContextRendering(ScriptableRenderContext context, List<Camera> cameras) => RenderPipelineManager.BeginContextRendering(context, cameras);
        protected static void BeginCameraRendering(ScriptableRenderContext context, Camera camera) => RenderPipelineManager.BeginCameraRendering(context, camera);
        protected static void EndContextRendering(ScriptableRenderContext context, List<Camera> cameras) => RenderPipelineManager.EndContextRendering(context, cameras);
        protected static void EndFrameRendering(ScriptableRenderContext context, Camera[] cameras) => RenderPipelineManager.EndContextRendering(context, new List<Camera>(cameras));
        protected static void EndCameraRendering(ScriptableRenderContext context, Camera camera) => RenderPipelineManager.EndCameraRendering(context, camera);

        internal void InternalRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (disposed) throw new ObjectDisposedException($"{this} has been disposed. Do not call Render on disposed a RenderPipeline.");
            Render(context, cameras);
        }

        internal void InternalProcessRenderRequests<RequestData>(ScriptableRenderContext context, Camera camera, RequestData renderRequest)
        {
            if (disposed) throw new ObjectDisposedException($"{this} has been disposed. Do not call Render on disposed a RenderPipeline.");
            ProcessRenderRequests(context, camera, renderRequest);
        }

        public static bool SupportsRenderRequest<RequestData>(Camera camera, RequestData data)
        {
            if (GraphicsSettings.currentRenderPipeline == null) return false;
            if (RenderPipelineManager.currentPipeline == null) RenderPipelineManager.TryPrepareRenderPipeline(GraphicsSettings.currentRenderPipeline);
            return RenderPipelineManager.currentPipeline?.IsRenderRequestSupported(camera, data) ?? false;
        }

        public static void SubmitRenderRequest<RequestData>(Camera camera, RequestData data)
        {
            if (!RenderPipelineManager.TryPrepareRenderPipeline(GraphicsSettings.currentRenderPipeline)) return;
            var context = ScriptableRenderContext.Begin();
            RenderPipelineManager.currentPipeline.InternalProcessRenderRequests(context, camera, data);
        }

        internal void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
            disposed = true;
        }

        protected virtual void Dispose(bool disposing) { }
    }

    /// <summary>Makes the active pipeline from the current asset and runs it each frame.</summary>
    public static partial class RenderPipelineManager
    {
        static bool s_CleanUpPipeline;
        static RenderPipelineAsset s_CurrentPipelineAsset;
        static RenderPipeline s_CurrentPipeline;

        internal static RenderPipelineAsset currentPipelineAsset => s_CurrentPipelineAsset;

        public static RenderPipeline currentPipeline
        {
            get => s_CurrentPipeline;
            private set => s_CurrentPipeline = value;
        }

        public static bool pipelineSwitchCompleted => ReferenceEquals(s_CurrentPipelineAsset, GraphicsSettings.currentRenderPipeline) && !IsPipelineRequireCreation();

        static bool isCurrentPipelineValid => s_CurrentPipeline != null && !s_CurrentPipeline.disposed;

        public static event Action<ScriptableRenderContext, List<Camera>> beginContextRendering;
        public static event Action<ScriptableRenderContext, List<Camera>> endContextRendering;
        public static event Action<ScriptableRenderContext, Camera> beginCameraRendering;
        public static event Action<ScriptableRenderContext, Camera> endCameraRendering;
        public static event Action activeRenderPipelineTypeChanged;
        public static event Action<RenderPipelineAsset, RenderPipelineAsset> activeRenderPipelineAssetChanged;
        public static event Action activeRenderPipelineCreated;
        public static event Action activeRenderPipelineDisposed;

        [Obsolete("beginFrameRendering is deprecated. Use beginContextRendering instead. #from 2023.3", false)]
        public static event Action<ScriptableRenderContext, Camera[]> beginFrameRendering;

        [Obsolete("endFrameRendering is deprecated. Use endContextRendering instead. #from 2023.3", false)]
        public static event Action<ScriptableRenderContext, Camera[]> endFrameRendering;

        internal static void BeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            beginContextRendering?.Invoke(context, cameras);
#pragma warning disable CS0618
            beginFrameRendering?.Invoke(context, cameras.ToArray());
#pragma warning restore CS0618
        }

        internal static void BeginCameraRendering(ScriptableRenderContext context, Camera camera) => beginCameraRendering?.Invoke(context, camera);

        internal static void EndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
#pragma warning disable CS0618
            endFrameRendering?.Invoke(context, cameras.ToArray());
#pragma warning restore CS0618
            endContextRendering?.Invoke(context, cameras);
        }

        internal static void EndCameraRendering(ScriptableRenderContext context, Camera camera) => endCameraRendering?.Invoke(context, camera);

        internal static void OnActiveRenderPipelineTypeChanged() => activeRenderPipelineTypeChanged?.Invoke();

        internal static void OnActiveRenderPipelineAssetChanged(ScriptableObject from, ScriptableObject to) =>
            activeRenderPipelineAssetChanged?.Invoke(from as RenderPipelineAsset, to as RenderPipelineAsset);

        internal static void HandleRenderPipelineChange(RenderPipelineAsset pipelineAsset)
        {
            bool changed = !ReferenceEquals(s_CurrentPipelineAsset, pipelineAsset);
            if (s_CleanUpPipeline || changed)
            {
                CleanupRenderPipeline();
                s_CurrentPipelineAsset = pipelineAsset;
            }
        }

        internal static void RecreateCurrentPipeline(RenderPipelineAsset pipelineAsset)
        {
            if (ReferenceEquals(s_CurrentPipelineAsset, pipelineAsset)) s_CleanUpPipeline = true;
        }

        internal static void CleanupRenderPipeline()
        {
            if (!isCurrentPipelineValid) return;
            if (GraphicsSettings.currentRenderPipeline == null) Shader.globalRenderPipeline = string.Empty;
            activeRenderPipelineDisposed?.Invoke();
            s_CurrentPipeline.Dispose();
            s_CurrentPipeline = null;
            s_CleanUpPipeline = false;
            s_CurrentPipelineAsset = null;
            SupportedRenderingFeatures.active = null;
        }

        internal static bool TryPrepareRenderPipeline(RenderPipelineAsset pipelineAsset)
        {
            HandleRenderPipelineChange(pipelineAsset);
            if (!IsPipelineRequireCreation()) return s_CurrentPipeline != null;
            s_CurrentPipeline = s_CurrentPipelineAsset.InternalCreatePipeline();
            Shader.globalRenderPipeline = s_CurrentPipelineAsset.renderPipelineShaderTag;
            activeRenderPipelineCreated?.Invoke();
            return s_CurrentPipeline != null;
        }

        static bool IsPipelineRequireCreation() => s_CurrentPipelineAsset != null && (s_CurrentPipeline == null || s_CurrentPipeline.disposed);

        /// <summary>The frame's cameras through the active pipeline (DoRenderLoop): false when there
        /// is no pipeline (the built-in one renders).</summary>
        internal static bool Render(List<Camera> cameras)
        {
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null)
            {
                if (s_CurrentPipelineAsset != null) CleanupRenderPipeline();
                return false;
            }
            if (!TryPrepareRenderPipeline(asset)) return true;
            var context = ScriptableRenderContext.Begin(cameras);
            try { s_CurrentPipeline.InternalRender(context, cameras); }
            catch (Exception e) { Debug.LogException(e); }
            return true;
        }
    }

    public partial interface IRenderPipelineGraphicsSettings
    {
        int version { get; }
        bool isAvailableInPlayerBuild => false;
        void Reset() { }
    }

    public partial interface IRenderPipelineResources : IRenderPipelineGraphicsSettings
    {
    }

    /// <summary>Settings and resources a pipeline keeps per project (the asset the GraphicsSettings
    /// map names for the pipeline type).</summary>
    public abstract partial class RenderPipelineGlobalSettings : ScriptableObject, ISerializationCallbackReceiver
    {
        protected RenderPipelineGlobalSettings() { }

        protected virtual List<IRenderPipelineGraphicsSettings> settingsList
        {
            get
            {
                Debug.LogWarning($"To be able to use IRenderPipelineGraphicsSettings in your {GetType()} you must override settingsList");
                return null;
            }
        }

        readonly Dictionary<Type, int> m_SettingsMap = new Dictionary<Type, int>();

        void RecreateSettingsMap()
        {
            m_SettingsMap.Clear();
            var list = settingsList;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) m_SettingsMap[list[i].GetType()] = i;
        }

        public virtual void Initialize(RenderPipelineGlobalSettings source = null) { }

        protected internal bool Add(IRenderPipelineGraphicsSettings settings)
        {
            var list = settingsList;
            if (list == null || m_SettingsMap.ContainsKey(settings.GetType())) return false;
            m_SettingsMap.Add(settings.GetType(), list.Count);
            list.Add(settings);
            return true;
        }

        protected internal bool Remove(IRenderPipelineGraphicsSettings settings)
        {
            var list = settingsList;
            if (list == null || !m_SettingsMap.TryGetValue(settings.GetType(), out var index)) return false;
            list.RemoveAt(index);
            RecreateSettingsMap();
            return true;
        }

        internal int IndexOf(Type settingsType) => settingsList == null ? -1 : m_SettingsMap.GetValueOrDefault(settingsType, -1);

        internal void CleanNullSettings()
        {
            var list = settingsList;
            if (list == null) return;
            list.RemoveAll(s => s == null);
            RecreateSettingsMap();
        }

        protected internal bool TryGet(Type type, out IRenderPipelineGraphicsSettings settings)
        {
            settings = null;
            var list = settingsList;
            if (list == null) return false;
            if (m_SettingsMap.Count == 0 && list.Count > 0) RecreateSettingsMap();
            if (!m_SettingsMap.TryGetValue(type, out var index)) return false;
            settings = list[index];
            return settings != null;
        }

        protected internal bool TryGetFirstSettingsImplementingInterface<TSettingsInterfaceType>(out TSettingsInterfaceType settings)
            where TSettingsInterfaceType : class, IRenderPipelineGraphicsSettings
        {
            settings = null;
            var list = settingsList;
            if (list == null) return false;
            foreach (var s in list)
            {
                if (s is TSettingsInterfaceType match)
                {
                    settings = match;
                    return true;
                }
            }
            return false;
        }

        protected internal bool GetSettingsImplementingInterface<TSettingsInterfaceType>(out List<TSettingsInterfaceType> settings)
            where TSettingsInterfaceType : class, IRenderPipelineGraphicsSettings
        {
            settings = new List<TSettingsInterfaceType>();
            var list = settingsList;
            if (list == null) return false;
            foreach (var s in list)
                if (s is TSettingsInterfaceType match) settings.Add(match);
            return settings.Count > 0;
        }

        protected internal bool Contains(Type type) => settingsList != null && m_SettingsMap.ContainsKey(type);

        public virtual void OnBeforeSerialize() { }

        public virtual void OnAfterDeserialize() => RecreateSettingsMap();
    }

    public static partial class RenderPipelineGraphicsSettingsExtensions
    {
        public static void SetValueAndNotify<T>(this IRenderPipelineGraphicsSettings settings, ref T currentPropertyValue, T newValue, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(currentPropertyValue, newValue)) return;
            currentPropertyValue = newValue;
            GraphicsSettings.CallOnIRenderPipelineGraphicsSettingsChange(settings, propertyName);
        }

        public static void NotifyValueChanged(this IRenderPipelineGraphicsSettings settings, [CallerMemberName] string propertyName = null) =>
            GraphicsSettings.CallOnIRenderPipelineGraphicsSettingsChange(settings, propertyName);
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public partial class SupportedOnRenderPipelineAttribute : Attribute
    {
        public enum SupportedMode
        {
            Unsupported = 0,
            Supported = 1,
            SupportedByBaseClass = 2,
        }

        public Type[] renderPipelineTypes { get; }

        public bool isSupportedOnCurrentPipeline => GetSupportedMode(renderPipelineTypes, GraphicsSettings.currentRenderPipelineAssetType) != SupportedMode.Unsupported;

        public SupportedOnRenderPipelineAttribute(Type renderPipeline) : this(new[] { renderPipeline }) { }

        public SupportedOnRenderPipelineAttribute(params Type[] renderPipeline)
        {
            if (renderPipeline == null)
            {
                Debug.LogError("The SupportedOnRenderPipelineAttribute parameters cannot be null.");
                return;
            }
            foreach (var type in renderPipeline)
            {
                if (type == null || !typeof(RenderPipelineAsset).IsAssignableFrom(type))
                {
                    Debug.LogError("The SupportedOnRenderPipelineAttribute Attribute targets an invalid RenderPipelineAsset.");
                    return;
                }
            }
            renderPipelineTypes = renderPipeline.Length == 0 ? new[] { typeof(RenderPipelineAsset) } : renderPipeline;
        }

        public SupportedMode GetSupportedMode(Type renderPipelineAssetType) => GetSupportedMode(renderPipelineTypes, renderPipelineAssetType);

        internal static SupportedMode GetSupportedMode(Type[] renderPipelineTypes, Type renderPipelineAssetType)
        {
            if (renderPipelineTypes == null) throw new ArgumentNullException("Parameter renderPipelineTypes cannot be null.");
            if (renderPipelineAssetType == null) return SupportedMode.Unsupported;
            foreach (var t in renderPipelineTypes)
                if (t == renderPipelineAssetType) return SupportedMode.Supported;
            foreach (var t in renderPipelineTypes)
                if (t.IsAssignableFrom(renderPipelineAssetType)) return SupportedMode.SupportedByBaseClass;
            return SupportedMode.Unsupported;
        }

        public static bool IsTypeSupportedOnRenderPipeline(Type type, Type renderPipelineAssetType)
        {
            var attribute = type.GetCustomAttribute<SupportedOnRenderPipelineAttribute>();
            return attribute == null || attribute.GetSupportedMode(renderPipelineAssetType) != SupportedMode.Unsupported;
        }
    }

    /// <summary>The project's graphics settings: the default render pipeline (a quality level may
    /// override it) and each pipeline's global settings asset.</summary>
    public sealed partial class GraphicsSettings : Object
    {
        GraphicsSettings() { }

        static RenderPipelineAsset s_DefaultRenderPipeline;
        static bool s_DefaultLoaded;
        static RenderPipelineGlobalSettings s_CurrentGlobalSettings;
        static readonly Dictionary<string, RenderPipelineGlobalSettings> s_GlobalSettings = new Dictionary<string, RenderPipelineGlobalSettings>();

        /// <summary>Set at start: the asset references of the project's settings.</summary>
        internal static ProjectRenderPipeline Project = new ProjectRenderPipeline();

        internal sealed class ProjectRenderPipeline
        {
            internal (string Guid, long FileId)? Default;
            internal List<(string Guid, long FileId)?> QualityLevels = new List<(string, long)?>();
            internal Dictionary<string, (string Guid, long FileId)> GlobalSettings = new Dictionary<string, (string, long)>();
        }

        internal static T LoadRef<T>((string Guid, long FileId)? reference) where T : Object =>
            reference.HasValue ? AssetDatabaseRuntime.Load(reference.Value.Guid, reference.Value.FileId, typeof(T)) as T : null;

        public static RenderPipelineAsset defaultRenderPipeline
        {
            get
            {
                if (!s_DefaultLoaded)
                {
                    s_DefaultLoaded = true;
                    s_DefaultRenderPipeline = LoadRef<RenderPipelineAsset>(Project.Default);
                }
                return s_DefaultRenderPipeline;
            }
            set
            {
                s_DefaultLoaded = true;
                s_DefaultRenderPipeline = value;
            }
        }

        [Obsolete("renderPipelineAsset has been deprecated. Use defaultRenderPipeline instead (UnityUpgradable) -> defaultRenderPipeline", false)]
        public static RenderPipelineAsset renderPipelineAsset { get => defaultRenderPipeline; set => defaultRenderPipeline = value; }

        /// <summary>The quality level's pipeline, else the default one.</summary>
        public static RenderPipelineAsset currentRenderPipeline => QualitySettings.renderPipeline ?? defaultRenderPipeline;

        public static bool isScriptableRenderPipelineEnabled => currentRenderPipeline != null;

        public static Type currentRenderPipelineAssetType => currentRenderPipeline?.GetType();

        public static RenderPipelineAsset[] allConfiguredRenderPipelines
        {
            get
            {
                var all = new List<RenderPipelineAsset>();
                if (defaultRenderPipeline != null) all.Add(defaultRenderPipeline);
                for (int i = 0; i < QualitySettings.count; i++)
                {
                    var p = QualitySettings.GetRenderPipelineAssetAt(i);
                    if (p != null && !all.Contains(p)) all.Add(p);
                }
                return all.ToArray();
            }
        }

        public static bool lightsUseLinearIntensity { get; set; }
        public static bool lightsUseColorTemperature { get; set; }
        public static bool useScriptableRenderPipelineBatching { get; set; }
        public static TransparencySortMode transparencySortMode { get; set; }
        public static Vector3 transparencySortAxis { get; set; } = new Vector3(0f, 0f, 1f);
        public static uint defaultRenderingLayerMask { get; set; } = 1;
        public static bool realtimeDirectRectangularAreaLights { get; set; }
        public static bool logWhenShaderIsCompiled { get; set; }
        public static bool disableBuiltinCustomRenderTextureUpdate { get; set; }
        public static bool cameraRelativeLightCulling { get; set; }
        public static bool cameraRelativeShadowCulling { get; set; }

        /// <summary>The global settings of the active pipeline: set by the pipeline, else the asset
        /// the project registers for its type.</summary>
        internal static RenderPipelineGlobalSettings currentRenderPipelineGlobalSettings
        {
            get
            {
                if (s_CurrentGlobalSettings != null) return s_CurrentGlobalSettings;
                var asset = currentRenderPipeline;
                return asset == null ? null : GetSettingsForRenderPipeline(asset.pipelineTypeFullName);
            }
            set => s_CurrentGlobalSettings = value;
        }

        static RenderPipelineGlobalSettings GetSettingsForRenderPipeline(string pipelineTypeFullName)
        {
            if (string.IsNullOrEmpty(pipelineTypeFullName)) return null;
            if (s_GlobalSettings.TryGetValue(pipelineTypeFullName, out var settings) && settings != null) return settings;
            if (Project.GlobalSettings.TryGetValue(pipelineTypeFullName, out var reference))
            {
                settings = LoadRef<RenderPipelineGlobalSettings>(reference);
                if (settings != null) s_GlobalSettings[pipelineTypeFullName] = settings;
            }
            return settings;
        }

        /// <summary>Every pipeline's global settings asset the project registers.</summary>
        internal static List<RenderPipelineGlobalSettings> AllGlobalSettings()
        {
            var all = new List<RenderPipelineGlobalSettings>();
            foreach (var type in Project.GlobalSettings.Keys)
                if (GetSettingsForRenderPipeline(type) is { } settings && !all.Contains(settings)) all.Add(settings);
            foreach (var settings in s_GlobalSettings.Values)
                if (settings != null && !all.Contains(settings)) all.Add(settings);
            return all;
        }

        static void CheckRenderPipelineType(Type renderPipelineType)
        {
            if (renderPipelineType == null) throw new ArgumentNullException(nameof(renderPipelineType));
            if (!typeof(RenderPipeline).IsAssignableFrom(renderPipelineType))
                throw new ArgumentException($"{renderPipelineType} must be a valid RenderPipeline");
        }

        [Obsolete("Please use EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset(renderPipelineType, newSettings). #from(23.2)", false)]
        public static void UpdateGraphicsSettings(RenderPipelineGlobalSettings newSettings, Type renderPipelineType)
        {
            CheckRenderPipelineType(renderPipelineType);
            if (newSettings != null) s_GlobalSettings[renderPipelineType.FullName] = newSettings;
            else s_GlobalSettings.Remove(renderPipelineType.FullName);
        }

        [Obsolete("Please use EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset(renderPipelineType, settings). #from(23.2)", false)]
        public static void RegisterRenderPipelineSettings(Type renderPipelineType, RenderPipelineGlobalSettings settings)
        {
            CheckRenderPipelineType(renderPipelineType);
            s_GlobalSettings[renderPipelineType.FullName] = settings;
        }

        [Obsolete("Please use EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<T>(settings). #from(23.2)", false)]
        public static void RegisterRenderPipelineSettings<T>(RenderPipelineGlobalSettings settings) where T : RenderPipeline =>
            s_GlobalSettings[typeof(T).FullName] = settings;

        [Obsolete("Please use EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<T>(null). #from(23.2)", false)]
        public static void UnregisterRenderPipelineSettings<T>() where T : RenderPipeline => s_GlobalSettings.Remove(typeof(T).FullName);

        [Obsolete("Please use EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset(renderPipelineType, null). #from(23.2)", false)]
        public static void UnregisterRenderPipelineSettings(Type renderPipelineType)
        {
            CheckRenderPipelineType(renderPipelineType);
            s_GlobalSettings.Remove(renderPipelineType.FullName);
        }

        public static RenderPipelineGlobalSettings GetSettingsForRenderPipeline<T>() where T : RenderPipeline => GetSettingsForRenderPipeline(typeof(T).FullName);

        public static RenderPipelineGlobalSettings GetSettingsForRenderPipeline(Type renderPipelineType)
        {
            CheckRenderPipelineType(renderPipelineType);
            return GetSettingsForRenderPipeline(renderPipelineType.FullName);
        }

        public static bool TrySetCurrentRenderPipelineGlobalSettings(RenderPipelineGlobalSettings asset)
        {
            currentRenderPipelineGlobalSettings = asset;
            return asset != null;
        }

        public static bool TryGetCurrentRenderPipelineGlobalSettings(out RenderPipelineGlobalSettings asset)
        {
            asset = currentRenderPipelineGlobalSettings;
            return asset != null;
        }

        public static T GetRenderPipelineSettings<T>() where T : class, IRenderPipelineGraphicsSettings
        {
            TryGetRenderPipelineSettings<T>(out var settings);
            return settings;
        }

        public static bool TryGetRenderPipelineSettings<T>(out T settings) where T : class, IRenderPipelineGraphicsSettings
        {
            settings = null;
            if (!TryGetCurrentRenderPipelineGlobalSettings(out var asset)) return false;
            if (asset.TryGet(typeof(T), out var found)) settings = found as T;
            return settings != null;
        }

        internal static event Action<IRenderPipelineGraphicsSettings, string> OnIRenderPipelineGraphicsSettingsChange;

        internal static void CallOnIRenderPipelineGraphicsSettingsChange(IRenderPipelineGraphicsSettings instance, string propertyName)
        {
            if (instance == null || string.IsNullOrEmpty(propertyName)) return;
            OnIRenderPipelineGraphicsSettingsChange?.Invoke(instance, propertyName);
            if (s_Subscribers.TryGetValue(instance.GetType(), out var callbacks))
                foreach (var callback in callbacks.ToArray()) callback(instance, propertyName);
        }

        static readonly Dictionary<Type, List<Action<IRenderPipelineGraphicsSettings, string>>> s_Subscribers = new Dictionary<Type, List<Action<IRenderPipelineGraphicsSettings, string>>>();
        static readonly Dictionary<Delegate, Action<IRenderPipelineGraphicsSettings, string>> s_Wrapped = new Dictionary<Delegate, Action<IRenderPipelineGraphicsSettings, string>>();

        public static void Subscribe<TChild>(Action<TChild, string> callback) where TChild : class, IRenderPipelineGraphicsSettings
        {
            if (callback == null) return;
            Action<IRenderPipelineGraphicsSettings, string> wrapped = (s, p) => callback((TChild)s, p);
            s_Wrapped[callback] = wrapped;
            if (!s_Subscribers.TryGetValue(typeof(TChild), out var list)) s_Subscribers[typeof(TChild)] = list = new List<Action<IRenderPipelineGraphicsSettings, string>>();
            list.Add(wrapped);
        }

        public static void Unsubscribe<TChild>(Action<TChild, string> callback) where TChild : class, IRenderPipelineGraphicsSettings
        {
            if (callback == null || !s_Wrapped.TryGetValue(callback, out var wrapped)) return;
            s_Wrapped.Remove(callback);
            if (s_Subscribers.TryGetValue(typeof(TChild), out var list)) list.Remove(wrapped);
        }

        public static void ForEach(Action<IRenderPipelineGraphicsSettings> callback)
        {
            if (callback == null || !TryGetCurrentRenderPipelineGlobalSettings(out var asset)) return;
            if (!asset.GetSettingsImplementingInterface<IRenderPipelineGraphicsSettings>(out var settings)) return;
            foreach (var s in settings) callback(s);
        }
    }
}

namespace UnityEngine.Rendering
{
    /// <summary>A serialized list of graphics settings (a pipeline's global settings hold them).</summary>
    [Serializable]
    public partial class RenderPipelineGraphicsSettingsCollection
    {
        [SerializeReference]
        List<IRenderPipelineGraphicsSettings> m_List = new List<IRenderPipelineGraphicsSettings>();

        public List<IRenderPipelineGraphicsSettings> settingsList => m_List;
    }
}
