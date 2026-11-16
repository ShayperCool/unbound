using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>
/// Writes exception types and messages into values.jsonl in $APIERRORS_OUT (tests/value_probe.py
/// rows) for bad arguments of Resources, SceneManager, AssetBundle, Mesh, Texture2D and Animator
/// (with and without a controller), with the log messages each action writes and its answer.
/// Values never contain instance ids or paths.
/// </summary>
public class ApiErrorsProbe : MonoBehaviour
{
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();
    static readonly List<string> threadLogs = new List<string>();
    static int mainThread;
    static string temp;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static string Thrown(Exception e) => "throws " + e.GetType().FullName + ": " + e.Message;

    static string Describe(object value)
    {
        if (value is UnityEngine.Object o)
            return o == null ? (ReferenceEquals(o, null) ? "null" : "Unity-null " + o.GetType().FullName) : o.GetType().FullName + " '" + o.name + "'";
        if (value is Array array) return value.GetType().Name + "[" + array.Length + "]";
        if (value is Scene scene) return "scene valid=" + scene.IsValid() + " name=" + scene.name;
        if (value is AnimatorStateInfo info) return "state hash=" + info.fullPathHash;
        return value.ToString();
    }

    /// <summary>What an action returns or throws, and the logs it writes.</summary>
    static void Try(string key, Func<object> action)
    {
        logs.Clear();
        lock (threadLogs) threadLogs.Clear();
        string result;
        try
        {
            var value = action();
            result = ReferenceEquals(value, null) ? "returns null" : "returns " + Describe(value);
        }
        catch (Exception e) { result = Thrown(e); }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", Clean(string.Join("\n---\n", logs)));
        lock (threadLogs)
            if (threadLogs.Count > 0) Row(key + ".thread_logs", Clean(string.Join("\n---\n", threadLogs)));
    }

    /// <summary>The temporary folder's path, which differs between machines, as "<temp>".</summary>
    static string Clean(string text) => text.Replace(temp, "<temp>");

