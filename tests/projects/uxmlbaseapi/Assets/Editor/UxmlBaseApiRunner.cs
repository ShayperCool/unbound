using System;
using UnityEditor;
using UxmlBaseApiControl;
public static class UxmlBaseApiRunner
{
    public static void Record()
    {
        UxmlBaseApiProbe.Record(Environment.GetEnvironmentVariable("UXML_BASE_API_OUT"));
        EditorApplication.Exit(0);
    }
}
