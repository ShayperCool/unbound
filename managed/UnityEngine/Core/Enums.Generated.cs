// Generated from the metadata of Unity 6000.0's reference assemblies: enum names and values are
// facts of the API that compiled game code inlines, so they must match exactly. Do not edit by hand;
// regenerate with the enum dumper (see docs/ARCHITECTURE.md).
using System;

namespace UnityEngine
{
    [Flags]
    public enum AdditionalCanvasShaderChannels
    {
        None = 0,
        TexCoord1 = 1,
        TexCoord2 = 2,
        TexCoord3 = 4,
        Normal = 8,
        Tangent = 16,
    }

    public enum AnimationBlendMode
    {
        Blend = 0,
        Additive = 1,
    }

    public enum AnimationCullingType
    {
        AlwaysAnimate = 0,
        BasedOnRenderers = 1,
        [Obsolete] BasedOnClipBounds = 2,
        [Obsolete] BasedOnUserBounds = 3,
    }

    public enum AnimatorControllerParameterType
    {
        Float = 1,
        Int = 3,
        Bool = 4,
        Trigger = 9,
    }

    public enum AnimatorCullingMode
    {
        AlwaysAnimate = 0,
        CullUpdateTransforms = 1,
        CullCompletely = 2,
        [Obsolete] BasedOnRenderers = 1,
    }

    public enum AnimatorRecorderMode
    {
        Offline = 0,
        Playback = 1,
        Record = 2,
    }

    public enum AnimatorUpdateMode
    {
        Normal = 0,
        Fixed = 1,
        UnscaledTime = 2,
        [Obsolete] AnimatePhysics = 1,
    }

    public enum AnisotropicFiltering
    {
        Disable = 0,
        Enable = 1,
        ForceEnable = 2,
    }

    public enum ApplicationInstallMode
    {
        Unknown = 0,
        Store = 1,
        DeveloperBuild = 2,
        Adhoc = 3,
        Enterprise = 4,
        Editor = 5,
    }

    public enum ApplicationMemoryUsage
    {
        Unknown = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4,
    }

    public enum ApplicationSandboxType
    {
        Unknown = 0,
        NotSandboxed = 1,
        Sandboxed = 2,
        SandboxBroken = 3,
    }

    public enum ArticulationJointType
    {
        FixedJoint = 0,
        PrismaticJoint = 1,
        RevoluteJoint = 2,
        SphericalJoint = 3,
    }

    public enum AudioClipLoadType
    {
        DecompressOnLoad = 0,
        CompressedInMemory = 1,
        Streaming = 2,
    }

    public enum AudioCompressionFormat
    {
        PCM = 0,
        Vorbis = 1,
        ADPCM = 2,
        MP3 = 3,
        VAG = 4,
        HEVAG = 5,
        XMA = 6,
        AAC = 7,
        GCADPCM = 8,
        ATRAC9 = 9,
    }

    public enum AudioDataLoadState
    {
        Unloaded = 0,
        Loading = 1,
        Loaded = 2,
        Failed = 3,
    }

    public enum AudioReverbPreset
    {
        Off = 0,
        Generic = 1,
        PaddedCell = 2,
        Room = 3,
        Bathroom = 4,
        Livingroom = 5,
        Stoneroom = 6,
        Auditorium = 7,
        Concerthall = 8,
        Cave = 9,
        Arena = 10,
        Hangar = 11,
        CarpetedHallway = 12,
        Hallway = 13,
        StoneCorridor = 14,
        Alley = 15,
        Forest = 16,
        City = 17,
        Mountains = 18,
        Quarry = 19,
        Plain = 20,
        ParkingLot = 21,
        SewerPipe = 22,
        Underwater = 23,
        Drugged = 24,
        Dizzy = 25,
        Psychotic = 26,
        User = 27,
    }

    public enum AudioRolloffMode
    {
        Logarithmic = 0,
        Linear = 1,
        Custom = 2,
    }

    public enum AudioSpeakerMode
    {
        [Obsolete] Raw = 0,
        Mono = 1,
        Stereo = 2,
        Quad = 3,
        Surround = 4,
        Mode5point1 = 5,
        Mode7point1 = 6,
        Prologic = 7,
    }

    public enum AudioType
    {
        UNKNOWN = 0,
        ACC = 1,
        AIFF = 2,
        IT = 10,
        MOD = 12,
        MPEG = 13,
        OGGVORBIS = 14,
        S3M = 17,
        WAV = 20,
        XM = 21,
        XMA = 22,
        VAG = 23,
        AUDIOQUEUE = 24,
    }

    public enum AudioVelocityUpdateMode
    {
        Auto = 0,
        Fixed = 1,
        Dynamic = 2,
    }

    public enum AvatarIKGoal
    {
        LeftFoot = 0,
        RightFoot = 1,
        LeftHand = 2,
        RightHand = 3,
    }

    public enum AvatarTarget
    {
        Root = 0,
        Body = 1,
        LeftFoot = 2,
        RightFoot = 3,
        LeftHand = 4,
        RightHand = 5,
    }

    public enum BatteryStatus
    {
        Unknown = 0,
        Charging = 1,
        Discharging = 2,
        NotCharging = 3,
        Full = 4,
    }

    public enum BlendWeights
    {
        [Obsolete] OneBone = 1,
        [Obsolete] TwoBones = 2,
        [Obsolete] FourBones = 4,
    }

    public enum CameraClearFlags
    {
        Skybox = 1,
        Color = 2,
        SolidColor = 2,
        Depth = 3,
        Nothing = 4,
    }

    [Flags]
    public enum CameraType
    {
        Game = 1,
        SceneView = 2,
        Preview = 4,
        VR = 8,
        Reflection = 16,
    }

    public enum CapsuleDirection2D
    {
        Vertical = 0,
        Horizontal = 1,
    }

    public enum CollisionDetectionMode
    {
        Discrete = 0,
        Continuous = 1,
        ContinuousDynamic = 2,
        ContinuousSpeculative = 3,
    }

    public enum CollisionDetectionMode2D
    {
        [Obsolete] None = 0,
        Discrete = 0,
        Continuous = 1,
    }


    public enum ComputeBufferMode
    {
        Immutable = 0,
        Dynamic = 1,
        [Obsolete] Circular = 2,
        [Obsolete] StreamOut = 3,
        SubUpdates = 4,
    }

    [Flags]
    public enum ComputeBufferType
    {
        Default = 0,
        Raw = 1,
        Append = 2,
        Counter = 4,
        Constant = 8,
        Structured = 16,
        [Obsolete] DrawIndirect = 256,
        IndirectArguments = 256,
        [Obsolete] GPUMemory = 512,
    }

    public enum ConfigurableJointMotion
    {
        Locked = 0,
        Limited = 1,
        Free = 2,
    }

    public enum CubemapFace
    {
        Unknown = -1,
        PositiveX = 0,
        NegativeX = 1,
        PositiveY = 2,
        NegativeY = 3,
        PositiveZ = 4,
        NegativeZ = 5,
    }

    public enum CursorLockMode
    {
        None = 0,
        Locked = 1,
        Confined = 2,
    }

    public enum CursorMode
    {
        Auto = 0,
        ForceSoftware = 1,
    }

    [Flags]
    public enum DepthTextureMode
    {
        None = 0,
        Depth = 1,
        DepthNormals = 2,
        MotionVectors = 4,
    }

    public enum DeviceOrientation
    {
        Unknown = 0,
        Portrait = 1,
        PortraitUpsideDown = 2,
        LandscapeLeft = 3,
        LandscapeRight = 4,
        FaceUp = 5,
        FaceDown = 6,
    }

    public enum DeviceType
    {
        Unknown = 0,
        Handheld = 1,
        Console = 2,
        Desktop = 3,
    }

    public enum DurationUnit
    {
        Fixed = 0,
        Normalized = 1,
    }

    public enum EffectorSelection2D
    {
        Rigidbody = 0,
        Collider = 1,
    }

    [Flags]
    public enum EventModifiers
    {
        None = 0,
        Shift = 1,
        Control = 2,
        Alt = 4,
        Command = 8,
        Numeric = 16,
        CapsLock = 32,
        FunctionKey = 64,
    }

    public enum EventType
    {
        MouseDown = 0,
        MouseUp = 1,
        MouseMove = 2,
        MouseDrag = 3,
        KeyDown = 4,
        KeyUp = 5,
        ScrollWheel = 6,
        Repaint = 7,
        Layout = 8,
        DragUpdated = 9,
        DragPerform = 10,
        DragExited = 15,
        Ignore = 11,
        Used = 12,
        ValidateCommand = 13,
        ExecuteCommand = 14,
        ContextClick = 16,
        MouseEnterWindow = 20,
        MouseLeaveWindow = 21,
        TouchDown = 30,
        TouchUp = 31,
        TouchMove = 32,
        TouchEnter = 33,
        TouchLeave = 34,
        TouchStationary = 35,
        [Obsolete] mouseDown = 0,
        [Obsolete] mouseUp = 1,
        [Obsolete] mouseMove = 2,
        [Obsolete] mouseDrag = 3,
        [Obsolete] keyDown = 4,
        [Obsolete] keyUp = 5,
        [Obsolete] scrollWheel = 6,
        [Obsolete] repaint = 7,
        [Obsolete] layout = 8,
        [Obsolete] dragUpdated = 9,
        [Obsolete] dragPerform = 10,
        [Obsolete] ignore = 11,
        [Obsolete] used = 12,
    }

