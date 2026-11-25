using System;
using UnityEngine;

namespace UnityEditor
{
    public partial class AnimationUtility
    {
        /// <summary>Sets one key's left mode bits only: the broken bit, the tangents and the
        /// weights stay, and the mode is not masked (measured: tests/projects/animinternal key.*;
        /// the VFX Graph editor calls these as it imports).</summary>
        internal static void SetKeyLeftTangentMode(ref Keyframe key, TangentMode tangentMode) =>
            key.tangentModeInternal = (key.tangentModeInternal & ~(k_ModeMask << k_LeftShift)) | ((int)tangentMode << k_LeftShift);

        internal static void SetKeyRightTangentMode(ref Keyframe key, TangentMode tangentMode) =>
            key.tangentModeInternal = (key.tangentModeInternal & ~(k_ModeMask << k_RightShift)) | ((int)tangentMode << k_RightShift);

        internal static void SetKeyBroken(ref Keyframe key, bool broken) =>
            key.tangentModeInternal = broken ? key.tangentModeInternal | k_BrokenMask : key.tangentModeInternal & ~k_BrokenMask;

        /// <summary>The rotation's angles for the order nearest the hint (the Timeline editor keeps
        /// its offsets' Euler angles with it); see <see cref="ClosestEulerAngles"/>.</summary>
        internal static Vector3 GetClosestEuler(Quaternion q, Vector3 eulerHint, RotationOrder rotationOrder) =>
            ClosestEulerAngles.Of(q, eulerHint, rotationOrder);
    }

    public partial class AnimationMode
    {
        static bool s_InAnimationRecordMode;

        internal static event Action onAnimationRecordingStart;
        internal static event Action onAnimationRecordingStop;

        /// <summary>Unity's C#: a flag and its events, raised on every call, apart from Animation
        /// mode (measured: tests/projects/animinternal record.*).</summary>
        internal static bool InAnimationRecording() => s_InAnimationRecordMode;

        internal static void StartAnimationRecording()
        {
            s_InAnimationRecordMode = true;
            onAnimationRecordingStart?.Invoke();
        }

        internal static void StopAnimationRecording()
        {
            s_InAnimationRecordMode = false;
            onAnimationRecordingStop?.Invoke();
        }
    }
}
