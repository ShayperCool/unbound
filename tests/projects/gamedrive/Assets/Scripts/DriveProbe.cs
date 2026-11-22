using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unbound.GameDrive;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds a small UI in Play mode and runs the game driver's script on it (see README.md): what
/// each step logged and which pointer events the UI got for it, and the UI's state at the end.
/// Nothing recorded depends on how long a frame or the run took.
/// </summary>
public class DriveProbe : MonoBehaviour
{
    const string Variable = "GAMEDRIVE_PROBE_OUT";

    // The popup appears a second after the start, on the wall clock; the script waits for it.
    const string Script = @"
wait:0.3
tap:0.2,0.8
click:Button
hide:Overlay
wait:0.3
tap:0.2,0.8
tap:0.5,0.8
tap:0.95,0.05
swipe:0.5,0.8:0.9,0.8:0.2
swipe:0.3,0.5:0.7,0.5:0.2
swipe:0.5,0.55:0.5,0.45:0.2
stick:Stick:1,0:0.3
stick:Stick:-0.5,0.5:0.3
drag:Stick:0,-1:0.2
swipe:0.45,0.25:0.7,0.25:0.2
until:Popup
click:Popup
gone:Popup
press:Hold:0.3
click:Canvas/Hold
swipe:0.1,0.55:0.1,0.45:0.2
click:Twin
dump
until:Nothing:0
";

    // The second run, on the frame clock: every step's log line is a fixed number of frames after
    // the one before it, and a frame is 1/50 s of game time.
    const string FrameScript = @"
wait:0.5
click:Button
stick:Stick:0,1:0.25
swipe:0.3,0.5:0.7,0.5:0.2
wait:0.1
";

