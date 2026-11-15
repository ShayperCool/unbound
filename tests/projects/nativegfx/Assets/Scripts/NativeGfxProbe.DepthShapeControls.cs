using System;
using UnityEngine;

public partial class NativeGfxProbe
{
    void DepthShapeControls()
    {
        var previousLogs = logs.ToArray();
        logs.Clear();
        global::DepthShapeControls.Run(Value, (rt, key) =>
        {
            for (int i = 0; i < 32; i++) NativeGfx_SetShape(i, "", IntPtr.Zero);
            NativeGfx_SetShape(0, "color", rt.GetNativeTexturePtr());
            NativeGfx_SetShape(1, "depth", rt.GetNativeDepthBufferPtr());
            Value(key + ".images", L(TextureEvent(0)));
        });
        Value("depthShape.logs", L(logs));
        logs.Clear();
        logs.AddRange(previousLogs);
    }
}
