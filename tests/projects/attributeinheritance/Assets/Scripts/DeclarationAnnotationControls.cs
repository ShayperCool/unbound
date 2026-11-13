using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class DeclarationAnnotationControls
{
    static readonly string[][] Declarations = {
        new string[] { "UnityEngine.AI.NavMeshObstacle", "UnityEngine.AIModule", "type" },
        new string[] { "UnityEngine.AI.NavMeshAgent", "UnityEngine.AIModule", "type" },
        new string[] { "UnityEngine.AnimatorOverrideController", "UnityEngine.AnimationModule", "type" },
        new string[] { "UnityEngine.Audio.AudioMixer", "UnityEngine.AudioModule", "type" },
        new string[] { "Unity.Baselib.LowLevel.Binding+Baselib_FileIO_OpenFlags", "UnityEngine.CoreModule", "type" },
        new string[] { "Unity.Baselib.LowLevel.Binding+Baselib_Socket_PollEvents", "UnityEngine.CoreModule", "type" },
        new string[] { "Unity.Collections.LowLevel.Unsafe.BurstLike+SharedStatic", "UnityEngine.CoreModule", "M:static GetOrCreateSharedStaticInternal`0(System.Int64,System.Int64,System.UInt32,System.UInt32):System.Void*" },
        new string[] { "Unity.Collections.NativeArrayOptions", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.GameObject", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.RefreshRate", "UnityEngine.CoreModule", "F:numerator:System.UInt32" },
        new string[] { "UnityEngine.RefreshRate", "UnityEngine.CoreModule", "F:denominator:System.UInt32" },
        new string[] { "UnityEngine.DisplayInfo", "UnityEngine.CoreModule", "F:handle:System.UInt64" },
        new string[] { "UnityEngine.DisplayInfo", "UnityEngine.CoreModule", "F:width:System.Int32" },
        new string[] { "UnityEngine.DisplayInfo", "UnityEngine.CoreModule", "F:height:System.Int32" },
        new string[] { "UnityEngine.DisplayInfo", "UnityEngine.CoreModule", "F:refreshRate:UnityEngine.RefreshRate" },
        new string[] { "UnityEngine.DisplayInfo", "UnityEngine.CoreModule", "F:workArea:UnityEngine.RectInt" },
        new string[] { "UnityEngine.DisplayInfo", "UnityEngine.CoreModule", "F:name:System.String" },
        new string[] { "UnityEngine.GamepadSpeakerOutputType", "UnityEngine.AudioModule", "type" },
        new string[] { "UnityEngine.HideFlags", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Object", "UnityEngine.CoreModule", "M:static FindObjectsOfType`0(System.Type,System.Boolean):UnityEngine.Object[]" },
        new string[] { "UnityEngine.Object", "UnityEngine.CoreModule", "M:static FindObjectOfType`0(System.Type):UnityEngine.Object" },
        new string[] { "UnityEngine.Object", "UnityEngine.CoreModule", "M:static FindObjectOfType`0(System.Type,System.Boolean):UnityEngine.Object" },
        new string[] { "UnityEditor.AssetImporter", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.AssetImporters.ScriptedImporter", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.LogMessageFlags", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.SelectionMode", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.Build.Profile.BuildProfile", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.MonoImporter", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.MonoScript", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.PluginImporter", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEngine.Light", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Camera", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Renderer", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.MeshFilter", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.SpriteRenderer", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Cubemap", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Halo", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Sprite", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.MaterialPropertyType", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Rendering.SortingGroup", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Texture2DArray", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.CubemapArray", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Texture3D", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.GUISkin", "UnityEngine.IMGUIModule", "type" },
        new string[] { "UnityEngine.Collider", "UnityEngine.PhysicsModule", "type" },
        new string[] { "UnityEngine.BoxCollider", "UnityEngine.PhysicsModule", "type" },
        new string[] { "UnityEngine.SphereCollider", "UnityEngine.PhysicsModule", "type" },
        new string[] { "UnityEngine.CapsuleCollider", "UnityEngine.PhysicsModule", "type" },
        new string[] { "UnityEngine.MeshCollider", "UnityEngine.PhysicsModule", "type" },
        new string[] { "UnityEngine.Joint", "UnityEngine.PhysicsModule", "type" },
        new string[] { "UnityEngine.FixedJoint", "UnityEngine.PhysicsModule", "type" },
        new string[] { "UnityEngine.HingeJoint", "UnityEngine.PhysicsModule", "type" },
        new string[] { "UnityEngine.SpringJoint", "UnityEngine.PhysicsModule", "type" },
        new string[] { "UnityEngine.CharacterJoint", "UnityEngine.PhysicsModule", "type" },
        new string[] { "UnityEngine.ConfigurableJoint", "UnityEngine.PhysicsModule", "type" },
        new string[] { "UnityEngine.Playables.PlayableAsset", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Playables.PlayableDirector", "UnityEngine.DirectorModule", "type" },
        new string[] { "Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle", "UnityEngine.CoreModule", "M:static GetByName__Unmanaged`0(Unity.Profiling.ProfilerCategory,System.Byte*,System.Int32):Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle" },
        new string[] { "Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility", "UnityEngine.CoreModule", "M:static CreateCategory__Unmanaged`0(System.Byte*,System.Int32,Unity.Profiling.ProfilerCategoryColor):System.UInt16" },
        new string[] { "Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility", "UnityEngine.CoreModule", "M:static CreateMarker__Unmanaged`0(System.Byte*,System.Int32,System.UInt16,Unity.Profiling.LowLevel.MarkerFlags,System.Int32):System.IntPtr" },
        new string[] { "Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility", "UnityEngine.CoreModule", "M:static SetMarkerMetadata__Unmanaged`0(System.IntPtr,System.Int32,System.Byte*,System.Int32,System.Byte,System.Byte):System.Void" },
        new string[] { "Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility", "UnityEngine.CoreModule", "M:static CreateCounterValue__Unmanaged`0(out System.IntPtr,System.Byte*,System.Int32,System.UInt16,Unity.Profiling.LowLevel.MarkerFlags,System.Byte,System.Byte,System.Int32,Unity.Profiling.ProfilerCounterOptions):System.Void*" },
        new string[] { "UnityEngine.AssetBundle", "UnityEngine.AssetBundleModule", "type" },
        new string[] { "UnityEngine.Animations.AimConstraint", "UnityEngine.AnimationModule", "type" },
        new string[] { "UnityEngine.Animations.LookAtConstraint", "UnityEngine.AnimationModule", "type" },
        new string[] { "UnityEngine.Animations.ParentConstraint", "UnityEngine.AnimationModule", "type" },
        new string[] { "UnityEngine.Animations.PositionConstraint", "UnityEngine.AnimationModule", "type" },
        new string[] { "UnityEngine.Animations.RotationConstraint", "UnityEngine.AnimationModule", "type" },
        new string[] { "UnityEngine.Animations.ScaleConstraint", "UnityEngine.AnimationModule", "type" },
        new string[] { "UnityEngine.TextCore.Text.FontAsset", "UnityEngine.TextCoreTextEngineModule", "type" },
        new string[] { "UnityEngine.TextCore.Text.SpriteAsset", "UnityEngine.TextCoreTextEngineModule", "type" },
        new string[] { "UnityEngine.TextCore.Text.TextStyleSheet", "UnityEngine.TextCoreTextEngineModule", "type" },
        new string[] { "UnityEngine.TextCore.Text.TextSettings", "UnityEngine.TextCoreTextEngineModule", "type" },
    };
    static readonly string[] FlagTypes = { "Unity.Baselib.LowLevel.Binding+Baselib_FileIO_OpenFlags, UnityEngine.CoreModule", "Unity.Baselib.LowLevel.Binding+Baselib_Socket_PollEvents, UnityEngine.CoreModule", "Unity.Collections.NativeArrayOptions, UnityEngine.CoreModule", "UnityEngine.GamepadSpeakerOutputType, UnityEngine.AudioModule", "UnityEngine.HideFlags, UnityEngine.CoreModule", "UnityEditor.LogMessageFlags, UnityEditor.CoreModule", "UnityEditor.SelectionMode, UnityEditor.CoreModule", "UnityEngine.MaterialPropertyType, UnityEngine.CoreModule" };
    static readonly HashSet<string> Kinds = new HashSet<string> { "System.FlagsAttribute", "UnityEngine.RequireComponent", "UnityEngine.ExcludeFromPresetAttribute", "UnityEngine.HelpURLAttribute", "UnityEngine.Scripting.RequiredMemberAttribute", "UnityEngine.Scripting.PreserveAttribute", "UnityEngine.AssetFileNameExtensionAttribute", "UnityEngine.ExecuteInEditMode", "System.ObsoleteAttribute" };
    static string Value(CustomAttributeTypedArgument value)
    {
        if (value.Value == null) return "null";
        if (value.ArgumentType.IsArray) return "[" + string.Join(",", ((IEnumerable<CustomAttributeTypedArgument>)value.Value).Select(Value).ToArray()) + "]";
        if (value.Value is Type) return ((Type)value.Value).FullName;
        return Convert.ToString(value.Value, CultureInfo.InvariantCulture);
    }
    public static string Attributes(IEnumerable<CustomAttributeData> attributes, ISet<string> kinds)
    {
        var values = attributes.Where(a => kinds.Contains(a.AttributeType.FullName)).Select(a => a.AttributeType.FullName + "(" + string.Join(",", a.ConstructorArguments.Select(Value).ToArray()) + "){" + string.Join(",", a.NamedArguments.OrderBy(n => n.MemberName, StringComparer.Ordinal).Select(n => n.MemberName + "=" + Value(n.TypedValue)).ToArray()) + "}").OrderBy(v => v, StringComparer.Ordinal).ToArray();
        return string.Join(";", values);
    }
    public static void Record(Action<string, object> row)
    {
        foreach (var d in Declarations)
        {
            var member = DeclarationSignatures.Find(d[0], d[1], d[2]);
            row("annotation." + d[0] + "." + d[2], Attributes(member.GetCustomAttributesData(), Kinds));
        }

        foreach (string typeName in FlagTypes)
        {
            Type type = Type.GetType(typeName, true);
            foreach (int value in new[] { 0, 1, 3, 5, 7, 15 })
                row("annotation.flags." + type.FullName + "." + value, Enum.ToObject(type, value).ToString());
        }
        var jointOwner = new UnityEngine.GameObject("annotation-joint");
        var lightOwner = new UnityEngine.GameObject("annotation-light");
        var cameraOwner = new UnityEngine.GameObject("annotation-camera");
        try
        {
            row("annotation.require.joint-before-rigidbody", jointOwner.GetComponent<UnityEngine.Rigidbody>() == null);
            jointOwner.AddComponent<UnityEngine.FixedJoint>();
            row("annotation.require.joint-rigidbody", jointOwner.GetComponent<UnityEngine.Rigidbody>() != null);
            jointOwner.AddComponent<UnityEngine.HingeJoint>();
            row("annotation.require.shared-rigidbody-count", jointOwner.GetComponents<UnityEngine.Rigidbody>().Length);
            lightOwner.AddComponent<UnityEngine.Light>();
            row("annotation.require.light-transform-count", lightOwner.GetComponents<UnityEngine.Transform>().Length);
            cameraOwner.AddComponent<UnityEngine.Camera>();
            row("annotation.require.camera-control-rigidbody", cameraOwner.GetComponent<UnityEngine.Rigidbody>() == null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(jointOwner);
            UnityEngine.Object.DestroyImmediate(lightOwner);
            UnityEngine.Object.DestroyImmediate(cameraOwner);
        }
    }
}
