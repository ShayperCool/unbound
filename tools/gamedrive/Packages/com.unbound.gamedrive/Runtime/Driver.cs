using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unbound.GameDrive
{
    /// <summary>How a run is driven (the GAMEDRIVE_* environment variables, or a caller's own).</summary>
    public sealed class DriveOptions
    {
        /// <summary>The directory of drive.log and the screenshots (GAMEDRIVE_OUT).</summary>
        public string Out;
        /// <summary>The steps, separated by ';' or line ends (GAMEDRIVE_SCRIPT, or the file GAMEDRIVE_SCRIPT_FILE).</summary>
        public string Script = "";
        /// <summary>Count time in frames of 1/Fps seconds instead of on the wall clock, with
        /// Time.captureFramerate set to Fps (GAMEDRIVE_CLOCK=frames, GAMEDRIVE_FPS).</summary>
        public bool Frames;
        public int Fps = 60;
        /// <summary>Seconds a step may wait for its target before the run ends with code 3 (GAMEDRIVE_TIMEOUT).</summary>
        public float Timeout = 120f;
        /// <summary>Seconds a target must stay ready before a step acts on it (GAMEDRIVE_SETTLE).</summary>
        public float Settle = 0.5f;
        /// <summary>UnityEngine.Random's seed, set when the run starts (GAMEDRIVE_SEED).</summary>
        public int? Seed;
        /// <summary>Leave the Editor's Play mode (or quit the player) when the script ends.</summary>
        public bool Exit = true;

        public static DriveOptions FromEnvironment()
        {
            string Get(string name) => Environment.GetEnvironmentVariable(name);
            var output = Get("GAMEDRIVE_OUT");
            if (string.IsNullOrEmpty(output)) return null;
            var options = new DriveOptions { Out = output, Script = Get("GAMEDRIVE_SCRIPT") ?? "" };
            var file = Get("GAMEDRIVE_SCRIPT_FILE");
            if (!string.IsNullOrEmpty(file)) options.Script = File.ReadAllText(file);
            options.Frames = Get("GAMEDRIVE_CLOCK") == "frames";
            if (int.TryParse(Get("GAMEDRIVE_FPS"), out int fps) && fps > 0) options.Fps = fps;
            if (float.TryParse(Get("GAMEDRIVE_TIMEOUT"), NumberStyles.Float, CultureInfo.InvariantCulture, out float timeout)) options.Timeout = timeout;
            if (float.TryParse(Get("GAMEDRIVE_SETTLE"), NumberStyles.Float, CultureInfo.InvariantCulture, out float settle)) options.Settle = settle;
            if (int.TryParse(Get("GAMEDRIVE_SEED"), out int seed)) options.Seed = seed;
            return options;
        }
    }

    /// <summary>
    /// Runs a script of steps against the playing game, one after another: waits, and clicks, taps,
    /// swipes and stick gestures sent as UI pointer events (<see cref="DrivePointer"/>). A step
    /// waits for its target to be there and ready, so the same script follows the game through
    /// loads and tweens of any length; times are seconds on the wall clock unless the frame clock
    /// is chosen. See the package's README for the steps.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class Driver : MonoBehaviour
    {
        /// <summary>Every line the driver logs, without its time and frame.</summary>
        public static event Action<string> Logged;
        /// <summary>The script ended: 0, a quit step's code, or 3 after a timeout.</summary>
        public static event Action<int> Finished;

        DriveOptions m_Options;
        StreamWriter m_Log;
        readonly List<string[]> m_Steps = new List<string[]>();
        readonly List<string> m_Hidden = new List<string>();
        readonly DrivePointer m_Pointer = new DrivePointer();
        int m_Index, m_Phase, m_CaptureFramerate;
        bool m_Began, m_Done;
        double m_StepStart, m_ReadySince = -1.0, m_PhaseStart;
        double m_NextHide;
        Vector2 m_From, m_To;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var options = DriveOptions.FromEnvironment();
            if (options != null) Run(options);
        }

        /// <summary>Starts driving with these options (what GAMEDRIVE_OUT does at startup).</summary>
        public static Driver Run(DriveOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var go = new GameObject("Unbound.GameDrive") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            var driver = go.AddComponent<Driver>();
            driver.Begin(options);
            return driver;
        }

        void Begin(DriveOptions options)
        {
            m_Options = options;
            if (!string.IsNullOrEmpty(options.Out))
            {
                Directory.CreateDirectory(options.Out);
                m_Log = new StreamWriter(Path.Combine(options.Out, "drive.log"), false) { AutoFlush = true };
            }
            foreach (var line in (options.Script ?? "").Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var step = line.Trim();
                if (step.Length > 0 && step[0] != '#') m_Steps.Add(step.Split(':').Select(p => p.Trim()).ToArray());
            }
            m_CaptureFramerate = Time.captureFramerate;
            if (options.Frames) Time.captureFramerate = options.Fps;
            if (options.Seed is int seed) UnityEngine.Random.InitState(seed);
            Application.logMessageReceived += GameLog;
            SceneManager.sceneLoaded += SceneLoaded;
            Log($"start {m_Steps.Count} steps clock {(options.Frames ? "frames@" + options.Fps : "real")} screen {Screen.width}x{Screen.height}");
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= GameLog;
            SceneManager.sceneLoaded -= SceneLoaded;
            m_Log?.Dispose();
            m_Log = null;
        }

        void GameLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            var first = message.Split('\n')[0];
            Log(type == LogType.Exception ? $"game {type}: {first} @ {stack.Split('\n')[0]}" : $"game {type}: {first}");
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode) => Log($"scene {scene.path} {mode}");

        /// <summary>Seconds on the driver's clock: the wall clock, or frames of 1/Fps.</summary>
        double Now => m_Options.Frames ? Time.frameCount / (double)m_Options.Fps : Time.realtimeSinceStartupAsDouble;

        /// <summary>Seconds since a time on the clock. On the frame clock a whole number of frames
        /// must count as its seconds whatever the frame it started at, so the answer leans a
        /// microsecond late: 6 frames at 60 are 0.1 s, not a rounding below it.</summary>
        float Since(double start) => (float)(Now - start + 1e-6);

        void Log(string text)
        {
            m_Log?.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F2} f{1} {2}", Time.realtimeSinceStartup, Time.frameCount, text));
            Logged?.Invoke(text);
        }

        void Update()
        {
            if (m_Done) return;
            Hide();
            // One step that sends input, or one that still waits, ends the frame's work.
            while (m_Index < m_Steps.Count)
            {
                var step = m_Steps[m_Index];
                if (!m_Began)
                {
                    m_Began = true;
                    m_Phase = 0;
                    m_StepStart = Now;
                    m_ReadySince = -1.0;
                }
                bool sentInput;
                bool finished;
                try { finished = Step(step, out sentInput); }
                catch (Exception e)
                {
                    Log($"step {m_Index} {string.Join(":", step)} failed: {e.GetType().Name}: {e.Message}");
                    Finish(4);
                    return;
                }
                if (m_Done) return;
                if (!finished)
                {
                    if (m_Phase == 0 && Since(m_StepStart) > m_Options.Timeout)
                    {
                        Log($"step {m_Index} {string.Join(":", step)} timeout");
                        Finish(3);
                    }
                    return;
                }
                m_Index++;
                m_Began = false;
                if (sentInput) return;
            }
            Log("end");
            Finish(0);
        }

        void Finish(int code)
        {
            if (m_Done) return;
            m_Done = true;
            if (m_Pointer.Pressed) m_Pointer.Up();
            if (m_Options.Frames) Time.captureFramerate = m_CaptureFramerate;
            Log($"done {code}");
            Finished?.Invoke(code);
            if (!m_Options.Exit) return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }

        // ------------------------------------------------------------------ steps

        static float Number(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>"X,Y" as fractions of the screen from its bottom left, in pixels.</summary>
        static Vector2 ScreenPoint(string text)
        {
            var parts = text.Split(',');
            return new Vector2(Number(parts[0]) * Screen.width, Number(parts[1]) * Screen.height);
        }

        static Vector2 Pair(string text)
        {
            var parts = text.Split(',');
            return new Vector2(Number(parts[0]), Number(parts[1]));
        }

        /// <summary>Runs or continues a step. True when it is finished; <paramref name="sentInput"/>
        /// when it sent pointer events this frame.</summary>
        bool Step(string[] s, out bool sentInput)
        {
            sentInput = false;
            string label = string.Join(":", s);
            switch (s[0])
            {
                case "wait":
                    if (Since(m_StepStart) < Number(s[1])) return false;
                    Log($"step {m_Index} {label}");
                    return true;
                case "until":
                    if (!Ready(FindActive(s[1]) != null, s.Length > 2 ? Number(s[2]) : m_Options.Settle)) return false;
                    Log($"step {m_Index} {label}");
                    return true;
                case "gone":
                    if (FindActive(s[1]) != null) return false;
                    Log($"step {m_Index} {label}");
                    return true;
                case "log":
                    Log($"step {m_Index} {label}");
                    return true;
                case "seed":
                    UnityEngine.Random.InitState(int.Parse(s[1], CultureInfo.InvariantCulture));
                    Log($"step {m_Index} {label}");
                    return true;
                case "hide":
                    m_Hidden.Add(s[1]);
                    m_NextHide = 0.0;
                    Log($"step {m_Index} {label}");
                    return true;
                case "shot":
                    if (!string.IsNullOrEmpty(m_Options.Out)) ScreenCapture.CaptureScreenshot(Path.Combine(m_Options.Out, s[1] + ".png"));
                    Log($"step {m_Index} {label}");
                    return true;
                case "dump":
                    Dump();
                    return true;
                case "quit":
                    Log($"step {m_Index} {label}");
                    Finish(s.Length > 1 ? int.Parse(s[1], CultureInfo.InvariantCulture) : 0);
                    return true;
                case "click":
                    return Press(s, label, 0f, out sentInput);
                case "press":
                    return Press(s, label, s.Length > 2 ? Number(s[2]) : 0f, out sentInput);
                case "tap":
                    return Tap(s, label, out sentInput);
                case "swipe":
                    return Swipe(s, label, out sentInput);
                case "stick":
                case "drag":
                    return Stick(s, label, out sentInput);
                default:
                    throw new FormatException("unknown step");
            }
        }

        /// <summary>Whether a condition has held for <paramref name="seconds"/> without a break.</summary>
        bool Ready(bool condition, float seconds)
        {
            if (!condition)
            {
                m_ReadySince = -1.0;
                return false;
            }
            if (m_ReadySince < 0.0) m_ReadySince = Now;
            return Since(m_ReadySince) >= seconds;
        }

        /// <summary>click:TARGET and press:TARGET:SECONDS: a press on the target itself at its
        /// centre, whatever is drawn over it, released (with the click) after the hold.</summary>
        bool Press(string[] s, string label, float hold, out bool sentInput)
        {
            sentInput = false;
            if (m_Phase == 0)
            {
                var target = FindPressable(s[1]);
                if (!Ready(target != null, m_Options.Settle)) return false;
                Vector2 at = ScreenCentre(target, out _);
                Log($"step {m_Index} {label} down {PathOf(target.transform)}");
                if (!m_Pointer.Down(at, target)) throw new InvalidOperationException("no EventSystem to press with");
                m_Phase = 1;
                m_PhaseStart = Now;
                sentInput = true;
                return false;
            }
            if (Since(m_PhaseStart) < hold)
            {
                m_Pointer.Stay();
                return false;
            }
            Log($"step {m_Index} {label} up");
            m_Pointer.Up();
            sentInput = true;
            return true;
        }

        /// <summary>tap:X,Y: a press and release on whatever is under the screen point.</summary>
        bool Tap(string[] s, string label, out bool sentInput)
        {
            sentInput = true;
            if (m_Phase == 0)
            {
                var at = ScreenPoint(s[1]);
                var under = DrivePointer.ObjectAt(at, out _);
                Log($"step {m_Index} {label} down {(under != null ? PathOf(under.transform) : "(nothing)")}");
                if (!m_Pointer.Down(at)) return true;
                m_Phase = 1;
                return false;
            }
            Log($"step {m_Index} {label} up");
            m_Pointer.Up();
            return true;
        }

        /// <summary>swipe:X1,Y1:X2,Y2:SECONDS: a press at the first point, moved to the second
        /// over the time, and released there.</summary>
        bool Swipe(string[] s, string label, out bool sentInput)
        {
            sentInput = true;
            if (m_Phase == 0)
            {
                m_From = ScreenPoint(s[1]);
                m_To = ScreenPoint(s[2]);
                var under = DrivePointer.ObjectAt(m_From, out _);
                Log($"step {m_Index} {label} down {(under != null ? PathOf(under.transform) : "(nothing)")}");
                if (!m_Pointer.Down(m_From)) return true;
                m_Phase = 1;
                m_PhaseStart = Now;
                return false;
            }
            if (m_Phase == 1)
            {
                float duration = s.Length > 3 ? Number(s[3]) : 0.2f;
                float t = duration > 0f ? Mathf.Clamp01(Since(m_PhaseStart) / duration) : 1f;
                m_Pointer.Move(Vector2.Lerp(m_From, m_To, t));
                if (t >= 1f) m_Phase = 2;
                return false;
            }
            Log($"step {m_Index} {label} up");
            m_Pointer.Up();
            return true;
        }

        /// <summary>stick:TARGET:DX,DY:SECONDS: a press on the target's centre, moved by DX,DY (in
        /// halves of the target's smaller side: 1,0 is its right edge), held there for the time and
        /// released. drag:TARGET:DX,DY:SECONDS moves over the time instead and releases at once.</summary>
        bool Stick(string[] s, string label, out bool sentInput)
        {
            sentInput = false;
            bool stick = s[0] == "stick";
            float seconds = s.Length > 3 ? Number(s[3]) : 0.5f;
            if (m_Phase == 0)
            {
                var target = FindPressable(s[1]);
                if (!Ready(target != null, m_Options.Settle)) return false;
                m_From = ScreenCentre(target, out float radius);
                m_To = m_From + Pair(s[2]) * radius;
                Log($"step {m_Index} {label} down {PathOf(target.transform)}");
                if (!m_Pointer.Down(m_From, target)) throw new InvalidOperationException("no EventSystem to press with");
                m_Phase = 1;
                m_PhaseStart = Now;
                sentInput = true;
                return false;
            }
            sentInput = true;
            if (m_Phase == 1)
            {
                // A stick is pushed over a few frames (a tenth of a second), a drag over its time.
                float duration = stick ? 0.1f : seconds;
                float t = duration > 0f ? Mathf.Clamp01(Since(m_PhaseStart) / duration) : 1f;
                m_Pointer.Move(Vector2.Lerp(m_From, m_To, t));
                if (t < 1f) return false;
                m_Phase = 2;
                m_PhaseStart = Now;
                if (stick) Log($"step {m_Index} {label} held");
                return false;
            }
            if (stick && Since(m_PhaseStart) < seconds)
            {
                m_Pointer.Stay();
                sentInput = false;
                return false;
            }
            Log($"step {m_Index} {label} up");
            m_Pointer.Up();
            return true;
        }

        // ------------------------------------------------------------------ targets

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        static bool Named(Transform t, string name) =>
            t.name == name || (name.IndexOf('/') >= 0 ? ("/" + PathOf(t)).EndsWith("/" + name, StringComparison.Ordinal) : false);

        /// <summary>An active object by its name, or by the end of its path when the name has a
        /// '/'; of several, the first in hierarchy order (<see cref="First"/>).</summary>
        GameObject FindActive(string name)
        {
            s_Matches.Clear();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (Named(t, name)) s_Matches.Add(t.gameObject);
            return First(name);
        }

        /// <summary>The object a press on a name goes to: an interactable Selectable, else an
        /// object with an enabled component that handles pointer downs, clicks or drags and no
        /// Selectable that is not interactable; of several, the first in hierarchy order.</summary>
        GameObject FindPressable(string name)
        {
            s_Matches.Clear();
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!behaviour.isActiveAndEnabled || !Named(behaviour.transform, name) || s_Matches.Contains(behaviour.gameObject)) continue;
                if (!(behaviour is Selectable || behaviour is IPointerDownHandler || behaviour is IPointerClickHandler || behaviour is IDragHandler)) continue;
                var selectable = behaviour.GetComponent<Selectable>();
                if (selectable != null && !(selectable.isActiveAndEnabled && selectable.IsInteractable())) continue;
                s_Matches.Add(behaviour.gameObject);
            }
            return First(name);
        }

        static readonly List<GameObject> s_Matches = new List<GameObject>();
        readonly HashSet<string> m_Ambiguous = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The first of the matches in hierarchy order: by scene (the loaded scenes in
        /// their order, then the objects kept across loads), then by place among the roots and
        /// among the siblings at each level, a parent before its children. The engine's own
        /// order of finding objects is not the same everywhere; this one is. Several matches are
        /// logged once per name.</summary>
        GameObject First(string name)
        {
            if (s_Matches.Count == 0) return null;
            GameObject first = s_Matches[0];
            for (int i = 1; i < s_Matches.Count; i++)
                if (Before(s_Matches[i].transform, first.transform)) first = s_Matches[i];
            if (s_Matches.Count > 1 && m_Ambiguous.Add(name))
            {
                Log($"{s_Matches.Count} objects match {name}: the first in the hierarchy is taken, {PathOf(first.transform)}");
            }
            return first;
        }

        static int SceneOrder(Transform t)
        {
            var scene = t.gameObject.scene;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i) == scene) return i;
            return SceneManager.sceneCount;
        }

        static readonly List<int> s_PlaceA = new List<int>(), s_PlaceB = new List<int>();

        /// <summary>An object's place: its scene's order, then the sibling index of each transform
        /// from its root down to it.</summary>
        static void Place(Transform t, List<int> place)
        {
            place.Clear();
            for (var u = t; u != null; u = u.parent) place.Add(u.GetSiblingIndex());
            place.Add(SceneOrder(t));
            place.Reverse();
        }

        static bool Before(Transform a, Transform b)
        {
            Place(a, s_PlaceA);
            Place(b, s_PlaceB);
            for (int i = 0; i < s_PlaceA.Count && i < s_PlaceB.Count; i++)
                if (s_PlaceA[i] != s_PlaceB[i]) return s_PlaceA[i] < s_PlaceB[i];
            return s_PlaceA.Count < s_PlaceB.Count;
        }

        /// <summary>The screen position of an object's centre, and half its smaller side in
        /// pixels (1 for an object that is no rectangle).</summary>
        static Vector2 ScreenCentre(GameObject target, out float radius)
        {
            radius = 1f;
            if (target.transform is RectTransform rect)
            {
                var canvas = target.GetComponentInParent<Canvas>();
                var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Vector2 low = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                Vector2 high = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                radius = Mathf.Min(Mathf.Abs(high.x - low.x), Mathf.Abs(high.y - low.y)) * 0.5f;
                return (low + high) * 0.5f;
            }
            var main = Camera.main;
            return main != null ? (Vector2)main.WorldToScreenPoint(target.transform.position) : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        /// <summary>Keeps the objects named by hide steps inactive (looked for four times a second).</summary>
        void Hide()
        {
            if (m_Hidden.Count == 0 || Now < m_NextHide) return;
            m_NextHide = Now + 0.25 - 1e-6;
            foreach (var name in m_Hidden)
            {
                var found = FindActive(name);
                if (found == null) continue;
                found.SetActive(false);
                Log($"hidden {PathOf(found.transform)}");
            }
        }

        /// <summary>dump: what could be pressed now.</summary>
        void Dump()
        {
            var buttons = new SortedSet<string>(StringComparer.Ordinal);
            var handlers = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!behaviour.isActiveAndEnabled) continue;
                if (behaviour is Selectable selectable)
                {
                    if (selectable.IsInteractable()) buttons.Add(PathOf(behaviour.transform));
                }
                else if (behaviour is IPointerDownHandler || behaviour is IPointerClickHandler || behaviour is IDragHandler) handlers.Add(PathOf(behaviour.transform));
            }
            Log($"step {m_Index} dump selectables: {string.Join(", ", buttons)}");
            Log($"step {m_Index} dump handlers: {string.Join(", ", handlers)}");
        }
    }
}
