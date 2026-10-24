using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace UnityEngine.Assertions
{
    /// <summary>Assertions: a failure is logged as an assert (or thrown when raiseExceptions).</summary>
    [DebuggerStepThrough]
    public static partial class Assert
    {
        internal const string UNITY_ASSERTIONS = "UNITY_ASSERTIONS";

        [Obsolete("Future versions of Unity are expected to always throw exceptions and not have this field.")]
        public static bool raiseExceptions = true;

        static void Fail(string message, string userMessage)
        {
            var text = string.IsNullOrEmpty(userMessage) ? message : userMessage + "\n" + message;
#pragma warning disable CS0618
            if (raiseExceptions) throw new AssertionException(text, userMessage);
#pragma warning restore CS0618
            Debug.LogAssertion(text);
        }

        [Conditional(UNITY_ASSERTIONS)] public static void IsTrue(bool condition) { if (!condition) Fail("Value was False", null); }
        [Conditional(UNITY_ASSERTIONS)] public static void IsTrue(bool condition, string message) { if (!condition) Fail("Value was False", message); }
        [Conditional(UNITY_ASSERTIONS)] public static void IsFalse(bool condition) { if (condition) Fail("Value was True", null); }
        [Conditional(UNITY_ASSERTIONS)] public static void IsFalse(bool condition, string message) { if (condition) Fail("Value was True", message); }

        [Conditional(UNITY_ASSERTIONS)] public static void AreApproximatelyEqual(float expected, float actual) => AreApproximatelyEqual(expected, actual, null, 1E-05f);
        [Conditional(UNITY_ASSERTIONS)] public static void AreApproximatelyEqual(float expected, float actual, string message) => AreApproximatelyEqual(expected, actual, message, 1E-05f);
        [Conditional(UNITY_ASSERTIONS)] public static void AreApproximatelyEqual(float expected, float actual, float tolerance) => AreApproximatelyEqual(expected, actual, null, tolerance);
        [Conditional(UNITY_ASSERTIONS)]
        public static void AreApproximatelyEqual(float expected, float actual, float tolerance, string message) => AreApproximatelyEqual(expected, actual, message, tolerance);

        static void AreApproximatelyEqual(float expected, float actual, string message, float tolerance)
        {
            if (Math.Abs(actual - expected) > tolerance) Fail($"Values are not close enough.\nExpected: {expected}\nActual: {actual}", message);
        }

        [Conditional(UNITY_ASSERTIONS)] public static void AreNotApproximatelyEqual(float expected, float actual) { if (Math.Abs(actual - expected) <= 1E-05f) Fail("Values are close.", null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotApproximatelyEqual(float expected, float actual, string message) { if (Math.Abs(actual - expected) <= 1E-05f) Fail("Values are close.", message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotApproximatelyEqual(float expected, float actual, float tolerance) { if (Math.Abs(actual - expected) <= tolerance) Fail("Values are close.", null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotApproximatelyEqual(float expected, float actual, float tolerance, string message) { if (Math.Abs(actual - expected) <= tolerance) Fail("Values are close.", message); }

        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual<T>(T expected, T actual) => AreEqual(expected, actual, null, EqualityComparer<T>.Default);
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual<T>(T expected, T actual, string message) => AreEqual(expected, actual, message, EqualityComparer<T>.Default);

        [Conditional(UNITY_ASSERTIONS)]
        public static void AreEqual<T>(T expected, T actual, string message, IEqualityComparer<T> comparer)
        {
            if (typeof(Object).IsAssignableFrom(typeof(T)))
            {
                AreEqual(expected as Object, actual as Object, message);
                return;
            }
            if (!comparer.Equals(actual, expected)) Fail($"Values are not equal.\nExpected: {expected}\nActual: {actual}", message);
        }

        [Conditional(UNITY_ASSERTIONS)]
        public static void AreEqual(Object expected, Object actual, string message)
        {
            if (actual != expected) Fail($"Values are not equal.\nExpected: {expected}\nActual: {actual}", message);
        }

        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual<T>(T expected, T actual) => AreNotEqual(expected, actual, null, EqualityComparer<T>.Default);
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual<T>(T expected, T actual, string message) => AreNotEqual(expected, actual, message, EqualityComparer<T>.Default);

        [Conditional(UNITY_ASSERTIONS)]
        public static void AreNotEqual<T>(T expected, T actual, string message, IEqualityComparer<T> comparer)
        {
            if (typeof(Object).IsAssignableFrom(typeof(T)))
            {
                AreNotEqual(expected as Object, actual as Object, message);
                return;
            }
            if (comparer.Equals(actual, expected)) Fail($"Values are equal.\nExpected: {expected} != {actual}", message);
        }

        [Conditional(UNITY_ASSERTIONS)]
        public static void AreNotEqual(Object expected, Object actual, string message)
        {
            if (actual == expected) Fail($"Values are equal.\nExpected: {expected} != {actual}", message);
        }

        [Conditional(UNITY_ASSERTIONS)] public static void IsNull<T>(T value) where T : class => IsNull(value, null);

        [Conditional(UNITY_ASSERTIONS)]
        public static void IsNull<T>(T value, string message) where T : class
        {
            if (typeof(Object).IsAssignableFrom(typeof(T))) IsNull(value as Object, message);
            else if (value != null) Fail("Value was not Null", message);
        }

        [Conditional(UNITY_ASSERTIONS)]
        public static void IsNull(Object value, string message)
        {
            if (value != null) Fail("Value was not Null", message);
        }

        [Conditional(UNITY_ASSERTIONS)] public static void IsNotNull<T>(T value) where T : class => IsNotNull(value, null);

        [Conditional(UNITY_ASSERTIONS)]
        public static void IsNotNull<T>(T value, string message) where T : class
        {
            if (typeof(Object).IsAssignableFrom(typeof(T))) IsNotNull(value as Object, message);
            else if (value == null) Fail("Value was Null", message);
        }

        [Conditional(UNITY_ASSERTIONS)]
        public static void IsNotNull(Object value, string message)
        {
            if (value == null) Fail("Value was Null", message);
        }

        // The primitive overloads (Unity's AssertPrimitiveTypes): compare inline, report as the generic ones.
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(sbyte expected, sbyte actual) { if (expected != actual) AreEqual<sbyte>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(sbyte expected, sbyte actual, string message) { if (expected != actual) AreEqual<sbyte>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(sbyte expected, sbyte actual) { if (expected == actual) AreNotEqual<sbyte>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(sbyte expected, sbyte actual, string message) { if (expected == actual) AreNotEqual<sbyte>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(byte expected, byte actual) { if (expected != actual) AreEqual<byte>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(byte expected, byte actual, string message) { if (expected != actual) AreEqual<byte>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(byte expected, byte actual) { if (expected == actual) AreNotEqual<byte>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(byte expected, byte actual, string message) { if (expected == actual) AreNotEqual<byte>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(char expected, char actual) { if (expected != actual) AreEqual<char>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(char expected, char actual, string message) { if (expected != actual) AreEqual<char>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(char expected, char actual) { if (expected == actual) AreNotEqual<char>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(char expected, char actual, string message) { if (expected == actual) AreNotEqual<char>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(short expected, short actual) { if (expected != actual) AreEqual<short>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(short expected, short actual, string message) { if (expected != actual) AreEqual<short>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(short expected, short actual) { if (expected == actual) AreNotEqual<short>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(short expected, short actual, string message) { if (expected == actual) AreNotEqual<short>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(ushort expected, ushort actual) { if (expected != actual) AreEqual<ushort>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(ushort expected, ushort actual, string message) { if (expected != actual) AreEqual<ushort>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(ushort expected, ushort actual) { if (expected == actual) AreNotEqual<ushort>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(ushort expected, ushort actual, string message) { if (expected == actual) AreNotEqual<ushort>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(int expected, int actual) { if (expected != actual) AreEqual<int>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(int expected, int actual, string message) { if (expected != actual) AreEqual<int>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(int expected, int actual) { if (expected == actual) AreNotEqual<int>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(int expected, int actual, string message) { if (expected == actual) AreNotEqual<int>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(uint expected, uint actual) { if (expected != actual) AreEqual<uint>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(uint expected, uint actual, string message) { if (expected != actual) AreEqual<uint>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(uint expected, uint actual) { if (expected == actual) AreNotEqual<uint>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(uint expected, uint actual, string message) { if (expected == actual) AreNotEqual<uint>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(long expected, long actual) { if (expected != actual) AreEqual<long>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(long expected, long actual, string message) { if (expected != actual) AreEqual<long>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(long expected, long actual) { if (expected == actual) AreNotEqual<long>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(long expected, long actual, string message) { if (expected == actual) AreNotEqual<long>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(ulong expected, ulong actual) { if (expected != actual) AreEqual<ulong>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreEqual(ulong expected, ulong actual, string message) { if (expected != actual) AreEqual<ulong>(expected, actual, message); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(ulong expected, ulong actual) { if (expected == actual) AreNotEqual<ulong>(expected, actual, null); }
        [Conditional(UNITY_ASSERTIONS)] public static void AreNotEqual(ulong expected, ulong actual, string message) { if (expected == actual) AreNotEqual<ulong>(expected, actual, message); }
    }

    public partial class AssertionException : Exception
    {
        readonly string m_UserMessage;

        public AssertionException(string message, string userMessage) : base(message)
        {
            m_UserMessage = userMessage;
        }

        public override string Message => m_UserMessage != null ? base.Message + '\n' + m_UserMessage : base.Message;
    }
}
