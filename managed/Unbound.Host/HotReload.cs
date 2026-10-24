using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using Unbound.Compiler;
using Unbound.Runtime;
using UnityEngine;

namespace Unbound.Host;

public sealed class HotReloadRequest
{
    public CompileRequest Compile { get; set; } = new();
}

public sealed class HotReloadResponse
{
    /// <summary><c>applied</c> (patched in place), <c>unchanged</c>, <c>compile-errors</c> (the game
    /// keeps its code) or <c>restart-required</c> (with the reason).</summary>
    public string Status { get; set; } = "";
    public string? Reason { get; set; }
    public List<string> Applied { get; set; } = new();
    public List<string> Changes { get; set; } = new();
    public CompileResponse? Compile { get; set; }
    public long Milliseconds { get; set; }
}

/// <summary>
/// Applies code edits to the running game: recompiles in the warm compiler, then patches each
/// changed assembly with Edit and Continue deltas (MetadataUpdater). When the runtime cannot apply
/// an edit, the caller restarts the game's session with the new outputs.
/// </summary>
internal static class HotReload
{
    static readonly EditAndContinue s_Enc = new();
    /// <summary>The output each game assembly was loaded from, by name.</summary>
    internal static readonly Dictionary<string, string> Loaded = new();

    /// <summary>Per assembly in a session: the loaded image (the deltas' baseline) and the
    /// metadata of the deltas applied to it, in order.</summary>
    static readonly Dictionary<string, (byte[] Image, List<byte[]> Deltas)> s_Applied = new();

    internal static void ResetSessions()
    {
        s_Enc.Reset();
        s_Applied.Clear();
    }

    internal static HotReloadResponse Run(CompilerService compiler, HotReloadRequest request)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var response = new HotReloadResponse();
        var compiled = compiler.Compile(request.Compile);
        response.Compile = compiled;
        if (compiled.Failed > 0)
        {
            response.Status = "compile-errors";
            response.Milliseconds = watch.ElapsedMilliseconds;
            return response;
        }
        if (!MetadataUpdater.IsSupported)
            return Restart(response, "the runtime was started without modifiable assemblies", watch);
        var results = compiled.Assemblies.ToDictionary(a => a.Name);
        var deltas = new List<(System.Reflection.Assembly Assembly, AssemblyDelta Delta)>();
        try
        {
            foreach (var spec in request.Compile.Assemblies)
            {
                if (!results.TryGetValue(spec.Name, out var result) || result.Dll == null) continue;
                if (!Loaded.TryGetValue(spec.Name, out var loadedDll))
                    return Restart(response, $"{spec.Name} is a new assembly", watch);
                if (string.Equals(result.Dll, loadedDll, StringComparison.Ordinal) && !s_Enc.HasSession(spec.Name))
                    continue;
                // An IL post-processor's output is not what the compiler emitted: deltas from the
                // compiler would not fit it.
                if (result.PostProcessed)
                    return Restart(response, $"{spec.Name} is IL post-processed", watch);
                var assembly = GameDomain.Find(spec.Name);
                if (assembly == null) return Restart(response, $"{spec.Name} is not loaded", watch);
                var (current, references) = compiler.CurrentCompilation(spec, request.Compile);
                if (current == null) return Restart(response, $"no compilation of {spec.Name}", watch);
                if (!s_Enc.HasSession(spec.Name))
                {
                    var loadedCompilation = compiler.RebuildCompilation(spec, request.Compile, loadedDll, references);
                    if (loadedCompilation == null)
                        return Restart(response, $"the sources {spec.Name} was loaded from are not stored", watch);
                    // The baseline is the image the runtime loaded: Unbound may have adapted the
                    // compiler's output (see GameDomain.ImageFilter), and a delta's rows follow the
                    // loaded image's. Its symbols are the compiler's (adapting keeps IL offsets).
                    var pdb = Path.ChangeExtension(loadedDll, ".pdb");
                    var image = File.ReadAllBytes(string.IsNullOrEmpty(assembly.Location) ? loadedDll : assembly.Location);
                    s_Enc.Begin(spec.Name, loadedCompilation, image, File.ReadAllBytes(pdb));
                    s_Applied[spec.Name] = (image, new List<byte[]>());
                }
                var delta = s_Enc.Emit(spec.Name, current);
                if (delta != null)
                {
                    deltas.Add((assembly, delta));
                    response.Changes.AddRange(delta.Changed.Select(c => $"{spec.Name}: {c}"));
                }
            }
        }
        catch (RudeEditException e)
        {
            return Restart(response, e.Message, watch);
        }
        if (deltas.Count == 0)
        {
            response.Status = "unchanged";
            response.Milliseconds = watch.ElapsedMilliseconds;
            return response;
        }
        try
        {
            foreach (var (assembly, delta) in deltas)
            {
                // Edited code formats numbers as the loaded code does (see NumberFormatShims).
                var (image, applied) = s_Applied[delta.Name];
                var il = NumberFormatShims.RedirectDelta(image, applied.Append(delta.Metadata).ToList(), delta.IL);
                MetadataUpdater.ApplyUpdate(assembly, delta.Metadata, il, delta.Pdb);
                applied.Add(delta.Metadata);
                response.Applied.Add(delta.Name);
            }
        }
        catch (Exception e)
        {
            return Restart(response, "the runtime refused the edit: " + e.Message, watch);
        }
        GameDomain.AfterCodeUpdate();
        response.Status = "applied";
        response.Milliseconds = watch.ElapsedMilliseconds;
        return response;
    }

    static HotReloadResponse Restart(HotReloadResponse response, string reason, System.Diagnostics.Stopwatch watch)
    {
        response.Status = "restart-required";
        response.Reason = reason;
        response.Milliseconds = watch.ElapsedMilliseconds;
        return response;
    }
}