    public enum FFTWindow
    {
        Rectangular = 0,
        Triangle = 1,
        Hamming = 2,
        Hanning = 3,
        Blackman = 4,
        BlackmanHarris = 5,
    }

    public enum FilterMode
    {
        Point = 0,
        Bilinear = 1,
        Trilinear = 2,
    }

    public enum FindObjectsInactive
    {
        Exclude = 0,
        Include = 1,
    }

    public enum FindObjectsSortMode
    {
        None = 0,
        InstanceID = 1,
    }

    public enum FocusType
    {
        [Obsolete] Native = 0,
        Keyboard = 1,
        Passive = 2,
    }

    public enum FogMode
    {
        Linear = 1,
        Exponential = 2,
        ExponentialSquared = 3,
    }

    public enum FontStyle
    {
        Normal = 0,
        Bold = 1,
        Italic = 2,
        BoldAndItalic = 3,
    }

    public enum ForceMode
    {
        Force = 0,
        Acceleration = 5,
        Impulse = 1,
        VelocityChange = 2,
    }

    public enum ForceMode2D
    {
        Force = 0,
        Impulse = 1,
    }

    public enum FullScreenMode
    {
        ExclusiveFullScreen = 0,
        FullScreenWindow = 1,
        MaximizedWindow = 2,
        Windowed = 3,
    }


    [Flags]
    public enum HideFlags
    {
        None = 0,
        HideInHierarchy = 1,
        HideInInspector = 2,
        DontSaveInEditor = 4,
        NotEditable = 8,
        DontSaveInBuild = 16,
        DontUnloadUnusedAsset = 32,
        DontSave = 52,
        HideAndDontSave = 61,
    }

    public enum HorizontalWrapMode
    {
        Wrap = 0,
        Overflow = 1,
    }

    public enum HumanBodyBones
    {
        Hips = 0,
        LeftUpperLeg = 1,
        RightUpperLeg = 2,
        LeftLowerLeg = 3,
        RightLowerLeg = 4,
        LeftFoot = 5,
        RightFoot = 6,
        Spine = 7,
        Chest = 8,
        UpperChest = 54,
        Neck = 9,
        Head = 10,
        LeftShoulder = 11,
        RightShoulder = 12,
        LeftUpperArm = 13,
        RightUpperArm = 14,
        LeftLowerArm = 15,
        RightLowerArm = 16,
        LeftHand = 17,
        RightHand = 18,
        LeftToes = 19,
        RightToes = 20,
        LeftEye = 21,
        RightEye = 22,
        Jaw = 23,
        LeftThumbProximal = 24,
        LeftThumbIntermediate = 25,
        LeftThumbDistal = 26,
        LeftIndexProximal = 27,
        LeftIndexIntermediate = 28,
        LeftIndexDistal = 29,
        LeftMiddleProximal = 30,
        LeftMiddleIntermediate = 31,
        LeftMiddleDistal = 32,
        LeftRingProximal = 33,
        LeftRingIntermediate = 34,
        LeftRingDistal = 35,
        LeftLittleProximal = 36,
        LeftLittleIntermediate = 37,
        LeftLittleDistal = 38,
        RightThumbProximal = 39,
        RightThumbIntermediate = 40,
        RightThumbDistal = 41,
        RightIndexProximal = 42,
        RightIndexIntermediate = 43,
        RightIndexDistal = 44,
        RightMiddleProximal = 45,
        RightMiddleIntermediate = 46,
        RightMiddleDistal = 47,
        RightRingProximal = 48,
        RightRingIntermediate = 49,
        RightRingDistal = 50,
        RightLittleProximal = 51,
        RightLittleIntermediate = 52,
        RightLittleDistal = 53,
        LastBone = 55,
    }

    public enum ImagePosition
    {
        ImageLeft = 0,
        ImageAbove = 1,
        ImageOnly = 2,
        TextOnly = 3,
    }

    public enum IMECompositionMode
    {
        Auto = 0,
        On = 1,
        Off = 2,
    }

    public enum JointLimitState2D
    {
        Inactive = 0,
        LowerLimit = 1,
        UpperLimit = 2,
        EqualLimits = 3,
    }

    public enum JointProjectionMode
    {
        None = 0,
        PositionAndRotation = 1,
        [Obsolete] PositionOnly = 2,
    }

