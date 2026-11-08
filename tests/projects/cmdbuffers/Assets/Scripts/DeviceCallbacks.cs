using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

public partial class CmdBuffersProbe
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void PluginCallback(int eventId, IntPtr data);
    static readonly int[] callbackEvents = new int[16];
    static int callbackCount;
    static IntPtr callbackPixels;
    static readonly PluginCallback pluginCallback = PluginCall;
    static void PluginCall(int eventId, IntPtr data)
    {
        int i = Interlocked.Increment(ref callbackCount) - 1;
        if (i < callbackEvents.Length) callbackEvents[i] = eventId;
        if (eventId == 7 || eventId == 9) Marshal.WriteIntPtr(data, callbackPixels);
    }

    void DeviceCallbacks()
    {
        var texture = Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
        texture.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black });
        texture.Apply();
        var image = Keep(new RenderTexture(2, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        image.Create();
        callbackPixels = Marshal.AllocHGlobal(16);
        Marshal.Copy(new byte[] { 21, 43, 65, 255, 21, 43, 65, 255, 21, 43, 65, 255, 21, 43, 65, 255 }, 0, callbackPixels, 16);
        IntPtr fn = Marshal.GetFunctionPointerForDelegate(pluginCallback);
        void Case(string key, Action<CommandBuffer> record)
        {
            callbackCount = 0;
            using var cmd = new CommandBuffer { name = "probe.device.callback" };
            Try(key + ".record", () => { record(cmd); Value(key + ".record.ok", "true"); });
            Try(key + ".execute", () => { Graphics.ExecuteCommandBuffer(cmd); Value(key + ".execute.ok", "true"); });
            Value(key + ".texture", Read(texture));
            Value(key + ".cpu", Q(ColorUtility.ToHtmlStringRGBA(texture.GetPixel(0, 0))));
            Value(key + ".events", U(Array.ConvertAll(callbackEvents[..Math.Min(callbackCount, 16)], x => (uint)x)));
        }
        try
        {
            Case("device.plugin.blit.callback", cmd => cmd.IssuePluginCustomBlit(fn, 2, image, image, 13, 0));
#pragma warning disable 618
            Case("device.plugin.texture.callback", cmd => cmd.IssuePluginCustomTextureUpdate(fn, texture, 11));
            Case("device.plugin.texture1.callback", cmd => cmd.IssuePluginCustomTextureUpdateV1(fn, texture, 12));
#pragma warning restore 618
            Case("device.plugin.texture2.callback", cmd => cmd.IssuePluginCustomTextureUpdateV2(fn, texture, 13));

        }
        finally { Marshal.FreeHGlobal(callbackPixels); callbackPixels = IntPtr.Zero; }
    }
}
