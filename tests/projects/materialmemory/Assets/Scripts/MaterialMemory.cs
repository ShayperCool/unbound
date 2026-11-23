using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

sealed class MaterialMemory : MonoBehaviour
{
    [Serializable] sealed class Row { public string k, v; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MATERIAL_MEMORY_OUT")))
            new GameObject().AddComponent<MaterialMemory>();
    }

    IEnumerator Start()
    {
        yield return null;
        string output = Environment.GetEnvironmentVariable("MATERIAL_MEMORY_OUT");
        var rows = new List<string>();
        void Put(string key, object value) => rows.Add(JsonUtility.ToJson(new Row { k = key, v = Convert.ToString(value, CultureInfo.InvariantCulture) }));
        void Size(string key, Material value) => Put(key, Profiler.GetRuntimeMemorySizeLong(value));
        Put("unity", Application.unityVersion);
        Put("device", SystemInfo.graphicsDeviceType.ToString());
        var texture = new Texture2D(4, 4);

        void Probe(string stage, Shader shader)
        {
            var kinds = new string[shader.GetPropertyCount()];
            for (int index = 0; index < kinds.Length; index++) kinds[index] = shader.GetPropertyType(index).ToString();
            Put(stage + ".propertyKinds", string.Join("/", kinds));
            var material = new Material(shader);
            Size(stage + ".created", material);
            var coldCopy = new Material(material);
            Size(stage + ".coldCopy", coldCopy);
            Object.DestroyImmediate(coldCopy);
            foreach (string name in new[] { "_F0", "_R", "_Glossiness" })
                if (material.HasProperty(name)) material.SetFloat(name, 0.75f);
            foreach (string name in new[] { "_V0", "_Color" })
                if (material.HasProperty(name)) material.SetColor(name, Color.green);
            foreach (string name in new[] { "_T0", "_MainTex" })
                if (material.HasProperty(name)) material.SetTexture(name, texture);
            if (material.HasProperty("_I")) material.SetInteger("_I", 7);
            Size(stage + ".declaredWrites", material);
            material.renderQueue = 2100;
            material.enableInstancing = true;
            Size(stage + ".settings", material);

            for (int count = 1; count <= 10; count++)
            {
                string property = "_ProbeExtra" + count;
                material.SetFloat(property, count);
                Size(stage + ".extra." + count, material);
            }
            material.SetFloat("_ProbeExtra4", 2f);
            Size(stage + ".overwritten", material);
            var copy = new Material(material);
            Size(stage + ".populatedCopy", copy);
            copy.SetFloat("_ProbeExtra4", 3f);
            Size(stage + ".copyEdited", copy);
            Size(stage + ".sourceAfterCopyEdit", material);
            var instance = Object.Instantiate(material);
            Size(stage + ".populatedInstance", instance);
            instance.SetFloat("_ProbeExtra4", 4f);
            Size(stage + ".instanceEdited", instance);
            Size(stage + ".sourceAfterInstanceEdit", material);
            Object.DestroyImmediate(instance);
            if (stage == "standard")
            {
                var queried = Object.Instantiate(material);
                _ = queried.shader;
                queried.renderQueue = 2200;
                Size(stage + ".instanceMetadata", queried);
                Put(stage + ".instanceHasProperty", queried.HasProperty("_Glossiness"));
                Size(stage + ".instanceAfterHasProperty", queried);
                Object.DestroyImmediate(queried);
                var read = Object.Instantiate(material);
                Put(stage + ".instanceDeclaredValue", read.GetFloat("_Glossiness"));
                Size(stage + ".instanceAfterRead", read);
                Object.DestroyImmediate(read);
            }
            if (stage == "standard" || stage == "unlit")
            {
                var invalid = Object.Instantiate(material);
                _ = invalid.SetPass(9999);
                Size(stage + ".instanceAfterInvalidPass", invalid);
                Object.DestroyImmediate(invalid);
                var selected = Object.Instantiate(material);
                _ = selected.SetPass(0);
                Size(stage + ".instanceAfterValidPass", selected);
                Object.Destroy(selected);

                var target = new RenderTexture(4, 4, 0);
                var previous = RenderTexture.active;
                var blit = Object.Instantiate(material);
                Graphics.Blit(null, target, blit);
                Size(stage + ".instanceAfterNullBlit", blit);
                Object.Destroy(blit);
                var withTexture = Object.Instantiate(material);
                Graphics.Blit(texture, target, withTexture);
                Size(stage + ".instanceAfterTextureBlit", withTexture);
                Object.Destroy(withTexture);
                var invalidBlit = Object.Instantiate(material);
                Graphics.Blit(texture, target, invalidBlit, 9999);
                Size(stage + ".instanceAfterInvalidBlit", invalidBlit);
                Object.Destroy(invalidBlit);
                RenderTexture.active = previous;
                Object.Destroy(target);
            }
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(material);
        }

        foreach (string name in new[] { "f0v0t0", "f3v0t0", "f0v2t0", "f1v1t1", "f4v1t2", "f16v2t9", "range", "int", "texdims" })
            Probe(name, Shader.Find("Hidden/Storage/" + name));
        Probe("unlit", Shader.Find("Unlit/Color"));
        Probe("standard", Shader.Find("Standard"));
        Object.Destroy(texture);
        yield return MaterialPassControls.Run(Put);
        rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), rows);
        // Keep the Editor alive across a frame boundary so queued blits can complete.
        yield return null;
        yield return null;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