    public enum KeyCode
    {
        None = 0,
        Backspace = 8,
        Delete = 127,
        Tab = 9,
        Clear = 12,
        Return = 13,
        Pause = 19,
        Escape = 27,
        Space = 32,
        Keypad0 = 256,
        Keypad1 = 257,
        Keypad2 = 258,
        Keypad3 = 259,
        Keypad4 = 260,
        Keypad5 = 261,
        Keypad6 = 262,
        Keypad7 = 263,
        Keypad8 = 264,
        Keypad9 = 265,
        KeypadPeriod = 266,
        KeypadDivide = 267,
        KeypadMultiply = 268,
        KeypadMinus = 269,
        KeypadPlus = 270,
        KeypadEnter = 271,
        KeypadEquals = 272,
        UpArrow = 273,
        DownArrow = 274,
        RightArrow = 275,
        LeftArrow = 276,
        Insert = 277,
        Home = 278,
        End = 279,
        PageUp = 280,
        PageDown = 281,
        F1 = 282,
        F2 = 283,
        F3 = 284,
        F4 = 285,
        F5 = 286,
        F6 = 287,
        F7 = 288,
        F8 = 289,
        F9 = 290,
        F10 = 291,
        F11 = 292,
        F12 = 293,
        F13 = 294,
        F14 = 295,
        F15 = 296,
        Alpha0 = 48,
        Alpha1 = 49,
        Alpha2 = 50,
        Alpha3 = 51,
        Alpha4 = 52,
        Alpha5 = 53,
        Alpha6 = 54,
        Alpha7 = 55,
        Alpha8 = 56,
        Alpha9 = 57,
        Exclaim = 33,
        DoubleQuote = 34,
        Hash = 35,
        Dollar = 36,
        Percent = 37,
        Ampersand = 38,
        Quote = 39,
        LeftParen = 40,
        RightParen = 41,
        Asterisk = 42,
        Plus = 43,
        Comma = 44,
        Minus = 45,
        Period = 46,
        Slash = 47,
        Colon = 58,
        Semicolon = 59,
        Less = 60,
        Equals = 61,
        Greater = 62,
        Question = 63,
        At = 64,
        LeftBracket = 91,
        Backslash = 92,
        RightBracket = 93,
        Caret = 94,
        Underscore = 95,
        BackQuote = 96,
        A = 97,
        B = 98,
        C = 99,
        D = 100,
        E = 101,
        F = 102,
        G = 103,
        H = 104,
        I = 105,
        J = 106,
        K = 107,
        L = 108,
        M = 109,
        N = 110,
        O = 111,
        P = 112,
        Q = 113,
        R = 114,
        S = 115,
        T = 116,
        U = 117,
        V = 118,
        W = 119,
        X = 120,
        Y = 121,
        Z = 122,
        LeftCurlyBracket = 123,
        Pipe = 124,
        RightCurlyBracket = 125,
        Tilde = 126,
        Numlock = 300,
        CapsLock = 301,
        ScrollLock = 302,
        RightShift = 303,
        LeftShift = 304,
        RightControl = 305,
        LeftControl = 306,
        RightAlt = 307,
        LeftAlt = 308,
        LeftMeta = 310,
        LeftCommand = 310,
        LeftApple = 310,
        LeftWindows = 311,
        RightMeta = 309,
        RightCommand = 309,
        RightApple = 309,
        RightWindows = 312,
        AltGr = 313,
        Help = 315,
        Print = 316,
        SysReq = 317,
        Break = 318,
        Menu = 319,
        WheelUp = 321,
        WheelDown = 322,
        F16 = 670,
        F17 = 671,
        F18 = 672,
        F19 = 673,
        F20 = 674,
        F21 = 675,
        F22 = 676,
        F23 = 677,
        F24 = 678,
        Mouse0 = 323,
        Mouse1 = 324,
        Mouse2 = 325,
        Mouse3 = 326,
        Mouse4 = 327,
        Mouse5 = 328,
        Mouse6 = 329,
        JoystickButton0 = 330,
        JoystickButton1 = 331,
        JoystickButton2 = 332,
        JoystickButton3 = 333,
        JoystickButton4 = 334,
        JoystickButton5 = 335,
        JoystickButton6 = 336,
        JoystickButton7 = 337,
        JoystickButton8 = 338,
        JoystickButton9 = 339,
        JoystickButton10 = 340,
        JoystickButton11 = 341,
        JoystickButton12 = 342,
        JoystickButton13 = 343,
        JoystickButton14 = 344,
        JoystickButton15 = 345,
        JoystickButton16 = 346,
        JoystickButton17 = 347,
        JoystickButton18 = 348,
        JoystickButton19 = 349,
        Joystick1Button0 = 350,
        Joystick1Button1 = 351,
        Joystick1Button2 = 352,
        Joystick1Button3 = 353,
        Joystick1Button4 = 354,
        Joystick1Button5 = 355,
        Joystick1Button6 = 356,
        Joystick1Button7 = 357,
        Joystick1Button8 = 358,
        Joystick1Button9 = 359,
        Joystick1Button10 = 360,
        Joystick1Button11 = 361,
        Joystick1Button12 = 362,
        Joystick1Button13 = 363,
        Joystick1Button14 = 364,
        Joystick1Button15 = 365,
        Joystick1Button16 = 366,
        Joystick1Button17 = 367,
        Joystick1Button18 = 368,
        Joystick1Button19 = 369,
        Joystick2Button0 = 370,
        Joystick2Button1 = 371,
        Joystick2Button2 = 372,
        Joystick2Button3 = 373,
        Joystick2Button4 = 374,
        Joystick2Button5 = 375,
        Joystick2Button6 = 376,
        Joystick2Button7 = 377,
        Joystick2Button8 = 378,
        Joystick2Button9 = 379,
        Joystick2Button10 = 380,
        Joystick2Button11 = 381,
        Joystick2Button12 = 382,
        Joystick2Button13 = 383,
        Joystick2Button14 = 384,
        Joystick2Button15 = 385,
        Joystick2Button16 = 386,
        Joystick2Button17 = 387,
        Joystick2Button18 = 388,
        Joystick2Button19 = 389,
        Joystick3Button0 = 390,
        Joystick3Button1 = 391,
        Joystick3Button2 = 392,
        Joystick3Button3 = 393,
        Joystick3Button4 = 394,
        Joystick3Button5 = 395,
        Joystick3Button6 = 396,
        Joystick3Button7 = 397,
        Joystick3Button8 = 398,
        Joystick3Button9 = 399,
        Joystick3Button10 = 400,
        Joystick3Button11 = 401,
        Joystick3Button12 = 402,
        Joystick3Button13 = 403,
        Joystick3Button14 = 404,
        Joystick3Button15 = 405,
        Joystick3Button16 = 406,
        Joystick3Button17 = 407,
        Joystick3Button18 = 408,
        Joystick3Button19 = 409,
        Joystick4Button0 = 410,
        Joystick4Button1 = 411,
        Joystick4Button2 = 412,
        Joystick4Button3 = 413,
        Joystick4Button4 = 414,
        Joystick4Button5 = 415,
        Joystick4Button6 = 416,
        Joystick4Button7 = 417,
        Joystick4Button8 = 418,
        Joystick4Button9 = 419,
        Joystick4Button10 = 420,
        Joystick4Button11 = 421,
        Joystick4Button12 = 422,
        Joystick4Button13 = 423,
        Joystick4Button14 = 424,
        Joystick4Button15 = 425,
        Joystick4Button16 = 426,
        Joystick4Button17 = 427,
        Joystick4Button18 = 428,
        Joystick4Button19 = 429,
        Joystick5Button0 = 430,
        Joystick5Button1 = 431,
        Joystick5Button2 = 432,
        Joystick5Button3 = 433,
        Joystick5Button4 = 434,
        Joystick5Button5 = 435,
        Joystick5Button6 = 436,
        Joystick5Button7 = 437,
        Joystick5Button8 = 438,
        Joystick5Button9 = 439,
        Joystick5Button10 = 440,
        Joystick5Button11 = 441,
        Joystick5Button12 = 442,
        Joystick5Button13 = 443,
        Joystick5Button14 = 444,
        Joystick5Button15 = 445,
        Joystick5Button16 = 446,
        Joystick5Button17 = 447,
        Joystick5Button18 = 448,
        Joystick5Button19 = 449,
        Joystick6Button0 = 450,
        Joystick6Button1 = 451,
        Joystick6Button2 = 452,
        Joystick6Button3 = 453,
        Joystick6Button4 = 454,
        Joystick6Button5 = 455,
        Joystick6Button6 = 456,
        Joystick6Button7 = 457,
        Joystick6Button8 = 458,
        Joystick6Button9 = 459,
        Joystick6Button10 = 460,
        Joystick6Button11 = 461,
        Joystick6Button12 = 462,
        Joystick6Button13 = 463,
        Joystick6Button14 = 464,
        Joystick6Button15 = 465,
        Joystick6Button16 = 466,
        Joystick6Button17 = 467,
        Joystick6Button18 = 468,
        Joystick6Button19 = 469,
        Joystick7Button0 = 470,
        Joystick7Button1 = 471,
        Joystick7Button2 = 472,
        Joystick7Button3 = 473,
        Joystick7Button4 = 474,
        Joystick7Button5 = 475,
        Joystick7Button6 = 476,
        Joystick7Button7 = 477,
        Joystick7Button8 = 478,
        Joystick7Button9 = 479,
        Joystick7Button10 = 480,
        Joystick7Button11 = 481,
        Joystick7Button12 = 482,
        Joystick7Button13 = 483,
        Joystick7Button14 = 484,
        Joystick7Button15 = 485,
        Joystick7Button16 = 486,
        Joystick7Button17 = 487,
        Joystick7Button18 = 488,
        Joystick7Button19 = 489,
        Joystick8Button0 = 490,
        Joystick8Button1 = 491,
        Joystick8Button2 = 492,
        Joystick8Button3 = 493,
        Joystick8Button4 = 494,
        Joystick8Button5 = 495,
        Joystick8Button6 = 496,
        Joystick8Button7 = 497,
        Joystick8Button8 = 498,
        Joystick8Button9 = 499,
        Joystick8Button10 = 500,
        Joystick8Button11 = 501,
        Joystick8Button12 = 502,
        Joystick8Button13 = 503,
        Joystick8Button14 = 504,
        Joystick8Button15 = 505,
        Joystick8Button16 = 506,
        Joystick8Button17 = 507,
        Joystick8Button18 = 508,
        Joystick8Button19 = 509,
    }

    [Flags]
    public enum LightmapBakeType
    {
        Realtime = 4,
        Baked = 2,
        Mixed = 1,
    }

    public enum LightRenderMode
    {
        Auto = 0,
        ForcePixel = 1,
        ForceVertex = 2,
    }

    public enum LightShadows
    {
        None = 0,
        Hard = 1,
        Soft = 2,
    }

    public enum LightType
    {
        Spot = 0,
        Directional = 1,
        Point = 2,
        [Obsolete] Area = 3,
        Rectangle = 3,
        Disc = 4,
        Pyramid = 5,
        Box = 6,
        Tube = 7,
    }

    public enum LineAlignment
    {
        View = 0,
        [Obsolete] Local = 1,
        TransformZ = 1,
    }

    public enum LineTextureMode
    {
        Stretch = 0,
        Tile = 1,
        DistributePerSegment = 2,
        RepeatPerSegment = 3,
        Static = 4,
    }

    public enum LogOption
    {
        None = 0,
        NoStacktrace = 1,
    }

    public enum LogType
    {
        Error = 0,
        Assert = 1,
        Warning = 2,
        Log = 3,
        Exception = 4,
    }

    [Flags]
    public enum MeshColliderCookingOptions
    {
        None = 0,
        [Obsolete] InflateConvexMesh = 1,
        CookForFasterSimulation = 2,
        EnableMeshCleaning = 4,
        WeldColocatedVertices = 8,
        UseFastMidphase = 16,
    }

    public enum MeshTopology
    {
        Triangles = 0,
        Quads = 2,
        Lines = 3,
        LineStrip = 4,
        Points = 5,
    }

    public enum MotionVectorGenerationMode
    {
        Camera = 0,
        Object = 1,
        ForceNoMotion = 2,
    }

    public enum NetworkReachability
    {
        NotReachable = 0,
        ReachableViaCarrierDataNetwork = 1,
        ReachableViaLocalAreaNetwork = 2,
    }

    public enum OperatingSystemFamily
    {
        Other = 0,
        MacOSX = 1,
        Windows = 2,
        Linux = 3,
    }

    public enum ParticleSystemAnimationMode
    {
        Grid = 0,
        Sprites = 1,
    }

    public enum ParticleSystemAnimationRowMode
    {
        Custom = 0,
        Random = 1,
        MeshIndex = 2,
    }

    public enum ParticleSystemAnimationTimeMode
    {
        Lifetime = 0,
        Speed = 1,
        FPS = 2,
    }

    public enum ParticleSystemAnimationType
    {
        WholeSheet = 0,
        SingleRow = 1,
    }

    public enum ParticleSystemColliderQueryMode
    {
        Disabled = 0,
        One = 1,
        All = 2,
    }