    static readonly SortedDictionary<string, List<string>> s_Steps = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
    static string s_Run = "step";
    static int s_Step = -1, s_LastFrame = -1;
    static int s_ButtonClicks, s_PopupClicks;
    static Slider s_Slider;
    static GameObject s_Popup;
    static DriveProbe s_Probe;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        s_Probe = Build();
        Driver.Logged += Logged;
        Driver.Finished += Finished;
        // The last step waits for an object that never comes: the run ends on the timeout.
        Driver.Run(new DriveOptions { Script = Script, Exit = false, Settle = 0.2f, Timeout = 3f });
        s_Probe.StartCoroutine(s_Probe.ShowPopup());
    }

    IEnumerator ShowPopup()
    {
        yield return new WaitForSecondsRealtime(1f);
        s_Popup.SetActive(true);
    }

    public static string F2(float v)
    {
        double rounded = Math.Round(v, 2);
        return (rounded == 0 ? 0 : rounded).ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>An event of the UI, once for a run of the same event (a drag's moves).</summary>
    public static void Event(string text) => Add(text);

    static void Add(string text)
    {
        string key = s_Run + "." + s_Step.ToString("00", CultureInfo.InvariantCulture);
        if (!s_Steps.TryGetValue(key, out var lines)) s_Steps[key] = lines = new List<string>();
        if (lines.Count == 0 || lines[lines.Count - 1] != text) lines.Add(text);
    }

    static void Logged(string line)
    {
        if (line.StartsWith("start ", StringComparison.Ordinal)) return;
        if (line.StartsWith("step ", StringComparison.Ordinal))
        {
            int space = line.IndexOf(' ', 5);
            s_Step = int.Parse(line.Substring(5, space - 5), CultureInfo.InvariantCulture);
            line = line.Substring(space + 1);
        }
        if (s_Run == "frames")
        {
            // Frames since the run's last line, and the frame's game time.
            line += " +" + (s_LastFrame < 0 ? 0 : Time.frameCount - s_LastFrame) + " dt=" + Time.deltaTime.ToString("0.0000", CultureInfo.InvariantCulture);
            s_LastFrame = Time.frameCount;
        }
        Add("> " + line);
    }

    static int s_FirstCode;

    static void Finished(int code)
    {
        if (s_Run == "step")
        {
            s_FirstCode = code;
            s_Run = "frames";
            s_Step = -1;
            Driver.Run(new DriveOptions { Script = FrameScript, Exit = false, Settle = 0.2f, Timeout = 3f, Frames = true, Fps = 50 });
            return;
        }
        Driver.Finished -= Finished;
        Driver.Logged -= Logged;
        string final = $"button={s_ButtonClicks} popup={s_PopupClicks} slider={F2(s_Slider.value)} popupActive={s_Popup.activeSelf} code={s_FirstCode},{code} captureFramerate={Time.captureFramerate}";
        HoverControls();
        var controls = new DriveDriverControls(s_Probe.transform, Environment.GetEnvironmentVariable(Variable));
        s_Probe.StartCoroutine(controls.Record(() => Write(final)));
    }

    static void Write(string final)
    {
        var o = new StringBuilder();
        void Put(string key, string value) => o.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");
        foreach (var step in s_Steps) Put(step.Key, string.Join(" | ", step.Value));
        Put("final", final);
        o.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), o.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }

    internal static void Control(string value)
    {
        s_Run = "controls";
        s_Step = 0;
        Add(value);
    }

    static void HoverControls()
    {
        // Move from a parent to a child, back to the parent, and then off both.
        var parent = new Vector2(0.85f * Screen.width, 0.15f * Screen.height);
        var child = new Vector2(0.91f * Screen.width, parent.y);
        var outside = new Vector2(0.97f * Screen.width, parent.y);
        s_Run = "hover";
        s_Step = 0;
#if UNITY_EDITOR
        var module = EventSystem.current.GetComponent<DriveProbeInputModule>();
        module.Sample(parent, TouchPhase.Began);
        module.Sample(child, TouchPhase.Moved);
        module.Sample(parent, TouchPhase.Moved);
        module.Sample(outside, TouchPhase.Moved);
        module.Sample(outside, TouchPhase.Ended);
        module.Sample(parent, TouchPhase.Began);
        module.Sample(child, TouchPhase.Moved);
        module.Sample(child, TouchPhase.Ended);
#else
        HoverWithDriver(parent, child, outside);
#endif
        s_Step = 1;
        HoverWithDriver(parent, child, outside);
    }

    static void HoverWithDriver(Vector2 parent, Vector2 child, Vector2 outside)
    {
        var pointer = new DrivePointer();
        if (!pointer.Down(parent)) throw new InvalidOperationException("hover control missed the parent");
        pointer.Move(child);
        pointer.Move(parent);
        pointer.Move(outside);
        pointer.Up();
        if (!pointer.Down(parent)) throw new InvalidOperationException("hover control missed the parent");
        pointer.Move(child);
        pointer.Up();
    }

    // ------------------------------------------------------------------ the UI

    internal static RectTransform Box(string name, Transform parent, float x0, float y0, float x1, float y1, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(x0, y0);
        rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        return rect;
    }

    static DriveProbe Build()
    {
#if UNITY_EDITOR
        new GameObject("EventSystem", typeof(EventSystem), typeof(DriveProbeInputModule));
#else
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
        var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var root = canvasObject.transform;

        var button = Box("Button", root, 0.1f, 0.7f, 0.3f, 0.9f, Color.green).gameObject.AddComponent<Button>();
        button.onClick.AddListener(() => { s_ButtonClicks++; Event("Button.click"); });
        button.gameObject.AddComponent<DriveSelect>();
        Box("TapArea", root, 0.4f, 0.7f, 0.6f, 0.9f, Color.yellow).gameObject.AddComponent<DriveTap>();
        Box("SwipeArea", root, 0.2f, 0.4f, 0.8f, 0.6f, Color.cyan).gameObject.AddComponent<DriveSwipe>();
        Box("Stick", root, 0.05f, 0.05f, 0.3f, 0.35f, Color.magenta).gameObject.AddComponent<DriveStick>();
        Box("Hold", root, 0.85f, 0.4f, 0.95f, 0.6f, Color.gray).gameObject.AddComponent<DriveTap>();

        // Two halves in a group that hears the pointer enter and leave: a swipe from one to the other.
        var group = Box("Group", root, 0.02f, 0.4f, 0.18f, 0.6f, Color.blue);
        group.gameObject.AddComponent<DriveHover>();
        Box("Upper", group, 0f, 0.5f, 1f, 1f, Color.white).gameObject.AddComponent<DriveTap>();
        Box("Lower", group, 0f, 0f, 1f, 0.5f, Color.black).gameObject.AddComponent<DriveTap>();

        var hover = Box("HoverParent", root, 0.83f, 0.08f, 0.93f, 0.22f, Color.blue);
        hover.gameObject.AddComponent<DriveHoverState>();
        Box("HoverChild", hover, 0.5f, 0f, 1f, 1f, Color.white).gameObject.AddComponent<DriveHoverState>();

        // Two buttons of one name: the one made second is put first among its siblings.
        foreach (var (label, x) in new[] { ("A", 0.4f), ("B", 0.52f) })
        {
            var twin = Box("Twin", root, x, 0.62f, x + 0.08f, 0.68f, Color.green).gameObject.AddComponent<Button>();
            twin.onClick.AddListener(() => Event("Twin.click " + label));
            if (label == "B") twin.transform.SetAsFirstSibling();
        }

        var sliderRect = Box("Slider", root, 0.4f, 0.2f, 0.8f, 0.3f, Color.white);
        var area = new GameObject("Handle Slide Area", typeof(RectTransform));
        var areaRect = (RectTransform)area.transform;
        areaRect.SetParent(sliderRect, false);
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = areaRect.offsetMax = Vector2.zero;
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        var handleRect = (RectTransform)handle.transform;
        handleRect.SetParent(areaRect, false);
        handleRect.sizeDelta = new Vector2(10f, 0f);
        s_Slider = sliderRect.gameObject.AddComponent<Slider>();
        s_Slider.handleRect = handleRect;
        s_Slider.targetGraphic = handle.GetComponent<Image>();
        s_Slider.direction = Slider.Direction.LeftToRight;

        var popup = Box("Popup", root, 0.85f, 0.7f, 0.95f, 0.9f, Color.red).gameObject.AddComponent<Button>();
        s_Popup = popup.gameObject;
        popup.onClick.AddListener(() => { s_PopupClicks++; Event("Popup.click"); s_Popup.SetActive(false); });
        s_Popup.SetActive(false);

        // Drawn over everything until the script hides it.
        Box("Overlay", root, 0f, 0f, 1f, 1f, new Color(0f, 0f, 0f, 0.5f));
        return canvasObject.AddComponent<DriveProbe>();
    }
}
