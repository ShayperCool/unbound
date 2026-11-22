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
/// counted from the first frame the scene plays (when the file gives a `seed`, every effect
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
    Vector3 m_Target;
    RenderTexture[] m_Targets;
    Texture2D m_Shot;
    int m_Frame;
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(dir)) return;
        var settings = JsonUtility.FromJson<Settings>(File.ReadAllText(Path.Combine(dir, "capture.json")));
        Time.captureDeltaTime = settings.deltaTime;
        var capture = new GameObject("UnboundSceneCapture").AddComponent<UnboundSceneCapture>();
        DontDestroyOnLoad(capture.gameObject);
        capture.m_Dir = dir;
        capture.m_Settings = settings;
    }

    /// <summary>A copy of the scene's main camera with only its camera and render pipeline
    /// components (no scripts that would move it, no children), at a view's fixed place.</summary>
    Camera MakeCamera(View view, int index)
    {
        var main = Camera.main;
        if (main == null)
            foreach (var camera in Camera.allCameras)
                if (camera.targetTexture == null) { main = camera; break; }
        GameObject copy;
        if (main != null)
        {
            copy = Instantiate(main.gameObject);
            for (int i = copy.transform.childCount - 1; i >= 0; i--) DestroyImmediate(copy.transform.GetChild(i).gameObject);
            // Scripts first (they may require the components kept), then what is not the
            // camera's or the render pipeline's.
            for (int pass = 0; pass < 2; pass++)
                foreach (var component in copy.GetComponents<Component>())
                {
                    if (component is Transform || component is Camera) continue;
                    string space = component.GetType().Namespace ?? "";
                    if (space.StartsWith("UnityEngine.Rendering", StringComparison.Ordinal)) continue;
                    if (pass == 0 && !(component is MonoBehaviour)) continue;
                    DestroyImmediate(component);
                }
            copy.tag = "Untagged";
        }
        else copy = new GameObject("capture camera", typeof(Camera));
        copy.name = "UnboundSceneCapture camera " + index;
        DontDestroyOnLoad(copy);
        var eye = copy.GetComponent<Camera>();
        if (string.IsNullOrEmpty(m_Settings.target))
            copy.transform.SetPositionAndRotation(view.position, Quaternion.Euler(view.rotation));
        else
        {
            var at = index == 0 ? (m_Target = Target()) : m_Target;
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

    /// <summary>The place of the settings' target: of the active objects of its name (sorted by
    /// place, x then y then z), the one nearest `targetNear`, else the first; every candidate is
    /// recorded.</summary>
    Vector3 Target()
    {
        var places = new System.Collections.Generic.List<Vector3>();
        foreach (var transform in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (transform.name == m_Settings.target) places.Add(transform.position);
        places.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z));
        if (places.Count == 0) throw new InvalidOperationException("UnboundSceneCapture: no active object named " + m_Settings.target);
        int chosen = 0;
        if (m_Settings.hasTargetNear)
            for (int i = 1; i < places.Count; i++)
                if ((places[i] - m_Settings.targetNear).sqrMagnitude < (places[chosen] - m_Settings.targetNear).sqrMagnitude) chosen = i;
        for (int i = 0; i < places.Count; i++)
            m_Out.Append("{\"k\":\"target/").Append(i).Append("\",\"v\":\"").Append(Place(places[i])).Append(i == chosen ? " (chosen)" : "").Append("\"}\n");
        return places[chosen];
    }

    static string Place(Vector3 p) => p.x.ToString("R") + "," + p.y.ToString("R") + "," + p.z.ToString("R");

    IEnumerator Start()
    {
        Seed();
        try
        {
            m_Cameras = new Camera[m_Settings.views.Length];
            m_Targets = new RenderTexture[m_Settings.views.Length];
            for (int i = 0; i < m_Cameras.Length; i++) m_Cameras[i] = MakeCamera(m_Settings.views[i], i);
        }
        catch (Exception e)
        {
            // No values.jsonl: the run reports it incomplete.
            Debug.LogError(e);
            Exit(1);
            yield break;
        }
        int last = 0;
        foreach (int frame in m_Settings.frames) last = Math.Max(last, frame);
        var wait = new WaitForEndOfFrame();
        while (m_Frame < last)
        {
            yield return wait;
            m_Frame++;
            if (Array.IndexOf(m_Settings.frames, m_Frame) < 0) continue;
            if (m_Shot == null) m_Shot = new Texture2D(m_Settings.width, m_Settings.height, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            for (int i = 0; i < m_Targets.Length; i++)
            {
                RenderTexture.active = m_Targets[i];
                m_Shot.ReadPixels(new Rect(0f, 0f, m_Settings.width, m_Settings.height), 0, 0);
                m_Shot.Apply();
                string name = "v" + i + "-frame-" + m_Frame.ToString("000") + ".png";
                File.WriteAllBytes(Path.Combine(m_Dir, name), m_Shot.EncodeToPNG());
                m_Out.Append("{\"k\":\"shot/v").Append(i).Append('/').Append(m_Frame.ToString("000")).Append("\",\"v\":\"").Append(name).Append("\"}\n");
            }
            RenderTexture.active = active;
        }
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString());
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
