using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Animation from an AssetBundle: the `anim` bundle's clips (their properties and events), each
/// clip sampled onto the rig at chosen times (SampleAnimation), the rig's Animator playing its
/// controller frame by frame (a trigger at frame 20) and the legacy Animation of the bobber, at a
/// frame per fixed step. Writes <c>anim.jsonl</c> into <c>ANIM_PROBE_OUT</c>, then quits.
/// </summary>
public class AnimProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("AnimProbe").AddComponent<AnimProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string Q(Quaternion q) => $"[{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}]";
    static string C(Color c) => $"[{F(c.r)},{F(c.g)},{F(c.b)},{F(c.a)}]";
    static string S(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static void Record(string kind, string body) => s_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    /// <summary>The rig's animated state (the integer field, whose curve crosses halves exactly at
    /// sampled times, only when <paramref name="count"/>: an animator's accumulated time rounds it
    /// either way).</summary>
    static string Pose(GameObject rig, bool count = true)
    {
        var target = rig.GetComponent<AnimTarget>();
        var arm = rig.transform.Find("Arm");
        var hand = arm.Find("Hand");
        var block = new MaterialPropertyBlock();
        arm.GetComponent<MeshRenderer>().GetPropertyBlock(block);
        return $"[{V(rig.transform.localPosition)},{Q(arm.localRotation)},{V(hand.localScale)},{(hand.gameObject.activeSelf ? "true" : "false")}," +
               $"[{F(target.amount)},{(count ? target.count : -1)},{(target.flag ? "true" : "false")},{V(target.offset)},{C(target.tint)}],{C(block.GetColor("_Color"))},{F(block.GetFloat("_Glossiness"))},{C(block.GetVector("_MainTex_ST"))}]";
    }

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("ANIM_PROBE_OUT") ?? "/tmp/anim";
        Directory.CreateDirectory(dir);
        Time.captureDeltaTime = Time.fixedDeltaTime;
        yield return null;

        var bundle = AssetBundle.LoadFromFile(Path.Combine(Application.dataPath, "..", "Reference", "bundles", "anim"));
        Record("bundle", $"\"loaded\":{(bundle != null ? "true" : "false")},\"assets\":[{string.Join(",", bundle.GetAllAssetNames().OrderBy(n => n).Select(S))}]");

        foreach (var name in new[] { "Move", "Spin", "Bob" })
        {
            var clip = bundle.LoadAsset<AnimationClip>(name);
            Record("clip", $"\"name\":{S(name)},\"length\":{F(clip.length)},\"frameRate\":{F(clip.frameRate)},\"wrapMode\":{(int)clip.wrapMode},\"legacy\":{(clip.legacy ? "true" : "false")}," +
                $"\"looping\":{(clip.isLooping ? "true" : "false")},\"empty\":{(clip.empty ? "true" : "false")},\"humanMotion\":{(clip.humanMotion ? "true" : "false")},\"genericRoot\":{(clip.hasGenericRootTransform ? "true" : "false")}," +
                $"\"bounds\":[{V(clip.localBounds.center)},{V(clip.localBounds.size)}],\"events\":[{string.Join(",", clip.events.Select(e => $"[{F(e.time)},{S(e.functionName)},{F(e.floatParameter)},{e.intParameter},{S(e.stringParameter)}]"))}]");
        }

        // Sampled onto a rig whose animator is off.
        var rigPrefab = bundle.LoadAsset<GameObject>("Rig");
        foreach (var name in new[] { "Move", "Spin" })
        {
            var clip = bundle.LoadAsset<AnimationClip>(name);
            var rig = Instantiate(rigPrefab);
            rig.GetComponent<Animator>().enabled = false;
            var samples = new List<string>();
            foreach (var t in new[] { 0f, 0.1f, 0.25f, 0.4f, 0.5f, 0.6f, 0.75f, 0.9f, 1f, 1.3f })
            {
                clip.SampleAnimation(rig, t);
                samples.Add($"[{F(t)},{Pose(rig)}]");
            }
            Record("sample", $"\"name\":{S(name)},\"samples\":[{string.Join(",", samples)}]");
            Destroy(rig);
            yield return null;
        }

        // The animator playing the controller.
        {
            AnimTarget.Events.Clear();
            var rig = Instantiate(rigPrefab);
            rig.name = "Rig";
            var animator = rig.GetComponent<Animator>();
            var frames = new List<string>();
            int start = Time.frameCount;
            for (int f = 0; f < 120; f++)
            {
                yield return null;
                if (f == 20) animator.SetTrigger("go");
                var info = animator.GetCurrentAnimatorStateInfo(0);
                frames.Add($"[{f},{Pose(rig, count: false)},{info.shortNameHash},{F(info.normalizedTime)},{(animator.IsInTransition(0) ? "true" : "false")}]");
            }
            Record("animator", $"\"frames\":[{string.Join(",", frames)}],\"events\":[{string.Join(",", AnimTarget.Events.Select(e => S(e.Substring(0, e.LastIndexOf('@')) + "@" + (int.Parse(e.Substring(e.LastIndexOf('@') + 1)) - start))))}]");
            Destroy(rig);
            yield return null;
        }

        // The legacy Animation.
        {
            var bobber = Instantiate(bundle.LoadAsset<GameObject>("Bobber"));
            var frames = new List<string>();
            for (int f = 0; f < 60; f++)
            {
                yield return null;
                frames.Add($"[{f},{V(bobber.transform.localPosition)}]");
            }
            Record("legacy", $"\"frames\":[{string.Join(",", frames)}]");
            Destroy(bobber);
        }

        bundle.Unload(true);
        File.WriteAllText(Path.Combine(dir, "anim.jsonl"), s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
