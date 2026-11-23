using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// A scene seen by a fixed camera, captured by the same script in the Unity Editor and in
/// Unbound (tests/projects/vfx/scene_capture.py copies it into a private copy of a project).
/// When UNBOUND_SCENE_CAPTURE names a directory holding `capture.json`, a copy of the scene's
/// main camera (its camera and render pipeline settings, without its scripts or children)
/// renders, for each of the file's views, from its place and rotation (or, when the file names a
/// `target` object, from that object's place plus the view's position, looking at it: of the
/// active objects of that name, the one nearest `targetNear` when the file gives it, else the
/// first by place, x then y then z) into a
/// texture of the size the file gives;
/// time advances by its fixed step (Time.captureDeltaTime); at the end of each frame it names,
/// counted from the frame the script starts in, which is frame 0 (a frame's number is the frames
/// since then, not the ends of frame seen: whether the first Play frame completes one is the
/// Editor session's own matter, and frame 0 is not shot) (when the file gives a `seed`, every effect
/// of the scene is first given that seed and started again, so that runs draw the same
/// particles), each view's colour is written to
/// `vK-frame-NNN.png` beside the file. Then `values.jsonl` lists the frames shot and the run
/// ends.
/// </summary>
public sealed class UnboundSceneCapture : MonoBehaviour
{
    /// <summary>What capture.json holds.</summary>
    [Serializable]
    public sealed class Settings
    {
        public string target = "";
        public bool hasTargetNear;
        public Vector3 targetNear;
        public View[] views = { new View() };
        public float fieldOfView;
        public int width = 480;
        public int height = 270;
        public float deltaTime = 1f / 30f;
        /// <summary>Negative: the effects keep their own seeds.</summary>
        public int seed = -1;
        /// <summary>Empty: every renderer; `effects` or `others`: only those under the target's
        /// parent (<see cref="Keep"/>).</summary>
        public string keep = "";
        /// <summary>Every other camera of the scene turned off: only the capture's render.</summary>
        public bool soleCameras;
        /// <summary>The scene's renderers listed in `renderers.txt` beside the shots (<see cref="ListRenderers"/>).</summary>
        public bool listRenderers;
        /// <summary>One view per effect of the scene instead of the views given: each from the
        /// first view's position as an offset from the effect, looking at it (<see cref="EachEffect"/>).</summary>
        public bool eachEffect;
        /// <summary>Empty: a row per effect while the scene has at most <see cref="MostEffectRows"/>;
        /// else a row per effect whose name starts with it, however many (<see cref="Effects"/>).</summary>
        public string effectRows = "";
        /// <summary>Empty: none; else, at each shot, a row per renderer whose object's name starts
        /// with one of these comma-separated prefixes (<see cref="Bounds"/>).</summary>
        public string boundsOf = "";
        /// <summary>For scene_capture.py's self-test only: the frame whose shot fails (0: none).</summary>
        public int selfTestThrowAtFrame;
        public int[] frames = { 30 };
    }

    [Serializable]
    public sealed class View
    {
        public Vector3 position;
        public Vector3 rotation;
    }

    const string Variable = "UNBOUND_SCENE_CAPTURE";