    public enum ParticleSystemCollisionMode
    {
        Collision3D = 0,
        Collision2D = 1,
    }

    public enum ParticleSystemCollisionQuality
    {
        High = 0,
        Medium = 1,
        Low = 2,
    }

    public enum ParticleSystemCollisionType
    {
        Planes = 0,
        World = 1,
    }

    public enum ParticleSystemCullingMode
    {
        Automatic = 0,
        PauseAndCatchup = 1,
        Pause = 2,
        AlwaysSimulate = 3,
    }

    public enum ParticleSystemCurveMode
    {
        Constant = 0,
        Curve = 1,
        TwoCurves = 2,
        TwoConstants = 3,
    }

    public enum ParticleSystemCustomData
    {
        Custom1 = 0,
        Custom2 = 1,
    }

    public enum ParticleSystemCustomDataMode
    {
        Disabled = 0,
        Vector = 1,
        Color = 2,
    }

    public enum ParticleSystemEmissionType
    {
        Time = 0,
        Distance = 1,
    }

    public enum ParticleSystemEmitterVelocityMode
    {
        Transform = 0,
        Rigidbody = 1,
        Custom = 2,
    }

    public enum ParticleSystemForceFieldShape
    {
        Sphere = 0,
        Hemisphere = 1,
        Cylinder = 2,
        Box = 3,
    }

    public enum ParticleSystemGameObjectFilter
    {
        LayerMask = 0,
        List = 1,
        LayerMaskAndList = 2,
    }

    public enum ParticleSystemGradientMode
    {
        Color = 0,
        Gradient = 1,
        TwoColors = 2,
        TwoGradients = 3,
        RandomColor = 4,
    }

    public enum ParticleSystemInheritVelocityMode
    {
        Initial = 0,
        Current = 1,
    }

    public enum ParticleSystemMeshDistribution
    {
        UniformRandom = 0,
        NonUniformRandom = 1,
    }

    public enum ParticleSystemMeshShapeType
    {
        Vertex = 0,
        Edge = 1,
        Triangle = 2,
    }

    public enum ParticleSystemNoiseQuality
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    public enum ParticleSystemOverlapAction
    {
        Ignore = 0,
        Kill = 1,
        Callback = 2,
    }

    public enum ParticleSystemRenderMode
    {
        Billboard = 0,
        Stretch = 1,
        HorizontalBillboard = 2,
        VerticalBillboard = 3,
        Mesh = 4,
        None = 5,
    }

    public enum ParticleSystemRenderSpace
    {
        View = 0,
        World = 1,
        Local = 2,
        Facing = 3,
        Velocity = 4,
    }

    public enum ParticleSystemRingBufferMode
    {
        Disabled = 0,
        PauseUntilReplaced = 1,
        LoopUntilReplaced = 2,
    }

    public enum ParticleSystemScalingMode
    {
        Hierarchy = 0,
        Local = 1,
        Shape = 2,
    }

    public enum ParticleSystemShapeMultiModeValue
    {
        Random = 0,
        Loop = 1,
        PingPong = 2,
        BurstSpread = 3,
    }

    public enum ParticleSystemShapeTextureChannel
    {
        Red = 0,
        Green = 1,
        Blue = 2,
        Alpha = 3,
    }

    public enum ParticleSystemShapeType
    {
        Sphere = 0,
        [Obsolete] SphereShell = 1,
        Hemisphere = 2,
        [Obsolete] HemisphereShell = 3,
        Cone = 4,
        Box = 5,
        Mesh = 6,
        [Obsolete] ConeShell = 7,
        ConeVolume = 8,
        [Obsolete] ConeVolumeShell = 9,
        Circle = 10,
        [Obsolete] CircleEdge = 11,
        SingleSidedEdge = 12,
        MeshRenderer = 13,
        SkinnedMeshRenderer = 14,
        BoxShell = 15,
        BoxEdge = 16,
        Donut = 17,
        Rectangle = 18,
        Sprite = 19,
        SpriteRenderer = 20,
    }

    public enum ParticleSystemSimulationSpace
    {
        Local = 0,
        World = 1,
        Custom = 2,
    }

    public enum ParticleSystemSortMode
    {
        None = 0,
        Distance = 1,
        OldestInFront = 2,
        YoungestInFront = 3,
        Depth = 4,
        DistanceReverse = 5,
        DepthReverse = 6,
    }

    public enum ParticleSystemStopAction
    {
        None = 0,
        Disable = 1,
        Destroy = 2,
        Callback = 3,
    }

    public enum ParticleSystemStopBehavior
    {
        StopEmittingAndClear = 0,
        StopEmitting = 1,
    }

    [Flags]
    public enum ParticleSystemSubEmitterProperties
    {
        InheritNothing = 0,
        InheritEverything = 31,
        InheritColor = 1,
        InheritSize = 2,
        InheritRotation = 4,
        InheritLifetime = 8,
        InheritDuration = 16,
    }

    public enum ParticleSystemSubEmitterType
    {
        Birth = 0,
        Collision = 1,
        Death = 2,
        Trigger = 3,
        Manual = 4,
    }

    public enum ParticleSystemTrailMode
    {
        PerParticle = 0,
        Ribbon = 1,
    }

    public enum ParticleSystemTrailTextureMode
    {
        Stretch = 0,
        Tile = 1,
        DistributePerSegment = 2,
        RepeatPerSegment = 3,
        Static = 4,
    }

    public enum ParticleSystemTriggerEventType
    {
        Inside = 0,
        Outside = 1,
        Enter = 2,
        Exit = 3,
    }

    public enum ParticleSystemVertexStream
    {
        Position = 0,
        Normal = 1,
        Tangent = 2,
        Color = 3,
        UV = 4,
        UV2 = 5,
        UV3 = 6,
        UV4 = 7,
        AnimBlend = 8,
        AnimFrame = 9,
        Center = 10,
        VertexID = 11,
        SizeX = 12,
        SizeXY = 13,
        SizeXYZ = 14,
        Rotation = 15,
        Rotation3D = 16,
        RotationSpeed = 17,
        RotationSpeed3D = 18,
        Velocity = 19,
        Speed = 20,
        AgePercent = 21,
        InvStartLifetime = 22,
        StableRandomX = 23,
        StableRandomXY = 24,
        StableRandomXYZ = 25,
        StableRandomXYZW = 26,
        VaryingRandomX = 27,
        VaryingRandomXY = 28,
        VaryingRandomXYZ = 29,
        VaryingRandomXYZW = 30,
        Custom1X = 31,
        Custom1XY = 32,
        Custom1XYZ = 33,
        Custom1XYZW = 34,
        Custom2X = 35,
        Custom2XY = 36,
        Custom2XYZ = 37,
        Custom2XYZW = 38,
        NoiseSumX = 39,
        NoiseSumXY = 40,
        NoiseSumXYZ = 41,
        NoiseImpulseX = 42,
        NoiseImpulseXY = 43,
        NoiseImpulseXYZ = 44,
        MeshIndex = 45,
        ParticleIndex = 46,
        ColorPackedAsTwoFloats = 47,
        MeshAxisOfRotation = 48,
        NextTrailCenter = 49,
        PreviousTrailCenter = 50,
        PercentageAlongTrail = 51,
        TrailWidth = 52,
    }

    [Flags]
    public enum ParticleSystemVertexStreams
    {
        Position = 1,
        Normal = 2,
        Tangent = 4,
        Color = 8,
        UV = 16,
        UV2BlendAndFrame = 32,
        CenterAndVertexID = 64,
        Size = 128,
        Rotation = 256,
        Velocity = 512,
        Lifetime = 1024,
        Custom1 = 2048,
        Custom2 = 4096,
        Random = 8192,
        None = 0,
        All = 2147483647,
    }

    public enum PhysicsMaterialCombine
    {
        Average = 0,
        Multiply = 1,
        Minimum = 2,
        Maximum = 3,
    }

    public enum PlayMode
    {
        StopSameLayer = 0,
        StopAll = 4,
    }

    public enum PointerType
    {
        Mouse = 0,
        Touch = 1,
        Pen = 2,
    }

    public enum PrimitiveType
    {
        Sphere = 0,
        Capsule = 1,
        Cylinder = 2,
        Cube = 3,
        Plane = 4,
        Quad = 5,
    }

    public enum QueryTriggerInteraction
    {
        UseGlobal = 0,
        Ignore = 1,
        Collide = 2,
    }

    public enum QueueMode
    {
        CompleteOthers = 0,
        PlayNow = 2,
    }

    public enum RenderingPath
    {
        UsePlayerSettings = -1,
        VertexLit = 0,
        Forward = 1,
        [Obsolete] DeferredLighting = 2,
        DeferredShading = 3,
    }

    public enum RenderMode
    {
        ScreenSpaceOverlay = 0,
        ScreenSpaceCamera = 1,
        WorldSpace = 2,
    }

