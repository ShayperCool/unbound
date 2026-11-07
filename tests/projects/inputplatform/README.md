# Input and host display answers

Unity 6000.0.71f1, Linux Editor, Play mode, Input System 1.19.0, both input backends.
`Reference/unity-6000.0.71f1/` has one capture for each display context, published
only after two independent GPU Editor processes exited successfully with identical records.
Repeats and their supervisor receipts stay in the validation cache. The `large` and `control`
contexts use `--display-only`; their references cover display facts only. A preliminary
large full-API pair exposed variable Game view focus and is retained only as raw cache output;
it is not a pointer oracle. Focused pointer behavior is checked by the normal private capture.

```
python3 tests/projects/inputplatform/run.py editor --release 6000.0.71f1 --label capture
python3 tests/projects/inputplatform/run.py publish --release 6000.0.71f1 --label capture
# Sequential captures on the authenticated host desktop:
python3 tests/projects/inputplatform/run.py editor --release 6000.0.71f1 --label host --host-display
python3 tests/projects/inputplatform/run.py publish --release 6000.0.71f1 --label host --host-display
UNBOUND=/path/to/unbound python3 tests/projects/inputplatform/run.py check --release 6000.0.71f1
```

The engine gate uses a private 1280x720 Xvfb with GPU rendering through VirtualGL.
It changes only that private display's clipboard. Dependencies are Xvfb, VirtualGL,
xclip, xrandr and xdotool. Host recording temporarily changes the host clipboard. The engine harness focuses its
private window before sampling; it cannot use the host Wayland connection.
`clipboard_control.py` is an independent X11 owner/reader. Reads run asynchronously
while the Editor pumps its event loop; external writes get five frames for ownership
notifications. Tests include empty, non-text, Unicode, line endings, 1 MiB text,
null, cross-property reads/writes and calls from another thread.

## Contracts and measured answers

| API / documentation, Unity 6000.0 | Documented guarantee or limitation | Measured Play mode behavior |
| --- | --- | --- |
| [systemCopyBuffer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUIUtility-systemCopyBuffer.html) | System-wide clipboard access | Real clipboard; null becomes empty text; absent/non-text becomes empty; both properties enforce the main thread. |
| [currentResolution](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Screen-currentResolution.html) | Windowed players report desktop size; VR has separate texture-size APIs | Desktop size, 60/1, even when the host mode is approximately 59.962 Hz. |
| [resolutions](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Screen-resolutions.html) | Monitor modes for exclusive full-screen players; do not assume those player guarantees in the Editor | Standard Editor sizes at 60/1, filtered by desktop dimensions, ordered by height then width. This differs from both Xvfb's single mode and the host's advertised modes. Display controls cover 1280x720, 1920x1080, 8192x8192 and a nonstandard 1234x777 desktop. The large control reveals all 66 standard sizes; every smaller list is its dimension-wise filter. |
| [dpi](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Screen-dpi.html) | Physical density, zero if unknown; Android reports a logical density bucket | Zero on both displays, which report no physical dimensions. Scaled logical density is not substituted. |
| [mainWindowDisplayInfo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Screen-mainWindowDisplayInfo.html) / [GetDisplayLayout](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Screen.GetDisplayLayout.html) | Desktop standalone support; allocation guarantee excludes the Editor | Real host name, desktop size, work area and unreduced mode-clock/total ratio. Host: 173000000/2885120 and work area [0,32,1920,960]. Xvfb: 0/1 and [0,0,1280,720]. Null list throws ArgumentNullException. |
| [Display.displays](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Display-displays.html) | Player display enumeration; initial display is primary | One active Game view display; system and rendering sizes both equal the Game view, 640x360, on either desktop. |
| [Mouse.WarpCursorPosition](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/api/UnityEngine.InputSystem.Mouse.html#UnityEngine_InputSystem_Mouse_WarpCursorPosition_UnityEngine_Vector2_) | Player window coordinates; platform restrictions apply | Asynchronous. On focused private Game views both input backends move together with the requested displacement. The Editor's fit scale/toolbar contributes a constant offset. The host Game view did not honor either warp in these captures. No public Cursor.WarpCursorPosition or Input.WarpMousePosition exists in this release. |
| [imeCompositionMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Input-imeCompositionMode.html) | Auto enables IME for selected text fields; On/Off force composition availability | Initial Auto, empty composition, zero cursor position, false selection. On/Off/Auto and cursor position round-trip. Composition remains empty and selection false. |

The comparator reports every missing, extra or different record. Display-dependent values
are compared against the private display's OS rule, not literal values from the host capture.
Warp checks compare displacement, asynchronous state and agreement between legacy and package
input. They do not impose the Editor window's toolbar offset on an application window.
The displacement tolerance is 0.001 pixels for the captured fit-scale rounding.

## Limits and follow-ups

No configured input method was available, so composition is unsupported with a clear warning;
committed text still travels through ordinary input events. There is no composition parity claim.
Other releases keep the prior host-mode interpretation. Multiple monitors,
physical-size DPI, native Wayland and macOS display metadata need their own captures.
Clipboard transport uses arboard's X11/Wayland and NSPasteboard backends; Wayland/macOS
clipboard behavior has not been measured here. The clipboard's host-error fallback stays
explicitly diagnosed. A blocking X11 event connection invalidates the cache on RandR changes and desktop
work-area properties. The work-area control changes the same output without recreating its
monitor, observes [0,40,1280,640], and then restores the full area. Its two Editor captures
match. Mode-change event tests retain the same screen identity while changing geometry;
Xvfb on this machine does not support adding another output mode. Frame pacing uses the
actual host refresh, separately from the public Editor resolution answer.
