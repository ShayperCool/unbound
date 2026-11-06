using System;
using System.Collections.Generic;

namespace Unbound.ApiTools;

/// <summary>
/// What a generated stub returns where its type's default (0, false, null, an enum's 0) is not the
/// Editor's answer, so the wrong value does not keep flowing after the stub's one warning. Keyed by
/// release family and member id (<c>Namespace.Type.member</c>, as the stub's warning names it; a
/// method's id covers its overloads). Each value is a C# expression of the member's type, written
/// with <c>global::</c> names.
///
/// Only constants belong here: what the Editor answers for every project and machine, measured in
/// Play mode by <c>tests/projects/stubdefaults</c>. A member whose answer depends on the project,
/// the scene or other API state needs a real implementation instead. gen-stubs reports an entry no
/// stub returns any more (implemented by hand, renamed) as a conflict. A settable member keeps
/// answering its constant after a set (the setter is a stub too, and shares the getter's warning).
/// </summary>
public static class StubDefaults
{
    // Unity 6000.0 (measured on 6000.0.71f1, Linux Editor, Play mode).
    static readonly Dictionary<string, string> Unity6000_0 = new(StringComparer.Ordinal)
    {
        // Outside OnDrawGizmos the color is white; the matrix is the zero matrix (the default).
        ["UnityEngine.Gizmos.color"] = "global::UnityEngine.Color.white",
        // 0.29999998f, one ulp under 0.3f.
        ["UnityEngine.Gizmos.probeSize"] = "0.29999998f",
        // Measured in a scene without realtime GI (nothing to converge); indirectScale is the
        // scene's Indirect Intensity, not a constant.
        ["UnityEngine.DynamicGI.updateThreshold"] = "1f",
        ["UnityEngine.DynamicGI.materialUpdateTimeSlice"] = "8",
        ["UnityEngine.DynamicGI.isConverged"] = "true",
        ["UnityEngine.Scripting.GarbageCollector.GCMode"] = "global::UnityEngine.Scripting.GarbageCollector.Mode.Enabled",
        ["UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds"] = "3000000UL",
        ["UnityEngine.ReflectionProbe.minBakedCubemapResolution"] = "16",
        ["UnityEngine.ReflectionProbe.maxBakedCubemapResolution"] = "2048",
        // The Editor shows no splash screen.
        ["UnityEngine.Rendering.SplashScreen.isFinished"] = "true",
        ["UnityEngine.Animations.MuscleHandle.muscleHandleCount"] = "95",
        ["UnityEngine.Caching.compressionEnabled"] = "true",
        ["UnityEngine.QualitySettings.maxQueuedFrames"] = "2",
        ["UnityEngine.Graphics.activeTier"] = "global::UnityEngine.Rendering.GraphicsTier.Tier3",
        ["UnityEngine.Accessibility.AccessibilitySettings.fontScale"] = "1f",
        ["UnityEngine.AvatarMask.humanoidBodyPartCount"] = "13",
        // Obsolete members whose message states the answer.
        ["UnityEngine.SystemInfo.supportsRenderTextures"] = "true",
        ["UnityEngine.SystemInfo.supportsStencil"] = "1",
        ["UnityEngine.SystemInfo.supportsVertexPrograms"] = "true",
        ["UnityEngine.SystemInfo.graphicsPixelFillrate"] = "-1",
        ["UnityEngine.RenderTexture.enabled"] = "true",
    };

    static readonly Dictionary<string, string> None = new(StringComparer.Ordinal);

    /// <summary>The table of a release family (<c>6000.0</c>); empty for a family without one.</summary>
    public static IReadOnlyDictionary<string, string> For(string family) => family switch
    {
        "6000.0" => Unity6000_0,
        _ => None,
    };
}
