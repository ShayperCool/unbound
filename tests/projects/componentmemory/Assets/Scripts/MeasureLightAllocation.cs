using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

static class MeasureLightAllocation
{
    [Serializable] sealed class Row { public string k, v; }
    static readonly List<string> Rows = new();
    static readonly List<CommandBuffer> Commands = new();

    static void Read(string key, Light light) =>
        Rows.Add(JsonUtility.ToJson(new Row
        {
            k = key, v = FormattableString.Invariant($"{light.commandBufferCount}/{Profiler.GetRuntimeMemorySizeLong(light)}")
        }));

    static CommandBuffer NewBuffer()
    {
        var commands = new CommandBuffer();
        Commands.Add(commands);
        return commands;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Measure()
    {
        string outDir = Environment.GetEnvironmentVariable("LIGHT_ALLOCATION_OUT");
        if (string.IsNullOrEmpty(outDir)) return;
        Rows.Add(JsonUtility.ToJson(new Row { k = "unity", v = Application.unityVersion }));
        var light = new GameObject("Light").AddComponent<Light>();
        var independent = new GameObject("Other light").AddComponent<Light>();
        var cookie = new Texture2D(4, 4);
        Read("initial", light);
        light.type = LightType.Spot;
        light.range = 10;
        light.cookie = cookie;
        light.shadows = LightShadows.Soft;
        Read("cookie", light);
        const LightEvent firstEvent = LightEvent.BeforeShadowMap;
        const LightEvent secondEvent = LightEvent.AfterShadowMap;
        var duplicate = NewBuffer();
        duplicate.SetGlobalFloat("_Control", 5);
        Read("unattachedBuffer", light);
        light.AddCommandBuffer(firstEvent, duplicate);
        duplicate.SetGlobalFloat("_Control2", 6);
        Read("growingBufferPayload", light);
        light.AddCommandBuffer(firstEvent, duplicate, ShadowMapPass.All);
        light.AddCommandBufferAsync(firstEvent, duplicate, ComputeQueueType.Default);
        Read("duplicate3", light);
        independent.AddCommandBuffer(firstEvent, duplicate);
        Read("independent", independent);
        light.AddCommandBuffer(secondEvent, duplicate);
        Read("differentEvent", light);
        light.RemoveCommandBuffer(firstEvent, duplicate);
        Read("removeAllOccurrencesFromFirstEvent", light);
        Read("independent.afterRemove", independent);
        for (int i = 1; i <= 17; i++)
        {
            light.AddCommandBuffer(firstEvent, NewBuffer());
            Read("grow." + i, light);
        }
        light.AddCommandBufferAsync(secondEvent, NewBuffer(), ShadowMapPass.All, ComputeQueueType.Default);
        Read("otherEventAsync", light);
        var clone = Object.Instantiate(light);
        Read("componentClone", clone);
        var clonedObject = Object.Instantiate(light.gameObject);
        Read("gameObjectClone", clonedObject.GetComponent<Light>());
        Read("sourceAfterClone", light);
        light.RemoveCommandBuffer(firstEvent, NewBuffer());
        Read("removeUnregistered", light);
        light.RemoveCommandBuffers(firstEvent);
        Read("removeFirstEvent", light);
        light.RemoveCommandBuffer(secondEvent, duplicate);
        Read("removeOneFromSecondEvent", light);
        light.RemoveAllCommandBuffers();
        Read("removeAcrossEvents", light);
        light.AddCommandBuffer(secondEvent, duplicate);
        Read("recreateEvent", light);
        light.RemoveCommandBuffer(secondEvent, duplicate);
        Read("retainEmptyEvent", light);
        light.RemoveCommandBuffers(secondEvent);
        Read("freeEmptyEvent", light);
        light.enabled = false;
        Read("disabled", light);
        Object.DestroyImmediate(clonedObject);
        Object.DestroyImmediate(clone.gameObject);
        Object.DestroyImmediate(light.gameObject);
        Object.DestroyImmediate(independent.gameObject);
        Object.DestroyImmediate(cookie);
        foreach (var commands in Commands) commands.Dispose();
        Rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(outDir, "values.jsonl"), Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
