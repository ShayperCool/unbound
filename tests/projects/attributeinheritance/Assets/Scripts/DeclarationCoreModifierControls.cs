using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Animations;

public static class DeclarationCoreModifierControls
{
    static readonly string[][] Declarations = {
        new string[] { "Unity.Properties.PropertyCollection`1+Enumerator", "UnityEngine.PropertiesModule", "M:get_Current`0():Unity.Properties.IProperty`1<!0>" },
        new string[] { "UnityEngine.AI.NavMeshDataInstance", "UnityEngine.AIModule", "M:get_id`0():System.Int32" },
        new string[] { "UnityEngine.AI.NavMeshLinkInstance", "UnityEngine.AIModule", "M:get_id`0():System.Int32" },
        new string[] { "UnityEngine.AI.NavMeshQueryFilter", "UnityEngine.AIModule", "M:get_agentTypeID`0():System.Int32" },
        new string[] { "UnityEngine.AI.NavMeshQueryFilter", "UnityEngine.AIModule", "M:get_areaMask`0():System.Int32" },
        new string[] { "UnityEngine.AI.NavMeshQueryFilter", "UnityEngine.AIModule", "M:get_costs`0():System.Single[]" },
        new string[] { "UnityEngine.AnimationClipPair", "UnityEngine.AnimationModule", "type" },
        new string[] { "UnityEngine.Animations.ConstraintSource", "UnityEngine.AnimationModule", "M:get_sourceTransform`0():UnityEngine.Transform" },
        new string[] { "UnityEngine.Animations.ConstraintSource", "UnityEngine.AnimationModule", "M:get_weight`0():System.Single" },
        new string[] { "UnityEngine.Animations.ConstraintSource", "UnityEngine.AnimationModule", "type" },
        new string[] { "UnityEngine.ApplicationMemoryUsageChange", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.BuildCompression", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.CombineInstance", "UnityEngine.CoreModule", "M:get_lightmapScaleOffset`0():UnityEngine.Vector4" },
        new string[] { "UnityEngine.CombineInstance", "UnityEngine.CoreModule", "M:get_mesh`0():UnityEngine.Mesh" },
        new string[] { "UnityEngine.CombineInstance", "UnityEngine.CoreModule", "M:get_realtimeLightmapScaleOffset`0():UnityEngine.Vector4" },
        new string[] { "UnityEngine.CombineInstance", "UnityEngine.CoreModule", "M:get_subMeshIndex`0():System.Int32" },
        new string[] { "UnityEngine.CombineInstance", "UnityEngine.CoreModule", "M:get_transform`0():UnityEngine.Matrix4x4" },
        new string[] { "UnityEngine.Events.PersistentListenerMode", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.ExcludeFromPresetAttribute", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.GlobalJavaObjectRef", "UnityEngine.AndroidJNIModule", "type" },
        new string[] { "UnityEngine.HideInInspector", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.InspectorOrderAttribute", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Internal.DefaultValueAttribute", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.JointAngleLimits2D", "UnityEngine.Physics2DModule", "M:get_max`0():System.Single" },
        new string[] { "UnityEngine.JointAngleLimits2D", "UnityEngine.Physics2DModule", "M:get_min`0():System.Single" },
        new string[] { "UnityEngine.JointMotor2D", "UnityEngine.Physics2DModule", "M:get_maxMotorTorque`0():System.Single" },
        new string[] { "UnityEngine.JointMotor2D", "UnityEngine.Physics2DModule", "M:get_motorSpeed`0():System.Single" },
        new string[] { "UnityEngine.JointSuspension2D", "UnityEngine.Physics2DModule", "M:get_angle`0():System.Single" },
        new string[] { "UnityEngine.JointSuspension2D", "UnityEngine.Physics2DModule", "M:get_dampingRatio`0():System.Single" },
        new string[] { "UnityEngine.JointSuspension2D", "UnityEngine.Physics2DModule", "M:get_frequency`0():System.Single" },
        new string[] { "UnityEngine.JointTranslationLimits2D", "UnityEngine.Physics2DModule", "M:get_max`0():System.Single" },
        new string[] { "UnityEngine.JointTranslationLimits2D", "UnityEngine.Physics2DModule", "M:get_min`0():System.Single" },
        new string[] { "UnityEngine.MissingComponentException", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.MissingReferenceException", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Object", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.PlayerPrefsException", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.RenderBuffer", "UnityEngine.CoreModule", "M:get_loadAction`0():UnityEngine.Rendering.RenderBufferLoadAction" },
        new string[] { "UnityEngine.RenderBuffer", "UnityEngine.CoreModule", "M:get_storeAction`0():UnityEngine.Rendering.RenderBufferStoreAction" },
        new string[] { "UnityEngine.RenderTextureDescriptor", "UnityEngine.CoreModule", "M:get_depthStencilFormat`0():UnityEngine.Experimental.Rendering.GraphicsFormat" },
        new string[] { "UnityEngine.Rendering.AsyncGPUReadbackRequest", "UnityEngine.CoreModule", "M:get_forcePlayerLoopUpdate`0():System.Boolean" },
        new string[] { "UnityEngine.Resources", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.SceneManagement.Scene", "UnityEngine.CoreModule", "M:get_isSubScene`0():System.Boolean" },
        new string[] { "UnityEngine.SecondarySpriteTexture", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.Texture", "UnityEngine.CoreModule", "M:get_graphicsFormat`0():UnityEngine.Experimental.Rendering.GraphicsFormat" },
        new string[] { "UnityEngine.Texture", "UnityEngine.CoreModule", "P:graphicsFormat():UnityEngine.Experimental.Rendering.GraphicsFormat" },
        new string[] { "UnityEngine.UnassignedReferenceException", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.UnityException", "UnityEngine.CoreModule", "type" },
    };
    public static void Record(Action<string, object> row)
    {
        foreach (var d in Declarations)
        {
            var member = DeclarationSignatures.Find(d[0], d[1], d[2]);
            string value;
            if (member is Type)
            {
                var type = (Type)member;
                value = ((type.Attributes & TypeAttributes.Serializable) != 0) + ":" + type.IsSealed + ":" + type.IsAbstract + ":" + DeclarationSignatures.IsReadOnly(type);
            }
            else if (member is FieldInfo) value = member.IsDefined(typeof(SerializeField), false).ToString();
            else
            {
                var method = member as MethodInfo ?? ((PropertyInfo)member).GetGetMethod(true);
                value = method.IsVirtual + ":" + method.IsFinal + ":" + method.IsAbstract + ":" + DeclarationSignatures.IsReadOnly(method);
            }
            row("core.modifier." + d[0] + "." + d[2], value);
        }
#pragma warning disable 0618
        row("core.serialize.clip-pair", JsonUtility.ToJson(new AnimationClipPair()));
#pragma warning restore 0618
        row("core.serialize.secondary", JsonUtility.ToJson(new SecondarySpriteTexture { name = "detail", texture = null }));
        row("core.serialize.compression", JsonUtility.ToJson(BuildCompression.LZ4));
        row("core.serialize.constraint", JsonUtility.ToJson(new ConstraintSource { weight = 0.75f }));
        var limits = new JointAngleLimits2D { min = -13.5f, max = 29 };
        var copy = limits;
        limits.min = 1;
        row("core.copy.limits.copy-min", copy.min);
        row("core.copy.limits.changed-min", limits.min);
        var motor = new JointMotor2D { motorSpeed = float.NaN, maxMotorTorque = float.PositiveInfinity };
        row("core.copy.motor.nan", float.IsNaN(motor.motorSpeed));
        row("core.copy.motor.infinity", float.IsPositiveInfinity(motor.maxMotorTorque));
    }
}
