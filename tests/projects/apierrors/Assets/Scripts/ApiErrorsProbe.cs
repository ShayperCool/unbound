using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
        yield return Formats();
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
        Do("scene.load_empty_name", () => SceneManager.LoadScene(""));
        Try("scene.load_async_empty_name", () => SceneManager.LoadSceneAsync(""));
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
        Try("bundle.load_memory_async_null_request", () => AssetBundle.LoadFromMemoryAsync(null));
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
        var copy = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Do("texture.copy_region_outside_source", () => Graphics.CopyTexture(copy, 0, 0, 2, 2, 4, 4, t, 0, 0, 0, 0));
        Do("texture.copy_region_outside_destination", () => Graphics.CopyTexture(copy, 0, 0, 0, 0, 4, 4, t, 0, 0, 2, 2));
        var wide = new Texture2D(8, 4, TextureFormat.RGBA32, false);
        var tall = new Texture2D(4, 8, TextureFormat.RGBA32, false);
        wide.SetPixel(0, 0, Color.red);
        wide.Apply();
        tall.SetPixel(0, 0, Color.blue);
        tall.Apply();
        Do("texture.copy_whole_size_mismatch", () => Graphics.CopyTexture(wide, tall));
        Try("texture.copy_whole_size_mismatch_pixel", () => tall.GetPixel(0, 0));
        Do("texture.copy_element_size_mismatch", () => Graphics.CopyTexture(wide, 0, 0, tall, 0, 0));
        Try("texture.copy_element_size_mismatch_pixel", () => tall.GetPixel(0, 0));
        Try("texture.copy_element_size_mismatch_far_pixel", () => tall.GetPixel(3, 3));
        Do("texture.copy_whole_larger", () => Graphics.CopyTexture(new Texture2D(8, 8, TextureFormat.RGBA32, false), copy));
        Destroy(copy);
        Destroy(wide);
        Destroy(tall);
        var locked = new Texture2D(4, 4);
        locked.Apply(false, true);
        Try("texture.non_readable_get_pixel", () => locked.GetPixel(0, 0));
        Do("texture.non_readable_set_pixel", () => locked.SetPixel(0, 0, Color.red));
        Try("texture.non_readable_get_pixels", () => locked.GetPixels());
        Destroy(t);
        Destroy(locked); CopyControls(); LayeredTextures();
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
        animator.logWarnings = false;
        Try("animator.quiet_layer_name_bad", () => animator.GetLayerName(5));
        Do("animator.quiet_play_bad_layer", () => animator.Play("Idle", 5));
        Do("animator.quiet_play_missing", () => animator.Play("NoSuchState"));
        Do("animator.quiet_set_float_missing", () => animator.SetFloat("NoSuch", 1f));
        animator.logWarnings = true;
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

    static Action pendingThrow;

    /// <summary>Throws the pending action's exception out of a callback once (unhandled).</summary>
    void Update()
    {
        var action = pendingThrow;
        if (action == null) return;
        pendingThrow = null;
        action();
    }

    /// <summary>What game code and the log see of one exception: its type and message, parameter
    /// name, actual value, ToString() without the stack lines, and Debug.LogException's line.</summary>
    static void Shape(string key, Action action)
    {
        Exception thrown = null;
        try { action(); }
        catch (Exception e) { thrown = e; }
        if (thrown == null) { Row(key, "returns"); return; }
        Row(key, Thrown(thrown));
        if (thrown is ArgumentException argument) Row(key + ".param", argument.ParamName ?? "null");
        if (thrown is ArgumentOutOfRangeException range) Row(key + ".actual", range.ActualValue?.ToString() ?? "null");
        if (thrown is ObjectDisposedException disposed) Row(key + ".object", disposed.ObjectName ?? "null");
        Row(key + ".string", Regex.Split(thrown.ToString(), "\n\\s+at ")[0]);
        logs.Clear();
        Debug.LogException(thrown);
        Row(key + ".log", string.Join("\n---\n", logs));
    }

    /// <summary>The log line of an exception a callback does not catch.</summary>
    static IEnumerator Unhandled(string key, Action action)
    {
        logs.Clear();
        pendingThrow = action;
        yield return null;
        yield return null;
        Row(key, string.Join("\n---\n", logs));
    }

    static IEnumerator Formats()
    {
        // Thrown by the game's own code.
        Shape("format.game_argument", () => throw new ArgumentException("Bad thing.", "p"));
        Shape("format.game_argument_no_name", () => throw new ArgumentException("Bad thing."));
        Shape("format.game_argument_empty_name", () => throw new ArgumentException("Bad thing.", ""));
        Shape("format.game_null", () => throw new ArgumentNullException("p"));
        Shape("format.game_null_message", () => throw new ArgumentNullException("p", "Must be set."));
        Shape("format.game_null_no_name", () => throw new ArgumentNullException());
        Shape("format.game_range", () => throw new ArgumentOutOfRangeException("p"));
        Shape("format.game_range_message", () => throw new ArgumentOutOfRangeException("p", "Too big."));
        Shape("format.game_range_actual", () => throw new ArgumentOutOfRangeException("p", 8, "Too big."));
        Shape("format.game_range_actual_float", () => throw new ArgumentOutOfRangeException("p", 2.5f, "Too big."));
        Shape("format.game_range_actual_no_name", () => throw new ArgumentOutOfRangeException(null, 8, "Too big."));
        Shape("format.game_disposed", () => throw new ObjectDisposedException("Thing"));
        Shape("format.game_disposed_message", () => throw new ObjectDisposedException("Thing", "Gone."));
        Shape("format.game_inner", () => throw new InvalidOperationException("Outer.", new ArgumentNullException("p")));
        // Thrown by the class library.
        Shape("format.library_list_index", () => new List<int>()[3].ToString());
        Shape("format.library_dictionary_null", () => new Dictionary<string, int>()[null].ToString());
        Shape("format.library_substring", () => "abc".Substring(5));
        // Thrown by Unity.
        Shape("format.unity_load_null_type", () => Resources.Load("Ctrl", null));
        Shape("format.unity_uvs_bad_channel", () => new Mesh().SetUVs(8, new Vector2[3]));
        Shape("format.unity_triangles_null", () => new Mesh().GetTriangles(null, 0));
        Shape("format.unity_stream_null", () => AssetBundle.LoadFromStream(null));
        Shape("format.unity_create_scene_null", () => SceneManager.CreateScene(null));
        Shape("format.unity_instantiate_null", () => Instantiate((UnityEngine.Object)null));
        yield return Unhandled("format.unhandled_game_null", () => throw new ArgumentNullException("p"));
        yield return Unhandled("format.unhandled_game_range_actual", () => throw new ArgumentOutOfRangeException("p", 8, "Too big."));
        yield return Unhandled("format.unhandled_library_list_index", () => new List<int>()[3].ToString());
        yield return Unhandled("format.unhandled_unity_load_null_type", () => Resources.Load("Ctrl", null));
        yield return Unhandled("format.unhandled_unity_uvs_bad_channel", () => new Mesh().SetUVs(8, new Vector2[3]));
        // Actual values whose texts differ between the number formats of the runtimes.
        Shape("format.game_range_actual_third", () => throw new ArgumentOutOfRangeException("p", 1f / 3f, "Too big."));
        Shape("format.game_range_actual_third_double", () => throw new ArgumentOutOfRangeException("p", 1.0 / 3.0, "Too big."));
        // A game exception whose Message is its ToString(), which reads base.Message and its inner
        // exception (as the Addressables' remote provider exception does).
        Shape("format.game_message_from_string", () => throw new MessageFromString("Remote failed.", new InvalidOperationException("Inner.")));
        Row("format.game_message_from_string.message", new MessageFromString("Remote failed.", null).Message);
        Extractions();
        pendingExtract = true;
        yield return null;
        yield return null;
    }

    sealed class MessageFromString : Exception
    {
        public MessageFromString(string message, Exception inner) : base(message, inner) { }
        public override string Message => ToString();
        public override string ToString() => GetType().Name + " : " + base.Message + "\nresult : 4\n" + InnerException;
    }

    static string project;

    /// <summary>The project's path, which differs between machines, as "<project>".</summary>
    static string Paths(string text) => text.Replace(project, "<project>");

    /// <summary>The exception an action throws (null when it returns).</summary>
    static Exception Caught(Action action)
    {
        try { action(); }
        catch (Exception e) { return e; }
        return null;
    }

    /// <summary>StackTraceUtility.ExtractStringFromException's text for an exception (whole, or
    /// only its first line where its stack lines are the class library's or Unity's).</summary>
    static void Extract(string key, object exception, bool firstLineOnly = false)
    {
        string text;
        try { text = StackTraceUtility.ExtractStringFromException(exception); }
        catch (Exception e) { Row(key, Thrown(e)); return; }
        if (firstLineOnly) text = text.Substring(0, text.IndexOf('\n'));
        Row(key, Paths(text));
    }

    static void ThrowNested(int depth)
    {
        if (depth == 0) throw new ArgumentNullException("p");
        try { ThrowNested(depth - 1); }
        catch (Exception e) { throw new InvalidOperationException("Level " + depth + ".", e); }
    }

    static void Rethrow()
    {
        try { throw new ArgumentException("Bad thing.", "p"); }
        catch (ArgumentException) { throw; }
    }

    static void Dispatch()
    {
        var info = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(Caught(() => throw new InvalidOperationException("Captured.")));
        info.Throw();
    }

    static int counter;

    static int Guard(int x)
    {
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
        counter += x;
        return counter * 2;
    }

    static void ThrowLast()
    {
        counter++;
        throw new InvalidOperationException("Last.");
    }

    static void ThrowInTryFinally()
    {
        try { throw new InvalidOperationException("In try."); }
        finally { counter++; }
    }

    static void ThrowInLoop()
    {
        for (int i = 0; i < 3; i++)
        {
            if (i == 1) throw new InvalidOperationException("In loop.");
            counter++;
        }
    }

    static string Coalesce(string s) => s ?? throw new ArgumentNullException(nameof(s));

    static void Generic<T>(T value, ref int r, int[] a, List<T> list) => ThrowLast();

    class Holder<T>
    {
        public T Value => throw new NotSupportedException("Getter.");
        public Holder(bool fail) { if (fail) throw new InvalidOperationException("Constructor."); }
    }

    static void Floats(List<float> values, Dictionary<string, int[]> map) => ThrowLast();

    static int ThrowInSwitch(int x)
    {
        switch (x)
        {
            case 0: counter++; break;
            case 1: counter += 2; break;
            default: throw new InvalidOperationException("In switch.");
        }
        return counter;
    }

    static void ThrowInElse(int x)
    {
        if (x > 0) counter++;
        else throw new InvalidOperationException("In else.");
        counter += 2;
    }

    static void ThrowInUsingBranch(int x)
    {
        using (var stream = new MemoryStream())
        {
            if (x < 0) throw new InvalidOperationException("In using.");
            counter++;
        }
        counter += 2;
    }

    static void ThrowOutOfRange(int x)
    {
        if (x < 0 || x > 10) throw new ArgumentOutOfRangeException(nameof(x));
        counter += x;
    }

    static int ThrowInConditional(int x)
    {
        counter++;
        return x > 0 ? x : throw new InvalidOperationException("In conditional.");
    }

    static void ThrowInCatchBranch(int x)
    {
        try { counter /= x; }
        catch (DivideByZeroException)
        {
            if (counter >= 0) throw new InvalidOperationException("In catch.");
            counter++;
        }
        counter += 2;
    }

    static void Extractions()
    {
        project = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
        Extract("format.extract.game_argument", Caught(() => throw new ArgumentException("Bad thing.", "p")));
        Extract("format.extract.game_range_actual", Caught(() => throw new ArgumentOutOfRangeException("p", 8, "Too big.")));
        Extract("format.extract.game_inner_not_thrown", Caught(() => throw new InvalidOperationException("Outer.", new ArgumentNullException("p"))));
        Extract("format.extract.game_nested", Caught(() => ThrowNested(2)));
        Extract("format.extract.game_rethrow", Caught(Rethrow));
        Extract("format.extract.game_dispatch", Caught(Dispatch));
        Extract("format.extract.game_blank_message", Caught(() => throw new Exception(" ")));
        Extract("format.extract.not_thrown", new InvalidOperationException("Never thrown."));
        Extract("format.extract.library_list_index", Caught(() => new List<int>()[3].ToString()), true);
        Extract("format.extract.unity_load_null_type", Caught(() => Resources.Load("Ctrl", null)), true);
        Extract("format.extract.not_exception", "text");
        Extract("format.extract.null", null);
        // Where the throwing frame's offset and line point, by the throw's place in its method.
        Extract("format.extract.frame_guard", Caught(() => Guard(-1)));
        Extract("format.extract.frame_last", Caught(ThrowLast));
        Extract("format.extract.frame_try_finally", Caught(ThrowInTryFinally));
        Extract("format.extract.frame_loop", Caught(ThrowInLoop));
        Extract("format.extract.frame_coalesce", Caught(() => Coalesce(null)));
        int r = 0;
        Extract("format.extract.frame_generic", Caught(() => Generic(2.5f, ref r, null, null)));
        Extract("format.extract.frame_getter", Caught(() => new Holder<int>(false).Value.ToString()));
        Extract("format.extract.frame_constructor", Caught(() => new Holder<string>(true)));
        Extract("format.extract.frame_constructed_parameters", Caught(() => Floats(null, null)));
        Extract("format.extract.frame_switch", Caught(() => ThrowInSwitch(5)));
        Extract("format.extract.frame_else", Caught(() => ThrowInElse(-1)));
        Extract("format.extract.frame_using_branch", Caught(() => ThrowInUsingBranch(-1)));
        Extract("format.extract.frame_catch_branch", Caught(() => ThrowInCatchBranch(0)));
        Extract("format.extract.frame_either_bound", Caught(() => ThrowOutOfRange(-1)));
        Extract("format.extract.frame_conditional", Caught(() => ThrowInConditional(-1)));
        Row("format.extract.coroutine_stack", Paths(StackTraceUtility.ExtractStackTrace()));
    }

    /// <summary>The texts asked from the first step of a coroutine a callback starts.</summary>
    IEnumerator Started()
    {
        Extract("format.extract.started_game_argument", Caught(() => throw new ArgumentException("Bad thing.", "p")));
        Row("format.extract.started_stack", Paths(StackTraceUtility.ExtractStackTrace()));
        yield break;
    }

    static bool pendingExtract;

    /// <summary>StackTraceUtility's texts asked from a callback (Update) rather than a coroutine.</summary>
    void LateUpdate()
    {
        if (!pendingExtract) return;
        pendingExtract = false;
        Extract("format.extract.update_game_argument", Caught(() => throw new ArgumentException("Bad thing.", "p")));
        Row("format.extract.update_stack", Paths(StackTraceUtility.ExtractStackTrace()));
        StartCoroutine(Started());
    }

    static Texture2D Solid(int width, int height, Color color)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>The corner and centre pixels of a texture (whether a copy happened).</summary>
    static string Pixels(Texture2D texture) =>
        string.Join(" ", new[] { texture.GetPixel(0, 0), texture.GetPixel(texture.width / 2, texture.height / 2), texture.GetPixel(texture.width - 1, texture.height - 1) });

    static void CopyCase(string key, int srcSize, int dstSize, Action<Texture2D, Texture2D> copy)
    {
        var source = Solid(srcSize, srcSize, Color.red);
        var destination = Solid(dstSize, dstSize, Color.blue);
        Do(key, () => copy(source, destination));
        Try(key + ".pixels", () => Pixels(destination));
        Destroy(source);
        Destroy(destination);
    }

    static void CopyControls()
    {
        // Whether a refused copy leaves the destination as it was.
        CopyCase("texture.copy_case.region_outside_source", 4, 4, (s, d) => Graphics.CopyTexture(s, 0, 0, 2, 2, 4, 4, d, 0, 0, 0, 0));
        CopyCase("texture.copy_case.region_outside_destination", 4, 4, (s, d) => Graphics.CopyTexture(s, 0, 0, 0, 0, 4, 4, d, 0, 0, 2, 2));
        CopyCase("texture.copy_case.whole_larger", 8, 4, (s, d) => Graphics.CopyTexture(s, d));
        CopyCase("texture.copy_case.whole_same", 4, 4, (s, d) => Graphics.CopyTexture(s, d));
        CopyCase("texture.copy_case.region_inside", 4, 4, (s, d) => Graphics.CopyTexture(s, 0, 0, 0, 0, 2, 2, d, 0, 0, 1, 1));
        // A region of width or height -1 with offsets.
        CopyCase("texture.copy_case.region_whole_destination_offset", 4, 4, (s, d) => Graphics.CopyTexture(s, 0, 0, 0, 0, -1, -1, d, 0, 0, 2, 2));
        CopyCase("texture.copy_case.region_whole_source_offset", 4, 4, (s, d) => Graphics.CopyTexture(s, 0, 0, 2, 2, -1, -1, d, 0, 0, 0, 0));
        CopyCase("texture.copy_case.region_whole_smaller_destination", 8, 4, (s, d) => Graphics.CopyTexture(s, 0, 0, 0, 0, -1, -1, d, 0, 0, 2, 2));
        CopyCase("texture.copy_case.region_width_only", 4, 4, (s, d) => Graphics.CopyTexture(s, 0, 0, 0, 0, -1, 2, d, 0, 0, 2, 2));
        CopyCase("texture.copy_case.element_larger", 8, 4, (s, d) => Graphics.CopyTexture(s, 0, d, 0));
        CopyCase("texture.copy_case.element_smaller", 4, 8, (s, d) => Graphics.CopyTexture(s, 0, d, 0));
        CopyCase("texture.copy_case.bad_source_element", 4, 4, (s, d) => Graphics.CopyTexture(s, 3, d, 0));
        CopyCase("texture.copy_case.bad_destination_mip", 4, 4, (s, d) => Graphics.CopyTexture(s, 0, 0, d, 0, 5));
        CopyCase("texture.copy_case.bad_source_mip", 4, 4, (s, d) => Graphics.CopyTexture(s, 0, 3, d, 0, 0));
        CopyCase("texture.copy_case.bad_destination_element", 4, 4, (s, d) => Graphics.CopyTexture(s, 0, d, 2));
        CopyCase("texture.copy_case.negative_source_element", 4, 4, (s, d) => Graphics.CopyTexture(s, -1, d, 0));
        // Formats in the size message, and copies between formats.
        Try("texture.copy_case.format_srgb", () => (int)Solid(4, 4, Color.red).graphicsFormat);
        var linear = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
        Try("texture.copy_case.format_linear", () => (int)linear.graphicsFormat);
        CopyCase("texture.copy_case.element_larger_linear", 4, 4, (s, d) => Graphics.CopyTexture(linear, 0, d, 0));
        Destroy(linear);
        var half = new Texture2D(4, 4, TextureFormat.RGBAHalf, false);
        Try("texture.copy_case.format_half", () => (int)half.graphicsFormat);
        CopyCase("texture.copy_case.element_other_size_format", 4, 4, (s, d) => Graphics.CopyTexture(half, 0, d, 0));
        Destroy(half);
        var halfSameBytes = new Texture2D(4, 2, TextureFormat.RGBAHalf, false);
        CopyCase("texture.copy_case.element_same_bytes_other_format", 4, 4, (s, d) => Graphics.CopyTexture(halfSameBytes, 0, d, 0));
        Destroy(halfSameBytes);
    }

    static Color[] Colors(int count) => new Color[count];

    static void LayeredTextures()
    {
        var cube = new Cubemap(4, TextureFormat.RGBA32, true) { name = "Cube" };
        Try("texture.cubemap.mips", () => cube.mipmapCount);
        Try("texture.cubemap.get_pixels_bad_mip", () => cube.GetPixels(CubemapFace.PositiveX, 9));
        Try("texture.cubemap.get_pixels_negative_mip", () => cube.GetPixels(CubemapFace.PositiveX, -1));
        Try("texture.cubemap.get_pixels_bad_face", () => cube.GetPixels((CubemapFace)7));
        Try("texture.cubemap.get_pixels_unknown_face", () => cube.GetPixels(CubemapFace.Unknown));
        Do("texture.cubemap.set_pixels_bad_mip", () => cube.SetPixels(Colors(16), CubemapFace.PositiveX, 9));
        Do("texture.cubemap.set_pixels_bad_face", () => cube.SetPixels(Colors(16), (CubemapFace)7));
        Do("texture.cubemap.set_pixels_short", () => cube.SetPixels(Colors(3), CubemapFace.PositiveX));
        Do("texture.cubemap.set_pixels_null", () => cube.SetPixels(null, CubemapFace.PositiveX));
        Do("texture.cubemap.set_pixels_long", () => cube.SetPixels(Colors(40), CubemapFace.PositiveX));
        Try("texture.cubemap.get_pixel_bad_mip", () => cube.GetPixel(CubemapFace.PositiveX, 0, 0, 9));
        Do("texture.cubemap.set_pixel_bad_mip", () => cube.SetPixel(CubemapFace.PositiveX, 0, 0, Color.red, 9));
        Try("texture.cubemap.get_pixel_bad_face", () => cube.GetPixel((CubemapFace)7, 0, 0));
        Try("texture.cubemap.get_pixel_data_bad_mip", () => cube.GetPixelData<byte>(9, CubemapFace.PositiveX).Length);
        Try("texture.cubemap.get_pixel_data_bad_face", () => cube.GetPixelData<byte>(0, (CubemapFace)7).Length);
        Do("texture.cubemap.set_pixel_data_bad_mip", () => cube.SetPixelData(new byte[64], 9, CubemapFace.PositiveX));
        Do("texture.cubemap.set_pixel_data_bad_face", () => cube.SetPixelData(new byte[64], 0, (CubemapFace)7));
        Do("texture.cubemap.set_pixel_data_short", () => cube.SetPixelData(new byte[3], 0, CubemapFace.PositiveX));
        Do("texture.cubemap.set_pixel_data_long", () => cube.SetPixelData(new byte[100], 0, CubemapFace.PositiveX));
        Try("texture.cubemap.set_pixel_data_long_kept", () => cube.GetPixel(CubemapFace.PositiveX, 0, 0));
        Destroy(cube);

        var array = new Texture2DArray(4, 4, 2, TextureFormat.RGBA32, true) { name = "Layers" };
        Try("texture.array.mips", () => array.mipmapCount);
        Try("texture.array.get_pixels_bad_element", () => array.GetPixels(5));
        Try("texture.array.get_pixels_negative_element", () => array.GetPixels(-1));
        Try("texture.array.get_pixels_bad_mip", () => array.GetPixels(0, 9));
        Try("texture.array.get_pixels32_bad_element", () => array.GetPixels32(5));
        Try("texture.array.get_pixels32_bad_mip", () => array.GetPixels32(0, 9));
        Do("texture.array.set_pixels_bad_element", () => array.SetPixels(Colors(16), 5));
        Do("texture.array.set_pixels_bad_mip", () => array.SetPixels(Colors(16), 0, 9));
        Do("texture.array.set_pixels_short", () => array.SetPixels(Colors(3), 0));
        Do("texture.array.set_pixels_null", () => array.SetPixels(null, 0));
        Do("texture.array.set_pixels_long", () => array.SetPixels(Colors(40), 0));
        Do("texture.array.set_pixels32_long", () => array.SetPixels32(new Color32[40], 0));
        Do("texture.array.set_pixels32_short", () => array.SetPixels32(new Color32[3], 0));
        Do("texture.array.set_pixels32_null", () => array.SetPixels32(null, 0));
        Do("texture.array.set_pixels32_bad_element", () => array.SetPixels32(new Color32[16], 5));
        Do("texture.array.set_pixels32_bad_mip", () => array.SetPixels32(new Color32[16], 0, 9));
        Try("texture.array.get_pixel_data_bad_mip", () => array.GetPixelData<byte>(9, 0).Length);
        Try("texture.array.get_pixel_data_bad_element", () => array.GetPixelData<byte>(0, 5).Length);
        Do("texture.array.set_pixel_data_bad_mip", () => array.SetPixelData(new byte[64], 9, 0));
        Do("texture.array.set_pixel_data_bad_element", () => array.SetPixelData(new byte[64], 0, 5));
        Do("texture.array.set_pixel_data_short", () => array.SetPixelData(new byte[3], 0, 0));
        Destroy(array);

        var volume = new Texture3D(4, 4, 4, TextureFormat.RGBA32, true) { name = "Volume" };
        Try("texture.volume.mips", () => volume.mipmapCount);
        Try("texture.volume.get_pixels_bad_mip", () => volume.GetPixels(9));
        Try("texture.volume.get_pixels_negative_mip", () => volume.GetPixels(-1));
        Try("texture.volume.get_pixels32_bad_mip", () => volume.GetPixels32(9));
        Do("texture.volume.set_pixels_bad_mip", () => volume.SetPixels(Colors(64), 9));
        Do("texture.volume.set_pixels_short", () => volume.SetPixels(Colors(3)));
        Do("texture.volume.set_pixels_null", () => volume.SetPixels(null));
        Do("texture.volume.set_pixels_long", () => volume.SetPixels(Colors(100)));
        Do("texture.volume.set_pixels32_long", () => volume.SetPixels32(new Color32[100]));
        Do("texture.volume.set_pixels32_short", () => volume.SetPixels32(new Color32[3]));
        Do("texture.volume.set_pixels32_null", () => volume.SetPixels32(null));
        Do("texture.volume.set_pixels32_bad_mip", () => volume.SetPixels32(new Color32[64], 9));
        Try("texture.volume.get_pixel_bad_mip", () => volume.GetPixel(0, 0, 0, 9));
        Do("texture.volume.set_pixel_bad_mip", () => volume.SetPixel(0, 0, 0, Color.red, 9));
        Try("texture.volume.get_pixel_bilinear_bad_mip", () => volume.GetPixelBilinear(0f, 0f, 0f, 9));
        Try("texture.volume.get_pixel_data_bad_mip", () => volume.GetPixelData<byte>(9).Length);
        Do("texture.volume.set_pixel_data_bad_mip", () => volume.SetPixelData(new byte[256], 9));
        Do("texture.volume.set_pixel_data_short", () => volume.SetPixelData(new byte[3], 0));
        Destroy(volume);

        var cubes = new CubemapArray(4, 2, TextureFormat.RGBA32, true) { name = "Cubes" };
        Try("texture.cube_array.mips", () => cubes.mipmapCount);
        Try("texture.cube_array.get_pixels_bad_element", () => cubes.GetPixels(CubemapFace.PositiveX, 5));
        Try("texture.cube_array.get_pixels_bad_mip", () => cubes.GetPixels(CubemapFace.PositiveX, 0, 9));
        Try("texture.cube_array.get_pixels_bad_face", () => cubes.GetPixels((CubemapFace)7, 0));
        Try("texture.cube_array.get_pixels32_bad_element", () => cubes.GetPixels32(CubemapFace.PositiveX, 5));
        Do("texture.cube_array.set_pixels_bad_element", () => cubes.SetPixels(Colors(16), CubemapFace.PositiveX, 5));
        Do("texture.cube_array.set_pixels_bad_mip", () => cubes.SetPixels(Colors(16), CubemapFace.PositiveX, 0, 9));
        Do("texture.cube_array.set_pixels_bad_face", () => cubes.SetPixels(Colors(16), (CubemapFace)7, 0));
        Do("texture.cube_array.set_pixels_short", () => cubes.SetPixels(Colors(3), CubemapFace.PositiveX, 0));
        Do("texture.cube_array.set_pixels_null", () => cubes.SetPixels(null, CubemapFace.PositiveX, 0));
        Do("texture.cube_array.set_pixels_long", () => cubes.SetPixels(Colors(40), CubemapFace.PositiveX, 0));
        Do("texture.cube_array.set_pixels32_null", () => cubes.SetPixels32(null, CubemapFace.PositiveX, 0));
        Do("texture.cube_array.set_pixels32_short", () => cubes.SetPixels32(new Color32[3], CubemapFace.PositiveX, 0));
        Do("texture.cube_array.set_pixels32_long", () => cubes.SetPixels32(new Color32[40], CubemapFace.PositiveX, 0));
        Do("texture.cube_array.set_pixels32_bad_element", () => cubes.SetPixels32(new Color32[16], CubemapFace.PositiveX, 5));
        Try("texture.cube_array.get_pixel_data_bad_mip", () => cubes.GetPixelData<byte>(9, CubemapFace.PositiveX, 0).Length);
        Try("texture.cube_array.get_pixel_data_bad_element", () => cubes.GetPixelData<byte>(0, CubemapFace.PositiveX, 5).Length);
        Do("texture.cube_array.set_pixel_data_bad_mip", () => cubes.SetPixelData(new byte[64], 9, CubemapFace.PositiveX, 0));
        Do("texture.cube_array.set_pixel_data_bad_element", () => cubes.SetPixelData(new byte[64], 0, CubemapFace.PositiveX, 5));
        Destroy(cubes);

        var lockedCube = new Cubemap(4, TextureFormat.RGBA32, false) { name = "LockedCube" };
        lockedCube.Apply(false, true);
        Try("texture.cubemap.non_readable_get_pixels", () => lockedCube.GetPixels(CubemapFace.PositiveX));
        Try("texture.cubemap.non_readable_get_pixel", () => lockedCube.GetPixel(CubemapFace.PositiveX, 0, 0));
        Destroy(lockedCube);
        var lockedArray = new Texture2DArray(4, 4, 2, TextureFormat.RGBA32, false) { name = "LockedLayers" };
        lockedArray.Apply(false, true);
        Try("texture.array.non_readable_get_pixels", () => lockedArray.GetPixels(0));
        Destroy(lockedArray);
    }
}
