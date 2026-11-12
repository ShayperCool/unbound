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
/// Only fixed initial answers and measured setter rules belong here, measured in
/// Play mode by <c>tests/projects/stubdefaults</c> or <c>tests/projects/defaultstate</c>. A member whose answer depends on the project,
/// the scene or other API state needs a real implementation instead. gen-stubs reports an entry no
/// stub returns any more (implemented by hand, renamed) as an error. Set expressions describe
/// measured setter storage, clamps or rejection; a missing expression leaves the value unchanged.
/// </summary>
public sealed record StubDefault(string Value, string? SetExpression = null)
{
    public static implicit operator StubDefault(string value) => new(value);
}

public static class StubDefaults
{
    // Unity 6000.0 (measured on 6000.0.71f1, Linux Editor, Play mode).
    static readonly Dictionary<string, StubDefault> Unity6000_0 = new(StringComparer.Ordinal)
    {
        // Measured in a scene without realtime GI; indirectScale is the scene's Indirect
        // Intensity, not a constant.
        ["UnityEngine.DynamicGI.materialUpdateTimeSlice"] = new("8", "global::System.Math.Max(0, value)"),
        // The Editor rejects modes other than Enabled, including undefined enum values.
        ["UnityEngine.Scripting.GarbageCollector.GCMode"] = new("global::UnityEngine.Scripting.GarbageCollector.Mode.Enabled",
            "value == global::UnityEngine.Scripting.GarbageCollector.Mode.Enabled ? value : throw new global::System.InvalidOperationException(\"Garbage collector mode cannot be changed in the Editor\")"),
        ["UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds"] = new("3000000UL", "value"),
        ["UnityEngine.ReflectionProbe.minBakedCubemapResolution"] = "16",
        ["UnityEngine.ReflectionProbe.maxBakedCubemapResolution"] = "2048",
        // The Editor shows no splash screen.
        ["UnityEngine.Rendering.SplashScreen.isFinished"] = "true",
        ["UnityEngine.QualitySettings.maxQueuedFrames"] = new("2", "value"),
        ["UnityEngine.Graphics.activeTier"] = new("global::UnityEngine.Rendering.GraphicsTier.Tier3", "value"),
        ["UnityEngine.Accessibility.AccessibilitySettings.fontScale"] = "1f",
        ["UnityEngine.AvatarMask.humanoidBodyPartCount"] = "13",
        // Obsolete members whose message states the answer.
        ["UnityEngine.SystemInfo.supportsRenderTextures"] = "true",
        ["UnityEngine.SystemInfo.supportsStencil"] = "1",
        ["UnityEngine.SystemInfo.supportsVertexPrograms"] = "true",
        ["UnityEngine.SystemInfo.graphicsPixelFillrate"] = "-1",
        ["UnityEngine.RenderTexture.enabled"] = "true",
        // Removed settings ignore writes, including values outside their old defaults (defaultstate).
        ["UnityEngine.MeshCollider.smoothSphereCollisions"] = "true",
        // Legacy UV channel alias remains null even after writes (defaultstate).
        ["UnityEngine.Mesh.uv1"] = "(global::UnityEngine.Vector2[])null",
    };

    static readonly Dictionary<string, StubDefault> None = new(StringComparer.Ordinal);

    /// <summary>The table of a release family (<c>6000.0</c>); empty for a family without one.</summary>
    public static IReadOnlyDictionary<string, StubDefault> For(string family) => family switch
    {
        "6000.0" => Unity6000_0,
        _ => None,
    };
}
