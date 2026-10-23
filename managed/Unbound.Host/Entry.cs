using System;
using System.Runtime.InteropServices;
using System.Text;
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
                return JsonSerializer.Serialize(FacadeGenerator.Generate(facades.UnityManagedEngineDir, facades.OutputDir), Json);
            case "api-gaps":
                var gaps = JsonSerializer.Deserialize<ApiGapsRequest>(body, Json)
                    ?? throw new ArgumentException("empty api-gaps request");
                return JsonSerializer.Serialize(ApiGaps.Analyze(gaps.GameAssemblies, gaps.ImplAssembly, gaps.UnityManagedEngineDir), Json);
            default:
                throw new ArgumentException($"unknown method `{method}`");
        }
    }
}
