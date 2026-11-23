using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class TransformMatrixProbe
{
    const string Variable = "TRANSFORM_MATRIX_OUT";
    static readonly StringBuilder Output = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string directory = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(directory)) return;
        var root = new GameObject("root").transform;
        var parent = new GameObject("parent").transform;
        var child = new GameObject("child").transform;
        parent.SetParent(root, false);
        child.SetParent(parent, false);
        root.SetLocalPositionAndRotation(new Vector3(1.7641f, -3.13f, 0.58093f),
            new Quaternion(0.31313f, -0.2941f, 0.36018f, 0.72811f));
        root.localScale = Vector3.one;
        parent.SetLocalPositionAndRotation(new Vector3(-0.51283f, 1.7872f, 0.5159f),
            new Quaternion(-0.645f, 0.0135f, 0.27376451f, 0.71f));
        parent.localScale = Vector3.one;
        child.SetLocalPositionAndRotation(new Vector3(0.663f, 0.8f, -1.259f),
            new Quaternion(0.1736f, 0.05623f, 0.4987f, -0.40198f));
        child.localScale = Vector3.one;
        Record("root", root);
        Record("unit", child);
        parent.localScale = new Vector3(-1.43f, 0.877f, 1.9999f);
        child.localScale = new Vector3(0.8123f, 1.191f, 1.37f);
        Record("shear", child);
        root.localScale = new Vector3(0.2108f, -2.613f, 1.842f);
        parent.localScale = new Vector3(0f, 1f, -1f);
        Record("degenerate", child);
        float negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000u));
        root.SetLocalPositionAndRotation(new Vector3(negativeZero, -8f, 0f),
            new Quaternion(0f, 0f, negativeZero, 1f));
        root.localScale = new Vector3(1f, -1f, negativeZero);
        Record("signed", root);
        child.SetParent(null, false);
        child.SetPositionAndRotation(new Vector3(-3f, 1.02f, 0.258f), Quaternion.Euler(24f, 36f, -92f));
        Record("euler", child);
        child.SetParent(parent, false);
        var position = new Vector3(4f, 1f, -2f);
        var rotation = new Quaternion(0.02626f, -0.2941f, 0.36018f, 0.72811f);
        root.SetLocalPositionAndRotation(position, rotation);
        root.localScale = new Vector3(-0.23f, 0.916f, 1.2f);
        parent.localScale = new Vector3(1.707f, 0.2f, -2f);
        Record("changedAncestor", child);
        Record("combined", root);
        root.localPosition = position;
        root.localRotation = rotation;
        Record("separate", root);
        root.SetLocalPositionAndRotation(position, new Quaternion(1e-17f, -2e-17f, 0f, 0f));
        Record("tinyRotation", root);
        root.SetLocalPositionAndRotation(position, new Quaternion(0f, 0f, 0f, 0f));
        Record("zeroRotation", root);

        parent.SetParent(null, false);
        parent.SetLocalPositionAndRotation(new Vector3(6f, 3f, 5f), new Quaternion(0.5f, 0.5f, 0.5f, 0.5f));
        parent.localScale = new Vector3(-2f, 3f, 4f);
        child.SetLocalPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.identity);
        child.localScale = Vector3.one;
        var sample = new Vector3(2f, -3f, 4f);
        Vector("geometry.localPoint", sample);
        Vector("geometry.worldPoint", child.localToWorldMatrix.MultiplyPoint3x4(sample));
        Vector("geometry.transformPoint", child.TransformPoint(sample));

        Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), Output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Record(string key, Transform target)
    {
        var matrix = target.localToWorldMatrix;
        Put(key + ".matrix", MatrixWords(matrix));
        Vector(key + ".localPosition", target.localPosition);
        var rotation = target.localRotation;
        Put(key + ".localRotation", Words(rotation.x, rotation.y, rotation.z, rotation.w));
        if (key == "unit" || key == "shear")
        {
            var worldTrs = Matrix4x4.TRS(target.position, target.rotation, target.lossyScale);
            bool differs = MatrixWords(matrix) != MatrixWords(worldTrs);
            Put(key + ".worldPoseMatrixDiffers", differs ? "true" : "false");
        }
    }

    static string Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value)).ToString("x8", CultureInfo.InvariantCulture);
    static string Words(params float[] values) => string.Join(" ", Array.ConvertAll(values, Bits));
    static void Vector(string key, Vector3 value) => Put(key, Words(value.x, value.y, value.z));

    static string MatrixWords(Matrix4x4 value)
    {
        var fields = new float[16];
        for (int i = 0; i < fields.Length; i++) fields[i] = value[i];
        return Words(fields);
    }

    static void Put(string key, string words) =>
        Output.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(words).Append("\"}\n");
}