    public enum RenderTextureFormat
    {
        ARGB32 = 0,
        Depth = 1,
        ARGBHalf = 2,
        Shadowmap = 3,
        RGB565 = 4,
        ARGB4444 = 5,
        ARGB1555 = 6,
        Default = 7,
        ARGB2101010 = 8,
        DefaultHDR = 9,
        ARGB64 = 10,
        ARGBFloat = 11,
        RGFloat = 12,
        RGHalf = 13,
        RFloat = 14,
        RHalf = 15,
        R8 = 16,
        ARGBInt = 17,
        RGInt = 18,
        RInt = 19,
        BGRA32 = 20,
        RGB111110Float = 22,
        RG32 = 23,
        RGBAUShort = 24,
        RG16 = 25,
        BGRA10101010_XR = 26,
        BGR101010_XR = 27,
        R16 = 28,
    }

    public enum RenderTextureReadWrite
    {
        Default = 0,
        Linear = 1,
        sRGB = 2,
    }

    public enum RigidbodyConstraints
    {
        None = 0,
        FreezePositionX = 2,
        FreezePositionY = 4,
        FreezePositionZ = 8,
        FreezeRotationX = 16,
        FreezeRotationY = 32,
        FreezeRotationZ = 64,
        FreezePosition = 14,
        FreezeRotation = 112,
        FreezeAll = 126,
    }

    [Flags]
    public enum RigidbodyConstraints2D
    {
        None = 0,
        FreezePositionX = 1,
        FreezePositionY = 2,
        FreezeRotation = 4,
        FreezePosition = 3,
        FreezeAll = 7,
    }

    public enum RigidbodyInterpolation
    {
        None = 0,
        Interpolate = 1,
        Extrapolate = 2,
    }

    public enum RigidbodyInterpolation2D
    {
        None = 0,
        Interpolate = 1,
        Extrapolate = 2,
    }

    public enum RigidbodySleepMode2D
    {
        NeverSleep = 0,
        StartAwake = 1,
        StartAsleep = 2,
    }

    public enum RigidbodyType2D
    {
        Dynamic = 0,
        Kinematic = 1,
        Static = 2,
    }

    public enum RotationDriveMode
    {
        XYAndZ = 0,
        Slerp = 1,
    }

    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad = 0,
        BeforeSceneLoad = 1,
        AfterAssembliesLoaded = 2,
        BeforeSplashScreen = 3,
        SubsystemRegistration = 4,
    }

    public enum RuntimePlatform
    {
        OSXEditor = 0,
        OSXPlayer = 1,
        WindowsPlayer = 2,
        [Obsolete] OSXWebPlayer = 3,
        [Obsolete] OSXDashboardPlayer = 4,
        [Obsolete] WindowsWebPlayer = 5,
        WindowsEditor = 7,
        IPhonePlayer = 8,
        [Obsolete] XBOX360 = 10,
        [Obsolete] PS3 = 9,
        Android = 11,
        [Obsolete] NaCl = 12,
        [Obsolete] FlashPlayer = 15,
        LinuxPlayer = 13,
        LinuxEditor = 16,
        WebGLPlayer = 17,
        [Obsolete] MetroPlayerX86 = 18,
        WSAPlayerX86 = 18,
        [Obsolete] MetroPlayerX64 = 19,
        WSAPlayerX64 = 19,
        [Obsolete] MetroPlayerARM = 20,
        WSAPlayerARM = 20,
        [Obsolete] WP8Player = 21,
        [Obsolete] BB10Player = 22,
        [Obsolete] BlackBerryPlayer = 22,
        [Obsolete] TizenPlayer = 23,
        [Obsolete] PSP2 = 24,
        PS4 = 25,
        [Obsolete] PSM = 26,
        XboxOne = 27,
        [Obsolete] SamsungTVPlayer = 28,
        [Obsolete] WiiU = 30,
        tvOS = 31,
        Switch = 32,
        [Obsolete] Lumin = 33,
        [Obsolete] Stadia = 34,
        [Obsolete] CloudRendering = -1,
        LinuxHeadlessSimulation = 35,
        [Obsolete] GameCoreScarlett = -1,
        GameCoreXboxSeries = 36,
        GameCoreXboxOne = 37,
        PS5 = 38,
        EmbeddedLinuxArm64 = 39,
        [Obsolete] EmbeddedLinuxArm32 = 40,
        EmbeddedLinuxX64 = 41,
        [Obsolete] EmbeddedLinuxX86 = 42,
        LinuxServer = 43,
        WindowsServer = 44,
        OSXServer = 45,
        [Obsolete] QNXArm32 = 46,
        QNXArm64 = 47,
        QNXX64 = 48,
        [Obsolete] QNXX86 = 49,
        VisionOS = 50,
        Switch2 = 51,
        [Obsolete] KeplerArm64 = 52,
        [Obsolete] KeplerX64 = 53,
    }

    public enum ScaleMode
    {
        StretchToFill = 0,
        ScaleAndCrop = 1,
        ScaleToFit = 2,
    }

    public enum ScreenOrientation
    {
        Portrait = 1,
        PortraitUpsideDown = 2,
        LandscapeLeft = 3,
        LandscapeRight = 4,
        AutoRotation = 5,
        [Obsolete] Unknown = 0,
        [Obsolete] Landscape = 3,
    }

    public enum SendMessageOptions
    {
        RequireReceiver = 0,
        DontRequireReceiver = 1,
    }

    public enum ShadowmaskMode
    {
        Shadowmask = 0,
        DistanceShadowmask = 1,
    }

    public enum ShadowProjection
    {
        CloseFit = 0,
        StableFit = 1,
    }

    public enum ShadowQuality
    {
        Disable = 0,
        HardOnly = 1,
        All = 2,
    }

    public enum ShadowResolution
    {
        Low = 0,
        Medium = 1,
        High = 2,
        VeryHigh = 3,
    }

    public enum SimulationMode
    {
        FixedUpdate = 0,
        Update = 1,
        Script = 2,
    }

    public enum SimulationMode2D
    {
        FixedUpdate = 0,
        Update = 1,
        Script = 2,
    }

    public enum SkinWeights
    {
        None = 0,
        OneBone = 1,
        TwoBones = 2,
        FourBones = 4,
        Unlimited = 255,
    }

    public enum Space
    {
        World = 0,
        Self = 1,
    }

    public enum SpriteAlignment
    {
        Center = 0,
        TopLeft = 1,
        TopCenter = 2,
        TopRight = 3,
        LeftCenter = 4,
        RightCenter = 5,
        BottomLeft = 6,
        BottomCenter = 7,
        BottomRight = 8,
        Custom = 9,
    }

    public enum SpriteDrawMode
    {
        Simple = 0,
        Sliced = 1,
        Tiled = 2,
    }

    public enum SpriteMaskInteraction
    {
        None = 0,
        VisibleInsideMask = 1,
        VisibleOutsideMask = 2,
    }

    public enum SpriteMeshType
    {
        FullRect = 0,
        Tight = 1,
    }

    public enum SpritePackingMode
    {
        Tight = 0,
        Rectangle = 1,
    }

    public enum SpritePackingRotation
    {
        None = 0,
        FlipHorizontal = 1,
        FlipVertical = 2,
        Rotate180 = 3,
        Any = 15,
    }

    public enum SpriteSortPoint
    {
        Center = 0,
        Pivot = 1,
    }

    public enum SpriteTileMode
    {
        Continuous = 0,
        Adaptive = 1,
    }

    public enum StackTraceLogType
    {
        None = 0,
        ScriptOnly = 1,
        Full = 2,
    }

    public enum StandaloneRenderResize
    {
        Enabled = 0,
        Disabled = 1,
    }

    public enum SystemLanguage
    {
        Afrikaans = 0,
        Arabic = 1,
        Basque = 2,
        Belarusian = 3,
        Bulgarian = 4,
        Catalan = 5,
        Chinese = 6,
        Czech = 7,
        Danish = 8,
        Dutch = 9,
        English = 10,
        Estonian = 11,
        Faroese = 12,
        Finnish = 13,
        French = 14,
        German = 15,
        Greek = 16,
        Hebrew = 17,
        [Obsolete] Hugarian = 18,
        Icelandic = 19,
        Indonesian = 20,
        Italian = 21,
        Japanese = 22,
        Korean = 23,
        Latvian = 24,
        Lithuanian = 25,
        Norwegian = 26,
        Polish = 27,
        Portuguese = 28,
        Romanian = 29,
        Russian = 30,
        SerboCroatian = 31,
        Slovak = 32,
        Slovenian = 33,
        Spanish = 34,
        Swedish = 35,
        Thai = 36,
        Turkish = 37,
        Ukrainian = 38,
        Vietnamese = 39,
        ChineseSimplified = 40,
        ChineseTraditional = 41,
        Hindi = 42,
        Unknown = 43,
        Hungarian = 18,
    }

    [Flags]
    public enum TerrainRenderFlags
    {
        [Obsolete] heightmap = 1,
        [Obsolete] trees = 2,
        [Obsolete] details = 4,
        [Obsolete] all = 7,
        Heightmap = 1,
        Trees = 2,
        Details = 4,
        All = 7,
    }

    public enum TextAlignment
    {
        Left = 0,
        Center = 1,
        Right = 2,
    }

    public enum TextAnchor
    {
        UpperLeft = 0,
        UpperCenter = 1,
        UpperRight = 2,
        MiddleLeft = 3,
        MiddleCenter = 4,
        MiddleRight = 5,
        LowerLeft = 6,
        LowerCenter = 7,
        LowerRight = 8,
    }

    public enum TextClipping
    {
        Overflow = 0,
        Clip = 1,
        Ellipsis = 2,
    }

    public enum TextureFormat
    {
        Alpha8 = 1,
        ARGB4444 = 2,
        RGB24 = 3,
        RGBA32 = 4,
        ARGB32 = 5,
        RGB565 = 7,
        R16 = 9,
        DXT1 = 10,
        DXT5 = 12,
        RGBA4444 = 13,
        BGRA32 = 14,
        RHalf = 15,
        RGHalf = 16,
        RGBAHalf = 17,
        RFloat = 18,
        RGFloat = 19,
        RGBAFloat = 20,
        YUY2 = 21,
        RGB9e5Float = 22,
        BC4 = 26,
        BC5 = 27,
        BC6H = 24,
        BC7 = 25,
        DXT1Crunched = 28,
        DXT5Crunched = 29,
        PVRTC_RGB2 = 30,
        PVRTC_RGBA2 = 31,
        PVRTC_RGB4 = 32,
        PVRTC_RGBA4 = 33,
        ETC_RGB4 = 34,
        EAC_R = 41,
        EAC_R_SIGNED = 42,
        EAC_RG = 43,
        EAC_RG_SIGNED = 44,
        ETC2_RGB = 45,
        ETC2_RGBA1 = 46,
        ETC2_RGBA8 = 47,
        ASTC_4x4 = 48,
        ASTC_5x5 = 49,
        ASTC_6x6 = 50,
        ASTC_8x8 = 51,
        ASTC_10x10 = 52,
        ASTC_12x12 = 53,
        [Obsolete] ETC_RGB4_3DS = -60,
        [Obsolete] ETC_RGBA8_3DS = -61,
        RG16 = 62,
        R8 = 63,
        ETC_RGB4Crunched = 64,
        ETC2_RGBA8Crunched = 65,
        ASTC_HDR_4x4 = 66,
        ASTC_HDR_5x5 = 67,
        ASTC_HDR_6x6 = 68,
        ASTC_HDR_8x8 = 69,
        ASTC_HDR_10x10 = 70,
        ASTC_HDR_12x12 = 71,
        RG32 = 72,
        RGB48 = 73,
        RGBA64 = 74,
        R8_SIGNED = 75,
        RG16_SIGNED = 76,
        RGB24_SIGNED = 77,
        RGBA32_SIGNED = 78,
        R16_SIGNED = 79,
        RG32_SIGNED = 80,
        RGB48_SIGNED = 81,
        RGBA64_SIGNED = 82,
        [Obsolete] ASTC_RGB_4x4 = -48,
        [Obsolete] ASTC_RGB_5x5 = -49,
        [Obsolete] ASTC_RGB_6x6 = -50,
        [Obsolete] ASTC_RGB_8x8 = -51,
        [Obsolete] ASTC_RGB_10x10 = -52,
        [Obsolete] ASTC_RGB_12x12 = -53,
        [Obsolete] ASTC_RGBA_4x4 = -54,
        [Obsolete] ASTC_RGBA_5x5 = -55,
        [Obsolete] ASTC_RGBA_6x6 = -56,
        [Obsolete] ASTC_RGBA_8x8 = -57,
        [Obsolete] ASTC_RGBA_10x10 = -58,
        [Obsolete] ASTC_RGBA_12x12 = -59,
    }

    public enum TextureWrapMode
    {
        Repeat = 0,
        Clamp = 1,
        Mirror = 2,
        MirrorOnce = 3,
    }

    public enum ThreadPriority
    {
        Low = 0,
        BelowNormal = 1,
        Normal = 2,
        High = 4,
    }

    public enum TouchPhase
    {
        Began = 0,
        Moved = 1,
        Stationary = 2,
        Ended = 3,
        Canceled = 4,
    }

    public enum TouchType
    {
        Direct = 0,
        Indirect = 1,
        Stylus = 2,
    }

    public enum TransparencySortMode
    {
        Default = 0,
        Perspective = 1,
        Orthographic = 2,
        CustomAxis = 3,
    }

    public enum UserAuthorization
    {
        WebCam = 1,
        Microphone = 2,
    }

    public enum VerticalWrapMode
    {
        Truncate = 0,
        Overflow = 1,
    }

    public enum WaitTimeoutMode
    {
        Realtime = 0,
        InGameTime = 1,
    }



}

