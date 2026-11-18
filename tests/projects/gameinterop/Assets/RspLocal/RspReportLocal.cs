using System.Collections.Generic;

/// <summary>Which response-file defines reached this assembly (tests/projects/gameinterop rsp.*).</summary>
public static class RspReportLocal
{
    public static string Defines()
    {
        var d = new List<string>();
#if RSP_GLOBAL
        d.Add("RSP_GLOBAL");
#endif
#if RSP_SECOND
        d.Add("RSP_SECOND");
#endif
#if RSP_SHORT
        d.Add("RSP_SHORT");
#endif
#if RSP_COMMA
        d.Add("RSP_COMMA");
#endif
#if RSP_LOCAL
        d.Add("RSP_LOCAL");
#endif
#if RSP_PACKAGE
        d.Add("RSP_PACKAGE");
#endif
#if PROBE_DELEGATE_DEFINE
        d.Add("PROBE_DELEGATE_DEFINE");
#endif
        return string.Join(",", d);
    }

    /// <summary>A nullable annotation: the compiler records it only in a nullable context.</summary>
    public static string Echo(string? text) => text;

    /// <summary>A constant interpolated string: C# 10.</summary>
    const string Part = "ten";
    public const string Version = $"csharp-{Part}";
}
