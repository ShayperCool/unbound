using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    internal static class ShaderWarmup
    {
        internal static Action<Shader, PassType, string[]> s_PrepareForTests;
        internal static Action<string> s_SendForTests;
        /// <summary>Shaders warm-up skipped, warned once each.</summary>
        static readonly HashSet<string> s_Unprepared = new HashSet<string>();

        internal static void PrepareAll()
        {
            var shaders = Resources.FindObjectsOfTypeAll<Shader>();
            Flush();
            foreach (var shader in shaders)
                if (Preparable(shader))
                    Send(shader.name, Array.Empty<int>(), Array.Empty<string>(), true);
        }

        /// <summary>Whether the renderer can compile the shader. The Editor warms every shader,
        /// built-in ones whose source Unity does not publish included (tests/projects/warmupbuiltin).
        /// Warming changes no result, so a shader the renderer cannot compile is skipped (counted as
        /// warm by a collection); drawing with it draws the error shader.</summary>
        static bool Preparable(Shader shader)
        {
            var info = ShaderPassInfo.Of(shader);
            if (info == null)
                throw new NotSupportedException("Unbound: shader warm-up requires an active renderer.");
            if (info.Found) return true;
            lock (s_Unprepared)
                if (s_Unprepared.Add(shader.name))
                    Debug.LogWarning($"Unbound: shader `{shader.name}` has no source the renderer can compile; warm-up skips it.");
            return false;
        }

        static void Flush()
        {
            if (s_SendForTests != null) return;
            GfxStream.SetColorSpace(QualitySettings.activeColorSpace);
            GfxStream.Flush();
        }

        static void Send(string shader, IEnumerable<int> passes, string[] keywords, bool allVariants)
        {
            var request = JsonSerializer.Serialize(new { shader, passes, keywords, all_variants = allVariants });
            if (s_SendForTests != null) s_SendForTests(request);
            else Native.Call("shader-warmup", request);
        }

        internal static void Prepare(Shader shader, PassType type, string keywords)
        {
            var keys = keywords.Length == 0 ? Array.Empty<string>() : keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (s_PrepareForTests != null) { s_PrepareForTests(shader, type, keys); return; }
            Flush();
            if (!Preparable(shader)) return;
            var passes = new List<int>();
            var source = ShaderPassInfo.FindPassSource(shader, candidate =>
            {
                for (int i = 0; i < candidate.Passes.Count; i++)
                {
                    candidate.Passes[i].Tags.TryGetValue("LightMode", out var mode);
                    if (ShaderVariantCollection.ShaderVariant.PassTypeOf(mode) == type) passes.Add(i);
                }
                return passes.Count > 0;
            }, out _);
            if (source == null) return;
            Send(source.DrawnName, passes, keys, false);
        }
    }
}
