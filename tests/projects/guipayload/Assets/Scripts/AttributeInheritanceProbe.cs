using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public class AttributeInheritanceProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod]
    static void Install() { new GameObject("GuiPayloadProbe").AddComponent<AttributeInheritanceProbe>(); }

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("GUI_PAYLOAD_OUT");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (var output = new StreamWriter(path))
            {
                DeclarationGuiPayloadControls.Record((key, value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) })));
                output.WriteLine("{\"key\":\"complete\",\"value\":\"1\"}");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    [Serializable] class Row { public string key; public string value; }
}
