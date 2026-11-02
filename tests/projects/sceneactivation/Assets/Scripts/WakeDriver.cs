using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Drives the two phases of the scene activation witness without touching the marked
/// objects. Play entry loads Wake.unity; on the driver's second Update it calls
/// <c>SceneManager.LoadScene("WakeCopy", LoadSceneMode.Single)</c> (the byte copy under another
/// GUID); three Updates after WakeCopy's sceneLoaded it records WakeCopy's hierarchy again,
/// unsubscribes, writes the terminal record and calls <see cref="finished"/>.
///
/// The driver object is made after Play entry's sceneLoaded (AfterSceneLoad) and moved to
/// DontDestroyOnLoad. It has no Awake, OnEnable or Start and no render callbacks, so it adds no
/// lifecycle entry; it is not in either marked scene's roots.</summary>
public class WakeDriver : MonoBehaviour
{
    /// <summary>Set by the Editor runner: leaves Play mode and quits.</summary>
    public static Action finished;

    static int s_Step;
    static int s_CopyLoadedStep = -1;
    static bool s_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register()
    {
        if (!WakeLog.Enabled) return;
        SceneManager.sceneLoaded += Loaded;
        SceneManager.sceneUnloaded += Unloaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (!WakeLog.Enabled) return;
        var driver = new GameObject("WakeDriver");
        DontDestroyOnLoad(driver);
        driver.AddComponent<WakeDriver>();
    }

    static void Loaded(Scene scene, LoadSceneMode mode)
    {
        new WakeLog.Record("loaded").Add("scene", scene.name).Add("mode", mode.ToString())
            .Add("frame", Time.frameCount).Write();
        WakeLog.Hierarchy("loaded", scene, true);
        if (scene.name == "WakeCopy") s_CopyLoadedStep = s_Step;
    }

    static void Unloaded(Scene scene) =>
        new WakeLog.Record("unloaded").Add("scene", scene.name).Add("frame", Time.frameCount).Write();

    void Update()
    {
        if (s_Done) return;
        s_Step++;
        if (s_Step == 1)
        {
            WakeLog.Hierarchy("settled", SceneManager.GetSceneByName("Wake"), true);
        }
        else if (s_Step == 2)
        {
            new WakeLog.Record("request").Add("scene", "WakeCopy").Add("frame", Time.frameCount).Write();
            SceneManager.LoadScene("WakeCopy", LoadSceneMode.Single);
        }
        else if (s_CopyLoadedStep >= 0 && s_Step == s_CopyLoadedStep + 3)
        {
            WakeLog.Hierarchy("settled", SceneManager.GetSceneByName("WakeCopy"), true);
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneUnloaded -= Unloaded;
            s_Done = true;
            WakeLog.Complete();
            finished?.Invoke();
        }
    }
}
