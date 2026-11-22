using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Undo records of SerializedObject.ApplyModifiedProperties
    /// (tests/projects/serializedundo records the Editor's): their names, what undo and redo
    /// restore, and how the current group's name follows Undo's flushes.</summary>
    public class SerializedObjectUndoTests
    {
        public class Sample : ScriptableObject
        {
            public int count = 3;
            public string title = "t";
            public List<int> numbers = new List<int> { 5, 6 };
            public int maxSpeed = 1;
            [SerializeField] int m_Level = 2;
            public int level => m_Level;
        }

        static Sample Make(string name)
        {
            var sample = ScriptableObject.CreateInstance<Sample>();
            sample.name = name;
            return sample;
        }

        static List<string> Events(System.Action action)
        {
            var events = new List<string>();
            Undo.UndoRedoEventCallback record = (in UndoRedoInfo info) => events.Add((info.isRedo ? "redo:" : "undo:") + info.undoName);
            Undo.undoRedoEvent += record;
            try { action(); }
            finally { Undo.undoRedoEvent -= record; }
            return events;
        }

        [Fact]
        public void AnApplyIsOneUndoRecordNamedByItsChangeOnceFlushed()
        {
            Undo.ClearAll();
            var a = Make("Alpha");
            try
            {
                Undo.IncrementCurrentGroup();
                var so = new SerializedObject(a);
                so.FindProperty("count").intValue = 10;
                Assert.True(so.ApplyModifiedProperties());
                Assert.Equal("", Undo.GetCurrentGroupName());
                Undo.FlushUndoRecordObjects();
                Assert.Equal("Modified Count in Alpha", Undo.GetCurrentGroupName());
                var events = Events(() => { Undo.PerformUndo(); Undo.PerformRedo(); });
                Assert.Equal(new[] { "undo:Modified Count in Alpha", "redo:Modified Count in Alpha" }, events);
                Assert.Equal(10, a.count);

                Undo.IncrementCurrentGroup();
                so.Update();
                so.FindProperty("title").stringValue = "u";
                so.FindProperty("numbers.Array.data[1]").intValue = 60;
                so.FindProperty("maxSpeed").intValue = 4;
                so.ApplyModifiedProperties();
                Undo.IncrementCurrentGroup();
                Assert.Equal("Modified 3 properties in Alpha", Undo.GetCurrentGroupName());
                foreach (var (path, name) in new[] { ("maxSpeed", "Max Speed"), ("m_Level", "Level"), ("numbers.Array.size", "Numbers.Array.size") })
                {
                    so.Update();
                    if (path == "numbers.Array.size") so.FindProperty(path).intValue = 4;
                    else so.FindProperty(path).intValue = 9;
                    so.ApplyModifiedProperties();
                    Undo.IncrementCurrentGroup();
                    Assert.Equal($"Modified {name} in Alpha", Undo.GetCurrentGroupName());
                }
                Assert.Equal(new[] { 5, 60, 60, 60 }, a.numbers);
                Undo.PerformUndo();
                Assert.Equal(new[] { 5, 60 }, a.numbers);
            }
            finally
            {
                Object.DestroyImmediate(a);
                Undo.ClearAll();
            }
        }

        public class Part : MonoBehaviour { }

        [Fact]
        public void ACreatedObjectOutranksAnAddedComponentWhichOutranksARecordInNamingItsGroup()
        {
            Undo.ClearAll();
            var a = Make("Alpha");
            var host = new GameObject("Host");
            var created = new List<Object>();
            try
            {
                string Named(System.Action first, System.Action second)
                {
                    Undo.IncrementCurrentGroup();
                    first();
                    second();
                    Undo.FlushUndoRecordObjects();
                    return Undo.GetCurrentGroupName();
                }
                void Record(string name) { Undo.RecordObject(a, name); a.count++; }
                void Create(string name)
                {
                    var go = new GameObject(name);
                    created.Add(go);
                    Undo.RegisterCreatedObjectUndo(go, name);
                }
                void Add() => Undo.AddComponent<Part>(host);
                Assert.Equal("Add Part", Named(() => Record("R1"), Add));
                Assert.Equal("Add Part", Named(Add, () => Record("R2")));
                Assert.Equal("C3", Named(() => Create("C3"), () => Record("R3")));
                Assert.Equal("C4", Named(() => Record("R4"), () => Create("C4")));
                Assert.Equal("C7", Named(() => Create("C7"), Add));
                Assert.Equal("C10", Named(() => Create("C9"), () => Create("C10")));
                Assert.Equal("Second", Named(() => Record("First"), () => Record("Second")));
            }
            finally
            {
                Undo.ClearAll();
                foreach (var o in created) Object.DestroyImmediate(o);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(a);
            }
        }

        [Fact]
        public void AnUndoRestoresOnlyWhatTheApplyChanged()
        {
            Undo.ClearAll();
            var a = Make("Alpha");
            try
            {
                Undo.IncrementCurrentGroup();
                var so = new SerializedObject(a);
                so.FindProperty("count").intValue = 40;
                so.ApplyModifiedProperties();
                Undo.IncrementCurrentGroup();
                so.FindProperty("title").stringValue = "w";
                so.ApplyModifiedPropertiesWithoutUndo();
                Undo.PerformUndo();
                Assert.Equal(3, a.count);
                Assert.Equal("w", a.title);
                // Nothing changed: no record.
                Undo.IncrementCurrentGroup();
                so.Update();
                Assert.False(so.ApplyModifiedProperties());
            }
            finally
            {
                Object.DestroyImmediate(a);
                Undo.ClearAll();
            }
        }

        [Fact]
        public void SeveralTargetsAreOneRecordAndTheLatestRecordNamesItsGroup()
        {
            Undo.ClearAll();
            var a = Make("Alpha");
            var b = Make("Beta");
            b.count = 4;
            try
            {
                Undo.IncrementCurrentGroup();
                var multi = new SerializedObject(new Object[] { a, b });
                multi.FindProperty("count").intValue = 60;
                multi.ApplyModifiedProperties();
                var events = Events(Undo.PerformUndo);
                Assert.Equal(new[] { "undo:Modified Count in 2 Objects" }, events);
                Assert.Equal(3, a.count);
                Assert.Equal(4, b.count);

                // A record of the object then an apply: the apply names the group, the undo
                // restores both.
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(a, "Record Alpha");
                a.title = "r";
                var so = new SerializedObject(a);
                so.FindProperty("count").intValue = 50;
                so.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                Assert.Equal("Modified Count in Alpha", Undo.GetCurrentGroupName());
                Undo.PerformUndo();
                Assert.Equal("t", a.title);
                Assert.Equal(3, a.count);

                // An explicit name stays the group's.
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Named Group");
                so.Update();
                so.FindProperty("count").intValue = 55;
                so.ApplyModifiedProperties();
                Assert.Equal("Named Group", Undo.GetCurrentGroupName());
                Assert.Equal(new[] { "undo:Named Group" }, Events(Undo.PerformUndo));
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Undo.ClearAll();
            }
        }
    }
}
