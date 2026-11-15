using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    // tests/projects/eventtypenames: two identical Unity 6000.0.71f1 captures.
    public class PersistentCallTypeNameTests
    {
        [Serializable]
        class Holder
        {
            public UnityEvent onFire = new UnityEvent();
            public int tail = 7;
        }

        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        static FieldInfo Declared(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
                if (type.GetField(name, Fields) is FieldInfo field) return field;
            throw new MissingFieldException(name);
        }

        static object Call(UnityEventBase e) =>
            ((IList)Declared(typeof(PersistentCallGroup), "m_Calls").GetValue(Declared(typeof(UnityEventBase), "m_PersistentCalls").GetValue(e)))[0];

        static string Stored(object call) => (string)Declared(call.GetType(), "m_TargetAssemblyTypeName").GetValue(call);

        static void Store(object call, string name, object value) => Declared(call.GetType(), name).SetValue(call, value);

        static string Json(string typeName) =>
            "{\"onFire\":{\"m_PersistentCalls\":{\"m_Calls\":[{\"m_TargetAssemblyTypeName\":\"" + typeName + "\",\"m_MethodName\":\"Fire\",\"m_Mode\":1}]}},\"tail\":9}";

        [Theory]
        [InlineData("A.B, Lib, Version=1.2.3.4, Culture=neutral, PublicKeyToken=null", "A.B, Lib")]
        [InlineData("A.B, Lib, Culture=en, Version=2.0.0.0", "A.B, Lib")]
        [InlineData("UnityEngine.Rigidbody, UnityEngine.PhysicsModule, Version=0.0.0.0", "UnityEngine.Rigidbody, UnityEngine")]
        [InlineData("A.B, Other, UnityEngine.XModule", "A.B, Other, UnityEngine")]
        [InlineData("A.B, UnityEngine.Thing, Version=1.0.0.0", "A.B, UnityEngine.Thing")]
        [InlineData("A.B, MyUnityEngine.Module", "A.B, MyUnityEngine.Module")]
        [InlineData("Plain", "Plain")]
        public void SerializationTidiesTheStoredTargetTypeName(string stored, string tidy)
        {
            var holder = new Holder();
            JsonUtility.FromJsonOverwrite(Json(stored), holder);
            Assert.Equal(tidy, Stored(Call(holder.onFire)));
            Assert.Equal(9, holder.tail);
            // Writing tidies the live call too, not only the output.
            Store(Call(holder.onFire), "m_TargetAssemblyTypeName", stored);
            string json = JsonUtility.ToJson(holder);
            Assert.Contains("\"m_TargetAssemblyTypeName\":\"" + tidy + "\"", json);
            Assert.Equal(tidy, Stored(Call(holder.onFire)));
        }

        [Fact]
        public void AnEmptyStoredNameIsFilledFromTheTargetWhenRead()
        {
            var holder = new Holder();
            JsonUtility.FromJsonOverwrite(Json("Placeholder, Lib"), holder);
            var call = Call(holder.onFire);
            var target = new GameObject("target");
            var targetName = call.GetType().GetProperty("targetAssemblyTypeName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            try
            {
                Store(call, "m_Target", target.transform);
                Assert.Equal("Placeholder, Lib", targetName.GetValue(call));
                Store(call, "m_TargetAssemblyTypeName", "");
                Assert.Equal("UnityEngine.Transform, UnityEngine", targetName.GetValue(call));
                Assert.Equal("UnityEngine.Transform, UnityEngine", Stored(call));
                Store(call, "m_Target", null);
                Store(call, "m_TargetAssemblyTypeName", null);
                Assert.Null(targetName.GetValue(call));
                Assert.Null(Stored(call));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