namespace UnityEngine.Events
{
    public enum PersistentListenerMode
    {
        EventDefined = 0,
        Void = 1,
        Object = 2,
        Int = 3,
        Float = 4,
        String = 5,
        Bool = 6,
    }

    public enum UnityEventCallState
    {
        Off = 0,
        EditorAndRuntime = 1,
        RuntimeOnly = 2,
    }

}

namespace UnityEngine.Experimental.Rendering
{
    public enum DefaultFormat
    {
        LDR = 0,
        HDR = 1,
        DepthStencil = 2,
        Shadow = 3,
        Video = 4,
    }

    public enum FormatUsage
    {
        Sample = 0,
        Linear = 1,
        Sparse = 2,
        Render = 4,
        Blend = 5,
        GetPixels = 6,
        SetPixels = 7,
        SetPixels32 = 8,
        ReadPixels = 9,
        LoadStore = 10,
        MSAA2x = 11,
        MSAA4x = 12,
        MSAA8x = 13,
        StencilSampling = 16,
    }

    public enum GraphicsFormat
    {
        None = 0,
        R8_SRGB = 1,
        R8G8_SRGB = 2,
        R8G8B8_SRGB = 3,
        R8G8B8A8_SRGB = 4,
        R8_UNorm = 5,
        R8G8_UNorm = 6,
        R8G8B8_UNorm = 7,
        R8G8B8A8_UNorm = 8,
        R8_SNorm = 9,
        R8G8_SNorm = 10,
        R8G8B8_SNorm = 11,
        R8G8B8A8_SNorm = 12,
        R8_UInt = 13,
        R8G8_UInt = 14,
        R8G8B8_UInt = 15,
        R8G8B8A8_UInt = 16,
        R8_SInt = 17,
        R8G8_SInt = 18,
        R8G8B8_SInt = 19,
        R8G8B8A8_SInt = 20,
        R16_UNorm = 21,
        R16G16_UNorm = 22,
        R16G16B16_UNorm = 23,
        R16G16B16A16_UNorm = 24,
        R16_SNorm = 25,
        R16G16_SNorm = 26,
        R16G16B16_SNorm = 27,
        R16G16B16A16_SNorm = 28,
        R16_UInt = 29,
        R16G16_UInt = 30,
        R16G16B16_UInt = 31,
        R16G16B16A16_UInt = 32,
        R16_SInt = 33,
        R16G16_SInt = 34,
        R16G16B16_SInt = 35,
        R16G16B16A16_SInt = 36,
        R32_UInt = 37,
        R32G32_UInt = 38,
        R32G32B32_UInt = 39,
        R32G32B32A32_UInt = 40,
        R32_SInt = 41,
        R32G32_SInt = 42,
        R32G32B32_SInt = 43,
        R32G32B32A32_SInt = 44,
        R16_SFloat = 45,
        R16G16_SFloat = 46,
        R16G16B16_SFloat = 47,
        R16G16B16A16_SFloat = 48,
        R32_SFloat = 49,
        R32G32_SFloat = 50,
        R32G32B32_SFloat = 51,
        R32G32B32A32_SFloat = 52,
        B8G8R8_SRGB = 56,
        B8G8R8A8_SRGB = 57,
        B8G8R8_UNorm = 58,
        B8G8R8A8_UNorm = 59,
        B8G8R8_SNorm = 60,
        B8G8R8A8_SNorm = 61,
        B8G8R8_UInt = 62,
        B8G8R8A8_UInt = 63,
        B8G8R8_SInt = 64,
        B8G8R8A8_SInt = 65,
        R4G4B4A4_UNormPack16 = 66,
        B4G4R4A4_UNormPack16 = 67,
        R5G6B5_UNormPack16 = 68,
        B5G6R5_UNormPack16 = 69,
        R5G5B5A1_UNormPack16 = 70,
        B5G5R5A1_UNormPack16 = 71,
        A1R5G5B5_UNormPack16 = 72,
        E5B9G9R9_UFloatPack32 = 73,
        B10G11R11_UFloatPack32 = 74,
        A2B10G10R10_UNormPack32 = 75,
        A2B10G10R10_UIntPack32 = 76,
        A2B10G10R10_SIntPack32 = 77,
        A2R10G10B10_UNormPack32 = 78,
        A2R10G10B10_UIntPack32 = 79,
        A2R10G10B10_SIntPack32 = 80,
        A2R10G10B10_XRSRGBPack32 = 81,
        A2R10G10B10_XRUNormPack32 = 82,
        R10G10B10_XRSRGBPack32 = 83,
        R10G10B10_XRUNormPack32 = 84,
        A10R10G10B10_XRSRGBPack32 = 85,
        A10R10G10B10_XRUNormPack32 = 86,
        D16_UNorm = 90,
        D24_UNorm = 91,
        D24_UNorm_S8_UInt = 92,
        D32_SFloat = 93,
        D32_SFloat_S8_UInt = 94,
        S8_UInt = 95,
        [Obsolete] RGB_DXT1_SRGB = 96,
        RGBA_DXT1_SRGB = 96,
        [Obsolete] RGB_DXT1_UNorm = 97,
        RGBA_DXT1_UNorm = 97,
        RGBA_DXT3_SRGB = 98,
        RGBA_DXT3_UNorm = 99,
        RGBA_DXT5_SRGB = 100,
        RGBA_DXT5_UNorm = 101,
        R_BC4_UNorm = 102,
        R_BC4_SNorm = 103,
        RG_BC5_UNorm = 104,
        RG_BC5_SNorm = 105,
        RGB_BC6H_UFloat = 106,
        RGB_BC6H_SFloat = 107,
        RGBA_BC7_SRGB = 108,
        RGBA_BC7_UNorm = 109,
        RGB_PVRTC_2Bpp_SRGB = 110,
        RGB_PVRTC_2Bpp_UNorm = 111,
        RGB_PVRTC_4Bpp_SRGB = 112,
        RGB_PVRTC_4Bpp_UNorm = 113,
        RGBA_PVRTC_2Bpp_SRGB = 114,
        RGBA_PVRTC_2Bpp_UNorm = 115,
        RGBA_PVRTC_4Bpp_SRGB = 116,
        RGBA_PVRTC_4Bpp_UNorm = 117,
        RGB_ETC_UNorm = 118,
        RGB_ETC2_SRGB = 119,
        RGB_ETC2_UNorm = 120,
        RGB_A1_ETC2_SRGB = 121,
        RGB_A1_ETC2_UNorm = 122,
        RGBA_ETC2_SRGB = 123,
        RGBA_ETC2_UNorm = 124,
        R_EAC_UNorm = 125,
        R_EAC_SNorm = 126,
        RG_EAC_UNorm = 127,
        RG_EAC_SNorm = 128,
        RGBA_ASTC4X4_SRGB = 129,
        RGBA_ASTC4X4_UNorm = 130,
        RGBA_ASTC5X5_SRGB = 131,
        RGBA_ASTC5X5_UNorm = 132,
        RGBA_ASTC6X6_SRGB = 133,
        RGBA_ASTC6X6_UNorm = 134,
        RGBA_ASTC8X8_SRGB = 135,
        RGBA_ASTC8X8_UNorm = 136,
        RGBA_ASTC10X10_SRGB = 137,
        RGBA_ASTC10X10_UNorm = 138,
        RGBA_ASTC12X12_SRGB = 139,
        RGBA_ASTC12X12_UNorm = 140,
        YUV2 = 141,
        [Obsolete] DepthAuto = 142,
        [Obsolete] ShadowAuto = 143,
        [Obsolete] VideoAuto = 144,
        RGBA_ASTC4X4_UFloat = 145,
        RGBA_ASTC5X5_UFloat = 146,
        RGBA_ASTC6X6_UFloat = 147,
        RGBA_ASTC8X8_UFloat = 148,
        RGBA_ASTC10X10_UFloat = 149,
        RGBA_ASTC12X12_UFloat = 150,
        D16_UNorm_S8_UInt = 151,
    }

