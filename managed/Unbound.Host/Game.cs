using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine;

namespace Unbound.Host;

/// <summary>What the native side tells the engine about the game it starts.</summary>
public sealed class GameConfig
{
    public string ProjectPath { get; set; } = "";
    public string UnityVersion { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Identifier { get; set; } = "";
    public string BundleVersion { get; set; } = "";
    public string PersistentDataPath { get; set; } = "";
    public string FacadeDir { get; set; } = "";
    public string FirstScene { get; set; } = "";
    public List<BuildSceneConfig> BuildScenes { get; set; } = new();
    public List<AssemblyConfig> Assemblies { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<string> Layers { get; set; } = new();
    public List<AxisConfig> InputAxes { get; set; } = new();
    public float FixedDeltaTime { get; set; } = 0.02f;
    public float MaximumDeltaTime { get; set; } = 0.3333333f;
    public float TimeScale { get; set; } = 1f;
    public bool SimulateTouch { get; set; }
    /// <summary>Script execution orders from the scripts' .meta files: guid → order.</summary>
    public Dictionary<string, int> ExecutionOrders { get; set; } = new();
    /// <summary>The release's built-in resources: "extra"/"default" → file id → class and name.</summary>
    public Dictionary<string, Dictionary<string, BuiltinConfig>> Builtins { get; set; } = new();
    public List<SortingLayerConfig> SortingLayers { get; set; } = new();
    /// <summary>0 gamma, 1 linear.</summary>
    public int ColorSpace { get; set; }
}

public sealed class BuiltinConfig
{
    public int Class { get; set; }
    public string Name { get; set; } = "";
}

public sealed class SortingLayerConfig
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
}

public sealed class BuildSceneConfig
{
    public string Path { get; set; } = "";
    public string Guid { get; set; } = "";
}

public sealed class AssemblyConfig
{
    public string Name { get; set; } = "";
    public string Dll { get; set; } = "";
    public string? Pdb { get; set; }
    public string? PluginGuid { get; set; }
    public List<ScriptConfig> Scripts { get; set; } = new();
}

public sealed class ScriptConfig
{
    public string FileName { get; set; } = "";
    public string Guid { get; set; } = "";
}

public sealed class AxisConfig
{
    public string Name { get; set; } = "";
    public string Negative { get; set; } = "";
    public string Positive { get; set; } = "";
    public string AltNegative { get; set; } = "";
    public string AltPositive { get; set; } = "";
    public float Gravity { get; set; }
    public float Dead { get; set; }
    public float Sensitivity { get; set; }
    public bool Snap { get; set; }
    public bool Invert { get; set; }
    public int Type { get; set; }
    public int Axis { get; set; }
}

/// <summary>The entry points of a running game: start once, then one call per frame.</summary>
public static unsafe class Game
{
    /// <summary>Connects the native API, loads the game's code and its first scene.
    /// Returns 0, or 1 with an error written to the log.</summary>
    [UnmanagedCallersOnly]
    public static int Start(void* api, byte* config, int configLength)
    {
        try
        {
            Native.Initialize((NativeApi*)api);
            var json = Encoding.UTF8.GetString(config, configLength);
            var settings = JsonSerializer.Deserialize<GameConfig>(json, Entry.Json)
                ?? throw new ArgumentException("empty game configuration");
            Configure(settings);
            var assemblies = new List<GameAssembly>();
            foreach (var a in settings.Assemblies)
            {
                var ga = new GameAssembly { Name = a.Name, Dll = a.Dll, Pdb = a.Pdb, PluginGuid = a.PluginGuid };
                foreach (var s in a.Scripts) ga.Scripts.Add((s.FileName, s.Guid));
                assemblies.Add(ga);
            }
            GameDomain.Load(assemblies, settings.FacadeDir);
            foreach (var (guid, order) in settings.ExecutionOrders)
            {
                var type = GameDomain.ResolveScript(guid, 11500000);
                if (type != null) ScriptMessages.ExecutionOrders[type] = order;
            }
            PlayerLoopDriver.Boot(settings.FirstScene);
            return 0;
        }
        catch (Exception e)
        {
            Native.Log((int)LogType.Exception, $"Unbound failed to start the game: {e}");
            return 1;
        }
    }