    static void Do(string key, Action action) => Try(key, () => { action(); return "done"; });

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APIERRORS_OUT"))) return;
        new GameObject("ApiErrorsProbe").AddComponent<ApiErrorsProbe>();
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("APIERRORS_OUT");
        Directory.CreateDirectory(dir);
        mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        temp = Application.temporaryCachePath;
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        // Logs written on other threads reach only the threaded callback.
        Application.logMessageReceivedThreaded += (message, stack, type) =>
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == mainThread) return;
            lock (threadLogs) threadLogs.Add(type + ": " + message);
        };
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try
        {
            Loading();
            Scenes();
            Bundles();
            Meshes();
            Textures();
        }
        catch (Exception e) { Row("aborted", Thrown(e)); }
        yield return Animators();
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static void Loading()
    {
        Try("resources.load_null", () => Resources.Load(null));
        Try("resources.load_missing", () => Resources.Load("NoSuchAsset"));
        Try("resources.load_wrong_type", () => Resources.Load<Texture2D>("Ctrl"));
        Try("resources.load_null_type", () => Resources.Load("Ctrl", null));
        Try("resources.load_all_null", () => Resources.LoadAll(null));
        Try("resources.load_all_missing", () => Resources.LoadAll("NoSuchFolder"));
        Try("resources.load_async_null", () => Resources.LoadAsync(null));
        Try("resources.load_async_missing", () => Resources.LoadAsync("NoSuchAsset").asset);
        Do("resources.unload_null", () => Resources.UnloadAsset(null));
        var go = new GameObject("Unloaded");
        Do("resources.unload_game_object", () => Resources.UnloadAsset(go));
        Do("resources.unload_component", () => Resources.UnloadAsset(go.transform));
        Destroy(go);
        Try("resources.find_all_null", () => Resources.FindObjectsOfTypeAll(null));
        Try("resources.find_all_non_object", () => Resources.FindObjectsOfTypeAll(typeof(string)));
        Try("resources.load_all_null_type", () => Resources.LoadAll("NoSuchFolder", null));
    }

    static void Scenes()
    {
        var active = SceneManager.GetActiveScene();
        Do("scene.load_missing_name", () => SceneManager.LoadScene("NoSuchScene"));
        Do("scene.load_bad_index", () => SceneManager.LoadScene(99));
        Do("scene.load_negative_index", () => SceneManager.LoadScene(-1));
        Try("scene.load_async_missing", () => SceneManager.LoadSceneAsync("NoSuchScene"));
        Try("scene.load_async_bad_index", () => SceneManager.LoadSceneAsync(99));
        Try("scene.get_at_bad", () => SceneManager.GetSceneAt(99));
        Try("scene.get_at_negative", () => SceneManager.GetSceneAt(-1));
        Try("scene.by_build_index_bad", () => SceneManager.GetSceneByBuildIndex(99));
        Try("scene.by_build_index_negative", () => SceneManager.GetSceneByBuildIndex(-1));
        Try("scene.by_build_index_loaded", () => SceneManager.GetSceneByBuildIndex(0));
        Try("scene.load_async_negative_index", () => SceneManager.LoadSceneAsync(-1));
        Try("scene.by_name_missing", () => SceneManager.GetSceneByName("NoSuchScene"));
        Try("scene.by_path_missing", () => SceneManager.GetSceneByPath("Assets/NoSuchScene.unity"));
        Try("scene.set_active_invalid", () => SceneManager.SetActiveScene(default(Scene)));
        Try("scene.unload_missing_name", () => SceneManager.UnloadSceneAsync("NoSuchScene"));
        Try("scene.unload_invalid", () => SceneManager.UnloadSceneAsync(default(Scene)));
        Try("scene.unload_bad_index", () => SceneManager.UnloadSceneAsync(99));
        Do("scene.move_null", () => SceneManager.MoveGameObjectToScene(null, active));
        var root = new GameObject("Root");
        var child = new GameObject("Child");
        child.transform.SetParent(root.transform);
        Do("scene.move_invalid_scene", () => SceneManager.MoveGameObjectToScene(root, default(Scene)));
        Do("scene.move_child", () => SceneManager.MoveGameObjectToScene(child, active));
        Try("scene.create_existing", () => SceneManager.CreateScene(active.name));
        Try("scene.create_empty", () => SceneManager.CreateScene(""));
        Try("scene.create_null", () => SceneManager.CreateScene(null));
        Do("scene.merge_invalid", () => SceneManager.MergeScenes(default(Scene), active));
        Destroy(root);
    }

    static void Bundles()
    {
        Try("bundle.load_missing_file", () => AssetBundle.LoadFromFile("NoSuchFolder/no_such_bundle"));
        Try("bundle.load_null_path", () => AssetBundle.LoadFromFile(null));
        Try("bundle.load_memory_null", () => AssetBundle.LoadFromMemory(null));
        Try("bundle.load_memory_garbage", () => AssetBundle.LoadFromMemory(new byte[16]));
        Try("bundle.load_stream_null", () => AssetBundle.LoadFromStream(null));
        Try("bundle.load_stream_garbage", () => AssetBundle.LoadFromStream(new MemoryStream(new byte[16])));
        var garbage = Path.Combine(temp, "garbage.bundle");
        File.WriteAllBytes(garbage, new byte[16]);
        Try("bundle.load_garbage_file", () => AssetBundle.LoadFromFile(garbage));
        Try("bundle.load_memory_async_null", () => AssetBundle.LoadFromMemoryAsync(null).assetBundle);
        File.Delete(garbage);
    }

    static void Meshes()
    {
        Mesh Fresh()
        {
            var m = new Mesh();
            m.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            m.triangles = new[] { 0, 1, 2 };
            return m;
        }
        var mesh = Fresh();
        Do("mesh.triangles_out_of_range", () => mesh.triangles = new[] { 0, 1, 5 });
        Try("mesh.triangles_after_bad", () => mesh.triangles);
        Do("mesh.triangles_not_multiple", () => Fresh().triangles = new[] { 0, 1 });
        Do("mesh.set_triangles_bad_submesh", () => Fresh().SetTriangles(new[] { 0, 1, 2 }, 5));
        Do("mesh.set_triangles_negative_submesh", () => Fresh().SetTriangles(new[] { 0, 1, 2 }, -1));
        Try("mesh.get_indices_bad_submesh", () => Fresh().GetIndices(5));
        Try("mesh.get_triangles_bad_submesh", () => Fresh().GetTriangles(5));
        Try("mesh.get_submesh_bad", () => Fresh().GetSubMesh(5));
        Try("mesh.get_topology_bad", () => Fresh().GetTopology(5));
        Do("mesh.uv_wrong_length", () => Fresh().uv = new Vector2[2]);
        Do("mesh.normals_wrong_length", () => Fresh().normals = new Vector3[1]);
        Do("mesh.colors_wrong_length", () => Fresh().colors = new Color[5]);
        Do("mesh.tangents_wrong_length", () => Fresh().tangents = new Vector4[2]);
        Do("mesh.vertices_null", () => Fresh().vertices = null);
        Do("mesh.set_vertices_null", () => Fresh().SetVertices((List<Vector3>)null));
        Do("mesh.set_indices_bad_count", () => Fresh().SetIndices(new[] { 0, 1, 2, 0 }, MeshTopology.Triangles, 0));
        Do("mesh.submesh_count_negative", () => Fresh().subMeshCount = -1);
        Do("mesh.set_uvs_bad_channel", () => Fresh().SetUVs(8, new List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero }));
        Do("mesh.get_uvs_bad_channel", () => Fresh().GetUVs(9, new List<Vector2>()));
        Do("mesh.set_colors_null", () => Fresh().SetColors((List<Color>)null));
        Try("mesh.get_index_start_bad", () => Fresh().GetIndexStart(5));
        Try("mesh.get_index_count_bad", () => Fresh().GetIndexCount(5));
        Try("mesh.get_base_vertex_bad", () => Fresh().GetBaseVertex(5));
        Try("mesh.get_indices_negative", () => Fresh().GetIndices(-1));
        Try("mesh.get_topology_negative", () => Fresh().GetTopology(-1));
        Try("mesh.get_submesh_negative", () => Fresh().GetSubMesh(-1));
        Do("mesh.get_triangles_list_bad", () => Fresh().GetTriangles(new List<int>(), 5));
        Do("mesh.get_triangles_list_null", () => Fresh().GetTriangles(null, 0));
        Do("mesh.get_indices_list_bad", () => Fresh().GetIndices(new List<int>(), 5));
        Do("mesh.get_uvs_null_list", () => Fresh().GetUVs(0, (List<Vector2>)null));
        Do("mesh.set_uvs_array_bad_channel", () => Fresh().SetUVs(8, new Vector2[3]));
        Do("mesh.set_indices_bad_submesh", () => Fresh().SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 5));
        Do("mesh.set_indices_lines_odd", () => Fresh().SetIndices(new[] { 0, 1, 2 }, MeshTopology.Lines, 0));
        Do("mesh.set_indices_out_of_range", () => Fresh().SetIndices(new[] { 0, 1, 2, 7 }, MeshTopology.Lines, 0));
        Try("mesh.triangles_not_multiple_submeshes", () =>
        {
            var m = Fresh();
            m.subMeshCount = 2;
            m.triangles = new[] { 0, 1 };
            return m.subMeshCount + " " + m.GetIndexCount(0);
        });
        Try("mesh.set_colors_null_clears", () =>
        {
            var m = Fresh();
            m.colors = new Color[3];
            m.SetColors((List<Color>)null);
            return m.colors.Length;
        });
        Try("mesh.submesh_count_negative_after", () =>
        {
            var m = Fresh();
            m.subMeshCount = -1;
            return m.subMeshCount;
        });
    }

    static void Textures()
    {
        Try("texture.new_negative", () => new Texture2D(-1, 4));
        Try("texture.new_zero", () => new Texture2D(0, 0));
        Try("texture.new_negative_height", () => new Texture2D(4, -1));
        Try("texture.new_zero_width", () => new Texture2D(0, 4));
        Try("texture.new_both_negative", () => new Texture2D(-1, -1));
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, true);
        Do("texture.set_pixels_short", () => t.SetPixels(new Color[3]));
        Do("texture.set_pixels32_short", () => t.SetPixels32(new Color32[2]));
        Try("texture.get_pixels_bad_mip", () => t.GetPixels(9));
        Do("texture.set_pixels_bad_mip", () => t.SetPixels(new Color[16], 9));
        Try("texture.get_pixel_outside", () => t.GetPixel(99, 99));
        Try("texture.get_pixels_block_outside", () => t.GetPixels(0, 0, 99, 99));
        Do("texture.set_pixels_block_outside", () => t.SetPixels(0, 0, 99, 99, new Color[99 * 99]));
        Do("texture.load_raw_short", () => t.LoadRawTextureData(new byte[3]));
        Do("texture.set_pixel_data_short", () => t.SetPixelData(new byte[3], 0));
        Do("texture.set_pixel_data_bad_mip", () => t.SetPixelData(new byte[64], 9));
        Try("texture.get_pixel_data_bad_mip", () => t.GetPixelData<byte>(9).Length);
        Try("texture.reinitialize_negative", () => t.Reinitialize(-1, 2));
        Try("texture.reinitialize_negative_height", () => t.Reinitialize(2, -1));
        Try("texture.size_after_reinitialize", () => t.width + "x" + t.height);
        Try("texture.get_pixels32_bad_mip", () => t.GetPixels32(9));
        Do("texture.set_pixels32_bad_mip", () => t.SetPixels32(new Color32[16], 9));
        Try("texture.get_pixel_bad_mip", () => t.GetPixel(0, 0, 9));
        Do("texture.set_pixel_bad_mip", () => t.SetPixel(0, 0, Color.red, 9));
        Try("texture.get_pixel_bilinear_bad_mip", () => t.GetPixelBilinear(0f, 0f, 9));
        Do("texture.set_pixels32_block_outside", () => t.SetPixels32(0, 0, 99, 99, new Color32[99 * 99]));
        Try("texture.get_pixels_block_negative", () => t.GetPixels(-1, 0, 2, 2));
        Do("texture.set_pixels_null", () => t.SetPixels(null));
        Do("texture.set_pixels_block_short", () => t.SetPixels(0, 0, 2, 2, new Color[3]));
        var locked = new Texture2D(4, 4);
        locked.Apply(false, true);
        Try("texture.non_readable_get_pixel", () => locked.GetPixel(0, 0));
        Do("texture.non_readable_set_pixel", () => locked.SetPixel(0, 0, Color.red));
        Try("texture.non_readable_get_pixels", () => locked.GetPixels());
        Destroy(t);
        Destroy(locked);
    }

    static IEnumerator Animators()
    {
        var go = new GameObject("Animated");
        var animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Ctrl");
        var bare = new GameObject("Bare").AddComponent<Animator>();
        yield return null;
        Do("animator.set_float_missing", () => animator.SetFloat("NoSuch", 1f));
        Try("animator.get_float_missing", () => animator.GetFloat("NoSuch"));
        Do("animator.set_float_on_bool", () => animator.SetFloat("flag", 1f));
        Do("animator.set_bool_on_float", () => animator.SetBool("speed", true));
        Do("animator.set_integer_on_trigger", () => animator.SetInteger("go", 1));
        Do("animator.set_trigger_missing", () => animator.SetTrigger("NoSuch"));
        Do("animator.reset_trigger_missing", () => animator.ResetTrigger("NoSuch"));
        Try("animator.get_bool_missing", () => animator.GetBool("NoSuch"));
        Do("animator.play_missing", () => animator.Play("NoSuchState"));
        Do("animator.cross_fade_missing", () => animator.CrossFade("NoSuchState", 0.1f));
        Do("animator.play_bad_layer", () => animator.Play("Idle", 5));
        Try("animator.state_info_bad_layer", () => animator.GetCurrentAnimatorStateInfo(5));
        Try("animator.layer_name_bad", () => animator.GetLayerName(5));
        Do("animator.layer_weight_bad", () => animator.SetLayerWeight(5, 1f));
        Try("animator.get_layer_weight_bad", () => animator.GetLayerWeight(5));
        Try("animator.get_parameter_bad", () => animator.GetParameter(9));
        Try("animator.get_parameter_negative", () => animator.GetParameter(-1));
        Do("animator.bare_set_float", () => bare.SetFloat("x", 1f));
        Try("animator.bare_get_float", () => bare.GetFloat("x"));
        Do("animator.bare_play", () => bare.Play("x"));
        Try("animator.bare_state_info", () => bare.GetCurrentAnimatorStateInfo(0));
        Try("animator.bare_parameter_count", () => bare.parameterCount);
        Do("animator.play_missing_layer0", () => animator.Play("NoSuchState", 0));
        Do("animator.play_missing_hash", () => animator.Play(12345));
        Do("animator.cross_fade_bad_layer", () => animator.CrossFade("Idle", 0.1f, 5));
        Do("animator.cross_fade_fixed_missing", () => animator.CrossFadeInFixedTime("NoSuchState", 0.1f));
        Try("animator.next_state_info_bad_layer", () => animator.GetNextAnimatorStateInfo(5));
        Try("animator.clip_info_bad_layer", () => animator.GetCurrentAnimatorClipInfo(5));
        Try("animator.in_transition_bad_layer", () => animator.IsInTransition(5));
        Try("animator.has_state_bad_layer", () => animator.HasState(5, Animator.StringToHash("Idle")));
        Try("animator.layer_index_missing", () => animator.GetLayerIndex("NoSuch"));
        Do("animator.set_trigger_on_float", () => animator.SetTrigger("speed"));
        Try("animator.get_integer_on_bool", () => animator.GetInteger("flag"));
        Do("animator.set_bool_missing_id", () => animator.SetBool(12345, true));
        Do("animator.set_float_on_bool_id", () => animator.SetFloat(Animator.StringToHash("flag"), 1f));
        Try("animator.bare_get_bool", () => bare.GetBool("x"));
        Do("animator.bare_set_trigger", () => bare.SetTrigger("x"));
        Do("animator.bare_cross_fade", () => bare.CrossFade("x", 0.1f));
        Try("animator.bare_layer_name", () => bare.GetLayerName(0));
        Try("animator.bare_layer_count", () => bare.layerCount);
        Try("animator.bare_layer_weight", () => bare.GetLayerWeight(0));
        Try("animator.bare_get_parameter", () => bare.GetParameter(0));
        Try("animator.bare_in_transition", () => bare.IsInTransition(0));
        var graph = PlayableGraph.Create("Probe");
        var playable = AnimatorControllerPlayable.Create(graph, animator.runtimeAnimatorController);
        Do("playable.set_float_missing", () => playable.SetFloat("NoSuch", 1f));
        Do("playable.set_bool_on_float", () => playable.SetBool("speed", true));
        Do("playable.play_missing", () => playable.Play("NoSuchState"));
        Do("playable.play_bad_layer", () => playable.Play("Idle", 5));
        Try("playable.layer_name_bad", () => playable.GetLayerName(5));
        Try("playable.state_info_bad_layer", () => playable.GetCurrentAnimatorStateInfo(5));
        graph.Destroy();
        // The same calls on a playable an output plays on an Animator.
        var host = new GameObject("Host").AddComponent<Animator>();
        var bound = PlayableGraph.Create("Bound");
        var played = AnimatorControllerPlayable.Create(bound, animator.runtimeAnimatorController);
        AnimationPlayableOutput.Create(bound, "Output", host).SetSourcePlayable(played);
        bound.Play();
        yield return null;
        Do("playable_bound.set_float_missing", () => played.SetFloat("NoSuch", 1f));
        Do("playable_bound.set_bool_on_float", () => played.SetBool("speed", true));
        Do("playable_bound.set_float_on_bool_id", () => played.SetFloat(Animator.StringToHash("flag"), 1f));
        Do("playable_bound.play_missing", () => played.Play("NoSuchState"));
        Do("playable_bound.play_missing_layer0", () => played.Play("NoSuchState", 0));
        Do("playable_bound.play_bad_layer", () => played.Play("Idle", 5));
        Try("playable_bound.layer_name_bad", () => played.GetLayerName(5));
        Try("playable_bound.state_info_bad_layer", () => played.GetCurrentAnimatorStateInfo(5));
        bound.Destroy();
        Destroy(host.gameObject);
        Destroy(go);
        Destroy(bare.gameObject);
    }
}
