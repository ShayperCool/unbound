using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
public sealed class InputConfigControl:MonoBehaviour
{
 static readonly List<string> Values=new List<string>();
 static string output;
 int frame,events,changes,states,beforeStates;
 [DllImport("libgdk-3.so.0",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr gdk_display_get_default();
 [DllImport("libgdk-3.so.0",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr gdk_keymap_get_for_display(IntPtr display);
 [DllImport("libgobject-2.0.so.0",CallingConvention=CallingConvention.Cdecl)] static extern void g_signal_emit_by_name(IntPtr instance,[MarshalAs(UnmanagedType.LPStr)]string name);

 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
 static void Begin(){output=Environment.GetEnvironmentVariable("INPUT_CONFIG_OUT");if(output==null)return;new GameObject("Input configuration").AddComponent<InputConfigControl>();}
 static void Put(string key,object v){Values.Add("{\"k\":\""+key+"\",\"v\":\""+Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture)+"\"}");}
 void Start(){InputSystem.onEvent+=(e,d)=>{if(d is Keyboard&&e.type==DeviceConfigurationEvent.Type)events++;if(d is Keyboard&&e.type==StateEvent.Type)states++;};InputSystem.onDeviceChange+=(d,c)=>{if(d is Keyboard&&c==InputDeviceChange.ConfigurationChanged)changes++;};Put("keyboard.present",Keyboard.current!=null);Put("layout",Keyboard.current.keyboardLayout);}
 void Update()
 {
  frame++;
  if(frame==3){Put("beforeName.events",events);Put("beforeName.changes",changes);Put("firstName",Keyboard.current.aKey.displayName);}
  if(frame==6){Put("afterName.events",events);Put("afterName.changes",changes);Put("secondName",Keyboard.current.bKey.displayName);}
  if(frame==9){Put("afterSecondName.events",events);Put("afterSecondName.changes",changes);}
  if(frame==10){var keymap=gdk_keymap_get_for_display(gdk_display_get_default());Put("signal.keymapPresent",keymap!=IntPtr.Zero);g_signal_emit_by_name(keymap,"keys-changed");}
  if(frame==15){Put("afterSignal.events",events);Put("afterSignal.changes",changes);g_signal_emit_by_name(gdk_keymap_get_for_display(gdk_display_get_default()),"keys-changed");}
  if(frame!=20)return;
  Put("afterSecondSignal.events",events);Put("afterSecondSignal.changes",changes);
  Values.Sort(StringComparer.Ordinal);Put("complete",1);File.WriteAllLines(output,Values);
#if UNITY_EDITOR
  UnityEditor.EditorApplication.Exit(0);
#endif
 }
}
