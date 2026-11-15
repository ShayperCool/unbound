using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public partial class NativeGfxVulkanProbe
{
    void BindMsSetterControls()
    {
        var previousLogs = logs.ToArray(); logs.Clear();
        var fence = new RenderTexture(1, 1, 0); fence.Create();
        try
        {
            foreach (int samples in new[] { 1, 4 })
            foreach (bool initial in new[] { false, true })
            foreach (bool same in new[] { false, true })
            foreach (bool pointerFirst in new[] { false, true })
            {
                string key = "setter." + samples + "." + initial + "." + same + "." + pointerFirst;
                var rt = new RenderTexture(5, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    { name = key, antiAliasing = samples, bindTextureMS = initial };
                try
                {
                    Value(key + ".created", B(rt.Create()));
                    Value(key + ".before", B(rt.bindTextureMS));
                    // Only created textures provide valid resource pointers to the plugin.
                    Func<IntPtr> pointer = () => rt.IsCreated() ? rt.GetNativeTexturePtr() : IntPtr.Zero;
                    Action<string> observe = suffix =>
                    {
                        NativeGfxVk_SetDeviceBuffer(IntPtr.Zero);
                        NativeGfxVk_SetDevice(0, pointer());
                        NativeGfxVk_SetDevice(1, IntPtr.Zero);
                        NativeGfxVk_Step(key + suffix);
                        GL.IssuePluginEvent(NativeGfxVk_DeviceEvent(), 0);
                        Pixels(fence, 1, 1);
                    };
                    IntPtr before = IntPtr.Zero;
                    if (pointerFirst) { before = pointer(); observe(".pointerBefore"); }
                    int start = logs.Count;
                    rt.bindTextureMS = same ? initial : !initial;
                    Value(key + ".set.logs", L(logs.Skip(start)));
                    Value(key + ".after", B(rt.bindTextureMS));
                    Value(key + ".afterCreated", B(rt.IsCreated()));
                    if (pointerFirst) Value(key + ".samePointer", B(before == pointer()));
                    observe(".after");
                    rt.Release();
                    Value(key + ".released", B(rt.IsCreated()));
                    rt.bindTextureMS = !initial;
                    Value(key + ".releasedFlag", B(rt.bindTextureMS));
                    Value(key + ".recreated", B(rt.Create()));
                    observe(".recreated");
                }
                finally { rt.Release(); DestroyImmediate(rt); }
            }
            foreach (var dimension in new[] { TextureDimension.Tex2DArray, TextureDimension.Cube, TextureDimension.Tex3D })
            {
                var rt = new RenderTexture(4, 4, 0) { dimension = dimension, volumeDepth = dimension == TextureDimension.Cube ? 1 : 2 };
                try
                {
                    string key = "setter.dimension." + dimension;
                    int start = logs.Count;
                    rt.bindTextureMS = true;
                    Value(key + ".flag", B(rt.bindTextureMS));
                    Value(key + ".created", B(rt.IsCreated()));
                    Value(key + ".logs", L(logs.Skip(start)));
                }
                finally { DestroyImmediate(rt); }
            }
            Value("setter.logs", L(logs));
        }
        finally
        {
            NativeGfxVk_SetDevice(0, IntPtr.Zero); NativeGfxVk_SetDevice(1, IntPtr.Zero);
            NativeGfxVk_SetDeviceBuffer(IntPtr.Zero); DestroyImmediate(fence);
            logs.Clear(); logs.AddRange(previousLogs);
        }
    }
}
