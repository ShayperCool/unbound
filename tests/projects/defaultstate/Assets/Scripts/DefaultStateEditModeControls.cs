using UnityEngine;
public class DefaultStateEditModeBaseControl : MonoBehaviour
{
    [System.NonSerialized] public string constructorRunMode;
    [System.NonSerialized] public string[] constructorBindings;
    string ReadBinding(System.Func<bool> read) { try { return read().ToString(); } catch (System.Exception e) { return e.GetType().FullName; } }
    public DefaultStateEditModeBaseControl()
    {
        constructorBindings = new[] { ReadBinding(() => useGUILayout), ReadBinding(() => didAwake), ReadBinding(() => didStart) };
        try { constructorRunMode = runInEditMode.ToString(); }
        catch (System.Exception error) { constructorRunMode = error.GetType().FullName; }
    }
}
public class DefaultStatePlainEditControl : DefaultStateEditModeBaseControl {}
[ExecuteAlways] public class DefaultStateAlwaysEditControl : DefaultStateEditModeBaseControl {}
public class DefaultStateInheritedEditControl : DefaultStateAlwaysEditControl {}
[ExecuteInEditMode] public class DefaultStateInEditControl : DefaultStateEditModeBaseControl {}

public class DefaultStateNestedEditControl : DefaultStateEditModeBaseControl
{
    static bool creatingNested;
    [System.NonSerialized] public DefaultStateNestedEditControl nested = MakeNested();
    static DefaultStateNestedEditControl MakeNested()
    {
        if (creatingNested) return null;
        creatingNested = true;
        try { return new DefaultStateNestedEditControl(); }
        finally { creatingNested = false; }
    }
}
public class DefaultStateFailingEditControl : MonoBehaviour
{
    [System.NonSerialized] public bool entered, awakened, enabledCallback;
    void Awake() { awakened = true; }
    void OnEnable() { enabledCallback = true; }
    public DefaultStateFailingEditControl()
    {
        entered = true; throw new System.InvalidOperationException("component-constructor-control");
    }
}

public class DefaultStateTimeFailingEditControl : MonoBehaviour
{
    [System.NonSerialized] public bool awakened;
    void Awake() { awakened = Time.time >= 0; }
    public DefaultStateTimeFailingEditControl() { var ignored = Time.time; }
}
