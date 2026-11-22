using System;
using System.Collections;
using System.IO;
using Unbound.GameDrive;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Exercises sequences that cross step boundaries and checks the public run lifecycle.</summary>
public sealed class DriveDriverControls
{
    readonly Transform m_Canvas;
    readonly string m_Output;
    GameObject m_Original, m_Replacement;
    int m_SwapFrame = -1;

    public DriveDriverControls(Transform canvas, string output)
    {
        m_Canvas = canvas;
        m_Output = Path.Combine(output, "driver");
    }

    public IEnumerator Record(Action finished)
    {
        yield return RecordMigration();
        Build();
        var heldLoad = SceneManager.LoadSceneAsync(0, LoadSceneMode.Additive);
        heldLoad.allowSceneActivation = false;
        try
        {
            var invalid = Driver.Run(new DriveOptions { Timeout = float.NaN, Exit = false });
            UnityEngine.Object.DestroyImmediate(invalid.gameObject);
            DriveProbe.Control("invalid timeout accepted");
        }
        catch (ArgumentOutOfRangeException error)
        {
            DriveProbe.Control("invalid timeout rejected: " + error.ParamName);
        }

        const string script = "wait:0.2;log:swap;click:Replace;click:Button;tap:0.95,0.05;log:empty;" +
                              "hide:ControlOverlay;tap:0.35,0.06;drag:Rotated:1,0:0.1;drag:CameraTarget:1,0:0.1;" +
                              "drag:CanvasOnly:1,0:0.1;drag:MainCameraOnly:1,0:0.1;click:DetachedControl";
        var code = 0;
        var done = false;
        var swapAt = int.MaxValue;

        void Logged(string line)
        {
            if (line == "step 1 log:swap") swapAt = Time.frameCount + 1;
            if (line == "step 5 log:empty")
                DriveProbe.Control("selection empty=" + (EventSystem.current.currentSelectedGameObject == null));
        }

        void Completed(int exitCode)
        {
            switch (code++)
            {
                case 0:
                    DriveProbe.Control("sequence exit " + exitCode);
                    const string marker = "warning after Finished";
                    Debug.LogWarning(marker);
                    bool received = File.ReadAllText(Path.Combine(m_Output, "drive.log")).Contains(marker);
                    DriveProbe.Control("finished run received warning=" + received, step: 1);
                    // Both runs open the same drive.log. The first must close it before Finished.
                    Restart("swipe:0.2,0.8:0.7,0.8:Infinity");
                    break;
                case 1:
                    DriveProbe.Control("invalid duration exit " + exitCode);
                    EventSystem.current.gameObject.SetActive(false);
                    Restart("tap:0.2,0.8");
                    break;
                default:
                    DriveProbe.Control("missing EventSystem exit " + exitCode);
                    done = true;
                    break;
            }
        }

        void Restart(string next)
        {
            try { Driver.Run(Options(next)); }
            catch (Exception error)
            {
                DriveProbe.Control("restart failed: " + error.GetType().Name);
                done = true;
            }
        }

        Driver.Finished += Completed;
        Driver.Logged += Logged;
        try
        {
            Driver.Run(Options(script));
            while (!done)
            {
                if (m_SwapFrame < 0 && Time.frameCount >= swapAt)
                {
                    m_SwapFrame = Time.frameCount;
                    m_Original.SetActive(false);
                    m_Replacement.SetActive(true);
                }
                yield return null;
            }
        }
        finally
        {
            Driver.Finished -= Completed;
            Driver.Logged -= Logged;
            DriveProbe.Control("additive load held=" + !heldLoad.isDone, step: 3);
            heldLoad.allowSceneActivation = true;
        }
        while (!heldLoad.isDone) yield return null;
        finished();
    }

    IEnumerator RecordMigration()
    {
        var target = new GameObject("MovingControl");
        bool done = false;
        int deactivateAt = int.MaxValue;
        void Logged(string line)
        {
            if (line == "step 1 log:migrate")
            {
                var scene = SceneManager.CreateScene("ControlDestination");
                SceneManager.MoveGameObjectToScene(target, scene);
                DriveProbe.Control($"moved active={target.activeInHierarchy} loaded={target.scene.isLoaded}", step: 5);
                deactivateAt = Time.frameCount + 3;
            }
            if (line == "step 2 gone:MovingControl")
                DriveProbe.Control("gone while active=" + target.activeInHierarchy, step: 5);
        }
        void Finished(int code)
        {
            DriveProbe.Control("migration exit " + code, step: 5);
            done = true;
        }
        Driver.Logged += Logged;
        Driver.Finished += Finished;
        try
        {
            Driver.Run(Options("until:MovingControl:0;log:migrate;gone:MovingControl"));
            while (!done)
            {
                if (Time.frameCount >= deactivateAt && target.activeSelf) target.SetActive(false);
                yield return null;
            }
        }
        finally
        {
            Driver.Logged -= Logged;
            Driver.Finished -= Finished;
            UnityEngine.Object.Destroy(target);
        }
    }

