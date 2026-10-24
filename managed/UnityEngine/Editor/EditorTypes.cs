using System;
using UnityEditor.Rendering;

// Value types and interfaces the hand-written Editor API names in its signatures (the engine must
// compile without the generated stubs).

namespace UnityEngine.Analytics
{
    public enum AnalyticsResult
    {
        Ok = 0,
        NotInitialized = 1,
        AnalyticsDisabled = 2,
        TooManyItems = 3,
        SizeLimitReached = 4,
        TooManyRequests = 5,
        InvalidData = 6,
        UnsupportedPlatform = 7,
    }

    public partial interface IAnalytic
    {
        bool TryGatherData(out IData data, out Exception error);

        public partial interface IData
        {
        }
    }
}

namespace UnityEditor
{
    public enum CoppaCompliance
    {
        CoppaUndefined = 0,
        CoppaCompliant = 1,
        CoppaNotCompliant = 2,
    }

    public enum SelectionMode
    {
        Unfiltered = 0,
        TopLevel = 1,
        Deep = 2,
        ExcludePrefab = 4,
        Editable = 8,
        Assets = 16,
        DeepAssets = 32,
        [Obsolete("'OnlyUserModifiable' is obsolete. Use 'Editable' instead. (UnityUpgradeable) -> Editable")] OnlyUserModifiable = 8,
    }

    public static partial class Progress
    {
        public enum Status
        {
            Running = 0,
            Succeeded = 1,
            Failed = 2,
            Canceled = 3,
            Paused = 4,
        }

        [Flags]
        public enum Options
        {
            None = 0,
            Sticky = 1,
            Indefinite = 2,
            Synchronous = 4,
            Managed = 8,
            Unmanaged = 16,
        }

        public enum TimeDisplayMode
        {
            NoTimeShown = 0,
            ShowRunningTime = 1,
            ShowRemainingTime = 2,
        }

        public enum Priority
        {
            Unresponsive = 0,
            Idle = 1,
            Low = 2,
            Normal = 6,
            High = 10,
        }
    }

    public partial struct ShaderMessage : IEquatable<ShaderMessage>
    {
        public ShaderMessage(string msg, ShaderCompilerMessageSeverity sev = ShaderCompilerMessageSeverity.Error)
        {
            message = msg;
            messageDetails = string.Empty;
            file = string.Empty;
            line = 0;
            platform = ShaderCompilerPlatform.None;
            severity = sev;
        }

        public string message { get; }
        public string messageDetails { get; }
        public string file { get; }
        public int line { get; }
        public ShaderCompilerPlatform platform { get; }
        public ShaderCompilerMessageSeverity severity { get; }

        public bool Equals(ShaderMessage other) =>
            message == other.message && messageDetails == other.messageDetails && file == other.file
            && line == other.line && platform == other.platform && severity == other.severity;

        public override bool Equals(object obj) => obj is ShaderMessage other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(message, messageDetails, file, line, platform, severity);

        public static bool operator ==(ShaderMessage left, ShaderMessage right) => left.Equals(right);

        public static bool operator !=(ShaderMessage left, ShaderMessage right) => !left.Equals(right);
    }
}

namespace UnityEditor.Rendering
{
    public enum ShaderCompilerPlatform
    {
        None = 0,
        D3D = 4,
        [Obsolete("OpenGL ES 2.0 is no longer supported since Unity 2023.1")] GLES20 = 5,
        GLES3x = 9,
        PS4 = 11,
        XboxOneD3D11 = 12,
        Metal = 14,
        OpenGLCore = 15,
        Vulkan = 18,
        Switch = 19,
        XboxOneD3D12 = 20,
        GameCoreXboxOne = 21,
        GameCoreXboxSeries = 22,
        PS5 = 23,
        PS5NGGC = 24,
        [Obsolete("GameCore  is deprecated, please use GameCoreXboxOne (UnityUpgradable) -> GameCoreXboxOne ")] GameCore = 25,
        WebGPU = 26,
        Switch2 = 27,
    }

    public enum ShaderCompilerMessageSeverity
    {
        Error = 0,
        Warning = 1,
    }
}

namespace UnityEditor.MPE
{
    public enum ProcessLevel
    {
        [Obsolete("... (UnityUpgradable) -> Undefined")] UMP_UNDEFINED = 0,
        Undefined = 0,
        [Obsolete("... (UnityUpgradable) -> Main")] UMP_MASTER = 1,
        Main = 1,
        [Obsolete("... (UnityUpgradable) -> Secondary")] UMP_SLAVE = 2,
        [Obsolete("... (UnityUpgradable) -> Secondary")] Slave = 2,
        Secondary = 2,
    }
}
