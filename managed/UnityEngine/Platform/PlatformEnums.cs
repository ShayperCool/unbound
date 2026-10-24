// Platform enumerations of the reference assemblies, carried over verbatim (the hand-written
// platform classes use them).
using System;

namespace UnityEngine
{
    public enum AndroidActivityIndicatorStyle
    {
        DontShow = -1,
        Large = 0,
        InversedLarge = 1,
        Small = 2,
        InversedSmall = 3,
    }
    public enum FullScreenMovieControlMode
    {
        Full = 0,
        Minimal = 1,
        CancelOnInput = 2,
        Hidden = 3,
    }
    public enum FullScreenMovieScalingMode
    {
        None = 0,
        AspectFit = 1,
        AspectFill = 2,
        Fill = 3,
    }
    public enum WebCamKind
    {
        Unknown = 0,
        WideAngle = 1,
        Telephoto = 2,
        ColorAndDepth = 3,
        UltraWideAngle = 4,
    }
}

namespace UnityEngine.iOS
{
    public enum ActivityIndicatorStyle
    {
        DontShow = -1,
        [Obsolete("WhiteLarge Activity Indicator has been deprecated by Apple. Use Large instead (UnityUpgradable) -> Large")] WhiteLarge = 0,
        [Obsolete("White Activity Indicator has been deprecated by Apple. Use Medium instead (UnityUpgradable) -> Medium")] White = 1,
        [Obsolete("Gray Activity Indicator has been deprecated by Apple. Use Medium instead (UnityUpgradable) -> Medium")] Gray = 2,
        Medium = 100,
        Large = 101,
    }
    public enum DeviceGeneration
    {
        Unknown = 0,
        iPhone = 1,
        iPhone3G = 2,
        iPhone3GS = 3,
        iPodTouch1Gen = 4,
        iPodTouch2Gen = 5,
        iPodTouch3Gen = 6,
        iPad1Gen = 7,
        iPhone4 = 8,
        iPodTouch4Gen = 9,
        iPad2Gen = 10,
        iPhone4S = 11,
        iPad3Gen = 12,
        iPhone5 = 13,
        iPodTouch5Gen = 14,
        iPadMini1Gen = 15,
        iPad4Gen = 16,
        iPhone5C = 17,
        iPhone5S = 18,
        iPadAir1 = 19,
        iPadMini2Gen = 20,
        iPhone6 = 21,
        iPhone6Plus = 22,
        iPadMini3Gen = 23,
        iPadAir2 = 24,
        iPhone6S = 25,
        iPhone6SPlus = 26,
        iPadPro1Gen = 27,
        iPadMini4Gen = 28,
        iPhoneSE1Gen = 29,
        iPadPro10Inch1Gen = 30,
        iPhone7 = 31,
        iPhone7Plus = 32,
        iPodTouch6Gen = 33,
        iPad5Gen = 34,
        iPadPro2Gen = 35,
        iPadPro10Inch2Gen = 36,
        iPhone8 = 37,
        iPhone8Plus = 38,
        iPhoneX = 39,
        iPhoneXS = 40,
        iPhoneXSMax = 41,
        iPhoneXR = 42,
        iPadPro11Inch = 43,
        iPadPro3Gen = 44,
        iPad6Gen = 45,
        iPadAir3Gen = 46,
        iPadMini5Gen = 47,
        iPhone11 = 48,
        iPhone11Pro = 49,
        iPhone11ProMax = 50,
        iPodTouch7Gen = 51,
        iPad7Gen = 52,
        iPhoneSE2Gen = 53,
        iPadPro11Inch2Gen = 54,
        iPadPro4Gen = 55,
        iPhone12Mini = 56,
        iPhone12 = 57,
        iPhone12Pro = 58,
        iPhone12ProMax = 59,
        iPad8Gen = 60,
        iPadAir4Gen = 61,
        iPad9Gen = 62,
        iPadMini6Gen = 63,
        iPhone13 = 64,
        iPhone13Mini = 65,
        iPhone13Pro = 66,
        iPhone13ProMax = 67,
        iPadPro5Gen = 68,
        iPadPro11Inch3Gen = 69,
        iPhoneSE3Gen = 70,
        iPadAir5Gen = 71,
        iPhone14 = 72,
        iPhone14Plus = 73,
        iPhone14Pro = 74,
        iPhone14ProMax = 75,
        iPadPro6Gen = 76,
        iPadPro11Inch4Gen = 77,
        iPad10Gen = 78,
        iPhone15 = 79,
        iPhone15Plus = 80,
        iPhone15Pro = 81,
        iPhone15ProMax = 82,
        iPhone16 = 83,
        iPhone16Plus = 84,
        iPhone16Pro = 85,
        iPhone16ProMax = 86,
        iPhone16e = 87,
        iPhone17 = 88,
        iPhoneAir = 89,
        iPhone17Pro = 90,
        iPhone17ProMax = 91,
        iPhoneUnknown = 10001,
        iPadUnknown = 10002,
        iPodTouchUnknown = 10003,
    }
    [Flags]
    public enum SystemGestureDeferMode : uint
    {
        None = 0U,
        TopEdge = 1U,
        LeftEdge = 2U,
        BottomEdge = 4U,
        RightEdge = 8U,
        All = 15U,
    }
}

namespace UnityEngine.Android
{
    public enum AndroidColorModeHdr
    {
        Undefined = 0,
        No = 4,
        Yes = 8,
    }
    public enum AndroidColorModeWideColorGamut
    {
        Undefined = 0,
        No = 1,
        Yes = 2,
    }
    public enum AndroidHardwareKeyboardHidden
    {
        Undefined = 0,
        No = 1,
        Yes = 2,
    }
    public enum AndroidKeyboard
    {
        Undefined = 0,
        NoKeys = 1,
        Qwerty = 2,
        _12Key = 3,
    }
    public enum AndroidKeyboardHidden
    {
        Undefined = 0,
        No = 1,
        Yes = 2,
    }
    public enum AndroidNavigation
    {
        Undefined = 0,
        NoNav = 1,
        Dpad = 2,
        TrackBall = 3,
        Wheel = 4,
    }
    public enum AndroidNavigationHidden
    {
        Undefined = 0,
        No = 1,
        Yes = 2,
    }
    public enum AndroidOrientation
    {
        Undefined = 0,
        Portrait = 1,
        Landscape = 2,
    }
    public enum AndroidScreenLayoutDirection
    {
        LTR = 64,
        RTL = 128,
    }
    public enum AndroidScreenLayoutLong
    {
        Undefined = 0,
        No = 16,
        Yes = 32,
    }
    public enum AndroidScreenLayoutRound
    {
        Undefined = 0,
        No = 256,
        Yes = 512,
    }
    public enum AndroidScreenLayoutSize
    {
        Undefined = 0,
        Small = 1,
        Normal = 2,
        Large = 3,
        XLarge = 4,
    }
    public enum AndroidTouchScreen
    {
        Undefined = 0,
        NoTouch = 1,
        Finger = 3,
    }
    public enum AndroidUIModeNight
    {
        Undefined = 0,
        No = 16,
        Yes = 32,
    }
    public enum AndroidUIModeType
    {
        Undefined = 0,
        Normal = 1,
        Desk = 2,
        Car = 3,
        Television = 4,
        Appliance = 5,
        Watch = 6,
        VrHeadset = 7,
    }
}
