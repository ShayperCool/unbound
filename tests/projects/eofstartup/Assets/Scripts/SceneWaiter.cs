using System.Collections;
using UnityEngine;

public class SceneWaiter : MonoBehaviour
{
    static int updates, awakeResumeUpdates = -1, startResumeUpdates = -1, resumePosts = -1, posts;
    static bool awakeDone, startDone;
    void Awake() => StartCoroutine(Wait(true));
    void Start() => StartCoroutine(Wait(false));
    void Update() => updates++;
    void OnPostRender() => posts++;
    IEnumerator Wait(bool awake)
    {
        yield return new WaitForEndOfFrame();
        if (awake) { awakeResumeUpdates=updates; awakeDone=true; resumePosts=posts; }
        else { startResumeUpdates=updates; startDone=true; }
    }
    public static void Finish()
    {
        StartupProbe.Record("additive.awake.completed",awakeDone?1:0);
        StartupProbe.Record("additive.start.completed",startDone?1:0);
        StartupProbe.Record("additive.awake.updates",awakeResumeUpdates);
        StartupProbe.Record("additive.start.updates",startResumeUpdates);
        StartupProbe.Record("additive.awake.posts",resumePosts);
    }
}
