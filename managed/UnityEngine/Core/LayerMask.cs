using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A set of layers, as a bit mask.</summary>
    public partial struct LayerMask
    {
        int m_Mask;

        public static implicit operator int(LayerMask mask) => mask.m_Mask;

        public static implicit operator LayerMask(int intVal)
        {
            LayerMask mask;
            mask.m_Mask = intVal;
            return mask;
        }

        public int value
        {
            get => m_Mask;
            set => m_Mask = value;
        }

        public static string LayerToName(int layer) =>
            layer >= 0 && layer < 32 ? TagManager.Layers[layer] ?? "" : "";

        public static int NameToLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return -1;
            for (int i = 0; i < 32; i++)
                if (TagManager.Layers[i] == layerName) return i;
            return -1;
        }

        public static int GetMask(params string[] layerNames)
        {
            if (layerNames == null) throw new ArgumentNullException(nameof(layerNames));
            int mask = 0;
            foreach (var name in layerNames)
            {
                int layer = NameToLayer(name);
                if (layer != -1) mask |= 1 << layer;
            }
            return mask;
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>GameObject.CreatePrimitive: the shapes come from the renderer and physics modules,
    /// which register how to build them.</summary>
    internal static class Primitives
    {
        internal static Func<UnityEngine.PrimitiveType, UnityEngine.GameObject> Factory;

        internal static UnityEngine.GameObject Create(UnityEngine.PrimitiveType type)
        {
            if (Factory != null) return Factory(type);
            UnityEngine.Debug.LogWarning($"Unbound: CreatePrimitive({type}) has no renderer yet; an empty GameObject is made.");
            return new UnityEngine.GameObject(type.ToString());
        }
    }
}
