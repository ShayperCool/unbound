using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>Engine-owned loading and copy operations, without adding interfaces to Unity types.
    /// Reading and cloning are registered separately and each resolves to the closest registered
    /// type in the object's hierarchy, so a type registered for one operation keeps its base's
    /// registration for the other, and a derived type's own registration wins over its base's.</summary>
    internal static class AssetAdapters
    {
        abstract class SerializedReader
        {
            internal abstract void Read(Object asset, YNode body, IReferenceResolver resolver);
            internal abstract MethodInfo Method { get; }
        }

        sealed class SerializedReader<T> : SerializedReader where T : Object
        {
            readonly Action<T, YNode, IReferenceResolver> m_Read;
            readonly MethodInfo m_Method = typeof(T).GetMethod("ReadSerialized",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, new[] { typeof(YNode), typeof(IReferenceResolver) }, null)
                ?? throw new MissingMethodException(typeof(T).FullName, "ReadSerialized");

            internal SerializedReader(Action<T, YNode, IReferenceResolver> read) => m_Read = read;

            internal override void Read(Object asset, YNode body, IReferenceResolver resolver) => m_Read((T)asset, body, resolver);
            internal override MethodInfo Method => m_Method;
        }

        static readonly Dictionary<Type, SerializedReader> s_Readers = new Dictionary<Type, SerializedReader>();
        static readonly Dictionary<Type, Func<Object, Object>> s_Clones = new Dictionary<Type, Func<Object, Object>>();

        static void Read<T>(Action<T, YNode, IReferenceResolver> read) where T : Object => s_Readers.Add(typeof(T), new SerializedReader<T>(read));

        static void Clone<T>(Func<T, Object> clone) where T : Object => s_Clones.Add(typeof(T), asset => clone((T)asset));

        static AssetAdapters()
        {
            Read<PhysicsMaterial>((a, b, r) => a.ReadSerialized(b, r));
            Clone<PhysicsMaterial>(a => a.CloneObject());
            Read<PhysicsMaterial2D>((a, b, r) => a.ReadSerialized(b, r));
            Clone<PhysicsMaterial2D>(a => a.CloneObject());
            Read<AssetBundleManifest>((a, b, r) => a.ReadSerialized(b, r));
            Clone<AssetBundleManifest>(a => a.CloneObject());
            Read<TextAsset>((a, b, r) => a.ReadSerialized(b, r));
            Clone<TextAsset>(a => a.CloneObject());
            Read<AudioClip>((a, b, r) => a.ReadSerialized(b, r));
            Clone<AudioClip>(a => a.CloneObject());
            Read<Font>((a, b, r) => a.ReadSerialized(b, r));
            Clone<Font>(a => a.CloneObject());
            Read<UnityEditor.VFX.VisualEffectResource>((a, b, r) => a.ReadSerialized(b, r));
            Read<UnityEngine.VFX.VisualEffectAsset>((a, b, r) => a.ReadSerialized(b, r));
            Clone<UnityEditor.VFX.VisualEffectResource>(a => a.CloneObject());

            // Read and cloned.
            Read<AnimationClip>((a, b, r) =>
            {
                a.ReadSerialized(b, r);
                UnityEditor.AnimationUtility.ClipAwake(a);
            });
            Clone<AnimationClip>(a => a.CloneObject());
            Read<Cubemap>((a, b, r) => a.ReadSerialized(b, r));
            Clone<Cubemap>(a => a.CloneObject());
            Read<RenderTexture>((a, b, r) => a.ReadSerialized(b, r));
            Clone<RenderTexture>(a => a.CloneObject());
            Read<CustomRenderTexture>((a, b, r) => a.ReadSerialized(b, r));
            Clone<CustomRenderTexture>(a => a.CloneObject());
            Read<LightingSettings>((a, b, r) => a.ReadSerialized(b, r));
            Clone<LightingSettings>(a => a.CloneObject());
            Read<Material>((a, b, r) => a.ReadSerialized(b, r));
            Clone<Material>(a => a.CloneObject());
            Read<Mesh>((a, b, r) => a.ReadSerialized(b, r));
            Clone<Mesh>(a => a.CloneObject());
            Read<RuntimeAnimatorController>((a, b, r) => a.ReadSerialized(b, r));
            Clone<RuntimeAnimatorController>(a => a.CloneObject());
            Read<ShaderVariantCollection>((a, b, r) => a.ReadSerialized(b, r));
            Clone<ShaderVariantCollection>(a => a.CloneObject());
            Read<Sprite>((a, b, r) => a.ReadSerialized(b, r));
            Clone<Sprite>(a => a.CloneObject());
            Read<Texture2D>((a, b, r) => a.ReadSerialized(b, r));
            Clone<Texture2D>(a => a.CloneObject());

            // Read only: Object.Instantiate of these is not supported.
            Read<UnityEngine.AI.NavMeshData>((a, b, r) => a.ReadSerialized(b, r));
            Read<AudioMixer>((a, b, r) => a.ReadSerialized(b, r));
            Read<AudioMixerGroup>((a, b, r) => a.ReadSerialized(b, r));
            Clone<AudioMixerGroup>(a => a.CloneObject());
            Read<AudioMixerSnapshot>((a, b, r) => a.ReadSerialized(b, r));
            Clone<AudioMixerSnapshot>(a => a.CloneObject());
            Read<Avatar>((a, b, r) => a.ReadSerialized(b, r));
            Read<AvatarMask>((a, b, r) => a.ReadSerialized(b, r));
            Read<BillboardAsset>((a, b, r) => a.ReadSerialized(b, r));
            Read<CubemapArray>((a, b, r) => a.ReadSerialized(b, r));
            Read<Flare>((a, b, r) => a.ReadSerialized(b, r));
            Read<TerrainData>((a, b, r) => a.ReadSerialized(b, r));
            Read<TerrainLayer>((a, b, r) => a.ReadSerialized(b, r));
            Read<Texture2DArray>((a, b, r) => a.ReadSerialized(b, r));
            Read<Texture3D>((a, b, r) => a.ReadSerialized(b, r));
            Read<SpriteAtlas>((a, b, r) => a.ReadSerialized(b, r));
            Read<UnityEditor.Animations.AnimatorStateMachine>((a, b, r) => a.ReadSerialized(b, r));
            Read<UnityEditor.Animations.AnimatorState>((a, b, r) => a.ReadSerialized(b, r));
            Read<UnityEditor.Animations.AnimatorTransitionBase>((a, b, r) => a.ReadSerialized(b, r));
            Read<UnityEditor.Animations.BlendTree>((a, b, r) => a.ReadSerialized(b, r));

            // Cloned only: loaded from their compiled forms.
            Clone<ComputeShader>(a => a.CloneObject());
            Clone<Shader>(a => a.CloneObject());
        }

        static TValue Find<TValue>(Dictionary<Type, TValue> map, Type type) where TValue : class
        {
            for (var current = type; current != null; current = current.BaseType)
                if (map.TryGetValue(current, out var value)) return value;
            return null;
        }

        internal static bool TryRead(Object asset, YNode body, IReferenceResolver resolver)
        {
            var reader = Find(s_Readers, asset.GetType());
            if (reader != null) { reader.Read(asset, body, resolver); return true; }
            if (asset is ISerializedAsset serialized) { serialized.ReadSerialized(body, resolver); return true; }
            return false;
        }

        internal static MethodInfo Reader(Type type)
        {
            var reader = Find(s_Readers, type);
            if (reader != null) return reader.Method;
            return typeof(ISerializedAsset).IsAssignableFrom(type) ? type.GetInterfaceMap(typeof(ISerializedAsset)).TargetMethods[0] : null;
        }

        internal static bool TryClone(Object original, out Object copy)
        {
            var clone = Find(s_Clones, original.GetType());
            if (clone != null) { copy = clone(original); return true; }
            if (original is Instantiation.ICloneable cloneable) { copy = cloneable.CloneObject(); return true; }
            copy = null;
            return false;
        }
    }
}
