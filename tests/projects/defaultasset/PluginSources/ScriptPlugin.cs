// The classes of a precompiled managed plug-in that the Editor may give script sub-assets:
// behaviours and scriptable objects in and outside a namespace, abstract, generic and nested
// ones, a class derived from UnityEngine.Object only, and plain classes.
using UnityEngine;

namespace ScriptPlugin
{
    public class PluginBehaviour : MonoBehaviour
    {
        public int value = 3;
    }

    public class PluginData : ScriptableObject
    {
        public string label = "data";
    }

    public abstract class PluginAbstract : MonoBehaviour
    {
    }

    public class PluginGeneric<T> : MonoBehaviour
    {
        public T item;
    }

    public class PluginOuter
    {
        public class PluginNested : MonoBehaviour
        {
        }
    }

    public class PluginObject : Object
    {
    }

    public class PluginPlain
    {
        public int number;
    }

    public static class PluginStatic
    {
        public static int Answer() => 42;
    }
}

public class GlobalPluginBehaviour : MonoBehaviour
{
}
