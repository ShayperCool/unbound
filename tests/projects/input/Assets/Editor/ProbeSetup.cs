using System.IO;
using Unity.IntegerTime;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Authors the probe's assets and scene with the Editor itself (batchmode):
/// <c>Unity -batchmode -projectPath tests/projects/input -executeMethod ProbeSetup.Build -quit</c>.</summary>
public static class ProbeSetup
{
    const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
    const string SettingsPath = "Assets/InputSystem.inputsettings.asset";
    const string ScenePath = "Assets/Scenes/Probe.unity";
    const string TemplatePath = "Packages/com.unity.inputsystem/InputSystem/Editor/ProjectWideActions/ProjectWideActionsTemplate.json";

    public static void Build()
    {
        // The project-wide actions: Unity 6's template, as the Editor creates it for a new project.
        if (!File.Exists(ActionsPath))
        {
            File.WriteAllText(ActionsPath, File.ReadAllText(Path.GetFullPath(TemplatePath)));
            AssetDatabase.ImportAsset(ActionsPath);
        }
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
        InputSystem.actions = actions;

        // A settings asset with one changed value, so its loading shows.
        var settings = AssetDatabase.LoadAssetAtPath<InputSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<InputSettings>();
            settings.defaultTapTime = 0.25f;
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        EditorBuildSettings.AddConfigObject("com.unity.input.settings", settings, true);

        Directory.CreateDirectory("Assets/Prefabs");
        var unityEventsPlayer = MakePlayerPrefab("Assets/Prefabs/PlayerUnityEvents.prefab", actions, PlayerNotifications.InvokeUnityEvents);
        var csharpPlayer = MakePlayerPrefab("Assets/Prefabs/PlayerCSharpEvents.prefab", actions, PlayerNotifications.InvokeCSharpEvents);

        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 0, -10);

        var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
        // Added in edit mode: its Reset assigns the package's DefaultInputActions asset.
        eventSystem.AddComponent<InputSystemUIInputModule>();

        var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.layer = 5;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var resources = new DefaultControls.Resources();
        var button = DefaultControls.CreateButton(resources);
        button.name = "Button";
        Place(button, canvasObject, new Vector2(-200, 0), new Vector2(160, 60));
        var label = button.GetComponentInChildren<Text>();
        label.font = font;
        label.text = "Press";
        button.AddComponent<UIEventLogger>();

        var slider = DefaultControls.CreateSlider(resources);
        slider.name = "Slider";
        Place(slider, canvasObject, new Vector2(150, 0), new Vector2(300, 20));
        slider.AddComponent<UIEventLogger>();

        var scroll = new GameObject("ScrollTarget", typeof(RectTransform), typeof(Image), typeof(UIEventLogger));
        Place(scroll, canvasObject, new Vector2(0, -150), new Vector2(300, 80));

