using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

static class MeasureTransformMemory
{
    [Serializable] sealed class Row { public string k, v; }

    sealed class LoggerCounter : ILogHandler
    {
        internal int Calls;
        public void LogException(Exception error, Object context) => Calls++;
        public void LogFormat(LogType type, Object context, string format, params object[] args) => Calls++;
    }

    static readonly List<string> Rows = new();

    static void Put(string key, string value) => Rows.Add(JsonUtility.ToJson(new Row { k = key, v = value }));

    static void Read(string stage, Transform node) => Put(stage, node == null ? "null" : FormattableString.Invariant(
        $"{node.hierarchyCapacity}/{node.hierarchyCount}/{node.childCount}/{Profiler.GetRuntimeMemorySizeLong(node)}"));

    static Transform Make(string name, Transform parent = null)
    {
        var node = new GameObject(name).transform;
        if (parent != null) node.SetParent(parent, false);
        return node;
    }

    static void MeasureExplicitCapacity()
    {
        var root = Make("Explicit hierarchy reserve");
        Read("capacity.fresh", root);
        for (int capacity = 1; capacity <= 64; capacity++)
        {
            root.hierarchyCapacity = capacity;
            Read("capacity." + capacity, root);
        }
        foreach (int capacity in new[] { 127, 128, 129, 255, 256, 257, 511, 512, 513, 1023, 1024, 1025 })
        {
            root.hierarchyCapacity = capacity;
            Read("capacity." + capacity, root);
        }
        root.hierarchyCapacity = 3;
        Read("capacity.shrinkToThree", root);
        Object.DestroyImmediate(root.gameObject);
    }

    static void MeasureDirectChildren()
    {
        var root = Make("Direct children");
        var children = new List<Transform>();
        for (int count = 1; count <= 33; count++)
        {
            var child = Make("Child " + count, root);
            children.Add(child);
            Read("direct.added." + count, root);
        }
        for (int index = children.Count - 1; index >= 0; index -= 2)
            Object.DestroyImmediate(children[index].gameObject);
        Read("direct.afterRemoval", root);
        root.hierarchyCapacity = root.hierarchyCount;
        Read("direct.compacted", root);
        var clone = Object.Instantiate(root);
        Read("direct.clone", clone);
        clone.GetChild(0).hierarchyCapacity = clone.hierarchyCount;
        Read("direct.cloneSetSameThroughChild", clone);
        clone.GetChild(0).hierarchyCapacity = clone.hierarchyCount + 1;
        Read("direct.cloneSetLargerThroughChild", clone);
        Read("direct.childSeesLargerOwner", clone.GetChild(0));
        Object.DestroyImmediate(clone.gameObject);
        Object.DestroyImmediate(root.gameObject);
    }

    static void MeasureOwnership()
    {
        var root = Make("Source root");
        var branch = Make("Branch", root);
        Make("First grandchild", branch);
        Make("Second grandchild", branch);
        Make("Other branch", root);
        root.hierarchyCapacity = 64;
        Read("ownership.source", root);
        Read("ownership.depthTwo", branch.GetChild(0));
        var clone = Object.Instantiate(root);
        Read("ownership.cloneRoot", clone);
        var from = clone.GetChild(0);
        var to = clone.GetChild(1);
        from.GetChild(0).SetParent(to, false);
        Read("ownership.sameTreeRoot", clone);
        Read("ownership.sameTreeFrom", from);
        Read("ownership.sameTreeTo", to);
        Object.DestroyImmediate(to.gameObject);
        Read("ownership.afterDestroy", clone);
        Make("Reused room", from);
        Read("ownership.growthWithinLazyReserve", clone);
        Object.DestroyImmediate(clone.gameObject);

        var parent = Make("Clone destination");
        var one = Object.Instantiate(branch, parent, false);
        Read("ownership.parentedCloneDestination", parent);
        Read("ownership.parentedClone", one);
        Read("ownership.parentedGrandchild", one.GetChild(1));
        var two = Object.Instantiate(branch, parent, false);
        Read("ownership.parentedWithinReserve", parent);
        Object.Instantiate(branch, parent, false);
        Read("ownership.parentedGrowth", parent);
        Object.DestroyImmediate(one.gameObject);
        Read("ownership.parentedDestroy", parent);
        two.SetParent(null, false);
        Read("ownership.detachedTree", two);
        Read("ownership.detachedChild", two.GetChild(0));
        Read("ownership.formerParent", parent);

        var oldOwner = Make("Warm destination");
        oldOwner.hierarchyCapacity = 9;
        root.SetParent(oldOwner, false);
        Read("ownership.preallocatedNowChild", root);
        Read("ownership.preallocatedOwner", oldOwner);
        root.SetParent(null, false);
        Read("ownership.formerRootRegained", root);
        Read("ownership.oldOwner", oldOwner);
        Object.DestroyImmediate(oldOwner.gameObject);
        Object.DestroyImmediate(two.gameObject);
        Object.DestroyImmediate(parent.gameObject);
        Object.DestroyImmediate(root.gameObject);
    }

