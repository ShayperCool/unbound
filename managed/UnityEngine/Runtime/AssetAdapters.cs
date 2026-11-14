using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>Engine-owned loading and copy operations, without adding interfaces to Unity types.</summary>
    internal static class AssetAdapters
    {
        abstract class Adapter
        {
            internal abstract void Read(Object asset, YNode body, IReferenceResolver resolver);
            internal abstract Object Clone(Object asset);
            internal abstract MethodInfo Reader { get; }
        }

        sealed class Adapter<T> : Adapter where T : Object
        {
            readonly Action<T, YNode, IReferenceResolver> m_Read;
            readonly Func<T, Object> m_Clone;
            readonly MethodInfo m_Reader = typeof(T).GetMethod("ReadSerialized",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, new[] { typeof(YNode), typeof(IReferenceResolver) }, null)
                ?? throw new MissingMethodException(typeof(T).FullName, "ReadSerialized");

            internal Adapter(Action<T, YNode, IReferenceResolver> read, Func<T, Object> clone)
            {
                m_Read = read;
                m_Clone = clone;
            }

            internal override void Read(Object asset, YNode body, IReferenceResolver resolver) => m_Read((T)asset, body, resolver);
            internal override Object Clone(Object asset) => m_Clone((T)asset);
            internal override MethodInfo Reader => m_Reader;
        }

        static readonly Dictionary<Type, Adapter> s_Adapters = new Dictionary<Type, Adapter>
        {
            { typeof(PhysicsMaterial), new Adapter<PhysicsMaterial>((asset, body, resolver) => asset.ReadSerialized(body, resolver), asset => asset.CloneObject()) },
            { typeof(PhysicsMaterial2D), new Adapter<PhysicsMaterial2D>((asset, body, resolver) => asset.ReadSerialized(body, resolver), asset => asset.CloneObject()) },
            { typeof(TextAsset), new Adapter<TextAsset>((asset, body, resolver) => asset.ReadSerialized(body, resolver), asset => asset.CloneObject()) },
        };

        static Adapter Find(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
                if (s_Adapters.TryGetValue(current, out var adapter)) return adapter;
            return null;
        }

        internal static bool TryRead(Object asset, YNode body, IReferenceResolver resolver)
        {
            var adapter = Find(asset.GetType());
            if (adapter != null) { adapter.Read(asset, body, resolver); return true; }
            if (asset is ISerializedAsset serialized) { serialized.ReadSerialized(body, resolver); return true; }
            return false;
        }

        internal static MethodInfo Reader(Type type)
        {
            var adapter = Find(type);
            if (adapter != null) return adapter.Reader;
            return typeof(ISerializedAsset).IsAssignableFrom(type) ? type.GetInterfaceMap(typeof(ISerializedAsset)).TargetMethods[0] : null;
        }

        internal static bool TryClone(Object original, out Object copy)
        {
            var adapter = Find(original.GetType());
            if (adapter != null) { copy = adapter.Clone(original); return true; }
            if (original is Instantiation.ICloneable cloneable) { copy = cloneable.CloneObject(); return true; }
            copy = null;
            return false;
        }
    }
}