    [Flags]
    public enum TextureCreationFlags
    {
        None = 0,
        MipChain = 1,
        DontInitializePixels = 4,
        Crunch = 64,
        DontUploadUponCreate = 1024,
        [Obsolete] IgnoreMipmapLimit = 2048,
    }

}


namespace UnityEngine.Rendering
{
    public enum AmbientMode
    {
        Skybox = 0,
        Trilight = 1,
        Flat = 3,
        Custom = 4,
    }

    public enum BlendMode
    {
        Zero = 0,
        One = 1,
        DstColor = 2,
        SrcColor = 3,
        OneMinusDstColor = 4,
        SrcAlpha = 5,
        OneMinusSrcColor = 6,
        DstAlpha = 7,
        OneMinusDstAlpha = 8,
        SrcAlphaSaturate = 9,
        OneMinusSrcAlpha = 10,
    }

    public enum BlendOp
    {
        Add = 0,
        Subtract = 1,
        ReverseSubtract = 2,
        Min = 3,
        Max = 4,
        LogicalClear = 5,
        LogicalSet = 6,
        LogicalCopy = 7,
        LogicalCopyInverted = 8,
        LogicalNoop = 9,
        LogicalInvert = 10,
        LogicalAnd = 11,
        LogicalNand = 12,
        LogicalOr = 13,
        LogicalNor = 14,
        LogicalXor = 15,
        LogicalEquivalence = 16,
        LogicalAndReverse = 17,
        LogicalAndInverted = 18,
        LogicalOrReverse = 19,
        LogicalOrInverted = 20,
        Multiply = 21,
        Screen = 22,
        Overlay = 23,
        Darken = 24,
        Lighten = 25,
        ColorDodge = 26,
        ColorBurn = 27,
        HardLight = 28,
        SoftLight = 29,
        Difference = 30,
        Exclusion = 31,
        HSLHue = 32,
        HSLSaturation = 33,
        HSLColor = 34,
        HSLLuminosity = 35,
    }

    public enum BuiltinRenderTextureType
    {
        PropertyName = -4,
        BufferPtr = -3,
        RenderTexture = -2,
        BindableTexture = -1,
        None = 0,
        CurrentActive = 1,
        CameraTarget = 2,
        Depth = 3,
        DepthNormals = 4,
        ResolvedDepth = 5,
        [Obsolete] PrepassNormalsSpec = 7,
        [Obsolete] PrepassLight = 8,
        [Obsolete] PrepassLightSpec = 9,
        GBuffer0 = 10,
        GBuffer1 = 11,
        GBuffer2 = 12,
        GBuffer3 = 13,
        Reflections = 14,
        MotionVectors = 15,
        GBuffer4 = 16,
        GBuffer5 = 17,
        GBuffer6 = 18,
        GBuffer7 = 19,
    }

    public enum CameraEvent
    {
        BeforeDepthTexture = 0,
        AfterDepthTexture = 1,
        BeforeDepthNormalsTexture = 2,
        AfterDepthNormalsTexture = 3,
        BeforeGBuffer = 4,
        AfterGBuffer = 5,
        BeforeLighting = 6,
        AfterLighting = 7,
        BeforeFinalPass = 8,
        AfterFinalPass = 9,
        BeforeForwardOpaque = 10,
        AfterForwardOpaque = 11,
        BeforeImageEffectsOpaque = 12,
        AfterImageEffectsOpaque = 13,
        BeforeSkybox = 14,
        AfterSkybox = 15,
        BeforeForwardAlpha = 16,
        AfterForwardAlpha = 17,
        BeforeImageEffects = 18,
        AfterImageEffects = 19,
        AfterEverything = 20,
        BeforeReflections = 21,
        AfterReflections = 22,
        BeforeHaloAndLensFlares = 23,
        AfterHaloAndLensFlares = 24,
    }

    [Flags]
    public enum ColorWriteMask
    {
        Alpha = 1,
        Blue = 2,
        Green = 4,
        Red = 8,
        All = 15,
    }

    public enum CompareFunction
    {
        Disabled = 0,
        Never = 1,
        Less = 2,
        Equal = 3,
        LessEqual = 4,
        Greater = 5,
        NotEqual = 6,
        GreaterEqual = 7,
        Always = 8,
    }

    [Flags]
    public enum CopyTextureSupport
    {
        None = 0,
        Basic = 1,
        Copy3D = 2,
        DifferentTypes = 4,
        TextureToRT = 8,
        RTToTexture = 16,
    }

    public enum CullMode
    {
        Off = 0,
        Front = 1,
        Back = 2,
    }

    public enum DefaultReflectionMode
    {
        Skybox = 0,
        Custom = 1,
    }

    public enum GraphicsDeviceType
    {
        [Obsolete] OpenGL2 = 0,
        [Obsolete] Direct3D9 = 1,
        Direct3D11 = 2,
        [Obsolete] PlayStation3 = 3,
        Null = 4,
        [Obsolete] Xbox360 = 6,
        [Obsolete] OpenGLES2 = 8,
        OpenGLES3 = 11,
        [Obsolete] PlayStationVita = 12,
        PlayStation4 = 13,
        XboxOne = 14,
        [Obsolete] PlayStationMobile = 15,
        Metal = 16,
        OpenGLCore = 17,
        Direct3D12 = 18,
        [Obsolete] N3DS = 19,
        Vulkan = 21,
        Switch = 22,
        XboxOneD3D12 = 23,
        GameCoreXboxOne = 24,
        [Obsolete] GameCoreScarlett = -1,
        GameCoreXboxSeries = 25,
        PlayStation5 = 26,
        PlayStation5NGGC = 27,
        WebGPU = 28,
        Switch2 = 29,
    }

