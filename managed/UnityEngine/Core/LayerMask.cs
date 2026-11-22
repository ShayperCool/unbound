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

        /// <summary>A primitive as Unity makes it: the built-in mesh, a MeshRenderer with the
        /// default material (the active pipeline's, else Default-Material) and the shape's collider.</summary>
        internal static UnityEngine.GameObject Create(UnityEngine.PrimitiveType type)
        {
            if (Factory != null) return Factory(type);
            var go = new UnityEngine.GameObject(NameOf(type));
            var mesh = MeshOf(type);
            go.AddComponent<UnityEngine.MeshFilter>().sharedMesh = mesh;
            go.AddComponent<UnityEngine.MeshRenderer>().sharedMaterial = DefaultMaterial();
            AddCollider(go, type, mesh, go.AddComponent);
            return go;
        }

        internal static string NameOf(UnityEngine.PrimitiveType type) => type switch
        {
            UnityEngine.PrimitiveType.Sphere => "Sphere",
            UnityEngine.PrimitiveType.Capsule => "Capsule",
            UnityEngine.PrimitiveType.Cylinder => "Cylinder",
            UnityEngine.PrimitiveType.Plane => "Plane",
            UnityEngine.PrimitiveType.Quad => "Quad",
            _ => "Cube",
        };

        /// <summary>The primitive's built-in mesh.</summary>
        internal static UnityEngine.Mesh MeshOf(UnityEngine.PrimitiveType type)
        {
            long fileId = type switch
            {
                UnityEngine.PrimitiveType.Sphere => 10207L,
                UnityEngine.PrimitiveType.Capsule => 10208L,
                UnityEngine.PrimitiveType.Cylinder => 10206L,
                UnityEngine.PrimitiveType.Plane => 10209L,
                UnityEngine.PrimitiveType.Quad => 10210L,
                _ => 10202L,
            };
            return BuiltinShaders.Resolve(BuiltinShaders.DefaultGuid, fileId) as UnityEngine.Mesh ?? UnityEngine.BuiltinMeshes.Create(NameOf(type));
        }

        /// <summary>The active pipeline's default material, else Default-Material.</summary>
        internal static UnityEngine.Material DefaultMaterial() =>
            UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.defaultMaterial
            ?? BuiltinShaders.Resolve(BuiltinShaders.ExtraGuid, 10303) as UnityEngine.Material;

        /// <summary>Adds the shape's collider through <paramref name="add"/>, fitted to the mesh.</summary>
        internal static void AddCollider(UnityEngine.GameObject go, UnityEngine.PrimitiveType type, UnityEngine.Mesh mesh, Func<Type, UnityEngine.Component> add)
        {
            switch (type)
            {
                case UnityEngine.PrimitiveType.Sphere: add(typeof(UnityEngine.SphereCollider)); break;
                case UnityEngine.PrimitiveType.Capsule:
                case UnityEngine.PrimitiveType.Cylinder: ((UnityEngine.CapsuleCollider)add(typeof(UnityEngine.CapsuleCollider))).height = 2f; break;
                case UnityEngine.PrimitiveType.Plane:
                case UnityEngine.PrimitiveType.Quad: ((UnityEngine.MeshCollider)add(typeof(UnityEngine.MeshCollider))).sharedMesh = mesh; break;
                default: add(typeof(UnityEngine.BoxCollider)); break;
            }
        }
    }
}
