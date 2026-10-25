using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine.Video
{
    public enum Video3DLayout { No3D = 0, SideBySide3D = 1, OverUnder3D = 2 }
    public enum VideoAspectRatio { NoScaling = 0, FitVertically = 1, FitHorizontally = 2, FitInside = 3, FitOutside = 4, Stretch = 5 }
    public enum VideoAudioOutputMode { None = 0, AudioSource = 1, Direct = 2, APIOnly = 3 }
    public enum VideoRenderMode { CameraFarPlane = 0, CameraNearPlane = 1, RenderTexture = 2, MaterialOverride = 3, APIOnly = 4 }
    public enum VideoSource { VideoClip = 0, Url = 1 }
    public enum VideoTimeReference { Freerun = 0, InternalTime = 1, ExternalTime = 2 }
    public enum VideoTimeSource { AudioDSPTimeSource = 0, GameTimeSource = 1 }
    public enum VideoTimeUpdateMode { DSPTime = 0, GameTime = 1, UnscaledGameTime = 2 }

    /// <summary>A video file of the project (a VideoClipImporter's asset): its properties come from
    /// ffprobe; the VideoPlayer decodes it through ffmpeg.</summary>
    [ClassId(329)]
    public sealed partial class VideoClip : Object
    {
        internal string m_File = "";
        internal string m_OriginalPath = "";
        internal ulong m_FrameCount;
        internal double m_FrameRate;
        internal double m_Length;
        internal uint m_Width, m_Height;
        internal uint m_AspectNumerator = 1, m_AspectDenominator = 1;
        internal ushort m_AudioTracks;

        VideoClip() { }

        public string originalPath => m_OriginalPath;
        public ulong frameCount => m_FrameCount;
        public double frameRate => m_FrameRate;
        public double length => m_Length;
        public uint width => m_Width;
        public uint height => m_Height;
        public uint pixelAspectRatioNumerator => m_AspectNumerator;
        public uint pixelAspectRatioDenominator => m_AspectDenominator;
        public bool sRGB => true;
        public ushort audioTrackCount => m_AudioTracks;

        public ushort GetAudioChannelCount(ushort audioTrackIdx) => 0;
        public uint GetAudioSampleRate(ushort audioTrackIdx) => 0;
        public string GetAudioLanguage(ushort audioTrackIdx) => "";

        /// <summary>A clip of a file, its properties probed.</summary>
        internal static VideoClip Probe(string file, string assetPath)
        {
            var root = JsonDocument.Parse(Native.Call("video-probe", JsonSerializer.Serialize(new { path = file }))).RootElement;
            VideoClip clip;
            using (ObjectRegistry.AsAssets())
                clip = new VideoClip { IsPersistentAsset = true };
            clip.m_File = file;
            clip.m_OriginalPath = assetPath ?? file;
            clip.m_Name = System.IO.Path.GetFileNameWithoutExtension(assetPath ?? file);
            clip.m_Width = root.GetProperty("width").GetUInt32();
            clip.m_Height = root.GetProperty("height").GetUInt32();
            clip.m_FrameRate = root.GetProperty("frameRate").GetDouble();
            clip.m_FrameCount = root.GetProperty("frameCount").GetUInt64();
            clip.m_Length = root.GetProperty("length").GetDouble();
            if (clip.m_FrameCount > 0 && clip.m_FrameRate > 0) clip.m_Length = clip.m_FrameCount / clip.m_FrameRate;
            clip.m_AudioTracks = (ushort)root.GetProperty("audioTracks").GetInt32();
            clip.m_AspectNumerator = root.GetProperty("aspectNumerator").GetUInt32();
            clip.m_AspectDenominator = root.GetProperty("aspectDenominator").GetUInt32();
            return clip;
        }
    }

    /// <summary>
    /// Plays a VideoClip (or a file URL): decoded frame by frame through ffmpeg into its texture,
    /// shown in a RenderTexture, a renderer's material property or nowhere (API only). Measured
    /// against the Editor (tests/projects/video): Prepare loads the clip (prepareCompleted), Pause
    /// shows frame 0, a paused player seeks frame by frame (frameReady, seekCompleted; asking for the
    /// frame it shows does nothing; stepping past the end does nothing), Play advances with the game
    /// time from where it is (started) to the end (loopPointReached), where it stops on the last frame.
    /// </summary>
    [ClassId(328)]
    [RequireComponent(typeof(Transform))]
    public sealed partial class VideoPlayer : Behaviour
    {
        VideoSource m_Source;
        string m_Url = "";
        VideoClip m_Clip;
        VideoRenderMode m_RenderMode = VideoRenderMode.CameraFarPlane;
        Camera m_TargetCamera;
        RenderTexture m_TargetTexture;
        Renderer m_TargetMaterialRenderer;
        string m_TargetMaterialProperty = "_MainTex";
        VideoAspectRatio m_AspectRatio = VideoAspectRatio.FitHorizontally;
        float m_TargetCameraAlpha = 1f;
        Video3DLayout m_TargetCamera3DLayout;
        bool m_WaitForFirstFrame = true, m_PlayOnAwake = true, m_Looping, m_SkipOnDrop = true, m_SendFrameReadyEvents;
        float m_PlaybackSpeed = 1f;
        VideoTimeUpdateMode m_TimeUpdateMode = VideoTimeUpdateMode.GameTime;
        VideoTimeReference m_TimeReference;
        double m_ExternalReferenceTime;
        VideoAudioOutputMode m_AudioOutputMode = VideoAudioOutputMode.AudioSource;
        ushort m_ControlledAudioTrackCount = 1;

        // Playback.
        bool m_Prepared, m_Preparing, m_Playing, m_Paused, m_StartPending;
        long m_Frame = -1;
        double m_Time;
        ulong m_Decoder;
        /// <summary>The frame's colours (VideoDecode's output) and its Y, U and V planes.</summary>
        RenderTexture m_Texture;
        Texture2D[] m_Planes;
        static Material s_Decode;
        /// <summary>Events to raise at the next video update, in order.</summary>
        readonly List<Action> m_Pending = new List<Action>();
        /// <summary>Events to raise at the update after the next (a seek's completion).</summary>
        readonly List<Action> m_PendingNext = new List<Action>();
        string m_File, m_Name;
        uint m_Width, m_Height;
        double m_FrameRate;
        ulong m_FrameCount;
        double m_Length;

        static readonly List<VideoPlayer> s_Active = new List<VideoPlayer>();
        static bool s_Registered;
        static readonly HashSet<string> s_Warned = new HashSet<string>();

        public VideoPlayer() { }

        public delegate void EventHandler(VideoPlayer source);
        public delegate void ErrorEventHandler(VideoPlayer source, string message);
        public delegate void FrameReadyEventHandler(VideoPlayer source, long frameIdx);
        public delegate void TimeEventHandler(VideoPlayer source, double seconds);

        public event EventHandler prepareCompleted;
        public event EventHandler loopPointReached;
        public event EventHandler started;
        public event EventHandler frameDropped;
        public event ErrorEventHandler errorReceived;
        public event EventHandler seekCompleted;
        public event TimeEventHandler clockResyncOccurred;
        public event FrameReadyEventHandler frameReady;
        internal static event Action<string> analyticsSent;

        public VideoSource source { get => m_Source; set => m_Source = value; }
        public string url { get => m_Url; set { m_Url = value ?? ""; Release(); } }
        public VideoClip clip { get => m_Clip; set { if (m_Clip != value) { m_Clip = value; Release(); } } }
        public VideoRenderMode renderMode { get => m_RenderMode; set => m_RenderMode = value; }
        public Camera targetCamera { get => m_TargetCamera; set => m_TargetCamera = value; }
        public RenderTexture targetTexture { get => m_TargetTexture; set => m_TargetTexture = value; }
        public Renderer targetMaterialRenderer { get => m_TargetMaterialRenderer; set => m_TargetMaterialRenderer = value; }
        public string targetMaterialProperty { get => m_TargetMaterialProperty; set => m_TargetMaterialProperty = value; }
        internal string effectiveTargetMaterialProperty => string.IsNullOrEmpty(m_TargetMaterialProperty) ? "_MainTex" : m_TargetMaterialProperty;
        public VideoAspectRatio aspectRatio { get => m_AspectRatio; set => m_AspectRatio = value; }
        public float targetCameraAlpha { get => m_TargetCameraAlpha; set => m_TargetCameraAlpha = Mathf.Clamp01(value); }
        public Video3DLayout targetCamera3DLayout { get => m_TargetCamera3DLayout; set => m_TargetCamera3DLayout = value; }
        public Texture texture => m_Prepared ? m_Texture : null;
        public bool isPrepared => m_Prepared;
        public bool waitForFirstFrame { get => m_WaitForFirstFrame; set => m_WaitForFirstFrame = value; }
        public bool playOnAwake { get => m_PlayOnAwake; set => m_PlayOnAwake = value; }
        public bool isPlaying => m_Playing && !m_Paused;
        public bool isPaused => m_Paused;
        public bool canSetTime => true;
        public bool canStep => true;
        public bool canSetPlaybackSpeed => true;
        public bool canSetTimeUpdateMode => true;
        public bool canSetSkipOnDrop => true;
        public bool canSetDirectAudioVolume => true;
        public float playbackSpeed { get => m_PlaybackSpeed; set => m_PlaybackSpeed = Mathf.Max(0f, value); }
        public bool isLooping { get => m_Looping; set => m_Looping = value; }
        public VideoTimeUpdateMode timeUpdateMode { get => m_TimeUpdateMode; set => m_TimeUpdateMode = value; }
        public VideoTimeReference timeReference { get => m_TimeReference; set => m_TimeReference = value; }
        public double externalReferenceTime { get => m_ExternalReferenceTime; set => m_ExternalReferenceTime = value; }
        public bool skipOnDrop { get => m_SkipOnDrop; set => m_SkipOnDrop = value; }
        public ulong frameCount => m_Prepared ? m_FrameCount : 0;
        public float frameRate => m_Prepared ? (float)m_FrameRate : 0f;
        public double length => m_Prepared ? m_Length : 0;
        public uint width => m_Prepared ? m_Width : 0;
        public uint height => m_Prepared ? m_Height : 0;
        public uint pixelAspectRatioNumerator => m_Clip != null ? m_Clip.pixelAspectRatioNumerator : 1;
        public uint pixelAspectRatioDenominator => m_Clip != null ? m_Clip.pixelAspectRatioDenominator : 1;
        public ushort audioTrackCount => m_Clip != null ? m_Clip.audioTrackCount : (ushort)0;
        public static ushort controlledAudioTrackMaxCount => 64;
        public ushort controlledAudioTrackCount { get => m_ControlledAudioTrackCount; set => m_ControlledAudioTrackCount = value; }
        public VideoAudioOutputMode audioOutputMode { get => m_AudioOutputMode; set => m_AudioOutputMode = value; }
        public bool sendFrameReadyEvents { get => m_SendFrameReadyEvents; set => m_SendFrameReadyEvents = value; }
        public double clockTime => m_Time;

        [Obsolete("VideoPlayer.canSetTimeSource is deprecated. Use canSetTimeUpdateMode instead. (UnityUpgradable) -> canSetTimeUpdateMode")]
        public bool canSetTimeSource => true;

        [Obsolete("VideoPlayer.timeSource is deprecated. Use timeUpdateMode instead. (UnityUpgradable) -> timeUpdateMode")]
        public VideoTimeSource timeSource
        {
            get => m_TimeUpdateMode == VideoTimeUpdateMode.DSPTime ? VideoTimeSource.AudioDSPTimeSource : VideoTimeSource.GameTimeSource;
            set => m_TimeUpdateMode = value == VideoTimeSource.AudioDSPTimeSource ? VideoTimeUpdateMode.DSPTime : VideoTimeUpdateMode.GameTime;
        }

        public double time
        {
            get => m_Time;
            set => frame = m_FrameRate > 0 ? (long)Math.Floor(value * m_FrameRate + 1e-6) : 0;
        }

        public long frame
        {
            get => m_Frame;
            set
            {
                if (!m_Prepared) return;
                long target = Math.Clamp(value, 0, (long)Math.Max(1, m_FrameCount) - 1);
                if (target == m_Frame) return;
                ShowFrame(target);
                m_Time = target / m_FrameRate;
                // A seek completes an update later (after `started` of a Play right after it).
                m_PendingNext.Add(() => seekCompleted?.Invoke(this));
            }
        }

        // Audio: not played yet (the clip's sound is not decoded).
        public string GetAudioLanguageCode(ushort trackIndex) => "";
        public ushort GetAudioChannelCount(ushort trackIndex) => 0;
        public uint GetAudioSampleRate(ushort trackIndex) => 0;
        readonly Dictionary<ushort, (bool Enabled, float Volume, bool Mute, AudioSource Source)> m_Tracks = new Dictionary<ushort, (bool, float, bool, AudioSource)>();
        (bool Enabled, float Volume, bool Mute, AudioSource Source) Track(ushort i) => m_Tracks.TryGetValue(i, out var t) ? t : (true, 1f, false, null);
        public void EnableAudioTrack(ushort trackIndex, bool enabled) { var t = Track(trackIndex); t.Enabled = enabled; m_Tracks[trackIndex] = t; }
        public bool IsAudioTrackEnabled(ushort trackIndex) => Track(trackIndex).Enabled;
        public float GetDirectAudioVolume(ushort trackIndex) => Track(trackIndex).Volume;
        public void SetDirectAudioVolume(ushort trackIndex, float volume) { var t = Track(trackIndex); t.Volume = volume; m_Tracks[trackIndex] = t; }
        public bool GetDirectAudioMute(ushort trackIndex) => Track(trackIndex).Mute;
        public void SetDirectAudioMute(ushort trackIndex, bool mute) { var t = Track(trackIndex); t.Mute = mute; m_Tracks[trackIndex] = t; }
        public AudioSource GetTargetAudioSource(ushort trackIndex) => Track(trackIndex).Source;
        public void SetTargetAudioSource(ushort trackIndex, AudioSource source) { var t = Track(trackIndex); t.Source = source; m_Tracks[trackIndex] = t; }

        static void Warn(string message)
        {
            if (s_Warned.Add(message)) Debug.LogWarning("Unbound: " + message);
        }

        // ------------------------------------------------------------ control

        public void Prepare()
        {
            if (m_Prepared || m_Preparing) return;
            Track();
            if (!Open()) return;
            m_Preparing = true;
        }

        public void Play()
        {
            if (!m_Prepared)
            {
                Prepare();
                if (!m_Preparing) return;
            }
            if (m_Playing && !m_Paused) return;
            m_Playing = true;
            m_Paused = false;
            m_StartPending = true;
        }

        public void Pause()
        {
            if (!m_Prepared)
            {
                Prepare();
                if (!m_Preparing) return;
            }
            m_Paused = true;
            if (m_Prepared && m_Frame < 0) ShowFrame(0);
        }

        public void Stop()
        {
            m_Playing = m_Paused = false;
            m_StartPending = false;
            Release();
        }

        public void StepForward()
        {
            if (!m_Prepared) return;
            m_Paused = true;
            m_Playing = false;
            if (m_Frame + 1 < (long)m_FrameCount) ShowFrame(m_Frame + 1);
        }

        /// <summary>Finds the clip's file and opens a decoder; false (an error) when it cannot.</summary>
        bool Open()
        {
            try
            {
                if (m_Source == VideoSource.Url)
                {
                    var path = m_Url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? new Uri(m_Url).LocalPath : m_Url;
                    m_File = path;
                    var probed = VideoClip.Probe(path, null);
                    Adopt(probed);
                    DestroyImmediate(probed, true);
                }
                else
                {
                    if (m_Clip == null) return Fail("VideoPlayer cannot play: no VideoClip is set.");
                    m_File = m_Clip.m_File;
                    Adopt(m_Clip);
                }
                if (m_AudioOutputMode != VideoAudioOutputMode.None && audioTrackCount > 0)
                    Warn($"the sound of video '{m_Name}' is not played yet (only its pictures).");
                m_Decoder = JsonDocument.Parse(Native.Call("video-open", JsonSerializer.Serialize(new { path = m_File, width = m_Width, height = m_Height, fps = m_FrameRate })))
                    .RootElement.GetProperty("id").GetUInt64();
                return true;
            }
            catch (Exception e)
            {
                return Fail($"VideoPlayer cannot play {m_File}: {e.Message}");
            }
        }

        void Adopt(VideoClip clip)
        {
            m_Name = clip.name;
            m_Width = clip.m_Width;
            m_Height = clip.m_Height;
            m_FrameRate = clip.m_FrameRate > 0 ? clip.m_FrameRate : 30;
            m_FrameCount = clip.m_FrameCount;
            m_Length = clip.m_Length;
        }

        bool Fail(string message)
        {
            Debug.LogError(message, this);
            m_Pending.Add(() => errorReceived?.Invoke(this, message));
            Track();
            return false;
        }

        void Release()
        {
            if (m_Decoder != 0)
            {
                try { Native.Call("video-close", JsonSerializer.Serialize(new { id = m_Decoder })); } catch (Exception) { }
                m_Decoder = 0;
            }
            m_Prepared = m_Preparing = false;
            m_Frame = -1;
            m_Time = 0;
        }

        /// <summary>Decodes a frame into the texture and shows it in the target.</summary>
        void ShowFrame(long n)
        {
            if (m_Decoder == 0) return;
            int width = (int)Math.Max(1, m_Width), height = (int)Math.Max(1, m_Height);
            if (m_Planes == null)
            {
                Texture2D Plane(string name, int w, int h)
                {
                    var plane = new Texture2D(w, h, TextureFormat.Alpha8, false)
                    {
                        name = name, hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                    };
                    plane.Apply(false, true);
                    return plane;
                }
                m_Planes = new[]
                {
                    Plane("VideoY", width, height),
                    Plane("VideoU", (width + 1) / 2, (height + 1) / 2),
                    Plane("VideoV", (width + 1) / 2, (height + 1) / 2),
                };
            }
            if (m_Texture == null)
            {
                m_Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = "VideoTexture", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                m_Texture.Create();
            }
            bool decoded;
            try
            {
                var handles = new[] { m_Planes[0].EnsureUploaded(), m_Planes[1].EnsureUploaded(), m_Planes[2].EnsureUploaded() };
                decoded = JsonDocument.Parse(Native.Call("video-frame", JsonSerializer.Serialize(new { id = m_Decoder, frame = n, handles })))
                    .RootElement.GetProperty("decoded").GetBoolean();
            }
            catch (Exception e)
            {
                Fail($"VideoPlayer could not decode frame {n} of {m_File}: {e.Message}");
                return;
            }
            if (!decoded) return;
            Decode();
            m_Frame = n;
            Present();
            if (m_SendFrameReadyEvents)
            {
                long shown = n;
                m_Pending.Add(() => frameReady?.Invoke(this, shown));
            }
        }

        /// <summary>The planes into colours with Unity's own conversion (Hidden/VideoDecode's
        /// YCbCr_To_RGB1 pass: BT.601 limited range, chroma sampled per 2x2 pixels).</summary>
        void Decode()
        {
            if (s_Decode == null)
            {
                s_Decode = new Material(Shader.Find("Hidden/VideoDecode")) { hideFlags = HideFlags.HideAndDontSave };
                if (QualitySettings.activeColorSpace == ColorSpace.Linear) s_Decode.EnableKeyword("ADJUST_TO_LINEARSPACE");
            }
            s_Decode.SetTexture("_SecondTex", m_Planes[1]);
            s_Decode.SetTexture("_ThirdTex", m_Planes[2]);
            Graphics.Blit(m_Planes[0], m_Texture, s_Decode, 0);
        }

        /// <summary>The current frame in the render target or the renderer's material property.</summary>
        void Present()
        {
            switch (m_RenderMode)
            {
                case VideoRenderMode.RenderTexture when m_TargetTexture != null:
                    BlitAspect(m_Texture, m_TargetTexture);
                    break;
                case VideoRenderMode.MaterialOverride:
                {
                    var renderer = m_TargetMaterialRenderer != null ? m_TargetMaterialRenderer : GetComponent<Renderer>();
                    if (renderer == null) break;
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    block.SetTexture(effectiveTargetMaterialProperty, m_Texture);
                    renderer.SetPropertyBlock(block);
                    break;
                }
                case VideoRenderMode.CameraFarPlane:
                case VideoRenderMode.CameraNearPlane:
                    if (GraphicsSettings.currentRenderPipeline != null)
                        Warn($"VideoPlayer render mode {m_RenderMode} is not supported under a render pipeline asset yet; use RenderTexture or MaterialOverride (video '{m_Name}').");
                    else if (FrameBuilder.CameraPlanes == null)
                        FrameBuilder.CameraPlanes = DrawCameraPlanes;
                    break;
            }
        }

        static Mesh s_Quad;
        Material m_Composite;

        /// <summary>The players showing on a camera's far or near plane: their frames on a quad there
        /// (Hidden/VideoComposite: blended by targetCameraAlpha, writing depth), placed and sized as
        /// the aspect ratio mode fits the frame to the view (the space around shows the scene).</summary>
        static void DrawCameraPlanes(Camera camera, bool near)
        {
            var mode = near ? VideoRenderMode.CameraNearPlane : VideoRenderMode.CameraFarPlane;
            foreach (var player in s_Active)
            {
                if (!player || player.m_RenderMode != mode || player.m_TargetCamera != camera || player.m_Frame < 0 || player.m_Texture == null) continue;
                if (!player.isActiveAndEnabled) continue;
                player.DrawOn(camera, near);
            }
        }

        void DrawOn(Camera camera, bool near)
        {
            if (s_Quad == null)
            {
                using (ObjectRegistry.AsAssets())
                    s_Quad = new Mesh
                    {
                        name = "Unbound Video Plane",
                        vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) },
                        uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) },
                        triangles = new[] { 0, 2, 1, 0, 3, 2 },
                    };
            }
            m_Composite ??= new Material(Shader.Find("Hidden/VideoComposite")) { hideFlags = HideFlags.HideAndDontSave };
            m_Composite.SetTexture("_MainTex", m_Texture);
            m_Composite.SetFloat("_AlphaParam", m_TargetCameraAlpha);
            // Just inside the plane, so the depth test keeps it (the far plane: behind everything).
            float distance = near ? camera.nearClipPlane * 1.001f : camera.farClipPlane * 0.999f;
            float viewHeight = camera.orthographic ? 2f * camera.orthographicSize : 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float viewWidth = viewHeight * camera.aspect;
            var scale = AspectScale(viewWidth, viewHeight);
            var transform = camera.transform;
            var model = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one)
                * Matrix4x4.TRS(new Vector3(0f, 0f, distance), Quaternion.identity, new Vector3(viewWidth * scale.x, viewHeight * scale.y, 1f));
            GfxStream.Draw(s_Quad.Sync(), 0, m_Composite.Sync(), 0, model, Color.white);
        }

        /// <summary>The frame into the target as the aspect ratio mode places it (black around it).</summary>
        void BlitAspect(Texture source, RenderTexture target)
        {
            var scale = AspectScale(target.width, target.height);
            if (Mathf.Approximately(scale.x, 1f) && Mathf.Approximately(scale.y, 1f))
            {
                Graphics.Blit(source, target);
                return;
            }
            // The frame covers the middle of the target: sampled with the inverse scale around the center.
            var inverse = new Vector2(1f / scale.x, 1f / scale.y);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(false, true, Color.black);
            RenderTexture.active = previous;
            Graphics.Blit(source, target, inverse, (Vector2.one - inverse) * 0.5f);
        }

        /// <summary>The frame's size in a target of the given size, as fractions of it, by the aspect
        /// ratio mode.</summary>
        Vector2 AspectScale(float tw, float th)
        {
            float sw = Math.Max(1, m_Width), sh = Math.Max(1, m_Height);
            Vector2 scale = Vector2.one;
            switch (m_AspectRatio)
            {
                case VideoAspectRatio.FitHorizontally: scale = new Vector2(1f, (sh / sw) * (tw / th)); break;
                case VideoAspectRatio.FitVertically: scale = new Vector2((sw / sh) * (th / tw), 1f); break;
                case VideoAspectRatio.FitInside:
                {
                    float s = Math.Min(tw / sw, th / sh);
                    scale = new Vector2(sw * s / tw, sh * s / th);
                    break;
                }
                case VideoAspectRatio.FitOutside:
                {
                    float s = Math.Max(tw / sw, th / sh);
                    scale = new Vector2(sw * s / tw, sh * s / th);
                    break;
                }
                case VideoAspectRatio.NoScaling: scale = new Vector2(sw / tw, sh / th); break;
            }
            return scale;
        }

        // ------------------------------------------------------------ the update

        void Track()
        {
            if (!s_Active.Contains(this)) s_Active.Add(this);
            if (!s_Registered)
            {
                s_Registered = true;
                PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateVideo), UpdateAll);
            }
        }

        static void UpdateAll()
        {
            foreach (var player in s_Active.ToArray())
            {
                if (!player) { s_Active.Remove(player); continue; }
                try { player.UpdatePlayback(); }
                catch (Exception e) { Debug.LogException(e, player); }
            }
        }

        void UpdatePlayback()
        {
            if (m_Preparing)
            {
                m_Preparing = false;
                m_Prepared = true;
                m_Pending.Add(() => prepareCompleted?.Invoke(this));
                if (m_Paused && m_Frame < 0) ShowFrame(0);
            }
            if (m_Prepared && m_Playing && !m_Paused)
            {
                if (m_StartPending)
                {
                    m_StartPending = false;
                    if (m_Frame < 0) ShowFrame(0);
                    m_Pending.Add(() => started?.Invoke(this));
                }
                else
                {
                    float dt = m_TimeUpdateMode == VideoTimeUpdateMode.UnscaledGameTime ? Time.unscaledDeltaTime : Time.deltaTime;
                    m_Time += dt * m_PlaybackSpeed;
                    if (m_Time >= m_Length)
                    {
                        m_Pending.Add(() => loopPointReached?.Invoke(this));
                        if (m_Looping)
                        {
                            m_Time %= Math.Max(m_Length, 1e-6);
                            ShowFrame((long)Math.Floor(m_Time * m_FrameRate + 1e-6));
                        }
                        else
                        {
                            m_Time = m_Length;
                            m_Playing = false;
                            if (m_Frame != (long)m_FrameCount - 1) ShowFrame((long)m_FrameCount - 1);
                        }
                    }
                    else
                    {
                        long target = Math.Min((long)Math.Floor(m_Time * m_FrameRate + 1e-6), (long)m_FrameCount - 1);
                        if (target != m_Frame) ShowFrame(target);
                    }
                }
            }
            if (m_Pending.Count > 0)
            {
                var events = m_Pending.ToArray();
                m_Pending.Clear();
                foreach (var e in events) e();
            }
            if (m_PendingNext.Count > 0)
            {
                m_Pending.AddRange(m_PendingNext);
                m_PendingNext.Clear();
            }
        }

        // ------------------------------------------------------------ lifecycle

        internal override void OnEngineEnable()
        {
            Track();
            // Play On Awake starts a player that has something to play.
            bool hasSource = m_Source == VideoSource.Url ? !string.IsNullOrEmpty(m_Url) : m_Clip != null;
            if (m_PlayOnAwake && !m_Playing && hasSource) Play();
        }

        internal override void OnEngineDisable()
        {
            Stop();
            s_Active.Remove(this);
        }

        internal override void OnEngineDestroy()
        {
            Stop();
            s_Active.Remove(this);
            if (m_Texture != null) DestroyImmediate(m_Texture);
            if (m_Composite != null) DestroyImmediate(m_Composite);
            if (m_Planes != null)
                foreach (var plane in m_Planes) DestroyImmediate(plane);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            T Ref<T>(string key) where T : Object
            {
                var r = body.Ref(key);
                return r.IsNull ? null : resolver.Resolve(r, typeof(T)) as T;
            }
            m_Clip = Ref<VideoClip>("m_VideoClip");
            m_TargetCamera = Ref<Camera>("m_TargetCamera");
            m_TargetTexture = Ref<RenderTexture>("m_TargetTexture");
            m_TargetMaterialRenderer = Ref<Renderer>("m_TargetMaterialRenderer");
            m_TargetMaterialProperty = body.Str("m_TargetMaterialProperty", "_MainTex");
            m_TargetCameraAlpha = body.Float("m_TargetCameraAlpha", 1f);
            m_TargetCamera3DLayout = (Video3DLayout)body.Int("m_TargetCamera3DLayout");
            m_TimeReference = (VideoTimeReference)body.Int("m_TimeReference");
            m_RenderMode = (VideoRenderMode)body.Int("m_RenderMode");
            m_AspectRatio = (VideoAspectRatio)body.Int("m_AspectRatio", 2);
            m_Source = (VideoSource)body.Int("m_DataSource");
            m_TimeUpdateMode = (VideoTimeUpdateMode)body.Int("m_TimeUpdateMode", 1);
            m_PlaybackSpeed = body.Float("m_PlaybackSpeed", 1f);
            m_AudioOutputMode = (VideoAudioOutputMode)body.Int("m_AudioOutputMode", 1);
            m_Url = body.Str("m_Url");
            m_ControlledAudioTrackCount = (ushort)body.Int("m_ControlledAudioTrackCount", 1);
            m_PlayOnAwake = body.Bool("m_PlayOnAwake", true);
            m_SkipOnDrop = body.Bool("m_SkipOnDrop", true);
            m_Looping = body.Bool("m_Looping");
            m_WaitForFirstFrame = body.Bool("m_WaitForFirstFrame", true);
            m_SendFrameReadyEvents = body.Bool("m_FrameReadyEventEnabled");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (VideoPlayer)source;
            m_Clip = remap(s.m_Clip) as VideoClip;
            m_TargetCamera = remap(s.m_TargetCamera) as Camera;
            m_TargetTexture = remap(s.m_TargetTexture) as RenderTexture;
            m_TargetMaterialRenderer = remap(s.m_TargetMaterialRenderer) as Renderer;
            m_TargetMaterialProperty = s.m_TargetMaterialProperty;
            m_TargetCameraAlpha = s.m_TargetCameraAlpha;
            m_TargetCamera3DLayout = s.m_TargetCamera3DLayout;
            m_TimeReference = s.m_TimeReference;
            m_RenderMode = s.m_RenderMode;
            m_AspectRatio = s.m_AspectRatio;
            m_Source = s.m_Source;
            m_TimeUpdateMode = s.m_TimeUpdateMode;
            m_PlaybackSpeed = s.m_PlaybackSpeed;
            m_AudioOutputMode = s.m_AudioOutputMode;
            m_Url = s.m_Url;
            m_ControlledAudioTrackCount = s.m_ControlledAudioTrackCount;
            m_PlayOnAwake = s.m_PlayOnAwake;
            m_SkipOnDrop = s.m_SkipOnDrop;
            m_Looping = s.m_Looping;
            m_WaitForFirstFrame = s.m_WaitForFirstFrame;
            m_SendFrameReadyEvents = s.m_SendFrameReadyEvents;
        }
    }
}