    string m_Dir;
    Settings m_Settings;
    Camera[] m_Cameras;
    Transform m_Target;
    RenderTexture[] m_Targets;
    Texture2D m_Shot;
    int m_Frame;
    int m_StartFrame;
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(dir)) return;
        Settings settings;
        try
        {
            settings = JsonUtility.FromJson<Settings>(File.ReadAllText(Path.Combine(dir, "capture.json")));
        }
        catch (Exception e)
        {
            Fail(e);
            return;
        }
        Time.captureDeltaTime = settings.deltaTime;
        var capture = new GameObject("UnboundSceneCapture").AddComponent<UnboundSceneCapture>();
        DontDestroyOnLoad(capture.gameObject);
        capture.m_Dir = dir;
        capture.m_Settings = settings;
    }

    /// <summary>A copy of the scene's main camera with only its camera and render pipeline
    /// components (no scripts that would move it, no children), at a view's fixed place. The copy
    /// is made under an inactive object and stripped there, so that none of its scripts ever
    /// wakes (a camera script that keeps one instance of itself would destroy the copy, or undo
    /// the original's state when destroyed).</summary>
    Camera MakeCamera(View view, int index)
    {
        var main = Camera.main;
        if (main == null)
            foreach (var camera in Camera.allCameras)
                if (camera.targetTexture == null) { main = camera; break; }
        GameObject copy;
        if (main != null)
        {
            var holder = new GameObject("UnboundSceneCapture holder");
            holder.SetActive(false);
            copy = Instantiate(main.gameObject, holder.transform);
            for (int i = copy.transform.childCount - 1; i >= 0; i--) DestroyImmediate(copy.transform.GetChild(i).gameObject);
            // Scripts first (they may require the components kept), then what is not the
            // camera's or the render pipeline's.
            for (int pass = 0; pass < 2; pass++)
                foreach (var component in copy.GetComponents<Component>())
                {
                    // A missing script's component is null: nothing to remove.
                    if (component == null || component is Transform || component is Camera) continue;
                    string space = component.GetType().Namespace ?? "";
                    if (space.StartsWith("UnityEngine.Rendering", StringComparison.Ordinal)) continue;
                    if (pass == 0 && !(component is MonoBehaviour)) continue;
                    DestroyImmediate(component);
                }
            copy.tag = "Untagged";
            copy.transform.SetParent(null, false);
            DestroyImmediate(holder);
        }
        else copy = new GameObject("capture camera", typeof(Camera));
        copy.name = "UnboundSceneCapture camera " + index;
        DontDestroyOnLoad(copy);
        var eye = copy.GetComponent<Camera>();
        if (string.IsNullOrEmpty(m_Settings.target))
            copy.transform.SetPositionAndRotation(view.position, Quaternion.Euler(view.rotation));
        else
        {
            if (index == 0) m_Target = Target();
            var at = m_Target.position;
            copy.transform.SetPositionAndRotation(at + view.position, Quaternion.LookRotation(-view.position));
        }
        if (m_Settings.fieldOfView > 0f) eye.fieldOfView = m_Settings.fieldOfView;
        m_Targets[index] = new RenderTexture(m_Settings.width, m_Settings.height, 24, RenderTextureFormat.ARGB32);
        eye.targetTexture = m_Targets[index];
        eye.enabled = true;
        return eye;
    }

    /// <summary>Every effect of the scene given the settings' seed, started again.</summary>
    void Seed()
    {
        if (m_Settings.seed < 0) return;
        int seeded = 0;
        foreach (var effect in FindObjectsByType<UnityEngine.VFX.VisualEffect>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
        {
            effect.resetSeedOnPlay = false;
            effect.startSeed = (uint)m_Settings.seed;
            effect.Reinit();
            seeded++;
        }
        m_Out.Append("{\"k\":\"seeded\",\"v\":").Append(seeded).Append("}\n");
    }

    /// <summary>The settings' target: of the active objects of its name (sorted by place, x then
    /// y then z), the one nearest `targetNear`, else the first; every candidate is recorded.</summary>
    Transform Target()
    {
        var found = new System.Collections.Generic.List<Transform>();
        foreach (var transform in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (transform.name == m_Settings.target) found.Add(transform);
        found.Sort((p, q) =>
        {
            Vector3 a = p.position, b = q.position;
            return a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z);
        });
        if (found.Count == 0) throw new InvalidOperationException("UnboundSceneCapture: no active object named " + m_Settings.target);
        int chosen = 0;
        if (m_Settings.hasTargetNear)
            for (int i = 1; i < found.Count; i++)
                if ((found[i].position - m_Settings.targetNear).sqrMagnitude < (found[chosen].position - m_Settings.targetNear).sqrMagnitude) chosen = i;
        for (int i = 0; i < found.Count; i++)
            m_Out.Append("{\"k\":\"target/").Append(i).Append("\",\"v\":\"").Append(Place(found[i].position)).Append(i == chosen ? " (chosen)" : "").Append("\"}\n");
        return found[chosen];
    }

    /// <summary>With `keep` set, only the renderers of one kind under the target's parent are
    /// drawn (`effects`: the effects' renderers; `others`: every other renderer), so that what
    /// differs can be told apart; every other renderer is turned off. The counts are recorded.</summary>
    void Keep()
    {
        if (string.IsNullOrEmpty(m_Settings.keep)) return;
        if (m_Target == null) throw new InvalidOperationException("UnboundSceneCapture: keep needs a target");
        bool effects = m_Settings.keep == "effects";
        if (!effects && m_Settings.keep != "others") throw new InvalidOperationException("UnboundSceneCapture: keep is effects or others, not " + m_Settings.keep);
        var scope = m_Target.parent != null ? m_Target.parent : m_Target;
        int kept = 0, hidden = 0;
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.InstanceID))
        {
            bool keep = renderer.transform.IsChildOf(scope) && (renderer is UnityEngine.VFX.VFXRenderer) == effects;
            if (keep) kept++;
            else
            {
                renderer.enabled = false;
                hidden++;
            }
        }
        m_Out.Append("{\"k\":\"keep\",\"v\":\"").Append(m_Settings.keep).Append(": ").Append(kept).Append(" drawn, ").Append(hidden).Append(" hidden\"}\n");
    }

    /// <summary>One line per renderer of an active object, as the scene starts: its type, whether
    /// it is enabled, and its object's path, sorted, to tell which objects one side has and the
    /// other has not (scene_capture.py compare counts the lines only one side has).</summary>
    void ListRenderers()
    {
        var lines = new System.Collections.Generic.List<string>();
        var path = new StringBuilder();
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            path.Clear();
            for (var t = renderer.transform; t != null; t = t.parent) path.Insert(0, "/" + t.name);
            lines.Add(renderer.GetType().Name + "\t" + (renderer.enabled ? "on" : "off") + "\t" + path);
        }
        lines.Sort(string.CompareOrdinal);
        File.WriteAllLines(Path.Combine(m_Dir, "renderers.txt"), lines);
        m_Out.Append("{\"k\":\"renderers\",\"v\":").Append(lines.Count).Append("}\n");
    }

    /// <summary>More effects than this get no rows of their own (a large scene has hundreds).</summary>
    const int MostEffectRows = 64;

    /// <summary>The order effects are listed in: by name, then by place.</summary>
    static int ByNameAndPlace(UnityEngine.VFX.VisualEffect a, UnityEngine.VFX.VisualEffect b)
    {
        int order = string.CompareOrdinal(a.name, b.name);
        if (order != 0) return order;
        Vector3 p = a.transform.position, q = b.transform.position;
        return p.x != q.x ? p.x.CompareTo(q.x) : p.y != q.y ? p.y.CompareTo(q.y) : p.z.CompareTo(q.z);
    }

    /// <summary>One row per effect at a shot (of the effects named as `effectRows` starts, if
    /// given): its alive particles, and each of its systems' alive count, capacity and sleeping
    /// state (as the effect last read them back), in the order of the effects' names and places,
    /// with its place when the rows are of named effects.</summary>
    void Effects()
    {
        var effects = new System.Collections.Generic.List<UnityEngine.VFX.VisualEffect>(FindObjectsByType<UnityEngine.VFX.VisualEffect>(FindObjectsSortMode.None));
        bool named = !string.IsNullOrEmpty(m_Settings.effectRows);
        if (named) effects.RemoveAll(e => !e.name.StartsWith(m_Settings.effectRows, StringComparison.Ordinal));
        else if (effects.Count > MostEffectRows)
        {
            m_Out.Append("{\"k\":\"effect/").Append(m_Frame.ToString("000")).Append("\",\"v\":\"").Append(effects.Count).Append(" effects: rows omitted\"}\n");
            return;
        }
        effects.Sort(ByNameAndPlace);
        var names = new System.Collections.Generic.List<string>();
        for (int i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            m_Out.Append("{\"k\":\"effect/").Append(m_Frame.ToString("000")).Append('/').Append(i).Append("\",\"v\":\"").Append(JsonText(effect.name))
                .Append(named ? " at " + Place(effect.transform.position) : "")
                .Append(" alive=").Append(effect.aliveParticleCount).Append(effect.culled ? " culled" : "");
            names.Clear();
            effect.GetParticleSystemNames(names);
            foreach (var name in names)
            {
                // A system the effect does not run (an effect that is not simulated) has no info.
                try
                {
                    var info = effect.GetParticleSystemInfo(name);
                    m_Out.Append(' ').Append(name).Append(':').Append(info.aliveCount).Append('/').Append(info.capacity).Append(info.sleeping ? "s" : "");
                }
                catch (ArgumentException)
                {
                    m_Out.Append(' ').Append(name).Append(":not running");
                }
            }
            m_Out.Append("\"}\n");
        }
    }

    /// <summary>Replaces the views by one per active effect, in the order of the effects' names
    /// and places, and records which view is which effect's.</summary>
    void EachEffect()
    {
        var effects = new System.Collections.Generic.List<UnityEngine.VFX.VisualEffect>(FindObjectsByType<UnityEngine.VFX.VisualEffect>(FindObjectsSortMode.None));
        effects.Sort(ByNameAndPlace);
        if (effects.Count == 0) throw new InvalidOperationException("UnboundSceneCapture: the scene has no active effect");
        var offset = m_Settings.views[0].position;
        var rotation = Quaternion.LookRotation(-offset).eulerAngles;
        m_Settings.target = "";
        m_Settings.views = new View[effects.Count];
        for (int i = 0; i < effects.Count; i++)
        {
            var at = effects[i].transform.position;
            m_Settings.views[i] = new View { position = at + offset, rotation = rotation };
            m_Out.Append("{\"k\":\"view/").Append(i).Append("\",\"v\":\"").Append(effects[i].name).Append(" at ").Append(Place(at)).Append("\"}\n");
        }
    }

    /// <summary>Turns off every camera but the capture's (recorded).</summary>
    void SoleCameras()
    {
        int off = 0;
        foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.InstanceID))
            if (Array.IndexOf(m_Cameras, camera) < 0 && camera.enabled)
            {
                camera.enabled = false;
                off++;
            }
        m_Out.Append("{\"k\":\"sole cameras\",\"v\":").Append(off).Append("}\n");
    }

    static string Vec(Vector4 v) => v.x.ToString("R") + "," + v.y.ToString("R") + "," + v.z.ToString("R") + "," + v.w.ToString("R");

    /// <summary>A name as the text of a JSON string: its quotes and backslashes escaped.</summary>
    static string JsonText(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    static string Place(Vector3 p) => p.x.ToString("R") + "," + p.y.ToString("R") + "," + p.z.ToString("R");

    /// <summary>Ends a run that cannot go on: no values.jsonl, so the run reports itself
    /// incomplete (rather than wait out its time limit).</summary>
    static void Fail(Exception e)
    {
        Debug.LogError(e);
        Exit(1);
    }

    /// <summary>One row per renderer of an active object named as `boundsOf` says, at a shot:
    /// its type and path, its bounds' centre and extents (what transparent draws are sorted by),
    /// and the squared distance of the centre from each view's camera, sorted by path.</summary>
    void Bounds()
    {
        if (string.IsNullOrEmpty(m_Settings.boundsOf)) return;
        var prefixes = m_Settings.boundsOf.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        var rows = new System.Collections.Generic.List<string>();
        var path = new StringBuilder();
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (Array.FindIndex(prefixes, p => renderer.name.StartsWith(p, StringComparison.Ordinal)) < 0) continue;
            path.Clear();
            for (var t = renderer.transform; t != null; t = t.parent) path.Insert(0, "/" + t.name);
            var bounds = renderer.bounds;
            var row = new StringBuilder().Append(path).Append(' ').Append(renderer.GetType().Name)
                .Append(" center=").Append(Place(bounds.center)).Append(" extents=").Append(Place(bounds.extents));
            foreach (var eye in m_Cameras) row.Append(" d2=").Append((bounds.center - eye.transform.position).sqrMagnitude.ToString("R"));
            rows.Add(row.ToString());
        }
        rows.Sort(string.CompareOrdinal);
        string frame = m_Frame.ToString("000");
        for (int i = 0; i < rows.Count; i++)
            m_Out.Append("{\"k\":\"bounds/").Append(frame).Append('/').Append(i).Append("\",\"v\":\"").Append(JsonText(rows[i])).Append("\"}\n");
    }

    /// <summary>The views' colours at the end of this frame, when it is one the settings name.</summary>
    void Shoot()
    {
        if (m_Frame == m_Settings.selfTestThrowAtFrame) throw new InvalidOperationException("UnboundSceneCapture: the self-test's failure at frame " + m_Frame);
        if (Array.IndexOf(m_Settings.frames, m_Frame) < 0) return;
        Effects();
        Bounds();
        // The clocks the frame's shaders read (as the pipeline last set them).
        string frame = m_Frame.ToString("000");
        m_Out.Append("{\"k\":\"time/").Append(frame).Append("\",\"v\":\"").Append(Time.time.ToString("R")).Append(' ')
            .Append(Vec(Shader.GetGlobalVector("_Time"))).Append(' ').Append(Vec(Shader.GetGlobalVector("_TimeParameters"))).Append("\"}\n");
        if (m_Shot == null) m_Shot = new Texture2D(m_Settings.width, m_Settings.height, TextureFormat.RGB24, false);
        var active = RenderTexture.active;
        try
        {
            for (int i = 0; i < m_Targets.Length; i++)
            {
                RenderTexture.active = m_Targets[i];
                m_Shot.ReadPixels(new Rect(0f, 0f, m_Settings.width, m_Settings.height), 0, 0);
                m_Shot.Apply();
                string name = "v" + i + "-frame-" + m_Frame.ToString("000") + ".png";
                File.WriteAllBytes(Path.Combine(m_Dir, name), m_Shot.EncodeToPNG());
                m_Out.Append("{\"k\":\"shot/v").Append(i).Append('/').Append(m_Frame.ToString("000")).Append("\",\"v\":\"").Append(name).Append("\"}\n");
            }
        }
        finally
        {
            RenderTexture.active = active;
        }
    }

    IEnumerator Start()
    {
        m_StartFrame = Time.frameCount;
        try
        {
            Seed();
            if (m_Settings.eachEffect) EachEffect();
            m_Cameras = new Camera[m_Settings.views.Length];
            m_Targets = new RenderTexture[m_Settings.views.Length];
            for (int i = 0; i < m_Cameras.Length; i++) m_Cameras[i] = MakeCamera(m_Settings.views[i], i);
            if (m_Settings.listRenderers) ListRenderers();
            Keep();
            if (m_Settings.soleCameras) SoleCameras();
        }
        catch (Exception e)
        {
            Fail(e);
            yield break;
        }
        int last = 0;
        foreach (int frame in m_Settings.frames) last = Math.Max(last, frame);
        var wait = new WaitForEndOfFrame();
        while (m_Frame < last)
        {
            yield return wait;
            int frame = Time.frameCount - m_StartFrame;
            if (frame == m_Frame) continue;
            m_Frame = frame;
            try
            {
                Shoot();
            }
            catch (Exception e)
            {
                Fail(e);
                yield break;
            }
        }
        try
        {
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString());
        }
        catch (Exception e)
        {
            Fail(e);
            yield break;
        }
        Exit(0);
    }

    static void Exit(int code)
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
