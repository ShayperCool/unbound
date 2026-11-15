using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    /// <summary>Streaming strips owned by their renderer, with a distinct mesh for each camera
    /// and shadow-casting light. Destroying either owner releases its cached meshes.</summary>
    internal sealed class LineRenderMeshes
    {
        readonly struct CameraKey : IEquatable<CameraKey>
        {
            internal readonly Camera Camera;
            internal CameraKey(Camera camera) { Camera = camera; }
            public bool Equals(CameraKey other) => ReferenceEquals(Camera, other.Camera);
            public override bool Equals(object obj) => obj is CameraKey other && Equals(other);
            public override int GetHashCode() => ReferenceEquals(Camera, null) ? 0 : RuntimeHelpers.GetHashCode(Camera);
        }

        static readonly Dictionary<Camera, HashSet<LineRenderMeshes>> s_Cameras = new();
        static readonly Dictionary<Light, HashSet<LineRenderMeshes>> s_Lights = new();
        readonly Dictionary<CameraKey, Mesh> m_Normal = new();
        readonly Dictionary<Light, Dictionary<CameraKey, Mesh>> m_Shadows = new();
        readonly Dictionary<CameraKey, int> m_CameraUses = new();
        readonly List<Light> m_EmptyLights = new();

        static void Register<T>(Dictionary<T, HashSet<LineRenderMeshes>> owners, T owner, LineRenderMeshes cache) where T : class
        {
            if (!owners.TryGetValue(owner, out var caches)) owners[owner] = caches = new();
            caches.Add(cache);
        }

        static void Unregister<T>(Dictionary<T, HashSet<LineRenderMeshes>> owners, T owner, LineRenderMeshes cache) where T : class
        {
            if (!owners.TryGetValue(owner, out var caches)) return;
            caches.Remove(cache);
            if (caches.Count == 0) owners.Remove(owner);
        }

        void AddCamera(CameraKey key)
        {
            if (m_CameraUses.TryGetValue(key, out int uses)) m_CameraUses[key] = uses + 1;
            else
            {
                m_CameraUses[key] = 1;
                if (!ReferenceEquals(key.Camera, null)) Register(s_Cameras, key.Camera, this);
            }
        }

        void RemoveCameraUse(CameraKey key)
        {
            int uses = m_CameraUses[key] - 1;
            if (uses > 0) m_CameraUses[key] = uses;
            else
            {
                m_CameraUses.Remove(key);
                if (!ReferenceEquals(key.Camera, null)) Unregister(s_Cameras, key.Camera, this);
            }
        }

        internal Mesh Get(Unbound.Runtime.FrameBuilder.CameraContext context, string name)
        {
            var camera = context.Camera;
            if (camera == null) camera = Camera.main;
            var key = new CameraKey(camera);
            var meshes = m_Normal;
            var light = context.StripShadowLight;
            if (!ReferenceEquals(light, null) && !m_Shadows.TryGetValue(light, out meshes))
            {
                m_Shadows[light] = meshes = new();
                Register(s_Lights, light, this);
            }
            bool exists = meshes.TryGetValue(key, out var mesh);
            if (exists && mesh != null) return mesh;
            meshes[key] = mesh = new Mesh { name = name };
            if (!exists) AddCamera(key);
            return mesh;
        }

        internal void Clear()
        {
            foreach (var mesh in m_Normal.Values) if (mesh != null) Object.DestroyImmediate(mesh);
            foreach (var light in m_Shadows)
            {
                Unregister(s_Lights, light.Key, this);
                foreach (var mesh in light.Value.Values) if (mesh != null) Object.DestroyImmediate(mesh);
            }
            foreach (var camera in m_CameraUses.Keys)
                if (!ReferenceEquals(camera.Camera, null)) Unregister(s_Cameras, camera.Camera, this);
            m_Normal.Clear(); m_Shadows.Clear(); m_CameraUses.Clear();
        }

        internal static void Release(Light light)
        {
            if (!s_Lights.Remove(light, out var caches)) return;
            foreach (var cache in caches)
            {
                if (!cache.m_Shadows.Remove(light, out var meshes)) continue;
                foreach (var pair in meshes)
                {
                    if (pair.Value != null) Object.DestroyImmediate(pair.Value);
                    cache.RemoveCameraUse(pair.Key);
                }
            }
        }

        internal static void Release(Camera camera)
        {
            if (!s_Cameras.Remove(camera, out var caches)) return;
            var key = new CameraKey(camera);
            foreach (var cache in caches)
            {
                if (cache.m_Normal.Remove(key, out var normal) && normal != null) Object.DestroyImmediate(normal);
                cache.m_EmptyLights.Clear();
                foreach (var light in cache.m_Shadows)
                {
                    if (light.Value.Remove(key, out var mesh) && mesh != null) Object.DestroyImmediate(mesh);
                    if (light.Value.Count == 0) cache.m_EmptyLights.Add(light.Key);
                }
                foreach (var light in cache.m_EmptyLights)
                {
                    cache.m_Shadows.Remove(light);
                    Unregister(s_Lights, light, cache);
                }
                cache.m_EmptyLights.Clear();
                cache.m_CameraUses.Remove(key);
            }
        }
    }
}