    static void MeasureLifetime()
    {
        var source = Make("Source");
        var destination = Make("Destination");
        var moving = Make("Moving", source);
        var leaf = Make("Leaf", moving);
        var watch = moving.gameObject.AddComponent<TransformMemoryCallbacks>();
        watch.BeforeParentChanged = () =>
        {
            Read("reparent.beforeSource", source);
            Read("reparent.beforeDestination", destination);
            Read("reparent.beforeMoving", moving);
        };
        watch.AfterParentChanged = () =>
        {
            Read("reparent.afterSource", source);
            Read("reparent.afterDestination", destination);
            Read("reparent.afterMoving", moving);
        };
        moving.SetParent(destination, false);
        watch.BeforeParentChanged = null;
        watch.AfterParentChanged = null;
        Read("reparent.completeSource", source);
        Read("reparent.completeDestination", destination);
        Read("reparent.completeMoving", moving);

        var parentWatch = destination.gameObject.AddComponent<TransformMemoryCallbacks>();
        parentWatch.ChildrenChanged = () =>
        {
            Read("destroy.callbackParent", destination);
            Read("destroy.callbackRemoved", moving);
            Read("destroy.callbackGrandchild", leaf);
        };
        watch.Destroying = () =>
        {
            Read("destroy.beforeParent", destination);
            Read("destroy.beforeRemoved", moving);
            Read("destroy.beforeGrandchild", leaf);
        };
        Object.DestroyImmediate(moving.gameObject);
        parentWatch.ChildrenChanged = null;
        Read("destroy.afterParent", destination);
        Read("destroy.afterRemoved", moving);
        Read("destroy.afterGrandchild", leaf);
        Object.DestroyImmediate(destination.gameObject);
        Object.DestroyImmediate(source.gameObject);
    }

    static void MeasureAuthoredAndRect()
    {
        foreach (string name in new[] { "Root", "Five" })
        {
            var authored = GameObject.Find("Authored " + name).transform;
            Read("authored." + name, authored);
            Read("authored." + name + ".child", authored.GetChild(0));
            var clone = Object.Instantiate(authored);
            Read("authored." + name + ".clone", clone);
            Make("Added after loading", authored);
            Read("authored." + name + ".added", authored);
            Object.DestroyImmediate(clone.gameObject);
        }

        var authoredRect = GameObject.Find("Authored Rect").transform;
        Read("rect.authored", authoredRect);
        var rectClone = Object.Instantiate(authoredRect);
        Read("rect.authoredClone", rectClone);
        Object.DestroyImmediate(rectClone.gameObject);
        var rect = new GameObject("New Rect", typeof(RectTransform)).transform;
        Read("rect.runtime", rect);
        rect.hierarchyCapacity = 1;
        Read("rect.sameCapacity", rect);
        rect.hierarchyCapacity = 2;
        Read("rect.grown", rect);
        rect.hierarchyCapacity = 1;
        Read("rect.explicitShrink", rect);
        rect.SetParent(authoredRect, false);
        Read("rect.parent", authoredRect);
        Read("rect.child", rect);
        rect.SetParent(null, false);
        Read("rect.detached", rect);
        Object.DestroyImmediate(rect.gameObject);

        var plain = Make("Replacement root");
        var leaf = Make("Replacement leaf", plain);
        plain.hierarchyCapacity = 17;
        Read("replacement.rootBefore", plain);
        var rectRoot = plain.gameObject.AddComponent<RectTransform>();
        Read("replacement.rootAfter", rectRoot);
        Read("replacement.childBefore", leaf);
        var rectLeaf = leaf.gameObject.AddComponent<RectTransform>();
        Read("replacement.childAfter", rectLeaf);
        Read("replacement.rootAfterChild", rectRoot);
        Object.DestroyImmediate(rectRoot.gameObject);
    }

    static void MeasureCapacityWarnings()
    {
        var root = Make("Capacity warning root");
        var child = Make("Capacity warning caller", root);
        var savedHandler = Debug.unityLogger.logHandler;
        var savedStack = Application.GetStackTraceLogType(LogType.Warning);
        var handler = new LoggerCounter();
        void Observe(string message, string stack, LogType type)
        {
            if (type == LogType.Warning && message.StartsWith("Changing desired", StringComparison.Ordinal))
                Put("warning." + Application.GetStackTraceLogType(LogType.Warning),
                    FormattableString.Invariant($"{message}/{(!string.IsNullOrEmpty(stack))}/{root.hierarchyCapacity}"));
        }
        try
        {
            Debug.unityLogger.logHandler = handler;
            Application.logMessageReceived += Observe;
            root.hierarchyCapacity = 33;
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            child.hierarchyCapacity = -7;
            Read("warning.afterNone", root);
            root.hierarchyCapacity = 21;
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.ScriptOnly);
            child.hierarchyCapacity = 0;
            Read("warning.afterScriptOnly", child);
        }
        finally
        {
            Application.logMessageReceived -= Observe;
            Application.SetStackTraceLogType(LogType.Warning, savedStack);
            Debug.unityLogger.logHandler = savedHandler;
        }
        Put("warning.customHandler", handler.Calls.ToString());
        Object.DestroyImmediate(root.gameObject);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Capture()
    {
        string output = Environment.GetEnvironmentVariable("TRANSFORM_MEMORY_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Put("unity", Application.unityVersion);
        MeasureExplicitCapacity();
        MeasureDirectChildren();
        MeasureOwnership();
        MeasureLifetime();
        MeasureAuthoredAndRect();
        MeasureCapacityWarnings();
        Rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