    static void Configure(GameConfig c)
    {
        ProjectInfo.ProjectPath = c.ProjectPath;
        ProjectInfo.AssetsPath = System.IO.Path.Combine(c.ProjectPath, "Assets");
        ProjectInfo.UnityVersion = c.UnityVersion;
        ProjectInfo.ProductName = c.ProductName;
        ProjectInfo.CompanyName = c.CompanyName;
        ProjectInfo.Identifier = c.Identifier;
        ProjectInfo.BundleVersion = c.BundleVersion;
        ProjectInfo.PersistentDataPath = c.PersistentDataPath;
        System.IO.Directory.CreateDirectory(c.PersistentDataPath);
        ProjectInfo.BuildScenes.Clear();
        foreach (var s in c.BuildScenes) ProjectInfo.BuildScenes.Add((s.Path, s.Guid));
        TagManager.SetProjectTags(c.Tags);
        for (int i = 0; i < c.Layers.Count && i < 32; i++) TagManager.Layers[i] = c.Layers[i];
        Time.s_FixedDeltaTime = c.FixedDeltaTime;
        Time.s_MaximumDeltaTime = c.MaximumDeltaTime;
        Time.s_TimeScale = c.TimeScale;
        Input.SimulateTouchWithMouse = c.SimulateTouch;
        var axes = new List<Input.InputAxis>();
        foreach (var a in c.InputAxes)
        {
            axes.Add(new Input.InputAxis
            {
                Name = a.Name, Negative = a.Negative, Positive = a.Positive, AltNegative = a.AltNegative, AltPositive = a.AltPositive,
                Gravity = a.Gravity, Dead = a.Dead, Sensitivity = a.Sensitivity, Snap = a.Snap, Invert = a.Invert, Type = a.Type, Axis = a.Axis,
            });
        }
        Input.ConfigureAxes(axes);
        foreach (var (kind, table) in c.Builtins)
        {
            var names = kind == "extra" ? BuiltinShaders.ExtraNames : BuiltinShaders.DefaultNames;
            var classes = kind == "extra" ? BuiltinObjects.ExtraClasses : BuiltinObjects.DefaultClasses;
            foreach (var (id, entry) in table)
            {
                if (!long.TryParse(id, out var fileId)) continue;
                names[fileId] = entry.Name;
                classes[fileId] = entry.Class;
            }
        }
        if (c.SortingLayers.Count > 0)
        {
            SortingLayer.s_Layers.Clear();
            foreach (var l in c.SortingLayers) SortingLayer.s_Layers.Add((l.Name, l.Id));
        }
        QualitySettings.s_ColorSpace = c.ColorSpace == 1 ? ColorSpace.Linear : ColorSpace.Gamma;
    }

    /// <summary>Runs one frame. Returns 0 to go on, 1 when the game asked to quit.</summary>
    [UnmanagedCallersOnly]
    public static int Frame(void* input)
    {
        try
        {
            PlayerLoopDriver.Tick((FrameInput*)input);
        }
        catch (Exception e)
        {
            Native.Log((int)LogType.Exception, $"Unbound frame failed: {e}");
        }
        return PlayerLoopDriver.QuitRequested ? 1 : 0;
    }

    /// <summary>The window is closing: OnApplicationQuit and the quitting events.</summary>
    [UnmanagedCallersOnly]
    public static void Shutdown()
    {
        try
        {
            PlayerLoopDriver.Quit();
        }
        catch (Exception e)
        {
            Native.Log((int)LogType.Exception, $"Unbound shutdown failed: {e}");
        }
    }
}
