using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unbound.ApiTools;
using Unbound.Compiler;

namespace Unbound.Host;

/// <summary>
/// The native entry points the Rust host calls. Control calls go through <see cref="Call"/>:
/// a method name and a JSON request in, a JSON response out. Per-frame calls get dedicated
/// function pointers of their own.
/// </summary>
public static unsafe class Entry
{
    internal static readonly CompilerService Compiler = new();

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Returns 0 and a response, or 1 and an error message; the caller frees the response
    /// with <see cref="Free"/>.</summary>
    [UnmanagedCallersOnly]
    public static int Call(byte* method, int methodLength, byte* request, int requestLength, byte** response, int* responseLength)
    {
        string text;
        int status;
        try
        {
            var name = Encoding.UTF8.GetString(method, methodLength);
            var body = new ReadOnlySpan<byte>(request, requestLength);
            text = Dispatch(name, body);
            status = 0;
        }
        catch (Exception e)
        {
            text = e.ToString();
            status = 1;
        }
        var bytes = Encoding.UTF8.GetBytes(text);
        var buffer = (byte*)NativeMemory.Alloc((nuint)Math.Max(bytes.Length, 1));
        bytes.CopyTo(new Span<byte>(buffer, bytes.Length));
        *response = buffer;
        *responseLength = bytes.Length;
        return status;
    }

    [UnmanagedCallersOnly]
    public static void Free(byte* buffer) => NativeMemory.Free(buffer);

    static string Dispatch(string method, ReadOnlySpan<byte> body)
    {
        switch (method)
        {
            case "ping":
                return JsonSerializer.Serialize(new { runtime = Environment.Version.ToString() }, Json);
            case "compile":
                var request = JsonSerializer.Deserialize<CompileRequest>(body, Json)
                    ?? throw new ArgumentException("empty compile request");
                return JsonSerializer.Serialize(Compiler.Compile(request), Json);
            case "hot-reload":
                var reload = JsonSerializer.Deserialize<HotReloadRequest>(body, Json)
                    ?? throw new ArgumentException("empty hot-reload request");
                return JsonSerializer.Serialize(HotReload.Run(Compiler, reload), Json);
            case "facades":
                var facades = JsonSerializer.Deserialize<FacadesRequest>(body, Json)
                    ?? throw new ArgumentException("empty facades request");
                return JsonSerializer.Serialize(FacadeGenerator.Generate(facades.UnityManagedEngineDir, facades.OutputDir, facades.PlatformModules), Json);
            case "api-gaps":
                var gaps = JsonSerializer.Deserialize<ApiGapsRequest>(body, Json)
                    ?? throw new ArgumentException("empty api-gaps request");
                return JsonSerializer.Serialize(ApiGaps.Analyze(gaps.GameAssemblies, gaps.ImplAssembly, gaps.UnityManagedEngineDir, gaps.PlatformModules), Json);
            case "api-check":
                var check = JsonSerializer.Deserialize<ApiCheckRequest>(body, Json)
                    ?? throw new ArgumentException("empty api-check request");
                return JsonSerializer.Serialize(ApiChecker.Check(check.UnityManagedEngineDir, check.ImplAssembly, check.PlatformModules, check.ImplSourceDir, check.Release), Json);
            case "api-defaults":
                var defaults = JsonSerializer.Deserialize<ApiCheckRequest>(body, Json)
                    ?? throw new ArgumentException("empty api-defaults request");
                return JsonSerializer.Serialize(DefaultState.Inspect(defaults.UnityManagedEngineDir, defaults.ImplAssembly, defaults.PlatformModules), Json);
            case "gen-stubs":
                var stubs = JsonSerializer.Deserialize<StubsRequest>(body, Json)
                    ?? throw new ArgumentException("empty gen-stubs request");
                return JsonSerializer.Serialize(StubGenerator.Generate(stubs.UnityManagedEngineDir, stubs.ImplAssembly, stubs.OutputDir, stubs.ImplSourceDir, stubs.PlatformModules, releaseFamily: stubs.ReleaseFamily), Json);
            case "message-coverage":
                var scripts = JsonSerializer.Deserialize<MessageCoverageRequest>(body, Json)
                    ?? throw new ArgumentException("empty message-coverage request");
                // The project's SpeedTree 7 and 8 assets decide which callbacks it could get, as the
                // editor domain load reports them.
                var speedTrees = UnityEditor.AssetPostprocessing.SpeedTreeAssets(scripts.AssetFiles);
                var unreachable = UnityEditor.AssetPostprocessing.Unreachable(speedTrees) ?? Array.Empty<string>();
                var messages = Compiler.FindUnsupportedMessages(scripts.Compilation,
                    Unbound.Runtime.ScriptMessages.NotSent.Where(m => Array.IndexOf(unreachable, m.Name) < 0).Select(m => (m.Host.FullName!, m.Name)));
                return JsonSerializer.Serialize(new { messages.Messages, messages.FailedAssemblies, UnimportedSpeedTrees = speedTrees }, Json);
            case "prepare-field-coverage":
                var setup = JsonSerializer.Deserialize<FieldCoverageSetup>(body, Json)
                    ?? throw new ArgumentException("empty prepare-field-coverage request");
                FieldCoverageHost.Prepare(setup);
                return "{}";
            case "realtime-gi-contributors":
                using (var lighting = JsonDocument.Parse(body.ToArray()))
                    return JsonSerializer.Serialize(UnityEngine.BakedLighting.RealtimeContributors(lighting.RootElement), Json);
            case "field-coverage":
                var fields = JsonSerializer.Deserialize<List<Unbound.Runtime.FieldCoverage.Input>>(body, Json)
                    ?? throw new ArgumentException("empty field-coverage request");
                return JsonSerializer.Serialize(Unbound.Runtime.FieldCoverage.Report(fields), Json);
            case "class-coverage":
                var coverage = JsonSerializer.Deserialize<ClassCoverageRequest>(body, Json)
                    ?? throw new ArgumentException("empty class-coverage request");
                return JsonSerializer.Serialize(Unbound.Runtime.ClassCoverage.Report(coverage.Release, coverage.Classes), Json);
            default:
                throw new ArgumentException($"unknown method `{method}`");
        }
    }
}

/// <summary>The classes `unbound doctor` found in a project's files, for the project's release.</summary>
internal sealed class ClassCoverageRequest
{
    public string? Release { get; set; }
    public List<Unbound.Runtime.ClassCoverage.Found> Classes { get; set; } = new();
}

/// <summary><c>message-coverage</c>: the project's scripts and its asset files (no folders).</summary>
internal sealed class MessageCoverageRequest
{
    public CompileRequest Compilation { get; set; } = new();
    public List<string> AssetFiles { get; set; } = new();
}
