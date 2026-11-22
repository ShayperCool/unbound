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
        /// <summary>Seconds to wait for a target or screenshot before exiting with code 3 (GAMEDRIVE_TIMEOUT).</summary>
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
            if (Get("GAMEDRIVE_TIMEOUT") is string timeout) options.Timeout = float.Parse(timeout, CultureInfo.InvariantCulture);
            if (Get("GAMEDRIVE_SETTLE") is string settle) options.Settle = float.Parse(settle, CultureInfo.InvariantCulture);
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
        /// <summary>The script ended: 0, a quit step's code, 3 after a timeout, or 4 after an error.</summary>
        public static event Action<int> Finished;

        DriveOptions m_Options;
        StreamWriter m_Log;
        readonly List<string[]> m_Steps = new List<string[]>();
        readonly List<string> m_Hidden = new List<string>();
        readonly DrivePointer m_Pointer = new DrivePointer();
        int m_Index, m_Phase, m_CaptureFramerate;
        bool m_Began, m_Done, m_DownHit, m_ChangedCaptureFramerate;
        double m_StepStart, m_ReadySince = -1.0, m_PhaseStart;
        double m_NextHide;
        string m_Label, m_Screenshot;
        GameObject m_ReadyTarget;
        float m_Duration;
        Vector2 m_From, m_To;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            try
            {
                var options = DriveOptions.FromEnvironment();
                if (options != null) Run(options);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                ExitProcess(4);
            }
        }

        /// <summary>Starts driving with these options (what GAMEDRIVE_OUT does at startup).</summary>
        public static Driver Run(DriveOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!Finite(options.Timeout) || options.Timeout < 0f) throw new ArgumentOutOfRangeException(nameof(options.Timeout));
            if (!Finite(options.Settle) || options.Settle < 0f) throw new ArgumentOutOfRangeException(nameof(options.Settle));
            if (options.Frames && options.Fps <= 0) throw new ArgumentOutOfRangeException(nameof(options.Fps));
            var go = new GameObject("Unbound.GameDrive") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            var driver = go.AddComponent<Driver>();
            try { driver.Begin(options); }
            catch { Destroy(go); throw; }
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
            if (options.Frames)
            {
                Time.captureFramerate = options.Fps;
                m_ChangedCaptureFramerate = true;
            }
            if (options.Seed is int seed) UnityEngine.Random.InitState(seed);
            Application.logMessageReceived += GameLog;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
            SceneManager.activeSceneChanged += ActiveSceneChanged;
            Log($"start {m_Steps.Count} steps clock {(options.Frames ? "frames@" + options.Fps : "real")} screen {Screen.width}x{Screen.height}");
        }

        void OnDestroy() => ReleaseResources();

        void RestoreCaptureFramerate()
        {
            if (m_ChangedCaptureFramerate)
            {
                Time.captureFramerate = m_CaptureFramerate;
                m_ChangedCaptureFramerate = false;
            }
        }

        void ReleaseResources()
        {
            RestoreCaptureFramerate();
            Application.logMessageReceived -= GameLog;
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            SceneManager.activeSceneChanged -= ActiveSceneChanged;
            m_Log?.Dispose();
            m_Log = null;
        }

        void GameLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            var first = message.Split('\n')[0];
            Log(type == LogType.Exception ? $"game {type}: {first} @ {stack.Split('\n')[0]}" : $"game {type}: {first}");
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            m_SceneListDirty = true;
            Log($"scene {scene.path} {mode}");
        }

        void SceneUnloaded(Scene scene) => m_SceneListDirty = true;

        void ActiveSceneChanged(Scene previous, Scene next) => m_SceneListDirty = true;

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
            try { Hide(); }
            catch (Exception error)
            {
                Log($"hide failed: {error.GetType().Name}: {error.Message}");
                Finish(4);
                return;
            }
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
                    m_ReadyTarget = null;
                    m_Label = string.Join(":", step);
                }
                bool sentInput;
                bool finished;
                try { finished = Step(step, out sentInput); }
                catch (Exception e)
                {
                    Log($"step {m_Index} {m_Label} failed: {e.GetType().Name}: {e.Message}");
                    Finish(4);
                    return;
                }
                if (m_Done) return;
                if (!finished)
                {
                    if (step[0] != "wait" && (m_Phase == 0 || step[0] == "shot") && Since(m_StepStart) > m_Options.Timeout)
                    {
                        Log($"step {m_Index} {m_Label} timeout");
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
            RestoreCaptureFramerate();
            Log($"done {code}");
            ReleaseResources();
            Destroy(gameObject);
            Finished?.Invoke(code);
            if (m_Options.Exit) ExitProcess(code);
        }

        static void ExitProcess(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }

        // ------------------------------------------------------------------ steps

        static bool Finite(float number) => !float.IsNaN(number) && !float.IsInfinity(number);

        static float Number(string text)
        {
            float value = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!Finite(value)) throw new FormatException("expected a finite number");
            return value;
        }

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
            string label = m_Label;
            switch (s[0])
            {
                case "wait":
                    if (Since(m_StepStart) < Number(s[1])) return false;
                    Log($"step {m_Index} {label}");
                    return true;
                case "until":
                    if (!Ready(FindActive(s[1]), s.Length > 2 ? Number(s[2]) : m_Options.Settle)) return false;
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
                    Hide();
                    return true;
                case "shot":
                    return Shot(s[1]);
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

        /// <summary>Whether the same target has been ready for <paramref name="seconds"/>.</summary>
        bool Ready(GameObject target, float seconds)
        {
            if (!ReferenceEquals(target, m_ReadyTarget))
            {
                m_ReadyTarget = target;
                m_ReadySince = Now;
            }
            return target != null && Since(m_ReadySince) >= seconds;
        }

        bool Shot(string name)
        {
            if (m_Phase == 0)
            {
                if (string.IsNullOrEmpty(m_Options.Out)) throw new InvalidOperationException("no screenshot output directory");
                if (name.Length == 0 || Path.GetFileName(name) != name) throw new FormatException("expected a screenshot file name");
                // A fresh path lets an earlier run's file remain in place while this capture is pending.
                m_Screenshot = Path.Combine(m_Options.Out, $".gamedrive-{Guid.NewGuid():N}.png");
                ScreenCapture.CaptureScreenshot(m_Screenshot);
                Log($"step {m_Index} {m_Label}");
                m_Phase = 1;
                return false;
            }
            if (!File.Exists(m_Screenshot)) return false;
            string destination = Path.Combine(m_Options.Out, name + ".png");
            if (File.Exists(destination)) File.Replace(m_Screenshot, destination, null);
            else File.Move(m_Screenshot, destination);
            m_Screenshot = null;
            return true;
        }

        /// <summary>click:TARGET and press:TARGET:SECONDS: a press on the target itself at its
        /// centre, whatever is drawn over it, released (with the click) after the hold.</summary>
        bool Press(string[] s, string label, float hold, out bool sentInput)
        {
            sentInput = false;
            if (m_Phase == 0)
            {
                var target = FindPressable(s[1]);
                if (!Ready(target, m_Options.Settle)) return false;
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
                if (!DownAt(at)) return true;
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
                m_Duration = s.Length > 3 ? Number(s[3]) : 0.2f;
                if (!DownAt(m_From)) return true;
                m_Phase = 1;
                m_PhaseStart = Now;
                return false;
            }
            if (m_Phase == 1)
            {
                float t = m_Duration > 0f ? Mathf.Clamp01(Since(m_PhaseStart) / m_Duration) : 1f;
                m_Pointer.Move(Vector2.Lerp(m_From, m_To, t));
                if (t >= 1f) m_Phase = 2;
                return false;
            }
            Log($"step {m_Index} {label} up");
            m_Pointer.Up();
            return true;
        }

        bool DownAt(Vector2 position)
        {
            if (!m_Pointer.Down(position, beforeDispatch: LogPointerDown))
                throw new InvalidOperationException("no EventSystem to press with");
            if (!m_DownHit) m_Pointer.Up();
            return m_DownHit;
        }

        void LogPointerDown(GameObject hit)
        {
            m_DownHit = hit != null;
            Log($"step {m_Index} {m_Label} down {(m_DownHit ? PathOf(hit.transform) : "(nothing)")}");
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
                if (!Ready(target, m_Options.Settle)) return false;
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

        static bool Named(Transform transform, string name, bool isPath)
        {
            if (!isPath) return transform.name == name;
            int end = name.Length;
            for (var t = transform; t != null; t = t.parent)
            {
                string part = t.name;
                int start = Math.Max(0, end - part.Length);
                int offset = part.Length - (end - start);
                if (offset > 0 && part[offset - 1] != '/') return false;
                if (string.CompareOrdinal(name, start, part, offset, end - start) != 0) return false;
                if (start == 0) return true;
                if (name[start - 1] != '/') return false;
                end = start - 1;
            }
            return false;
        }

        readonly List<GameObject> m_Roots = new List<GameObject>();
        readonly Stack<Transform> m_Descendants = new Stack<Transform>();
        readonly List<MonoBehaviour> m_Behaviours = new List<MonoBehaviour>();
        readonly HashSet<string> m_Ambiguous = new HashSet<string>(StringComparer.Ordinal);
        readonly List<Scene> m_Scenes = new List<Scene>();
        readonly List<Transform> m_Detached = new List<Transform>();
        const double SceneRefreshSeconds = 0.25;
        bool m_SceneListDirty = true;
        double m_NextSceneRefresh, m_NextDetachedRefresh;

        GameObject FindActive(string name) => FindTarget(name, pressable: false);

        GameObject FindPressable(string name) => FindTarget(name, pressable: true);

        /// <summary>Visits loaded scenes in order, then the objects kept across scene loads.
        /// Within a scene, visits each branch in sibling order, with each parent before its children.</summary>
        GameObject FindTarget(string name, bool pressable)
        {
            GameObject first = null;
            int matches = 0;
            bool isPath = name.IndexOf('/') >= 0;
            var persistentScene = gameObject.scene;
            bool scannedPersistent = false;
            RefreshScenes();
            foreach (var scene in m_Scenes)
            {
                if (!scene.IsValid() || !scene.isLoaded) continue;
                ScanScene(scene, name, isPath, pressable, ref first, ref matches);
                scannedPersistent |= scene == persistentScene;
            }
            if (!scannedPersistent && persistentScene.IsValid() && persistentScene.isLoaded)
                ScanScene(persistentScene, name, isPath, pressable, ref first, ref matches);
            if (first == null) return FindDetached(name, isPath, pressable);
            if (matches > 1 && m_Ambiguous.Add(name))
                Log($"{matches} objects match {name}: the first in the hierarchy is taken, {PathOf(first.transform)}");
            return first;
        }

        void RefreshScenes()
        {
            double now = Now;
            if (!m_SceneListDirty && now < m_NextSceneRefresh) return;
            m_NextSceneRefresh = now + SceneRefreshSeconds;
            int count = SceneManager.sceneCount;
            // CreateScene may add an empty scene without raising a load event.
            // Keep the snapshot between polls, including while another scene is waiting to load.
            if (!m_SceneListDirty && count == m_Scenes.Count) return;
            m_SceneListDirty = false;
            m_Scenes.Clear();
            for (int i = 0; i < count; i++) m_Scenes.Add(SceneManager.GetSceneAt(i));
        }

        GameObject FindDetached(string name, bool isPath, bool pressable)
        {
            double now = Now;
            if (now >= m_NextDetachedRefresh)
            {
                m_NextDetachedRefresh = now + SceneRefreshSeconds;
                m_Detached.Clear();
                // A detached transform has no scene root list. Use the allocation-bearing
                // global query at the polling interval and only when no loaded target matches.
                foreach (var transform in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (!transform.gameObject.scene.IsValid()) m_Detached.Add(transform);
            }
            GameObject first = null;
            foreach (var transform in m_Detached)
            {
                if (transform == null) continue;
                var target = transform.gameObject;
                if (!target.activeInHierarchy || target.scene.IsValid() || !Findable(target) || !Named(transform, name, isPath)) continue;
                if (pressable && !IsPressable(target)) continue;
                if (first != null) throw new InvalidOperationException($"ambiguous detached target {name}");
                first = target;
            }
            return first;
        }

        void ScanScene(Scene scene, string name, bool isPath, bool pressable, ref GameObject first, ref int matches)
        {
            scene.GetRootGameObjects(m_Roots);
            for (int i = m_Roots.Count - 1; i >= 0; i--) m_Descendants.Push(m_Roots[i].transform);
            m_Roots.Clear();
            while (m_Descendants.Count > 0)
            {
                var t = m_Descendants.Pop();
                var target = t.gameObject;
                if (!target.activeSelf) continue;
                for (int i = t.childCount - 1; i >= 0; i--) m_Descendants.Push(t.GetChild(i));
                if (!Findable(target)) continue;
                if (!Named(t, name, isPath)) continue;
                if (pressable && !IsPressable(target)) continue;
                if (matches++ == 0) first = target;
            }
        }

        static bool Findable(GameObject target) => (target.hideFlags & HideFlags.DontSave) != HideFlags.DontSave;

        bool IsPressable(GameObject target)
        {
            var selectable = target.GetComponent<Selectable>();
            if (selectable != null && !(selectable.isActiveAndEnabled && selectable.IsInteractable())) return false;
            target.GetComponents(m_Behaviours);
            bool hasHandler = false;
            foreach (var behaviour in m_Behaviours)
                if (behaviour != null && behaviour.isActiveAndEnabled
                    && (behaviour is Selectable || behaviour is IPointerDownHandler || behaviour is IPointerClickHandler || behaviour is IDragHandler))
                {
                    hasHandler = true;
                    break;
                }
            m_Behaviours.Clear();
            return hasHandler;
        }

        /// <summary>The screen position of an object's centre, and half its smaller side in
        /// pixels (1 for an object that is no rectangle).</summary>
        static Vector2 ScreenCentre(GameObject target, out float radius)
        {
            radius = 1f;
            if (target.transform is RectTransform rect)
            {
                var raycaster = DrivePointer.RaycasterFor(target);
                var canvas = target.GetComponentInParent<Canvas>()?.rootCanvas;
                var camera = raycaster != null
                    ? raycaster.eventCamera
                    : canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Vector2 corner = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                Vector2 left = RectTransformUtility.WorldToScreenPoint(camera, corners[1]) - corner;
                Vector2 bottom = RectTransformUtility.WorldToScreenPoint(camera, corners[3]) - corner;
                radius = Mathf.Min(left.magnitude, bottom.magnitude) * 0.5f;
                return RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
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
