using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Unbound.ApiTools;

/// <summary>Exact, release-scoped declaration debt. A changed declaration never matches an old
/// exception. Raw differences remain available separately from the failing errors.</summary>
public sealed class ApiCheckException
{
    public string Release { get; set; } = "";
    public string Category { get; set; } = "";
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public string Reason { get; set; } = "";
}

public static class ApiCheckAllowList
{
    public static void Apply(ApiCheckReport report, string path, string release)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var entries = JsonSerializer.Deserialize<List<ApiCheckException>>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException("API declaration allow-list must be an array");
        var keys = new HashSet<(string, string, string, string)>();
        foreach (var entry in entries)
        {
            if (new[] { entry.Release, entry.Category, entry.Type, entry.Message, entry.Reason }.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Every API declaration exception needs a release, category, type, message and reason");
            if (!keys.Add((entry.Release, entry.Category, entry.Type, entry.Message)))
                throw new InvalidDataException($"Duplicate API declaration exception for {entry.Type}: {entry.Message}");
        }
        var applicable = entries.Where(e => e.Release == release).ToList();
        foreach (var error in report.Errors.ToArray())
        {
            var entry = applicable.FirstOrDefault(e => e.Category == error.Category && e.Type == error.Type && e.Message == error.Message);
            if (entry == null) continue;
            report.Errors.Remove(error);
            report.Allowed.Add(entry);
            applicable.Remove(entry);
        }
        foreach (var stale in applicable)
            report.Errors.Add(new ApiCheckError { Category = "allow-list", Type = stale.Type,
                Message = $"exception no longer matches: {stale.Category}: {stale.Message}" });
    }
}
