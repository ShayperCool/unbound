using System;
using Unity.Collections;

namespace UnityEngine
{
    /// <summary>The JNI as the Editor on a desktop answers it: there is no Java VM, so every call
    /// returns the type's default (a null reference, a zero handle, a null array) and does nothing;
    /// only <see cref="GetStringUTFChars"/> answers an empty string. The members mirror the reference
    /// assembly's signatures one for one.</summary>
    public static unsafe partial class AndroidJNI
    {
        public static IntPtr GetJavaVM() { return default; }
        public static int AttachCurrentThread() { return default; }
        public static int DetachCurrentThread() { return default; }
        public static void InvokeAttached(Action action) { }
        public static int GetVersion() { return default; }
        public static IntPtr FindClass(string name) { return default; }
        public static IntPtr FromReflectedMethod(IntPtr refMethod) { return default; }
        public static IntPtr FromReflectedField(IntPtr refField) { return default; }
        public static IntPtr ToReflectedMethod(IntPtr clazz, IntPtr methodID, bool isStatic) { return default; }
        public static IntPtr ToReflectedField(IntPtr clazz, IntPtr fieldID, bool isStatic) { return default; }
        public static IntPtr GetSuperclass(IntPtr clazz) { return default; }
        public static bool IsAssignableFrom(IntPtr clazz1, IntPtr clazz2) { return default; }
        public static int Throw(IntPtr obj) { return default; }
        public static int ThrowNew(IntPtr clazz, string message) { return default; }
        public static IntPtr ExceptionOccurred() { return default; }
        public static void ExceptionDescribe() { }
        public static void ExceptionClear() { }
        public static void FatalError(string message) { }
        public static int PushLocalFrame(int capacity) { return default; }
        public static IntPtr PopLocalFrame(IntPtr ptr) { return default; }
        public static IntPtr NewGlobalRef(IntPtr obj) { return default; }
        public static void DeleteGlobalRef(IntPtr obj) { }
        internal static void QueueDeleteGlobalRef(IntPtr obj) { }
        internal static uint GetQueueGlobalRefsCount() { return default; }
        internal static void CleanQueueGlobalRefs() { }
        public static IntPtr NewWeakGlobalRef(IntPtr obj) { return default; }
        public static void DeleteWeakGlobalRef(IntPtr obj) { }
        public static IntPtr NewLocalRef(IntPtr obj) { return default; }
        public static void DeleteLocalRef(IntPtr obj) { }
        public static bool IsSameObject(IntPtr obj1, IntPtr obj2) { return default; }
        public static int EnsureLocalCapacity(int capacity) { return default; }
        public static IntPtr AllocObject(IntPtr clazz) { return default; }
        public static IntPtr NewObject(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static IntPtr NewObject(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { return default; }
        public static IntPtr NewObjectA(IntPtr clazz, IntPtr methodID, jvalue* args) { return default; }
        public static IntPtr GetObjectClass(IntPtr obj) { return default; }
        public static bool IsInstanceOf(IntPtr obj, IntPtr clazz) { return default; }
        public static IntPtr GetMethodID(IntPtr clazz, string name, string sig) { return default; }
        public static IntPtr GetFieldID(IntPtr clazz, string name, string sig) { return default; }
        public static IntPtr GetStaticMethodID(IntPtr clazz, string name, string sig) { return default; }
        public static IntPtr GetStaticFieldID(IntPtr clazz, string name, string sig) { return default; }
        public static IntPtr NewString(string chars) { return default; }
        public static IntPtr NewString(char[] chars) { return default; }
        public static IntPtr NewStringUTF(string bytes) { return default; }
        public static string GetStringChars(IntPtr str) { return default; }
        public static int GetStringLength(IntPtr str) { return default; }
        public static int GetStringUTFLength(IntPtr str) { return default; }
        public static string GetStringUTFChars(IntPtr str) { return ""; }
        public static string CallStringMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { return default; }
        public static string CallStringMethod(IntPtr obj, IntPtr methodID, Span<jvalue> args) { return default; }
        public static string CallStringMethodUnsafe(IntPtr obj, IntPtr methodID, jvalue* args) { return default; }
        public static IntPtr CallObjectMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { return default; }
        public static IntPtr CallObjectMethod(IntPtr obj, IntPtr methodID, Span<jvalue> args) { return default; }
        public static IntPtr CallObjectMethodUnsafe(IntPtr obj, IntPtr methodID, jvalue* args) { return default; }
        public static int CallIntMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { return default; }
        public static int CallIntMethod(IntPtr obj, IntPtr methodID, Span<jvalue> args) { return default; }
        public static int CallIntMethodUnsafe(IntPtr obj, IntPtr methodID, jvalue* args) { return default; }
        public static bool CallBooleanMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { return default; }
        public static bool CallBooleanMethod(IntPtr obj, IntPtr methodID, Span<jvalue> args) { return default; }
        public static bool CallBooleanMethodUnsafe(IntPtr obj, IntPtr methodID, jvalue* args) { return default; }
        public static short CallShortMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { return default; }
        public static short CallShortMethod(IntPtr obj, IntPtr methodID, Span<jvalue> args) { return default; }
        public static short CallShortMethodUnsafe(IntPtr obj, IntPtr methodID, jvalue* args) { return default; }
        [Obsolete("AndroidJNI.CallByteMethod is obsolete. Use AndroidJNI.CallSByteMethod method instead")] public static byte CallByteMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { return default; }
        public static sbyte CallSByteMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { return default; }
        public static sbyte CallSByteMethod(IntPtr obj, IntPtr methodID, Span<jvalue> args) { return default; }
        public static sbyte CallSByteMethodUnsafe(IntPtr obj, IntPtr methodID, jvalue* args) { return default; }
        public static char CallCharMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { return default; }
        public static char CallCharMethod(IntPtr obj, IntPtr methodID, Span<jvalue> args) { return default; }
        public static char CallCharMethodUnsafe(IntPtr obj, IntPtr methodID, jvalue* args) { return default; }
        public static float CallFloatMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { return default; }
        public static float CallFloatMethod(IntPtr obj, IntPtr methodID, Span<jvalue> args) { return default; }
        public static float CallFloatMethodUnsafe(IntPtr obj, IntPtr methodID, jvalue* args) { return default; }
        public static double CallDoubleMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { return default; }
        public static double CallDoubleMethod(IntPtr obj, IntPtr methodID, Span<jvalue> args) { return default; }
        public static double CallDoubleMethodUnsafe(IntPtr obj, IntPtr methodID, jvalue* args) { return default; }
        public static long CallLongMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { return default; }
        public static long CallLongMethod(IntPtr obj, IntPtr methodID, Span<jvalue> args) { return default; }
        public static long CallLongMethodUnsafe(IntPtr obj, IntPtr methodID, jvalue* args) { return default; }
        public static void CallVoidMethod(IntPtr obj, IntPtr methodID, jvalue[] args) { }
        public static void CallVoidMethod(IntPtr obj, IntPtr methodID, Span<jvalue> args) { }
        public static void CallVoidMethodUnsafe(IntPtr obj, IntPtr methodID, jvalue* args) { }
        public static string GetStringField(IntPtr obj, IntPtr fieldID) { return default; }
        public static IntPtr GetObjectField(IntPtr obj, IntPtr fieldID) { return default; }
        public static bool GetBooleanField(IntPtr obj, IntPtr fieldID) { return default; }
        [Obsolete("AndroidJNI.GetByteField is obsolete. Use AndroidJNI.GetSByteField method instead")] public static byte GetByteField(IntPtr obj, IntPtr fieldID) { return default; }
        public static sbyte GetSByteField(IntPtr obj, IntPtr fieldID) { return default; }
        public static char GetCharField(IntPtr obj, IntPtr fieldID) { return default; }
        public static short GetShortField(IntPtr obj, IntPtr fieldID) { return default; }
        public static int GetIntField(IntPtr obj, IntPtr fieldID) { return default; }
        public static long GetLongField(IntPtr obj, IntPtr fieldID) { return default; }
        public static float GetFloatField(IntPtr obj, IntPtr fieldID) { return default; }
        public static double GetDoubleField(IntPtr obj, IntPtr fieldID) { return default; }
        public static void SetStringField(IntPtr obj, IntPtr fieldID, string val) { }
        public static void SetObjectField(IntPtr obj, IntPtr fieldID, IntPtr val) { }
        public static void SetBooleanField(IntPtr obj, IntPtr fieldID, bool val) { }
        [Obsolete("AndroidJNI.SetByteField is obsolete. Use AndroidJNI.SetSByteField method instead")] public static void SetByteField(IntPtr obj, IntPtr fieldID, byte val) { }
        public static void SetSByteField(IntPtr obj, IntPtr fieldID, sbyte val) { }
        public static void SetCharField(IntPtr obj, IntPtr fieldID, char val) { }
        public static void SetShortField(IntPtr obj, IntPtr fieldID, short val) { }
        public static void SetIntField(IntPtr obj, IntPtr fieldID, int val) { }
        public static void SetLongField(IntPtr obj, IntPtr fieldID, long val) { }
        public static void SetFloatField(IntPtr obj, IntPtr fieldID, float val) { }
        public static void SetDoubleField(IntPtr obj, IntPtr fieldID, double val) { }
        public static string CallStaticStringMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static string CallStaticStringMethod(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { return default; }
        public static string CallStaticStringMethodUnsafe(IntPtr clazz, IntPtr methodID, jvalue* args) { return default; }
        public static IntPtr CallStaticObjectMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static IntPtr CallStaticObjectMethod(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { return default; }
        public static IntPtr CallStaticObjectMethodUnsafe(IntPtr clazz, IntPtr methodID, jvalue* args) { return default; }
        public static int CallStaticIntMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static int CallStaticIntMethod(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { return default; }
        public static int CallStaticIntMethodUnsafe(IntPtr clazz, IntPtr methodID, jvalue* args) { return default; }
        public static bool CallStaticBooleanMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static bool CallStaticBooleanMethod(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { return default; }
        public static bool CallStaticBooleanMethodUnsafe(IntPtr clazz, IntPtr methodID, jvalue* args) { return default; }
        public static short CallStaticShortMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static short CallStaticShortMethod(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { return default; }
        public static short CallStaticShortMethodUnsafe(IntPtr clazz, IntPtr methodID, jvalue* args) { return default; }
        [Obsolete("AndroidJNI.CallStaticByteMethod is obsolete. Use AndroidJNI.CallStaticSByteMethod method instead")] public static byte CallStaticByteMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static sbyte CallStaticSByteMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static sbyte CallStaticSByteMethod(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { return default; }
        public static sbyte CallStaticSByteMethodUnsafe(IntPtr clazz, IntPtr methodID, jvalue* args) { return default; }
        public static char CallStaticCharMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static char CallStaticCharMethod(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { return default; }
        public static char CallStaticCharMethodUnsafe(IntPtr clazz, IntPtr methodID, jvalue* args) { return default; }
        public static float CallStaticFloatMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static float CallStaticFloatMethod(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { return default; }
        public static float CallStaticFloatMethodUnsafe(IntPtr clazz, IntPtr methodID, jvalue* args) { return default; }
        public static double CallStaticDoubleMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static double CallStaticDoubleMethod(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { return default; }
        public static double CallStaticDoubleMethodUnsafe(IntPtr clazz, IntPtr methodID, jvalue* args) { return default; }
        public static long CallStaticLongMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { return default; }
        public static long CallStaticLongMethod(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { return default; }
        public static long CallStaticLongMethodUnsafe(IntPtr clazz, IntPtr methodID, jvalue* args) { return default; }
        public static void CallStaticVoidMethod(IntPtr clazz, IntPtr methodID, jvalue[] args) { }
        public static void CallStaticVoidMethod(IntPtr clazz, IntPtr methodID, Span<jvalue> args) { }
        public static void CallStaticVoidMethodUnsafe(IntPtr clazz, IntPtr methodID, jvalue* args) { }
        public static string GetStaticStringField(IntPtr clazz, IntPtr fieldID) { return default; }
        public static IntPtr GetStaticObjectField(IntPtr clazz, IntPtr fieldID) { return default; }
        public static bool GetStaticBooleanField(IntPtr clazz, IntPtr fieldID) { return default; }
        [Obsolete("AndroidJNI.GetStaticByteField is obsolete. Use AndroidJNI.GetStaticSByteField method instead")] public static byte GetStaticByteField(IntPtr clazz, IntPtr fieldID) { return default; }
        public static sbyte GetStaticSByteField(IntPtr clazz, IntPtr fieldID) { return default; }
        public static char GetStaticCharField(IntPtr clazz, IntPtr fieldID) { return default; }
        public static short GetStaticShortField(IntPtr clazz, IntPtr fieldID) { return default; }
        public static int GetStaticIntField(IntPtr clazz, IntPtr fieldID) { return default; }
        public static long GetStaticLongField(IntPtr clazz, IntPtr fieldID) { return default; }
        public static float GetStaticFloatField(IntPtr clazz, IntPtr fieldID) { return default; }
        public static double GetStaticDoubleField(IntPtr clazz, IntPtr fieldID) { return default; }
        public static void SetStaticStringField(IntPtr clazz, IntPtr fieldID, string val) { }
        public static void SetStaticObjectField(IntPtr clazz, IntPtr fieldID, IntPtr val) { }
        public static void SetStaticBooleanField(IntPtr clazz, IntPtr fieldID, bool val) { }
        [Obsolete("AndroidJNI.SetStaticByteField is obsolete. Use AndroidJNI.SetStaticSByteField method instead")] public static void SetStaticByteField(IntPtr clazz, IntPtr fieldID, byte val) { }
        public static void SetStaticSByteField(IntPtr clazz, IntPtr fieldID, sbyte val) { }
        public static void SetStaticCharField(IntPtr clazz, IntPtr fieldID, char val) { }
        public static void SetStaticShortField(IntPtr clazz, IntPtr fieldID, short val) { }
        public static void SetStaticIntField(IntPtr clazz, IntPtr fieldID, int val) { }
        public static void SetStaticLongField(IntPtr clazz, IntPtr fieldID, long val) { }
        public static void SetStaticFloatField(IntPtr clazz, IntPtr fieldID, float val) { }
        public static void SetStaticDoubleField(IntPtr clazz, IntPtr fieldID, double val) { }
        public static IntPtr ToBooleanArray(bool[] array) { return default; }
        [Obsolete("AndroidJNI.ToByteArray is obsolete. Use AndroidJNI.ToSByteArray method instead")] public static IntPtr ToByteArray(byte[] array) { return default; }
        public static IntPtr ToSByteArray(sbyte[] array) { return default; }
        public static IntPtr ToSByteArray(sbyte* array, int length) { return default; }
        public static IntPtr ToCharArray(char[] array) { return default; }
        public static IntPtr ToCharArray(char* array, int length) { return default; }
        public static IntPtr ToShortArray(short[] array) { return default; }
        public static IntPtr ToShortArray(short* array, int length) { return default; }
        public static IntPtr ToIntArray(int[] array) { return default; }
        public static IntPtr ToIntArray(int* array, int length) { return default; }
        public static IntPtr ToLongArray(long[] array) { return default; }
        public static IntPtr ToLongArray(long* array, int length) { return default; }
        public static IntPtr ToFloatArray(float[] array) { return default; }
        public static IntPtr ToFloatArray(float* array, int length) { return default; }
        public static IntPtr ToDoubleArray(double[] array) { return default; }
        public static IntPtr ToDoubleArray(double* array, int length) { return default; }
        public static IntPtr ToObjectArray(IntPtr* array, int length, IntPtr arrayClass) { return default; }
        public static IntPtr ToObjectArray(IntPtr[] array, IntPtr arrayClass) { return default; }
        public static IntPtr ToObjectArray(IntPtr[] array) { return default; }
        public static bool[] FromBooleanArray(IntPtr array) { return null; }
        [Obsolete("AndroidJNI.FromByteArray is obsolete. Use AndroidJNI.FromSByteArray method instead")] public static byte[] FromByteArray(IntPtr array) { return null; }
        public static sbyte[] FromSByteArray(IntPtr array) { return null; }
        public static char[] FromCharArray(IntPtr array) { return null; }
        public static short[] FromShortArray(IntPtr array) { return null; }
        public static int[] FromIntArray(IntPtr array) { return null; }
        public static long[] FromLongArray(IntPtr array) { return null; }
        public static float[] FromFloatArray(IntPtr array) { return null; }
        public static double[] FromDoubleArray(IntPtr array) { return null; }
        public static IntPtr[] FromObjectArray(IntPtr array) { return null; }
        public static int GetArrayLength(IntPtr array) { return default; }
        public static IntPtr NewBooleanArray(int size) { return default; }
        [Obsolete("AndroidJNI.NewByteArray is obsolete. Use AndroidJNI.NewSByteArray method instead")] public static IntPtr NewByteArray(int size) { return default; }
        public static IntPtr NewSByteArray(int size) { return default; }
        public static IntPtr NewCharArray(int size) { return default; }
        public static IntPtr NewShortArray(int size) { return default; }
        public static IntPtr NewIntArray(int size) { return default; }
        public static IntPtr NewLongArray(int size) { return default; }
        public static IntPtr NewFloatArray(int size) { return default; }
        public static IntPtr NewDoubleArray(int size) { return default; }
        public static IntPtr NewObjectArray(int size, IntPtr clazz, IntPtr obj) { return default; }
        public static bool GetBooleanArrayElement(IntPtr array, int index) { return default; }
        [Obsolete("AndroidJNI.GetByteArrayElement is obsolete. Use AndroidJNI.GetSByteArrayElement method instead")] public static byte GetByteArrayElement(IntPtr array, int index) { return default; }
        public static sbyte GetSByteArrayElement(IntPtr array, int index) { return default; }
        public static char GetCharArrayElement(IntPtr array, int index) { return default; }
        public static short GetShortArrayElement(IntPtr array, int index) { return default; }
        public static int GetIntArrayElement(IntPtr array, int index) { return default; }
        public static long GetLongArrayElement(IntPtr array, int index) { return default; }
        public static float GetFloatArrayElement(IntPtr array, int index) { return default; }
        public static double GetDoubleArrayElement(IntPtr array, int index) { return default; }
        public static IntPtr GetObjectArrayElement(IntPtr array, int index) { return default; }
        [Obsolete("AndroidJNI.SetBooleanArrayElement(IntPtr, int, byte) is obsolete. Use AndroidJNI.SetBooleanArrayElement(IntPtr, int, bool) method instead")] public static void SetBooleanArrayElement(IntPtr array, int index, byte val) { }
        public static void SetBooleanArrayElement(IntPtr array, int index, bool val) { }
        [Obsolete("AndroidJNI.SetByteArrayElement is obsolete. Use AndroidJNI.SetSByteArrayElement method instead")] public static void SetByteArrayElement(IntPtr array, int index, sbyte val) { }
        public static void SetSByteArrayElement(IntPtr array, int index, sbyte val) { }
        public static void SetCharArrayElement(IntPtr array, int index, char val) { }
        public static void SetShortArrayElement(IntPtr array, int index, short val) { }
        public static void SetIntArrayElement(IntPtr array, int index, int val) { }
        public static void SetLongArrayElement(IntPtr array, int index, long val) { }
        public static void SetFloatArrayElement(IntPtr array, int index, float val) { }
        public static void SetDoubleArrayElement(IntPtr array, int index, double val) { }
        public static void SetObjectArrayElement(IntPtr array, int index, IntPtr obj) { }
        public static IntPtr NewDirectByteBuffer(byte* buffer, long capacity) { return default; }
        public static IntPtr NewDirectByteBuffer(NativeArray<byte> buffer) { return default; }
        public static IntPtr NewDirectByteBuffer(NativeArray<sbyte> buffer) { return default; }
        public static sbyte* GetDirectBufferAddress(IntPtr buffer) { return default; }
        public static long GetDirectBufferCapacity(IntPtr buffer) { return default; }
        public static NativeArray<byte> GetDirectByteBuffer(IntPtr buffer) { return default; }
        public static NativeArray<sbyte> GetDirectSByteBuffer(IntPtr buffer) { return default; }
        public static int RegisterNatives(IntPtr clazz, JNINativeMethod[] methods) { return default; }
        public static int UnregisterNatives(IntPtr clazz) { return default; }
    }
}

namespace UnityEngine
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    public struct jvalue
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public bool z;
        [System.Runtime.InteropServices.FieldOffset(0)] public sbyte b;
        [System.Runtime.InteropServices.FieldOffset(0)] public char c;
        [System.Runtime.InteropServices.FieldOffset(0)] public short s;
        [System.Runtime.InteropServices.FieldOffset(0)] public int i;
        [System.Runtime.InteropServices.FieldOffset(0)] public long j;
        [System.Runtime.InteropServices.FieldOffset(0)] public float f;
        [System.Runtime.InteropServices.FieldOffset(0)] public double d;
        [System.Runtime.InteropServices.FieldOffset(0)] public IntPtr l;
    }

    public struct JNINativeMethod
    {
        public string name;
        public string signature;
        public IntPtr fnPtr;
    }

    public delegate void AndroidJavaRunnable();
}