    public enum IndexFormat
    {
        UInt16 = 0,
        UInt32 = 1,
    }

    public enum LightProbeUsage
    {
        Off = 0,
        BlendProbes = 1,
        UseProxyVolume = 2,
        CustomProvided = 4,
    }

    public enum LightShadowResolution
    {
        FromQualitySettings = -1,
        Low = 0,
        Medium = 1,
        High = 2,
        VeryHigh = 3,
    }

    [Flags]
    public enum MeshUpdateFlags
    {
        Default = 0,
        DontValidateIndices = 1,
        DontResetBoneBounds = 2,
        DontNotifyMeshUsers = 4,
        DontRecalculateBounds = 8,
    }

    public enum OpaqueSortMode
    {
        Default = 0,
        FrontToBack = 1,
        NoDistanceSort = 2,
    }

    public enum ReflectionProbeUsage
    {
        Off = 0,
        BlendProbes = 1,
        BlendProbesAndSkybox = 2,
        Simple = 3,
    }

    public enum RenderQueue
    {
        Background = 1000,
        Geometry = 2000,
        AlphaTest = 2450,
        GeometryLast = 2500,
        Transparent = 3000,
        Overlay = 4000,
    }

    [Flags]
    public enum ShaderPropertyFlags
    {
        None = 0,
        HideInInspector = 1,
        PerRendererData = 2,
        NoScaleOffset = 4,
        Normal = 8,
        HDR = 16,
        Gamma = 32,
        NonModifiableTextureData = 64,
        MainTexture = 128,
        MainColor = 256,
    }

    public enum ShaderPropertyType
    {
        Color = 0,
        Vector = 1,
        Float = 2,
        Range = 3,
        Texture = 4,
        Int = 5,
    }

    public enum ShadowCastingMode
    {
        Off = 0,
        On = 1,
        TwoSided = 2,
        ShadowsOnly = 3,
    }

    public enum ShadowSamplingMode
    {
        CompareDepths = 0,
        RawDepth = 1,
        None = 2,
    }

    [Flags]
    public enum SortingCriteria
    {
        None = 0,
        SortingLayer = 1,
        RenderQueue = 2,
        BackToFront = 4,
        QuantizedFrontToBack = 8,
        OptimizeStateChanges = 16,
        CanvasOrder = 32,
        RendererPriority = 64,
        CommonOpaque = 59,
        CommonTransparent = 23,
    }

    public enum StencilOp
    {
        Keep = 0,
        Zero = 1,
        Replace = 2,
        IncrementSaturate = 3,
        DecrementSaturate = 4,
        Invert = 5,
        IncrementWrap = 6,
        DecrementWrap = 7,
    }

    public enum TextureDimension
    {
        Unknown = -1,
        None = 0,
        Any = 1,
        Tex2D = 2,
        Tex3D = 3,
        Cube = 4,
        Tex2DArray = 5,
        CubeArray = 6,
    }

    public enum VertexAttribute
    {
        Position = 0,
        Normal = 1,
        Tangent = 2,
        Color = 3,
        TexCoord0 = 4,
        TexCoord1 = 5,
        TexCoord2 = 6,
        TexCoord3 = 7,
        TexCoord4 = 8,
        TexCoord5 = 9,
        TexCoord6 = 10,
        TexCoord7 = 11,
        BlendWeight = 12,
        BlendIndices = 13,
    }

    public enum VertexAttributeFormat
    {
        Float32 = 0,
        Float16 = 1,
        UNorm8 = 2,
        SNorm8 = 3,
        UNorm16 = 4,
        SNorm16 = 5,
        UInt8 = 6,
        SInt8 = 7,
        UInt16 = 8,
        SInt16 = 9,
        UInt32 = 10,
        SInt32 = 11,
    }

}

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode
    {
        Single = 0,
        Additive = 1,
    }

    [Flags]
    public enum LocalPhysicsMode
    {
        None = 0,
        Physics2D = 1,
        Physics3D = 2,
    }

    [Flags]
    public enum UnloadSceneOptions
    {
        None = 0,
        UnloadAllEmbeddedSceneObjects = 1,
    }

}

namespace UnityEditor
{
    public enum AssetPathToGUIDOptions
    {
        IncludeRecentlyDeletedAssets = 0,
        OnlyExistingAssets = 1,
    }

    [Flags]
    public enum ImportAssetOptions
    {
        Default = 0,
        ForceUpdate = 1,
        ForceSynchronousImport = 8,
        ImportRecursive = 256,
        DontDownloadFromCacheServer = 8192,
        ForceUncompressedImport = 16384,
    }

}

namespace UnityEngine
{
    public enum NPOTSupport
    {
        [Obsolete] None = 0,
        Restricted = 1,
        Full = 2,
    }

}


namespace UnityEditor
{
    public enum BuildTarget
    {
        StandaloneOSX = 2,
        [Obsolete] StandaloneOSXUniversal = 3,
        [Obsolete] StandaloneOSXIntel = 4,
        StandaloneWindows = 5,
        [Obsolete] WebPlayer = 6,
        [Obsolete] WebPlayerStreamed = 7,
        iOS = 9,
        [Obsolete] PS3 = 10,
        [Obsolete] XBOX360 = 11,
        Android = 13,
        [Obsolete] StandaloneLinux = 17,
        StandaloneWindows64 = 19,
        WebGL = 20,
        WSAPlayer = 21,
        StandaloneLinux64 = 24,
        [Obsolete] StandaloneLinuxUniversal = 25,
        [Obsolete] WP8Player = 26,
        [Obsolete] StandaloneOSXIntel64 = 27,
        [Obsolete] BlackBerry = 28,
        [Obsolete] Tizen = 29,
        [Obsolete] PSP2 = 30,
        PS4 = 31,
        [Obsolete] PSM = 32,
        XboxOne = 33,
        [Obsolete] SamsungTV = 34,
        [Obsolete] N3DS = 35,
        [Obsolete] WiiU = 36,
        tvOS = 37,
        Switch = 38,
        [Obsolete] Lumin = 39,
        [Obsolete] Stadia = 40,
        [Obsolete] CloudRendering = 41,
        LinuxHeadlessSimulation = 41,
        [Obsolete] GameCoreScarlett = 42,
        GameCoreXboxSeries = 42,
        GameCoreXboxOne = 43,
        PS5 = 44,
        EmbeddedLinux = 45,
        QNX = 46,
        VisionOS = 47,
        Switch2 = 48,
        Kepler = 49,
        [Obsolete] iPhone = -1,
        [Obsolete] BB10 = -1,
        [Obsolete] MetroPlayer = -1,
        NoTarget = -2,
    }

    public enum BuildTargetGroup
    {
        Unknown = 0,
        Standalone = 1,
        [Obsolete] WebPlayer = 2,
        [Obsolete] iPhone = 4,
        iOS = 4,
        [Obsolete] PS3 = 5,
        [Obsolete] XBOX360 = 6,
        Android = 7,
        WebGL = 13,
        WSA = 14,
        [Obsolete] Metro = 14,
        [Obsolete] WP8 = 15,
        [Obsolete] BlackBerry = 16,
        [Obsolete] Tizen = 17,
        [Obsolete] PSP2 = 18,
        PS4 = 19,
        [Obsolete] PSM = 20,
        XboxOne = 21,
        [Obsolete] SamsungTV = 22,
        [Obsolete] N3DS = 23,
        [Obsolete] WiiU = 24,
        tvOS = 25,
        [Obsolete] Facebook = 26,
        Switch = 27,
        [Obsolete] Lumin = 28,
        [Obsolete] Stadia = 29,
        [Obsolete] CloudRendering = 30,
        LinuxHeadlessSimulation = 30,
        [Obsolete] GameCoreScarlett = 31,
        GameCoreXboxSeries = 31,
        GameCoreXboxOne = 32,
        PS5 = 33,
        EmbeddedLinux = 34,
        QNX = 35,
        VisionOS = 36,
        Switch2 = 37,
        Kepler = 38,
    }

    public enum PauseState
    {
        Paused = 0,
        Unpaused = 1,
    }

    public enum PlayModeStateChange
    {
        EnteredEditMode = 0,
        ExitingEditMode = 1,
        EnteredPlayMode = 2,
        ExitingPlayMode = 3,
    }

    public enum PrefabAssetType
    {
        NotAPrefab = 0,
        Regular = 1,
        Model = 2,
        Variant = 3,
        MissingAsset = 4,
    }

    public enum PrefabInstanceStatus
    {
        NotAPrefab = 0,
        Connected = 1,
        [Obsolete] Disconnected = 2,
        MissingAsset = 3,
    }

}

