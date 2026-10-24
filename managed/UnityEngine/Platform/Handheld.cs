// Phone-only services as the Editor on a desktop answers them: Handheld and iOS.Device report
// "no device" (vibration only logs, movies count as played, activity indicators and settings are
// ignored); the webcam and microphone see no capture devices.
using System;
using System.IO;

namespace UnityEngine
{
    public partial class Handheld
    {
        public static bool PlayFullScreenMovie(string path, Color bgColor, FullScreenMovieControlMode controlMode, FullScreenMovieScalingMode scalingMode) => true;
        public static bool PlayFullScreenMovie(string path, Color bgColor, FullScreenMovieControlMode controlMode) => true;
        public static bool PlayFullScreenMovie(string path, Color bgColor) => true;
        public static bool PlayFullScreenMovie(string path) => true;
        public static void Vibrate() => Debug.Log("iPhoneUtils.Vibrate()");
        public static void SetActivityIndicatorStyle(iOS.ActivityIndicatorStyle style) { }
        public static void SetActivityIndicatorStyle(AndroidActivityIndicatorStyle style) { }
        public static int GetActivityIndicatorStyle() => -1;
        public static void StartActivityIndicator() { }
        public static void StopActivityIndicator() { }
        public static void ClearShaderCache() { }

        [Obsolete("Property Handheld.use32BitDisplayBuffer has been deprecated. Modifying it has no effect, use PlayerSettings instead.")]
        public static bool use32BitDisplayBuffer
        {
            get => true;
            set { }
        }
    }

    public partial struct WebCamDevice
    {
        internal string m_Name;
        internal string m_DepthCameraName;
        internal int m_Flags;
        internal WebCamKind m_Kind;
        internal Resolution[] m_Resolutions;

        public string name => m_Name;
        public bool isFrontFacing => (m_Flags & 1) != 0;
        public WebCamKind kind => m_Kind;
        public string depthCameraName => m_DepthCameraName == "" ? null : m_DepthCameraName;
        public bool isAutoFocusPointSupported => (m_Flags & 2) != 0;
        public Resolution[] availableResolutions => m_Resolutions;
    }

    public sealed partial class WebCamTexture : Texture
    {
        string m_DeviceName;
        int m_RequestedWidth;
        int m_RequestedHeight;
        float m_RequestedFPS;
        Vector2? m_AutoFocusPoint;

        public WebCamTexture(string deviceName, int requestedWidth, int requestedHeight, int requestedFPS)
        {
            m_DeviceName = deviceName;
            m_RequestedWidth = requestedWidth;
            m_RequestedHeight = requestedHeight;
            m_RequestedFPS = requestedFPS;
            m_Width = m_Height = 16;
        }

        public WebCamTexture(string deviceName, int requestedWidth, int requestedHeight) : this(deviceName, requestedWidth, requestedHeight, 0) { }
        public WebCamTexture(string deviceName) : this(deviceName, 0, 0, 0) { }
        public WebCamTexture(int requestedWidth, int requestedHeight, int requestedFPS) : this("", requestedWidth, requestedHeight, requestedFPS) { }
        public WebCamTexture(int requestedWidth, int requestedHeight) : this("", requestedWidth, requestedHeight, 0) { }
        public WebCamTexture() : this("", 0, 0, 0) { }

        /// <summary>No capture device is listed, so there is nothing to start; on a machine that
        /// has a camera the Editor would stream from it, which Unbound does not do yet.</summary>
        public void Play()
        {
            if (HasVideoDevices()) Unbound.Runtime.Stubs.NotImplemented("UnityEngine.WebCamTexture.Play (camera capture)");
        }

        public void Pause() { }
        public void Stop() { }

        static bool HasVideoDevices()
        {
            try { return Directory.GetFiles("/dev", "video*").Length > 0; }
            catch (Exception) { return false; }
        }

        public static WebCamDevice[] devices => Array.Empty<WebCamDevice>();
        public bool isPlaying => false;
        public string deviceName { get => m_DeviceName; set => m_DeviceName = value; }
        public float requestedFPS { get => m_RequestedFPS; set => m_RequestedFPS = value; }
        public int requestedWidth { get => m_RequestedWidth; set => m_RequestedWidth = value; }
        public int requestedHeight { get => m_RequestedHeight; set => m_RequestedHeight = value; }
        public int videoRotationAngle => 0;
        public bool videoVerticallyMirrored => false;
        public bool didUpdateThisFrame => false;
        public Vector2? autoFocusPoint { get => m_AutoFocusPoint; set => m_AutoFocusPoint = value; }
        public bool isDepth => false;
    }

    public sealed partial class Microphone
    {
        /// <summary>A named device that is not listed fails as in Unity; recording from the default
        /// device is not supported yet.</summary>
        public static AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency)
        {
            if (!string.IsNullOrEmpty(deviceName))
                throw new ArgumentException("Couldn't acquire device ID for device name " + deviceName);
            Unbound.Runtime.Stubs.NotImplemented("UnityEngine.Microphone.Start (audio capture)");
            return null;
        }

        public static void End(string deviceName) { }
        public static bool IsRecording(string deviceName) => false;
        public static int GetPosition(string deviceName) => 0;

        public static void GetDeviceCaps(string deviceName, out int minFreq, out int maxFreq)
        {
            minFreq = 0;
            maxFreq = 0;
        }
    }
}

namespace UnityEngine.iOS
{
    public sealed partial class Device
    {
        public static bool RequestStoreReview() => false;
        public static void SetNoBackupFlag(string path) { }
        public static void ResetNoBackupFlag(string path) { }
        public static bool hideHomeButton { get => false; set { } }
        public static bool wantsSoftwareDimming { get => false; set { } }
        public static SystemGestureDeferMode deferSystemGesturesMode { get => SystemGestureDeferMode.None; set { } }
        public static string systemVersion => "";
        public static DeviceGeneration generation => DeviceGeneration.Unknown;
        public static string vendorIdentifier => "";
        public static bool lowPowerModeEnabled => false;
        public static bool advertisingTrackingEnabled => false;
        public static string advertisingIdentifier => "";
        public static bool iosAppOnMac => false;
        public static bool runsOnSimulator => false;
    }
}
