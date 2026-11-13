// Android permissions and the application object as the Editor on a desktop answers them: every
// permission counts as granted, requests do nothing and call nothing back, and there is no
// activity, context or player object.
using System;

namespace UnityEngine.Android
{
    [Unbound.EditorFaithful("measured: tests/projects/platform")]
    public partial struct Permission
    {
        public static bool ShouldShowRequestPermissionRationale(string permission) => true;
        public static bool HasUserAuthorizedPermission(string permission) => true;
        public static void RequestUserPermission(string permission) { }
        public static void RequestUserPermissions(string[] permissions) { }
        public static void RequestUserPermission(string permission, PermissionCallbacks callbacks) { }
        public static void RequestUserPermissions(string[] permissions, PermissionCallbacks callbacks) { }
    }

    /// <summary>The Java callback a permission request reports to; Unity's player calls it with
    /// the permission's name.</summary>
    public partial class PermissionCallbacks : AndroidJavaProxy
    {
        public PermissionCallbacks() : base("com.unity3d.player.IPermissionRequestCallbacks") { }

        public event Action<string> PermissionGranted;
        public event Action<string> PermissionDenied;
        [global::System.@ObsoleteAttribute("Unreliable. Query ShouldShowRequestPermissionRationale and use PermissionDenied event.", false)]
        public event Action<string> PermissionDeniedAndDontAskAgain;
        public event Action<string> PermissionRequestDismissed;

        public override IntPtr Invoke(string methodName, IntPtr javaArgs)
        {
            IntPtr element = AndroidJNI.GetObjectArrayElement(javaArgs, 0);
            string permission = AndroidJNI.GetStringUTFChars(element);
            AndroidJNISafe.DeleteLocalRef(element);
            switch (methodName)
            {
                case "onPermissionGranted": PermissionGranted?.Invoke(permission); break;
                case "onPermissionDenied": PermissionDenied?.Invoke(permission); break;
#pragma warning disable CS0618
                case "onPermissionDeniedAndDontAskAgain":
                    if (PermissionDeniedAndDontAskAgain != null) PermissionDeniedAndDontAskAgain(permission);
                    else PermissionDenied?.Invoke(permission);
                    break;
#pragma warning restore CS0618
                case "onPermissionRequestDismissed": PermissionRequestDismissed?.Invoke(permission); break;
                default: return base.Invoke(methodName, javaArgs);
            }
            return IntPtr.Zero;
        }
    }

    [Unbound.EditorFaithful("measured: tests/projects/platform")]
    public static partial class AndroidApplication
    {
        public static void InvokeOnUIThread(Action action) { }
        public static void InvokeOnUnityMainThread(Action action) { }
        internal static IntPtr UnityPlayerRaw => IntPtr.Zero;
        public static AndroidJavaObject currentContext => null;
        public static AndroidJavaObject currentActivity => null;
        public static AndroidJavaObject unityPlayer => null;
        public static AndroidConfiguration currentConfiguration => null;
#pragma warning disable CS0067 // The configuration never changes in the Editor.
        public static event Action<AndroidConfiguration> onConfigurationChanged;
#pragma warning restore CS0067
    }
}

namespace UnityEngine.Android
{
    /// <summary>The device configuration Android reports (Java's <c>Configuration</c>).</summary>
    public sealed partial class AndroidConfiguration
    {
        public AndroidConfiguration() { }
        public AndroidConfiguration(AndroidConfiguration otherConfiguration) => CopyFrom(otherConfiguration);

        public void CopyFrom(AndroidConfiguration otherConfiguration)
        {
            densityDpi = otherConfiguration.densityDpi;
            fontScale = otherConfiguration.fontScale;
            fontWeightAdjustment = otherConfiguration.fontWeightAdjustment;
            keyboard = otherConfiguration.keyboard;
            hardKeyboardHidden = otherConfiguration.hardKeyboardHidden;
            keyboardHidden = otherConfiguration.keyboardHidden;
            mobileCountryCode = otherConfiguration.mobileCountryCode;
            mobileNetworkCode = otherConfiguration.mobileNetworkCode;
            navigation = otherConfiguration.navigation;
            navigationHidden = otherConfiguration.navigationHidden;
            orientation = otherConfiguration.orientation;
            screenHeightDp = otherConfiguration.screenHeightDp;
            screenWidthDp = otherConfiguration.screenWidthDp;
            smallestScreenWidthDp = otherConfiguration.smallestScreenWidthDp;
            touchScreen = otherConfiguration.touchScreen;
            m_Value_colorModeHdr = otherConfiguration.colorModeHdr;
            m_Value_colorModeWideColorGamut = otherConfiguration.colorModeWideColorGamut;
            m_Value_screenLayoutDirection = otherConfiguration.screenLayoutDirection;
            m_Value_screenLayoutLong = otherConfiguration.screenLayoutLong;
            m_Value_screenLayoutRound = otherConfiguration.screenLayoutRound;
            m_Value_screenLayoutSize = otherConfiguration.screenLayoutSize;
            m_Value_uiModeNight = otherConfiguration.uiModeNight;
            m_Value_uiModeType = otherConfiguration.uiModeType;
        }

        public int densityDpi { get; private set; }
        public float fontScale { get; private set; }
        public int fontWeightAdjustment { get; private set; }
        public AndroidKeyboard keyboard { get; private set; }
        public AndroidHardwareKeyboardHidden hardKeyboardHidden { get; private set; }
        public AndroidKeyboardHidden keyboardHidden { get; private set; }
        public int mobileCountryCode { get; private set; }
        public int mobileNetworkCode { get; private set; }
        public AndroidNavigation navigation { get; private set; }
        public AndroidNavigationHidden navigationHidden { get; private set; }
        public AndroidOrientation orientation { get; private set; }
        public int screenHeightDp { get; private set; }
        public int screenWidthDp { get; private set; }
        public int smallestScreenWidthDp { get; private set; }
        public AndroidTouchScreen touchScreen { get; private set; }
        AndroidColorModeHdr m_Value_colorModeHdr;
        public AndroidColorModeHdr colorModeHdr => m_Value_colorModeHdr;
        AndroidColorModeWideColorGamut m_Value_colorModeWideColorGamut;
        public AndroidColorModeWideColorGamut colorModeWideColorGamut => m_Value_colorModeWideColorGamut;
        AndroidScreenLayoutDirection m_Value_screenLayoutDirection;
        public AndroidScreenLayoutDirection screenLayoutDirection => m_Value_screenLayoutDirection;
        AndroidScreenLayoutLong m_Value_screenLayoutLong;
        public AndroidScreenLayoutLong screenLayoutLong => m_Value_screenLayoutLong;
        AndroidScreenLayoutRound m_Value_screenLayoutRound;
        public AndroidScreenLayoutRound screenLayoutRound => m_Value_screenLayoutRound;
        AndroidScreenLayoutSize m_Value_screenLayoutSize;
        public AndroidScreenLayoutSize screenLayoutSize => m_Value_screenLayoutSize;
        AndroidUIModeNight m_Value_uiModeNight;
        public AndroidUIModeNight uiModeNight => m_Value_uiModeNight;
        AndroidUIModeType m_Value_uiModeType;
        public AndroidUIModeType uiModeType => m_Value_uiModeType;
    }
}
