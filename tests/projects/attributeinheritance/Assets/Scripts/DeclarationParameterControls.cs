using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public static class DeclarationParameterControls
{
    static readonly string[][] Declarations = {
        new string[] { "UnityEditor.GlobalObjectId", "UnityEditor.CoreModule", "M:static GetGlobalObjectIdsSlow`0(System.Int32[],UnityEditor.GlobalObjectId[]):System.Void", "1" },
        new string[] { "UnityEditor.GlobalObjectId", "UnityEditor.CoreModule", "M:static GetGlobalObjectIdsSlow`0(UnityEngine.Object[],UnityEditor.GlobalObjectId[]):System.Void", "1" },
        new string[] { "UnityEditor.GlobalObjectId", "UnityEditor.CoreModule", "M:static GlobalObjectIdentifiersToInstanceIDsSlow`0(UnityEditor.GlobalObjectId[],System.Int32[]):System.Void", "1" },
        new string[] { "UnityEditor.GlobalObjectId", "UnityEditor.CoreModule", "M:static GlobalObjectIdentifiersToObjectsSlow`0(UnityEditor.GlobalObjectId[],UnityEngine.Object[]):System.Void", "1" },
        new string[] { "UnityEngine.AI.NavMeshPath", "UnityEngine.AIModule", "M:GetCornersNonAlloc`0(UnityEngine.Vector3[]):System.Int32", "0" },
        new string[] { "UnityEngine.Animations.MuscleHandle", "UnityEngine.AnimationModule", "M:static GetMuscleHandles`0(UnityEngine.Animations.MuscleHandle[]):System.Void", "0" },
        new string[] { "UnityEngine.Experimental.Rendering.GraphicsFormatUtility", "UnityEngine.CoreModule", "M:static ComputeMipChainSize`0(System.Int32,System.Int32,System.Int32,UnityEngine.Experimental.Rendering.GraphicsFormat,System.Int32):System.UInt32", "4" },
        new string[] { "UnityEngine.Experimental.Rendering.GraphicsFormatUtility", "UnityEngine.CoreModule", "M:static ComputeMipChainSize`0(System.Int32,System.Int32,UnityEngine.Experimental.Rendering.GraphicsFormat,System.Int32):System.UInt32", "3" },
        new string[] { "UnityEngine.LineRenderer", "UnityEngine.CoreModule", "M:GetPositions`0(Unity.Collections.NativeArray`1<UnityEngine.Vector3>):System.Int32", "0" },
        new string[] { "UnityEngine.LineRenderer", "UnityEngine.CoreModule", "M:GetPositions`0(Unity.Collections.NativeSlice`1<UnityEngine.Vector3>):System.Int32", "0" },
        new string[] { "UnityEngine.LineRenderer", "UnityEngine.CoreModule", "M:GetPositions`0(UnityEngine.Vector3[]):System.Int32", "0" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetColors`0(System.Collections.Generic.List`1<UnityEngine.Color32>,System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "3" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetColors`0(System.Collections.Generic.List`1<UnityEngine.Color>,System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "3" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetColors`0(UnityEngine.Color32[],System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "3" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetColors`0(UnityEngine.Color[],System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "3" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetNormals`0(System.Collections.Generic.List`1<UnityEngine.Vector3>,System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "3" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetNormals`0(UnityEngine.Vector3[],System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "3" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetTangents`0(System.Collections.Generic.List`1<UnityEngine.Vector4>,System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "3" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetTangents`0(UnityEngine.Vector4[],System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "3" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetUVs`0(System.Int32,System.Collections.Generic.List`1<UnityEngine.Vector2>,System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "4" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetUVs`0(System.Int32,System.Collections.Generic.List`1<UnityEngine.Vector3>,System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "4" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetUVs`0(System.Int32,System.Collections.Generic.List`1<UnityEngine.Vector4>,System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "4" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetUVs`0(System.Int32,UnityEngine.Vector2[],System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "4" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetUVs`0(System.Int32,UnityEngine.Vector3[],System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "4" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetUVs`0(System.Int32,UnityEngine.Vector4[],System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "4" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetVertices`0(System.Collections.Generic.List`1<UnityEngine.Vector3>,System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "3" },
        new string[] { "UnityEngine.Mesh", "UnityEngine.CoreModule", "M:SetVertices`0(UnityEngine.Vector3[],System.Int32,System.Int32,UnityEngine.Rendering.MeshUpdateFlags):System.Void", "3" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:GetParticles`0(Unity.Collections.NativeArray`1<UnityEngine.ParticleSystem+Particle>):System.Int32", "0" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:GetParticles`0(Unity.Collections.NativeArray`1<UnityEngine.ParticleSystem+Particle>,System.Int32):System.Int32", "0" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:GetParticles`0(Unity.Collections.NativeArray`1<UnityEngine.ParticleSystem+Particle>,System.Int32,System.Int32):System.Int32", "0" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:GetParticles`0(UnityEngine.ParticleSystem+Particle[]):System.Int32", "0" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:GetParticles`0(UnityEngine.ParticleSystem+Particle[],System.Int32):System.Int32", "0" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:GetParticles`0(UnityEngine.ParticleSystem+Particle[],System.Int32,System.Int32):System.Int32", "0" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:SetParticles`0(Unity.Collections.NativeArray`1<UnityEngine.ParticleSystem+Particle>):System.Void", "0" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:SetParticles`0(Unity.Collections.NativeArray`1<UnityEngine.ParticleSystem+Particle>,System.Int32):System.Void", "0" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:SetParticles`0(Unity.Collections.NativeArray`1<UnityEngine.ParticleSystem+Particle>,System.Int32,System.Int32):System.Void", "0" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:SetParticles`0(UnityEngine.ParticleSystem+Particle[]):System.Void", "0" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:SetParticles`0(UnityEngine.ParticleSystem+Particle[],System.Int32):System.Void", "0" },
        new string[] { "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemModule", "M:SetParticles`0(UnityEngine.ParticleSystem+Particle[],System.Int32,System.Int32):System.Void", "0" },
        new string[] { "UnityEngine.ParticleSystemRenderer", "UnityEngine.ParticleSystemModule", "M:GetMeshWeightings`0(System.Single[]):System.Int32", "0" },
        new string[] { "UnityEngine.ParticleSystemRenderer", "UnityEngine.ParticleSystemModule", "M:GetMeshes`0(UnityEngine.Mesh[]):System.Int32", "0" },
        new string[] { "UnityEngine.Rendering.CommandBuffer", "UnityEngine.CoreModule", "M:ClearRenderTarget`0(System.Boolean,System.Boolean,UnityEngine.Color,System.Single,System.UInt32):System.Void", "3" },
        new string[] { "UnityEngine.Rendering.CommandBuffer", "UnityEngine.CoreModule", "M:ClearRenderTarget`0(System.Boolean,System.Boolean,UnityEngine.Color,System.Single,System.UInt32):System.Void", "4" },
        new string[] { "UnityEngine.Rendering.CommandBuffer", "UnityEngine.CoreModule", "M:ClearRenderTarget`0(UnityEngine.Rendering.RTClearFlags,UnityEngine.Color,System.Single,System.UInt32):System.Void", "2" },
        new string[] { "UnityEngine.Rendering.CommandBuffer", "UnityEngine.CoreModule", "M:ClearRenderTarget`0(UnityEngine.Rendering.RTClearFlags,UnityEngine.Color,System.Single,System.UInt32):System.Void", "3" },
        new string[] { "UnityEngine.Rendering.CommandBuffer", "UnityEngine.CoreModule", "M:ClearRenderTarget`0(UnityEngine.Rendering.RTClearFlags,UnityEngine.Color[],System.Single,System.UInt32):System.Void", "2" },
        new string[] { "UnityEngine.Rendering.CommandBuffer", "UnityEngine.CoreModule", "M:ClearRenderTarget`0(UnityEngine.Rendering.RTClearFlags,UnityEngine.Color[],System.Single,System.UInt32):System.Void", "3" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(System.Int32,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "1" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(System.Int32,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "2" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(System.Int32,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "3" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(System.String,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "1" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(System.String,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "2" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(System.String,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "3" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(UnityEngine.Rendering.BuiltinRenderTextureType,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "1" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(UnityEngine.Rendering.BuiltinRenderTextureType,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "2" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(UnityEngine.Rendering.BuiltinRenderTextureType,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "3" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(UnityEngine.Rendering.RenderTargetIdentifier,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "2" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(UnityEngine.Rendering.RenderTargetIdentifier,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "3" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(UnityEngine.Texture,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "1" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(UnityEngine.Texture,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "2" },
        new string[] { "UnityEngine.Rendering.RenderTargetIdentifier", "UnityEngine.CoreModule", "M:.ctor`0(UnityEngine.Texture,System.Int32,UnityEngine.CubemapFace,System.Int32):System.Void", "3" },
        new string[] { "UnityEngine.Rigidbody2D", "UnityEngine.Physics2DModule", "M:GetAttachedColliders`0(UnityEngine.Collider2D[]):System.Int32", "0" },
        new string[] { "UnityEngine.Rigidbody2D", "UnityEngine.Physics2DModule", "M:GetAttachedColliders`0(UnityEngine.Collider2D[],System.Boolean):System.Int32", "0" },
        new string[] { "UnityEngine.Rigidbody2D", "UnityEngine.Physics2DModule", "M:OverlapCollider`0(UnityEngine.ContactFilter2D,UnityEngine.Collider2D[]):System.Int32", "1" },
        new string[] { "UnityEngine.Rigidbody2D", "UnityEngine.Physics2DModule", "M:Overlap`0(UnityEngine.ContactFilter2D,UnityEngine.Collider2D[]):System.Int32", "1" },
        new string[] { "UnityEngine.TrailRenderer", "UnityEngine.CoreModule", "M:AddPositions`0(Unity.Collections.NativeArray`1<UnityEngine.Vector3>):System.Void", "0" },
        new string[] { "UnityEngine.TrailRenderer", "UnityEngine.CoreModule", "M:AddPositions`0(Unity.Collections.NativeSlice`1<UnityEngine.Vector3>):System.Void", "0" },
        new string[] { "UnityEngine.TrailRenderer", "UnityEngine.CoreModule", "M:GetPositions`0(Unity.Collections.NativeArray`1<UnityEngine.Vector3>):System.Int32", "0" },
        new string[] { "UnityEngine.TrailRenderer", "UnityEngine.CoreModule", "M:GetPositions`0(Unity.Collections.NativeSlice`1<UnityEngine.Vector3>):System.Int32", "0" },
        new string[] { "UnityEngine.TrailRenderer", "UnityEngine.CoreModule", "M:GetPositions`0(UnityEngine.Vector3[]):System.Int32", "0" },
        new string[] { "UnityEngine.TrailRenderer", "UnityEngine.CoreModule", "M:GetVisiblePositions`0(Unity.Collections.NativeArray`1<UnityEngine.Vector3>):System.Int32", "0" },
        new string[] { "UnityEngine.TrailRenderer", "UnityEngine.CoreModule", "M:GetVisiblePositions`0(Unity.Collections.NativeSlice`1<UnityEngine.Vector3>):System.Int32", "0" },
        new string[] { "UnityEngine.TrailRenderer", "UnityEngine.CoreModule", "M:GetVisiblePositions`0(UnityEngine.Vector3[]):System.Int32", "0" },
    };
    public static void Record(Action<string, object> row)
    {
        foreach (var declaration in Declarations)
        {
            var method = (MethodBase)DeclarationSignatures.Find(declaration[0], declaration[1], declaration[2]);
            var parameter = method.GetParameters()[int.Parse(declaration[3], CultureInfo.InvariantCulture)];
            object value = parameter.HasDefaultValue ? parameter.RawDefaultValue : null;
            string expected = !parameter.HasDefaultValue ? "absent" : value == null ? "null" : value.GetType().FullName + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
            row("parameter." + declaration[0] + "." + declaration[2] + "." + declaration[3], parameter.IsIn + ":" + parameter.IsOut + ":" + parameter.IsOptional + ":" + parameter.HasDefaultValue + ":" + expected);
        }
        var format = GraphicsFormat.R8G8B8A8_UNorm;
        var two = typeof(GraphicsFormatUtility).GetMethod("ComputeMipChainSize", new[] { typeof(int), typeof(int), typeof(GraphicsFormat), typeof(int) });
        var three = typeof(GraphicsFormatUtility).GetMethod("ComputeMipChainSize", new[] { typeof(int), typeof(int), typeof(int), typeof(GraphicsFormat), typeof(int) });
        row("parameter.mips.2d.default", two.Invoke(null, new object[] { 9, 5, format, Type.Missing }));
        row("parameter.mips.2d.explicit", GraphicsFormatUtility.ComputeMipChainSize(9, 5, format, -1));
        row("parameter.mips.3d.default", three.Invoke(null, new object[] { 9, 5, 3, format, Type.Missing }));
        row("parameter.mips.3d.control", GraphicsFormatUtility.ComputeMipChainSize(9, 5, 3, format, 2));
        var ctor = typeof(RenderTargetIdentifier).GetConstructor(new[] { typeof(BuiltinRenderTextureType), typeof(int), typeof(CubemapFace), typeof(int) });
        var target = (RenderTargetIdentifier)ctor.Invoke(new object[] { BuiltinRenderTextureType.CameraTarget, Type.Missing, Type.Missing, Type.Missing });
        row("parameter.target.default-equals-explicit", target.Equals(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget, 0, CubemapFace.Unknown, 0)));
        var mesh = new Mesh();
        try
        {
            mesh.vertices = new[] { Vector3.zero, Vector3.one };
            var colors = new[] { Color.red, Color.blue };
            var set = typeof(Mesh).GetMethod("SetColors", new[] { typeof(Color[]), typeof(int), typeof(int), typeof(MeshUpdateFlags) });
            string exception = "none";
            try { set.Invoke(mesh, new object[] { colors, 0, 2, Type.Missing }); }
            catch (TargetInvocationException e) { exception = e.InnerException.GetType().FullName; }
            catch (Exception e) { exception = e.GetType().FullName; }
            row("parameter.mesh.required-flags", exception);
            mesh.SetColors(colors, 0, 2);
            row("parameter.mesh.three-argument-colors", mesh.colors[0].r + ":" + mesh.colors[1].b);
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }
}
