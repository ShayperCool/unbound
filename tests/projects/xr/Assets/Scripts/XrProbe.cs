using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// What the XR API answers without an XR device or plug-in (the Editor with no XR provider):
/// XRSettings, XRDevice, XRStats, InputDevices and InputTracking, the XR subsystems, an invalid
/// InputDevice, CommonUsages' names; then the same after asking for a device by name. Writes
/// <c>xr.jsonl</c> into <c>XR_PROBE_OUT</c>, then quits.
/// </summary>
public class XrProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("XrProbe").AddComponent<XrProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string B(bool v) => v ? "true" : "false";
    static string Q(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    void Record(string kind, string body) => m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");
    void OnLog(string message, string stack, LogType type) => m_Logs.Add(type + ": " + message);
    string Logs()
    {
        var text = "[" + string.Join(",", m_Logs.Select(Q)) + "]";
        m_Logs.Clear();
        return text;
    }

    string Settings()
    {
        var desc = XRSettings.eyeTextureDesc;
        var supported = XRSettings.supportedDevices;
        return $"\"enabled\":{B(XRSettings.enabled)},\"isDeviceActive\":{B(XRSettings.isDeviceActive)},\"loadedDeviceName\":{Q(XRSettings.loadedDeviceName)},\"supportedDevices\":{(supported == null ? "null" : "[" + string.Join(",", supported.Select(Q)) + "]")},\"eyeTextureWidth\":{XRSettings.eyeTextureWidth},\"eyeTextureHeight\":{XRSettings.eyeTextureHeight},\"eyeTextureResolutionScale\":{F(XRSettings.eyeTextureResolutionScale)},\"renderViewportScale\":{F(XRSettings.renderViewportScale)},\"occlusionMaskScale\":{F(XRSettings.occlusionMaskScale)},\"useOcclusionMesh\":{B(XRSettings.useOcclusionMesh)},\"showDeviceView\":{B(XRSettings.showDeviceView)},\"gameViewRenderMode\":\"{XRSettings.gameViewRenderMode}\",\"deviceEyeTextureDimension\":\"{XRSettings.deviceEyeTextureDimension}\",\"stereoRenderingMode\":\"{XRSettings.stereoRenderingMode}\",\"eyeTextureDesc\":[{desc.width},{desc.height},{desc.volumeDepth},\"{desc.dimension}\",\"{desc.graphicsFormat}\",{desc.msaaSamples},\"{desc.vrUsage}\"]";
    }

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("XR_PROBE_OUT") ?? "/tmp/xr";
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += OnLog;
        Record("settings", Settings() + $",\"logs\":{Logs()}");
#pragma warning disable 618
        Record("device", $"\"refreshRate\":{F(XRDevice.refreshRate)},\"fovZoomFactor\":{F(XRDevice.fovZoomFactor)},\"trackingSpace\":\"{XRDevice.GetTrackingSpaceType()}\",\"setTrackingSpace\":{B(XRDevice.SetTrackingSpaceType(TrackingSpaceType.RoomScale))},\"trackingSpaceAfter\":\"{XRDevice.GetTrackingSpaceType()}\",\"nativePtr\":{(XRDevice.GetNativePtr() == System.IntPtr.Zero ? "0" : "1")},\"logs\":{Logs()}");
#pragma warning restore 618
        Record("stats", $"\"gpu\":{B(XRStats.TryGetGPUTimeLastFrame(out var gpu))},\"gpuValue\":{F(gpu)},\"dropped\":{B(XRStats.TryGetDroppedFrameCount(out var dropped))},\"droppedValue\":{dropped},\"present\":{B(XRStats.TryGetFramePresentCount(out var present))},\"presentValue\":{present},\"logs\":{Logs()}");

        var devices = new List<InputDevice> { default };
        InputDevices.GetDevices(devices);
        var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        var characteristic = new List<InputDevice> { default };
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, characteristic);
        var nodes = new List<XRNodeState> { default };
        InputTracking.GetNodeStates(nodes);
        var usages = new List<InputFeatureUsage> { default };
        bool gotUsages = head.TryGetFeatureUsages(usages);
        bool gotButton = head.TryGetFeatureValue(CommonUsages.primaryButton, out var pressed);
        bool gotPosition = head.TryGetFeatureValue(CommonUsages.devicePosition, out var position);
        Record("input", $"\"devices\":{devices.Count},\"withCharacteristics\":{characteristic.Count},\"nodes\":{nodes.Count},\"head\":[{B(head.isValid)},{Q(head.name)},{Q(head.manufacturer)},{Q(head.serialNumber)},\"{head.characteristics}\",{B(head.subsystem == null)}],\"usages\":[{B(gotUsages)},{usages.Count}],\"button\":[{B(gotButton)},{B(pressed)}],\"position\":[{B(gotPosition)},{F(position.x)}],\"haptics\":{B(head.TryGetHapticCapabilities(out _))},\"impulse\":{B(head.SendHapticImpulse(0, 0.5f, 0.1f))},\"logs\":{Logs()}");
        var all = typeof(CommonUsages).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(f => $"[{Q(f.Name)},{Q(f.GetValue(null)?.GetType().GetProperty("name")?.GetValue(f.GetValue(null)) as string)},{Q(f.FieldType.IsGenericType ? f.FieldType.GetGenericArguments()[0].Name : f.FieldType.Name)}]");
        Record("all usages", $"\"fields\":[{string.Join(",", all)}]");
        Record("usages", $"\"names\":[{Q(CommonUsages.primaryButton.name)},{Q(CommonUsages.trigger.name)},{Q(CommonUsages.devicePosition.name)},{Q(CommonUsages.deviceRotation.name)},{Q(CommonUsages.primary2DAxis.name)},{Q(CommonUsages.userPresence.name)},{Q(CommonUsages.isTracked.name)},{Q(CommonUsages.batteryLevel.name)}]");

        var displays = new List<XRDisplaySubsystem> { null };
        SubsystemManager.GetSubsystems(displays);
        var inputs = new List<XRInputSubsystem> { null };
        SubsystemManager.GetSubsystems(inputs);
        var displayDescriptors = new List<XRDisplaySubsystemDescriptor> { null };
        SubsystemManager.GetSubsystemDescriptors(displayDescriptors);
        Record("subsystems", $"\"displays\":{displays.Count},\"inputs\":{inputs.Count},\"displayDescriptors\":{displayDescriptors.Count},\"logs\":{Logs()}");

        XRSettings.enabled = true;
        XRSettings.LoadDeviceByName("MockHMD");
        yield return null;
        yield return null;
        Record("after load", Settings() + $",\"logs\":{Logs()}");
        XRSettings.enabled = false;
        XRSettings.eyeTextureResolutionScale = 1.5f;
        XRSettings.renderViewportScale = 0.5f;
        yield return null;
        Record("after set", Settings() + $",\"logs\":{Logs()}");

        Application.logMessageReceived -= OnLog;
        File.WriteAllText(Path.Combine(dir, "xr.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
