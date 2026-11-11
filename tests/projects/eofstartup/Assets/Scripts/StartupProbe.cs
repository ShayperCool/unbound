using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StartupProbe : MonoBehaviour
{
    public static bool Finished;
    static readonly StringBuilder Output = new StringBuilder();
    static int firstUpdate = -1;
    static int step = -1;
    static Camera screen, texture, manual;
    static readonly int[] screenFrames = new int[12], textureFrames = new int[12];
    static readonly int[] awakeFrames = new int[12], startFrames = new int[12], laterFrames = new int[12];
    static int manualPosts;
    static readonly int[] updateFrames = new int[12], awaitFrames = new int[12];
    static void Row(string key, int value) => Output.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("EOF_STARTUP_OUT") == null) return;
        Application.runInBackground = true;
        var go = new GameObject("StartupProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<StartupProbe>();
    }

    void Awake()
    {
        screen = new GameObject("Screen").AddComponent<Camera>();
        texture = new GameObject("Texture").AddComponent<Camera>();
        texture.targetTexture = new RenderTexture(16,16,0);
        manual = new GameObject("Manual").AddComponent<Camera>();
        manual.enabled = false;
        manual.targetTexture = new RenderTexture(16,16,0);
        Camera.onPostRender += Post;
        StartCoroutine(Wait(awakeFrames));
    }
    void Start()
    {
        StartCoroutine(Wait(startFrames));
        AwaitEnd();
    }
    static async void AwaitEnd()
    {
        while (!Finished)
        {
            await Awaitable.EndOfFrameAsync();
            if (step >= 0 && step < awaitFrames.Length) awaitFrames[step]++;
        }
    }
    static IEnumerator Wait(int[] frames)
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();
            if (step >= 0 && step < frames.Length) frames[step]++;
        }
    }
    static void Post(Camera camera)
    {
        if (camera == manual) { manualPosts++; return; }
        if (step < 0 || step >= screenFrames.Length) return;
        if (camera == screen) screenFrames[step]++;
        if (camera == texture) textureFrames[step]++;
    }
    void Update()
    {
        if (firstUpdate < 0) firstUpdate = Time.frameCount;
        step = Time.frameCount - firstUpdate;
        if (step == 0)
        {
            StartCoroutine(Wait(updateFrames));
            Row("manual.before", manualPosts);
            manual.Render();
            Row("manual.after", manualPosts);
        }
        if (step == 2) StartCoroutine(Wait(laterFrames));
        if (step == 3) SceneManager.LoadScene("Extra", LoadSceneMode.Additive);
        if (step != 10 || Finished) return;
        for (int i=0;i<10;i++)
        {
            Row("frame."+i+".screen",screenFrames[i]);
            Row("frame."+i+".texture",textureFrames[i]);
            Row("frame."+i+".awake",awakeFrames[i]);
            Row("frame."+i+".start",startFrames[i]);
            Row("frame."+i+".later",laterFrames[i]);
            Row("frame."+i+".update",updateFrames[i]);
            Row("frame."+i+".await",awaitFrames[i]);
        }
        SceneWaiter.Finish();
        Row("complete",1);
        File.WriteAllText(Environment.GetEnvironmentVariable("EOF_STARTUP_OUT"),Output.ToString());
        Finished = true;
    }
    public static void Record(string key, int value) => Row(key,value);
}
