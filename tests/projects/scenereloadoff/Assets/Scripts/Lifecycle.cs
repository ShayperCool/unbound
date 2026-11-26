using System;
using System.IO;
using UnityEngine;

/// <summary>Records its callbacks for tests/projects/scenereloadoff (SCENERELOADOFF_OUT): the
/// callback, Edit or Play mode, and how many times this instance has had it (a field that is not
/// serialized: a reloaded object starts again from 0). Awake hides `template` in Play mode only, as
/// TMP_Dropdown hides its template.</summary>
public class Lifecycle : MonoBehaviour
{
    public GameObject template;
    [NonSerialized] int m_Awakes, m_Enables, m_Starts;
    [NonSerialized] bool m_Updated;

    protected void Line(string callback, int count)
    {
        var path = Environment.GetEnvironmentVariable("SCENERELOADOFF_OUT");
        if (string.IsNullOrEmpty(path)) return;
        File.AppendAllText(path, name + "\t" + callback + count + "\t" + (Application.isPlaying ? "play" : "edit") + "\n");
    }

    void Awake()
    {
        Line("Awake", ++m_Awakes);
        if (template != null && Application.isPlaying) template.SetActive(false);
    }

    void OnEnable() => Line("OnEnable", ++m_Enables);
    void Start() => Line("Start", ++m_Starts);
    void OnDisable() => Line("OnDisable", 0);
    void OnDestroy() => Line("OnDestroy", 0);

    void Update()
    {
        if (m_Updated) return;
        m_Updated = true;
        Line("Update", 1);
    }
}
