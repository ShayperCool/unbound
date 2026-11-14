using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;

public class XRValueProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("XRValueProbe").AddComponent<XRValueProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    static string Bits(float v) => BitConverter.SingleToInt32Bits(v).ToString("x8");
    static string Value(object v)
    {
        if (v is Vector3 p) return Bits(p.x)+","+Bits(p.y)+","+Bits(p.z);
        if (v is Quaternion q) return Bits(q.x)+","+Bits(q.y)+","+Bits(q.z)+","+Bits(q.w);
        return Convert.ToString(v, CultureInfo.InvariantCulture);
    }
    static object Set(object boxed, string field, object value)
    {
        var f = boxed.GetType().GetField(field, Fields);
        f.SetValue(boxed, f.FieldType.IsEnum ? Enum.ToObject(f.FieldType, value) : value); return boxed;
    }
    void Start()
    {
        string path = Environment.GetEnvironmentVariable("XR_VALUES_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Handles<Bone>(); Handles<Eyes>(); Handles<Hand>(); Caps(); Nodes(); NoProvider(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    void Layout(object value)
    {
        var type = value.GetType(); string key = type.Name+".layout.";
        Put(key+"kind", type.StructLayoutAttribute.Value); Put(key+"size", Marshal.SizeOf(type));
        var fields = type.GetFields(Fields); Put(key+"fields", fields.Length);
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(type));
        try
        {
            for (int i = 0; i < Marshal.SizeOf(type); i++) Marshal.WriteByte(ptr, i, 0);
            Marshal.StructureToPtr(value, ptr, false);
            object restored = Marshal.PtrToStructure(ptr, type);
            foreach (var f in fields)
            {
                Put(key+f.Name+".offset", Marshal.OffsetOf(type, f.Name).ToInt64());
                Put(key+f.Name+".value", Value(f.GetValue(value)));
                Put(key+f.Name+".restore", Value(f.GetValue(restored)));
            }
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
    static T Handle<T>(ulong device, uint feature) where T : struct
        => (T)Set(Set(default(T), "m_DeviceId", device), "m_FeatureIndex", feature);
    void Handles<T>() where T : struct, IEquatable<T>
    {
        string key = typeof(T).Name+".";
        T[] values = { default(T), Handle<T>(17, 3), Handle<T>(17, 4), Handle<T>(18, 3), Handle<T>(0x80000001ffffffffUL, 0x80000001U), Handle<T>(ulong.MaxValue, uint.MaxValue) };
        var equal = typeof(T).GetMethod("op_Equality"); var different = typeof(T).GetMethod("op_Inequality");
        var set = new HashSet<T>(); var map = new Dictionary<T,int>();
        for (int i = 0; i < values.Length; i++)
        {
            T v = values[i]; set.Add(v); map[v] = i;
            Put(key+i+".hash", v.GetHashCode()); Put(key+i+".null", v.Equals((object)null)); Put(key+i+".wrong", v.Equals((object)17));
            Put(key+i+".device", typeof(T).GetProperty("deviceId", Fields).GetValue(v));
            Put(key+i+".feature", typeof(T).GetProperty("featureIndex", Fields).GetValue(v));
            for (int j = 0; j < values.Length; j++)
            {
                Put(key+i+"."+j+".typed", v.Equals(values[j])); Put(key+i+"."+j+".object", v.Equals((object)values[j]));
                Put(key+i+"."+j+".operator", equal.Invoke(null, new object[] { v, values[j] }));
                Put(key+i+"."+j+".different", different.Invoke(null, new object[] { v, values[j] }));
            }
        }
        Put(key+"set", set.Count); Put(key+"map", map.Count);
        for (int i = 0; i < values.Length; i++) Put(key+i+".lookup", map[values[i]]);
        T copy = values[1]; var modified = (T)Set(copy, "m_FeatureIndex", 9U);
        Put(key+"copy.original", copy.Equals(values[1])); Put(key+"copy.modified", modified.Equals(copy));
        Layout(values[4]);
    }
    void Caps()
    {
        object boxed = default(HapticCapabilities);
        string[] fields = { "m_NumChannels", "m_SupportsImpulse", "m_SupportsBuffer", "m_BufferFrequencyHz", "m_BufferMaxSize", "m_BufferOptimalSize" };
        object[] values = { 0x80000001U, true, true, 0xffffffffU, 0x80000003U, 0x76543210U };
        string[] props = { "numChannels", "supportsImpulse", "supportsBuffer", "bufferFrequencyHz", "bufferMaxSize", "bufferOptimalSize" };
        var original = (HapticCapabilities)boxed;
        var set = new HashSet<HapticCapabilities> { original };
        for (int i = 0; i < fields.Length; i++)
        {
            var changed = (HapticCapabilities)Set(original, fields[i], values[i]); set.Add(changed);
            Put("caps."+i+".equals", changed.Equals(original)); Put("caps."+i+".object", changed.Equals((object)original));
            Put("caps."+i+".operator", changed == original); Put("caps."+i+".different", changed != original); Put("caps."+i+".hash", changed.GetHashCode());
            typeof(HapticCapabilities).GetProperty(props[i]).SetValue(boxed, values[i]);
            Put("caps."+i+".set", typeof(HapticCapabilities).GetProperty(props[i]).GetValue(boxed));
        }
        var all = (HapticCapabilities)boxed; set.Add(all);
        Put("caps.set", set.Count); Put("caps.all.hash", all.GetHashCode()); Put("caps.copy", all.Equals((HapticCapabilities)boxed));
        Put("caps.null", all.Equals((object)null)); Put("caps.wrong", all.Equals((object)default(Bone)));
        Layout(all);
    }
    static readonly Vector3[] Vectors = { new Vector3(1.25f, -2, 3), new Vector3(4, 5, -6), new Vector3(-7, 8, 9), new Vector3(10, -11, 12), new Vector3(13, 14, -15) };
    static readonly Quaternion Rotation = new Quaternion(.25f, -.5f, .75f, -1);
    void State(string key, XRNodeState state)
    {
        Put(key+".id", state.uniqueID); Put(key+".node", (int)state.nodeType); Put(key+".tracked", state.tracked);
        Put(key+".position.ok", state.TryGetPosition(out var p)); Put(key+".position", Value(p));
        Put(key+".rotation.ok", state.TryGetRotation(out var r)); Put(key+".rotation", Value(r));
        Put(key+".velocity.ok", state.TryGetVelocity(out var v)); Put(key+".velocity", Value(v));
        Put(key+".angularVelocity.ok", state.TryGetAngularVelocity(out var av)); Put(key+".angularVelocity", Value(av));
        Put(key+".acceleration.ok", state.TryGetAcceleration(out var a)); Put(key+".acceleration", Value(a));
        Put(key+".angularAcceleration.ok", state.TryGetAngularAcceleration(out var aa)); Put(key+".angularAcceleration", Value(aa));
        Put(key+".flags", (int)typeof(XRNodeState).GetField("m_AvailableFields", Fields).GetValue(state));
    }
    void Nodes()
    {
        var state = new XRNodeState(); State("node.default", state);
        state.uniqueID = 0xfedcba9876543210UL; state.nodeType = XRNode.RightHand; state.tracked = true;
        State("node.identity", state);
        string[] props = { "position", "rotation", "velocity", "angularVelocity", "acceleration", "angularAcceleration" };
        object[] values = { Vectors[0], Rotation, Vectors[1], Vectors[2], Vectors[3], Vectors[4] };
        object boxed = state;
        for (int i = 0; i < props.Length; i++)
        {
            typeof(XRNodeState).GetProperty(props[i]).SetValue(boxed, values[i]); State("node.set."+props[i], (XRNodeState)boxed);
        }
        state = (XRNodeState)boxed; Layout(state);
        State("node.unavailable", (XRNodeState)Set(state, "m_AvailableFields", 0));
        State("node.rotationOnly", (XRNodeState)Set(state, "m_AvailableFields", 2));
        var copy = state; copy.position = new Vector3(-17, 18, 19); copy.uniqueID = 23; copy.tracked = false;
        State("node.copy.original", state); State("node.copy.changed", copy);
        foreach (int raw in new[] { -1, 0, 1, 2 }) Put("node.tracked."+raw, ((XRNodeState)Set(state, "m_Tracked", raw)).tracked);
        object exceptional = state;
        typeof(XRNodeState).GetProperty("position").SetValue(exceptional, new Vector3(float.NaN, float.PositiveInfinity, -0f));
        State("node.nonfinite", (XRNodeState)exceptional);
    }
    void NoProvider()
    {
        Put("provider.active", XRSettings.isDeviceActive);
        var devices = new List<InputDevice> { default(InputDevice) }; InputDevices.GetDevices(devices);
        Put("provider.devices", devices.Count);
        var device = default(InputDevice); Put("provider.invalid", device.isValid);
        Hand hand = Handle<Hand>(17, 3); Eyes eyes = Handle<Eyes>(17, 3); Bone bone = Handle<Bone>(17, 3);
        Put("provider.hand.ok", device.TryGetFeatureValue(CommonUsages.handData, out hand)); Put("provider.hand.cleared", hand.Equals(default(Hand)));
        Put("provider.eyes.ok", device.TryGetFeatureValue(CommonUsages.eyesData, out eyes)); Put("provider.eyes.cleared", eyes.Equals(default(Eyes)));
        Put("provider.bone.ok", device.TryGetFeatureValue(new InputFeatureUsage<Bone>("InvalidBone"), out bone)); Put("provider.bone.cleared", bone.Equals(default(Bone)));
        var capabilities = (HapticCapabilities)Set(default(HapticCapabilities), "m_NumChannels", 17U);
        Put("provider.caps.ok", device.TryGetHapticCapabilities(out capabilities)); Put("provider.caps.cleared", capabilities.Equals(default(HapticCapabilities)));
    }

}
