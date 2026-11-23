using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Jobs;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// TransformAccessArray's capacity as the Unity 6000.0.71f1 Editor keeps it
    /// (tests/projects/transformaccess): a capacity below the length is ignored (compared
    /// unsigned: a negative one is kept), Add and SetTransforms past the capacity make it twice
    /// the new length.
    /// </summary>
    public sealed class TransformAccessArrayCapacityTests : IDisposable
    {
        readonly Transform[] m_Transforms = Enumerable.Range(0, 6).Select(i => new GameObject("T" + i).transform).ToArray();
        readonly FieldInfo m_MainThread = typeof(PlayerLoopDriver).GetField("s_MainThreadId", BindingFlags.Static | BindingFlags.NonPublic);
        readonly object m_PreviousMainThread;

        public TransformAccessArrayCapacityTests()
        {
            m_PreviousMainThread = m_MainThread.GetValue(null);
            m_MainThread.SetValue(null, Environment.CurrentManagedThreadId);
        }

        public void Dispose()
        {
            foreach (var t in m_Transforms) Object.DestroyImmediate(t.gameObject);
            m_MainThread.SetValue(null, m_PreviousMainThread);
        }

        struct Mark : IJobParallelForTransform
        {
            public void Execute(int index, TransformAccess transform) => transform.localPosition = new Vector3(index + 1, 0, 0);
        }

        static string Shape(TransformAccessArray array) => array.length + "/" + array.capacity;

        string Moved()
        {
            var moved = string.Join(",", m_Transforms.Select(t => t.localPosition.x));
            foreach (var t in m_Transforms) t.localPosition = Vector3.zero;
            return moved;
        }

        [Fact]
        public void ACapacityBelowTheLengthIsIgnoredAndTheTransformsReplacedAfterItAreTheOnesAJobReaches()
        {
            // As the 2D Animation package's deformation does when sprites leave: the capacity set to
            // the new count first, then the transforms.
            var array = new TransformAccessArray(m_Transforms.Take(5).ToArray());
            try
            {
                array.capacity = 3;
                Assert.Equal("5/5", Shape(array));
                Assert.Equal("T0,T1,T2,T3,T4", string.Join(",", Enumerable.Range(0, array.length).Select(i => array[i].name)));
                array.SetTransforms(m_Transforms.Take(3).ToArray());
                Assert.Equal("3/5", Shape(array));
                new Mark().Schedule(array).Complete();
                Assert.Equal("1,2,3,0,0,0", Moved());
                Assert.Throws<IndexOutOfRangeException>(() => array[3]);
            }
            finally { array.Dispose(); }
        }

        [Fact]
        public void AShrinkKeepsAGrownCapacityAndACapacityAtOrAboveTheLengthIsTaken()
        {
            var array = new TransformAccessArray(m_Transforms.Take(5).ToArray());
            try
            {
                array.capacity = 9;
                array.capacity = 3;
                Assert.Equal("5/9", Shape(array));
                array.capacity = 7;
                Assert.Equal("5/7", Shape(array));
                array.capacity = 5;
                Assert.Equal("5/5", Shape(array));
                array.capacity = 0;
                Assert.Equal("5/5", Shape(array));
            }
            finally { array.Dispose(); }
        }

        [Fact]
        public void ANegativeCapacityIsKeptAndNeverGrown()
        {
            var array = new TransformAccessArray(m_Transforms.Take(5).ToArray());
            try
            {
                array.capacity = -1;
                Assert.Equal("5/-1", Shape(array));
                array.Add(m_Transforms[5]);
                Assert.Equal("6/-1", Shape(array));
                array.SetTransforms(m_Transforms.Take(2).ToArray());
                Assert.Equal("2/-1", Shape(array));
                // Scheduled, it asserts and runs all the same.
                var logs = new List<string>();
                Application.LogCallback log = (message, _, type) => logs.Add(type + ": " + message);
                Application.logMessageReceived += log;
                try { new Mark().Schedule(array).Complete(); }
                finally { Application.logMessageReceived -= log; }
                Assert.Equal(new[] { "Assert: Assertion failed on expression: 'desiredJobCount != 0'" }, logs);
                Assert.Equal("1,2,0,0,0,0", Moved());
            }
            finally { array.Dispose(); }
        }

        [Fact]
        public void AddAndSetTransformsPastTheCapacityMakeItTwiceTheNewLength()
        {
            var grown = new TransformAccessArray(2);
            var empty = new TransformAccessArray(0);
            var set = new TransformAccessArray(2);
            try
            {
                var sizes = new List<string>();
                foreach (var t in m_Transforms)
                {
                    grown.Add(t);
                    sizes.Add(Shape(grown));
                }
                Assert.Equal("1/2 2/2 3/6 4/6 5/6 6/6", string.Join(" ", sizes));
                empty.Add(m_Transforms[0]);
                Assert.Equal("1/2", Shape(empty));
                set.SetTransforms(m_Transforms.Take(5).ToArray());
                Assert.Equal("5/10", Shape(set));
                // At the capacity, or below it: no growth.
                set.SetTransforms(m_Transforms.Take(4).ToArray());
                Assert.Equal("4/10", Shape(set));
                var exact = new TransformAccessArray(m_Transforms.Take(5).ToArray());
                Assert.Equal("5/5", Shape(exact));
                exact.Dispose();
            }
            finally
            {
                grown.Dispose();
                empty.Dispose();
                set.Dispose();
            }
        }
    }
}
