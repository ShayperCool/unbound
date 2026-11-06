using System;
using UnityEditor;
using UnityEngine;

/// <summary>`Unity -batchmode -executeMethod TexFormatsRunner.Record` on the GPU, with
/// <c>TEXTURE_FORMATS_PROBE_OUT</c> set: records the probe in Edit mode and quits.</summary>
public static class TexFormatsRunner
{
    public static void Record()
    {
        try { TexFormatsProbe.Record(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
