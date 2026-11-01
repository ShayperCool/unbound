using System;
using UnityEditor;
using UnityEngine;

public static class RawProbeRunner
{
    public static void Record()
    {
        try { TextureRawProbe.Record(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
