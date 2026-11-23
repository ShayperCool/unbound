using System;
using UnityEditor;
using UnityEngine;

/// <summary>`Unity -batchmode -executeMethod TexFormatsRunner.Record` on the GPU, with
/// <c>TEXTURE_FORMATS_PROBE_OUT</c> set: records the probe in Edit mode and quits.</summary>
public static class TexFormatsRunner
{
    public static void Record()
    {
        try
        {
            TexFormatsProbe.Record();
            // Complete the Editor's graphics updates after the capture before quitting.
            const double shutdownDelaySeconds = 5;
            double exitAt = EditorApplication.timeSinceStartup + shutdownDelaySeconds;
            EditorApplication.update += ExitAfterCapture;

            void ExitAfterCapture()
            {
                if (EditorApplication.timeSinceStartup < exitAt) return;
                EditorApplication.update -= ExitAfterCapture;
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
