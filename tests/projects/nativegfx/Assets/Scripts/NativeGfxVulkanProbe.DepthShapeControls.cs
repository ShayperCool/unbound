using System;
using UnityEngine;

public partial class NativeGfxVulkanProbe
{
    void DepthShapeControls()
    {
        var previousLogs = logs.ToArray();
        logs.Clear();
        global::DepthShapeControls.Run(Value, (rt, key) =>
        {
            NativeGfxVk_SetDeviceBuffer(IntPtr.Zero);
            NativeGfxVk_SetDevice(0, rt.GetNativeTexturePtr());
            NativeGfxVk_SetDevice(1, rt.GetNativeDepthBufferPtr());
            NativeGfxVk_Step(key);
            GL.IssuePluginEvent(NativeGfxVk_DeviceEvent(), 0);
        });
        Value("depthShape.logs", L(logs));
        logs.Clear();
        logs.AddRange(previousLogs);
    }
}