        var buttonComponent = button.GetComponent<Button>();
        var sliderComponent = slider.GetComponent<Slider>();
        // Arrow keys move between the button and the slider.
        buttonComponent.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = sliderComponent };
        sliderComponent.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = buttonComponent };
        eventSystem.GetComponent<EventSystem>().firstSelectedGameObject = button;

        // The player of the scene: PlayerInput with Send Messages, the actions' first control scheme.
        var player = new GameObject("Player");
        player.SetActive(false);
        var playerInput = player.AddComponent<PlayerInput>();
        playerInput.actions = actions;
        playerInput.defaultActionMap = "Player";
        playerInput.notificationBehavior = PlayerNotifications.SendMessages;
        player.AddComponent<PlayerMessages>();
        player.SetActive(true);

        var probe = new GameObject("InputProbe").AddComponent<InputProbe>();
        probe.button = buttonComponent;
        probe.slider = sliderComponent;
        probe.unityEventsPlayer = unityEventsPlayer;
        probe.csharpPlayer = csharpPlayer;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[probe] setup done");
    }

    /// <summary>Records RationalTime's native arithmetic (rounding, rates) for Unbound's port:
    /// <c>-executeMethod ProbeSetup.MeasureIntegerTime</c>, output in INPUT_PROBE_OUT.</summary>
    public static void MeasureIntegerTime()
    {
        var lines = new System.Collections.Generic.List<string>();
        var rates = new[]
        {
            RationalTime.TicksPerSecond.DefaultTicksPerSecond, RationalTime.TicksPerSecond.TicksPerSecond60,
            RationalTime.TicksPerSecond.TicksPerSecond2997, new RationalTime.TicksPerSecond(48000), new RationalTime.TicksPerSecond(3),
        };
        string R(RationalTime t) => $"[{t.Count},{t.Ticks.Numerator},{t.Ticks.Denominator}]";
        string Try(System.Func<string> f)
        {
            try { return f(); }
            catch (System.Exception e) { return "\"" + e.GetType().Name + ": " + e.Message + "\""; }
        }
        string D(double d) => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var rate in rates)
            foreach (var t in new[] { 0.0, 0.5, 1.0 / 3.0, 1.2345678, -0.7, 2.5e-9, -1.0 / 3.0, 0.016666666666666666 })
                lines.Add($"{{\"op\":\"FromDouble\",\"t\":{D(t)},\"rate\":[{rate.Numerator},{rate.Denominator}],\"r\":{Try(() => R(RationalTime.FromDouble(t, rate)))}}}");
        var samples = new[]
        {
            new RationalTime(7, new RationalTime.TicksPerSecond(3)), new RationalTime(-7, new RationalTime.TicksPerSecond(3)),
            new RationalTime(1, RationalTime.TicksPerSecond.TicksPerSecond2997), new RationalTime(5, new RationalTime.TicksPerSecond(2)),
            new RationalTime(11, new RationalTime.TicksPerSecond(6)), new RationalTime(141120001, RationalTime.TicksPerSecond.DefaultTicksPerSecond),
        };
        foreach (var a in samples)
        {
            lines.Add($"{{\"op\":\"ToDouble\",\"a\":{R(a)},\"r\":{Try(() => D(a.ToDouble()))}}}");
            lines.Add($"{{\"op\":\"ToDiscrete\",\"a\":{R(a)},\"r\":{Try(() => ((DiscreteTime)a).Value.ToString())}}}");
            foreach (var rate in rates)
                lines.Add($"{{\"op\":\"Convert\",\"a\":{R(a)},\"rate\":[{rate.Numerator},{rate.Denominator}],\"r\":{Try(() => R(a.Convert(rate)))}}}");
            foreach (var b in samples)
            {
                lines.Add($"{{\"op\":\"Add\",\"a\":{R(a)},\"b\":{R(b)},\"r\":{Try(() => R(a.Add(b)))}}}");
                lines.Add($"{{\"op\":\"Subtract\",\"a\":{R(a)},\"b\":{R(b)},\"r\":{Try(() => R(a.Subtract(b)))}}}");
                lines.Add($"{{\"op\":\"Multiply\",\"a\":{R(a)},\"b\":{R(b)},\"r\":{Try(() => R(a.Multiply(b)))}}}");
                lines.Add($"{{\"op\":\"Divide\",\"a\":{R(a)},\"b\":{R(b)},\"r\":{Try(() => R(a.Divide(b)))}}}");
            }
        }
        lines.Add($"{{\"op\":\"Valid\",\"zero\":{(new RationalTime.TicksPerSecond(0).Valid ? 1 : 0)},\"zeroDen\":{(new RationalTime.TicksPerSecond(5, 0).Valid ? 1 : 0)},\"defaultValid\":{(default(RationalTime).IsValid() ? 1 : 0)},\"default\":{R(default)}}}");
        File.WriteAllText(System.Environment.GetEnvironmentVariable("INPUT_PROBE_OUT"), string.Join("\n", lines) + "\n");
    }

    /// <summary>The file ids the Editor gives the objects of the .inputactions assets (a
    /// ScriptedImporter's): <c>-executeMethod ProbeSetup.MeasureFileIds</c>, output in
    /// INPUT_PROBE_OUT. Each is xxHash64 of "Type:MonoBehaviour->" + identifier + "0", the
    /// identifier being "&lt;root&gt;" for the asset, an action's id for its reference and the
    /// action's "map/name" for the hidden backward-compatible reference.</summary>
    public static void MeasureFileIds()
    {
        var lines = new System.Collections.Generic.List<string>();
        foreach (var path in new[] { ActionsPath, "Packages/com.unity.inputsystem/InputSystem/Plugins/PlayerInput/DefaultInputActions.inputactions" })
        {
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long fileId)) continue;
                string identifier = obj is InputActionAsset ? "<root>"
                    : obj is InputActionReference reference && (obj.hideFlags & HideFlags.HideInHierarchy) == 0 ? reference.action.id.ToString()
                    : obj.name;
                lines.Add($"{{\"path\":\"{path}\",\"type\":\"{obj.GetType().Name}\",\"identifier\":\"{identifier}\",\"fileId\":{fileId}}}");
            }
        }
        File.WriteAllText(System.Environment.GetEnvironmentVariable("INPUT_PROBE_OUT"), string.Join("\n", lines) + "\n");
    }

    static void Place(GameObject go, GameObject parent, Vector2 position, Vector2 size)
    {
        go.transform.SetParent(parent.transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static PlayerInput MakePlayerPrefab(string path, InputActionAsset actions, PlayerNotifications behavior)
    {
        var go = new GameObject(Path.GetFileNameWithoutExtension(path));
        go.SetActive(false);
        var playerInput = go.AddComponent<PlayerInput>();
        playerInput.actions = actions;
        playerInput.defaultActionMap = "Player";
        playerInput.notificationBehavior = behavior;
        var messages = go.AddComponent<PlayerMessages>();
        if (behavior == PlayerNotifications.InvokeUnityEvents)
        {
            // An event per action, each with a persistent listener, as the inspector sets them up.
            var events = new System.Collections.Generic.List<PlayerInput.ActionEvent>();
            foreach (var map in actions.actionMaps)
                foreach (var action in map.actions)
                {
                    var evt = new PlayerInput.ActionEvent(action);
                    UnityEventTools.AddPersistentListener(evt, messages.OnActionEvent);
                    events.Add(evt);
                }
            playerInput.actionEvents = events.ToArray();
        }
        go.SetActive(true);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab.GetComponent<PlayerInput>();
    }
}