    DriveOptions Options(string script) => new DriveOptions
    {
        Out = m_Output, Script = script, Frames = true, Fps = 50, Settle = 0.08f, Timeout = 0.15f, Exit = false,
    };

    void Build()
    {
        m_Original = DriveProbe.Box("Replace", m_Canvas, 0.3f, 0.12f, 0.4f, 0.18f, Color.red).gameObject;
        m_Original.AddComponent<Button>().onClick.AddListener(() => DriveProbe.Control("original clicked"));
        m_Replacement = DriveProbe.Box("Replace", m_Canvas, 0.3f, 0.12f, 0.4f, 0.18f, Color.green).gameObject;
        m_Replacement.AddComponent<Button>().onClick.AddListener(() => DriveProbe.Control("replacement age " + (Time.frameCount - m_SwapFrame)));
        m_Replacement.SetActive(false);
        DriveProbe.Box("ControlTap", m_Canvas, 0.32f, 0.02f, 0.38f, 0.10f, Color.green).gameObject
            .AddComponent<Button>().onClick.AddListener(() => DriveProbe.Control("uncovered click"));
        DriveProbe.Box("ControlOverlay", m_Canvas, 0.32f, 0.02f, 0.38f, 0.10f, Color.red);

        var rotated = DriveProbe.Box("Rotated", m_Canvas, 0.7f, 0.08f, 0.7f, 0.08f, Color.blue);
        rotated.sizeDelta = new Vector2(80f, 80f);
        rotated.localRotation = Quaternion.Euler(0f, 0f, 45f);
        rotated.gameObject.AddComponent<DriveGestureResult>();

        var camera = new GameObject("InputCamera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.orthographicSize = 3f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        WorldTarget("CameraTarget", camera, raycaster: true);
        WorldTarget("CanvasOnly", camera, raycaster: false);
        WorldTarget("MainCameraOnly", null, raycaster: false);

        var root = new GameObject("DetachedDriverRoot");
        var detached = DriveProbe.Box("DetachedControl", root.transform, 0f, 0f, 1f, 1f, Color.white).gameObject;
        detached.AddComponent<Button>().onClick.AddListener(() => DriveProbe.Control("detached target clicked", step: 3));
        root.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        DriveProbe.Control($"detached target active={detached.activeInHierarchy} sceneValid={detached.scene.IsValid()}", step: 3);
    }

    static void WorldTarget(string name, Camera camera, bool raycaster)
    {
        var world = new GameObject(name + "Canvas", typeof(RectTransform), typeof(Canvas));
        ((RectTransform)world.transform).sizeDelta = new Vector2(2f, 2f);
        world.transform.position = new Vector3(1f, -1f, 0f);
        var canvas = world.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        if (raycaster) world.AddComponent<GraphicRaycaster>();
        DriveProbe.Box(name, world.transform, 0f, 0f, 1f, 1f, Color.blue)
            .gameObject.AddComponent<DriveGestureResult>().ProjectionOnly = !raycaster;
    }
}

public sealed class DriveGestureResult : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public bool ProjectionOnly;

    public void OnPointerDown(PointerEventData e) { }

    public void OnDrag(PointerEventData e) { }

    public void OnPointerUp(PointerEventData e)
    {
        var camera = e.pressEventCamera;
        if (ProjectionOnly)
        {
            if (name == "CanvasOnly")
                DriveProbe.Control($"canvas-only drag={e.dragging} noRaycaster={camera == null}", step: 2);
            var canvas = GetComponentInParent<Canvas>();
            var view = canvas.worldCamera ?? Camera.main;
            var rect = (RectTransform)transform;
            Vector2 centre = view.WorldToScreenPoint(rect.TransformPoint(rect.rect.center));
            Vector2 edge = view.WorldToScreenPoint(rect.TransformPoint(new Vector2(rect.rect.xMax, rect.rect.center.y)));
            bool pressedAtCentre = Vector2.Distance(e.pressPosition, centre) < 0.1f;
            bool releasedAtEdge = Vector2.Distance(e.position, edge) < 0.1f;
            DriveProbe.Control($"{name} centre={pressedAtCentre} edge={releasedAtEdge} noRaycaster={camera == null}", step: 4);
            return;
        }
        var dx = e.position.x - e.pressPosition.x;
        DriveProbe.Control($"{name} camera={(camera != null ? camera.name : "none")} dragged={e.dragging} fortyPixels={Mathf.Abs(dx - 40f) < 0.1f}");
    }
}
