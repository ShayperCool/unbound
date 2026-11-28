using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class HandlesStateProbe
{
    static string Output => Environment.GetEnvironmentVariable("HANDLES_STATE_OUT");
    static string B(bool value) => value ? "true" : "false";
    static readonly List<string> Rows = new List<string>();

    static void Row(string key, string value) => Rows.Add("{\"k\":\"" + key + "\",\"v\":" + value + "}");
    static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 value) => $"[{F(value.x)},{F(value.y)},{F(value.z)}]";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (string.IsNullOrEmpty(Output)) return;
        Row("defaults", $"[{(int)Handles.zTest},{B(Handles.lighting)},{B(Handles.matrix == Matrix4x4.identity)}]");
        Handles.zTest = CompareFunction.Greater;
        Handles.lighting = false;
        var transform = Matrix4x4.TRS(new Vector3(6, 3, 9), Quaternion.identity, new Vector3(2, 3, 4));
        Handles.color = Color.red;
        Handles.matrix = transform;
        Row("assigned/state", $"[{(int)Handles.zTest},{B(Handles.lighting)}]");
        using (new Handles.DrawingScope(Color.blue, Matrix4x4.Scale(Vector3.one * 4)))
        {
            Row("scope/blue", B(Handles.color == Color.blue));
            Handles.zTest = CompareFunction.LessEqual;
            Handles.lighting = true;
            using (new Handles.DrawingScope(Color.green)) Row("nested/green", B(Handles.color == Color.green));
            Row("nested/restored-blue", B(Handles.color == Color.blue));
        }
        Row("restored/state", $"[{(int)Handles.zTest},{B(Handles.lighting)},{B(Handles.color == Color.red)},{B(Handles.matrix == transform)}]");
        var lineStart = new Vector3(2, 1, 0);
        var lineEnd = new Vector3(6, 1, 0);
        Row("line/parameter", $"[{F(HandleUtility.PointOnLineParameter(new Vector3(8, 3, 0), lineStart, Vector3.right * 2))}," +
            $"{F(HandleUtility.PointOnLineParameter(new Vector3(-2, 3, 0), lineStart, Vector3.right * 2))}," +
            $"{B(float.IsNaN(HandleUtility.PointOnLineParameter(lineEnd, lineStart, Vector3.zero)))}]");
        Row("line/interior", V(HandleUtility.ProjectPointLine(new Vector3(4, 3, 1), lineStart, lineEnd)));
        Row("line/clipped", $"[{V(HandleUtility.ProjectPointLine(new Vector3(-2, 1, 0), lineStart, lineEnd))}," +
            $"{V(HandleUtility.ProjectPointLine(new Vector3(8, 1, 0), lineStart, lineEnd))}]");
        Row("line/coincident", V(HandleUtility.ProjectPointLine(lineEnd, lineStart, lineStart)));
        const float length = 1e-7f;
        Row("line/short-scale", F(HandleUtility.ProjectPointLine(Vector3.one, Vector3.zero, Vector3.right * length).x / length));
        Row("line/distance", $"[{F(HandleUtility.DistancePointLine(new Vector3(8, 1, 0), lineStart, lineEnd))}," +
            $"{F(HandleUtility.DistancePointLine(lineEnd, lineStart, lineStart))}]");
        var diagonalStart = new Vector3(3, -3, 7);
        var diagonalDirection = new Vector3(0, 3, 4);
        var diagonalEnd = diagonalStart + diagonalDirection;
        var offsetPoint = diagonalEnd + Vector3.right * 2;
        Row("line/diagonal-projection", V(HandleUtility.ProjectPointLine(offsetPoint, diagonalStart, diagonalEnd)));
        Row("line/diagonal-distance", F(HandleUtility.DistancePointLine(offsetPoint, diagonalStart, diagonalEnd)));
        Row("line/diagonal-parameter", F(HandleUtility.PointOnLineParameter(offsetPoint, diagonalStart, diagonalDirection)));
        const float normalizedLength = 2e-6f;
        Row("line/normalized-short", F(HandleUtility.ProjectPointLine(Vector3.one, Vector3.zero,
            Vector3.forward * normalizedLength).z / normalizedLength));
        var worker = new Thread(() =>
        {
            CheckWorker("get-ztest", "get_zTest", () => _ = Handles.zTest);
            CheckWorker("set-ztest", "set_zTest", () => Handles.zTest = CompareFunction.Never);
            CheckWorker("get-lighting", "get_lighting", () => _ = Handles.lighting);
            CheckWorker("set-lighting", "set_lighting", () => Handles.lighting = false);
            Row("worker/distance", F(HandleUtility.DistancePointLine(lineEnd, lineStart, lineStart)));
        });
        worker.Start();
        worker.Join();
        Row("worker/state-after", $"[{(int)Handles.zTest},{B(Handles.lighting)}]");
        Row("complete", "true");
        Directory.CreateDirectory(Output);
        File.WriteAllLines(Path.Combine(Output, "values.jsonl"), Rows);
        EditorApplication.Exit(0);
    }

    static void CheckWorker(string key, string method, Action action)
    {
        try
        {
            action();
            Row("worker/" + key, "false");
        }
        catch (UnityException error)
        {
            Row("worker/" + key, B(error.Message.StartsWith(method + " can only be called from the main thread.")));
        }
    }
}
