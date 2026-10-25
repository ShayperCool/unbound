using System.Collections.Generic;
using R = Unbound.Runtime.ParticleRandom;

namespace UnityEngine
{
    // The lights module: after the frame's particle update, copies of the module's light sit at a
    // share of the particles (by the ratio, at most maxLights), tinted by the particle's color,
    // their range and intensity scaled by the module's curves. The copies are hidden objects (the
    // scene's queries do not find them, measured: FindObjectsByType<Light> sees the template only);
    // they light only in the player loop, not through Simulate (measured).
    public sealed partial class ParticleSystem
    {
        readonly List<Light> m_ModuleLights = new List<Light>();

        internal void UpdateModuleLights()
        {
            var L = m_Lights;
            var template = L.light as Light;
            int used = 0;
            if (L.enabled && template != null && m_GameObject != null && m_GameObject.activeInHierarchy)
            {
                var toWorld = SimulationToWorld();
                int limit = Mathf.Max(0, L.maxLights);
                float ratio = Mathf.Clamp01(L.ratio);
                int every = ratio > 0f ? Mathf.Max(1, Mathf.RoundToInt(1f / ratio)) : int.MaxValue;
                for (int i = 0; i < m_Count && used < limit; i++)
                {
                    ref var p = ref m_Particles[i];
                    bool lit = L.useRandomDistribution ? R.Value(p.m_RandomSeed, R.Light) < ratio : ratio > 0f && i % every == 0;
                    if (!lit) continue;
                    var light = ModuleLight(used++, template);
                    float age = p.Age01;
                    Color color = L.useParticleColor ? (Color)CurrentColor(ref p) : Color.white;
                    var size = CurrentSize3D(ref p);
                    light.transform.position = toWorld.MultiplyPoint3x4(p.m_Position);
                    light.color = template.color * new Color(color.r, color.g, color.b, 1f);
                    float intensity = template.intensity * L.intensity.Evaluate(age, R.Value(p.m_RandomSeed, R.Light + 1));
                    light.intensity = L.alphaAffectsIntensity ? intensity * color.a : intensity;
                    float range = template.range * L.range.Evaluate(age, R.Value(p.m_RandomSeed, R.Light + 2));
                    light.range = L.sizeAffectsRange ? range * Mathf.Max(size.x, Mathf.Max(size.y, size.z)) : range;
                    light.enabled = true;
                }
            }
            for (int i = used; i < m_ModuleLights.Count; i++)
                if (m_ModuleLights[i] != null) m_ModuleLights[i].enabled = false;
        }

        Light ModuleLight(int index, Light template)
        {
            while (m_ModuleLights.Count <= index) m_ModuleLights.Add(null);
            var light = m_ModuleLights[index];
            if (light == null)
            {
                var go = new GameObject("Particle System Light") { hideFlags = HideFlags.HideAndDontSave };
                light = go.AddComponent<Light>();
                m_ModuleLights[index] = light;
            }
            light.type = template.type;
            light.spotAngle = template.spotAngle;
            light.innerSpotAngle = template.innerSpotAngle;
            light.shadows = template.shadows;
            light.cullingMask = template.cullingMask;
            light.renderMode = template.renderMode;
            light.bounceIntensity = template.bounceIntensity;
            light.cookie = template.cookie;
            return light;
        }

        internal void DestroyModuleLights()
        {
            foreach (var light in m_ModuleLights)
                if (light != null) DestroyImmediate(light.gameObject);
            m_ModuleLights.Clear();
        }
    }
}
