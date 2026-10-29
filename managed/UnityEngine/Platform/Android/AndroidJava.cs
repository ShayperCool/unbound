// The Java wrappers (AndroidJavaObject, AndroidJavaClass, AndroidJavaProxy) and AndroidJNIHelper,
// layered on AndroidJNI the way Unity layers them. In the Editor on a desktop the JNI answers
// nothing (see AndroidJNI.cs), so the wrappers behave as the Editor's do: objects construct around
// null handles, calls return their type's default, field lookups fail with Unity's message.
using System;
using System.Reflection;
using System.Text;

namespace UnityEngine
{
    internal sealed partial class GlobalJavaObjectRef
    {
        bool m_Disposed;
        protected IntPtr m_jobject;

        public GlobalJavaObjectRef(IntPtr jobject)
        {
            m_jobject = jobject == IntPtr.Zero ? IntPtr.Zero : AndroidJNI.NewGlobalRef(jobject);
        }

        ~GlobalJavaObjectRef() => Dispose();

        public static implicit operator IntPtr(GlobalJavaObjectRef obj) => obj.m_jobject;

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            if (m_jobject != IntPtr.Zero) AndroidJNI.DeleteGlobalRef(m_jobject);
        }
    }

    public partial class AndroidJavaObject : IDisposable
    {
        static readonly object[] s_NoArgs = new object[0];
        bool m_Disposed;
        internal GlobalJavaObjectRef m_jobject;
        internal GlobalJavaObjectRef m_jclass;

        public AndroidJavaObject(string className, string[] args) : this() => _AndroidJavaObject(className, (object[])args);
        public AndroidJavaObject(string className, AndroidJavaObject[] args) : this() => _AndroidJavaObject(className, (object[])args);
        public AndroidJavaObject(string className, AndroidJavaClass[] args) : this() => _AndroidJavaObject(className, (object[])args);
        public AndroidJavaObject(string className, AndroidJavaProxy[] args) : this() => _AndroidJavaObject(className, (object[])args);
        public AndroidJavaObject(string className, AndroidJavaRunnable[] args) : this() => _AndroidJavaObject(className, (object[])args);
        public AndroidJavaObject(string className, params object[] args) : this() => _AndroidJavaObject(className, args);

        public AndroidJavaObject(IntPtr jobject) : this()
        {
            if (jobject == IntPtr.Zero) throw new Exception("JNI: Init'd AndroidJavaObject with null ptr!");
            IntPtr jclass = AndroidJNISafe.GetObjectClass(jobject);
            m_jobject = new GlobalJavaObjectRef(jobject);
            m_jclass = new GlobalJavaObjectRef(jclass);
            AndroidJNISafe.DeleteLocalRef(jclass);
        }

        public AndroidJavaObject(IntPtr clazz, IntPtr constructorID, params object[] args) : this()
        {
            if (clazz == IntPtr.Zero) throw new Exception("JNI: Init'd AndroidJavaObject with null ptr!");
            m_jclass = new GlobalJavaObjectRef(clazz);
            jvalue[] jniArgs = AndroidJNIHelper.CreateJNIArgArray(args ?? s_NoArgs);
            try
            {
                IntPtr jobject = AndroidJNISafe.NewObject(clazz, constructorID, jniArgs);
                m_jobject = new GlobalJavaObjectRef(jobject);
                AndroidJNISafe.DeleteLocalRef(jobject);
            }
            finally { AndroidJNIHelper.DeleteJNIArgArray(args ?? s_NoArgs, jniArgs); }
        }

        internal AndroidJavaObject() { }

        ~AndroidJavaObject() => Dispose(true);

        void _AndroidJavaObject(string className, params object[] args)
        {
            DebugPrint("Creating AndroidJavaObject from " + className);
            args ??= s_NoArgs;
            IntPtr jclass = AndroidJNISafe.FindClass(className.Replace('.', '/'));
            m_jclass = new GlobalJavaObjectRef(jclass);
            AndroidJNISafe.DeleteLocalRef(jclass);
            jvalue[] jniArgs = AndroidJNIHelper.CreateJNIArgArray(args);
            try
            {
                IntPtr constructorID = AndroidJNIHelper.GetConstructorID(m_jclass, args);
                IntPtr jobject = AndroidJNISafe.NewObject(m_jclass, constructorID, jniArgs);
                m_jobject = new GlobalJavaObjectRef(jobject);
                AndroidJNISafe.DeleteLocalRef(jobject);
            }
            finally { AndroidJNIHelper.DeleteJNIArgArray(args, jniArgs); }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_jobject?.Dispose();
            m_jobject = null;
            m_jclass?.Dispose();
            m_jclass = null;
        }

        public void Call<T>(string methodName, T[] args) => _Call(methodName, new object[] { args });
        public void Call<T>(IntPtr methodID, T[] args) => _Call(methodID, new object[] { args });
        public void Call(string methodName, params object[] args) => _Call(methodName, args);
        public void Call(IntPtr methodID, params object[] args) => _Call(methodID, args);
        public void CallStatic<T>(string methodName, T[] args) => _CallStatic(methodName, new object[] { args });
        public void CallStatic<T>(IntPtr methodID, T[] args) => _CallStatic(methodID, new object[] { args });
        public void CallStatic(string methodName, params object[] args) => _CallStatic(methodName, args);
        public void CallStatic(IntPtr methodID, params object[] args) => _CallStatic(methodID, args);
        public FieldType Get<FieldType>(string fieldName) => _Get<FieldType>(fieldName);
        public FieldType Get<FieldType>(IntPtr fieldID) => _Get<FieldType>(fieldID);
        public void Set<FieldType>(string fieldName, FieldType val) => _Set(fieldName, val);
        public void Set<FieldType>(IntPtr fieldID, FieldType val) => _Set(fieldID, val);
        public FieldType GetStatic<FieldType>(string fieldName) => _GetStatic<FieldType>(fieldName);
        public FieldType GetStatic<FieldType>(IntPtr fieldID) => _GetStatic<FieldType>(fieldID);
        public void SetStatic<FieldType>(string fieldName, FieldType val) => _SetStatic(fieldName, val);
        public void SetStatic<FieldType>(IntPtr fieldID, FieldType val) => _SetStatic(fieldID, val);
        public IntPtr GetRawObject() => _GetRawObject();
        public IntPtr GetRawClass() => _GetRawClass();

        public AndroidJavaObject CloneReference()
        {
            if (m_jclass == null) throw new Exception("Cannot clone a disposed reference");
            var clone = new AndroidJavaObject { m_jclass = new GlobalJavaObjectRef(m_jclass) };
            if (m_jobject != null) clone.m_jobject = new GlobalJavaObjectRef(m_jobject);
            return clone;
        }

        public ReturnType Call<ReturnType, T>(string methodName, T[] args) => _Call<ReturnType>(methodName, new object[] { args });
        public ReturnType Call<ReturnType, T>(IntPtr methodID, T[] args) => _Call<ReturnType>(methodID, new object[] { args });
        public ReturnType Call<ReturnType>(string methodName, params object[] args) => _Call<ReturnType>(methodName, args);
        public ReturnType Call<ReturnType>(IntPtr methodID, params object[] args) => _Call<ReturnType>(methodID, args);
        public ReturnType CallStatic<ReturnType, T>(string methodName, T[] args) => _CallStatic<ReturnType>(methodName, new object[] { args });
        public ReturnType CallStatic<ReturnType, T>(IntPtr methodID, T[] args) => _CallStatic<ReturnType>(methodID, new object[] { args });
        public ReturnType CallStatic<ReturnType>(string methodName, params object[] args) => _CallStatic<ReturnType>(methodName, args);
        public ReturnType CallStatic<ReturnType>(IntPtr methodID, params object[] args) => _CallStatic<ReturnType>(methodID, args);

        /// <summary>Unity's trace of JNI calls when <see cref="AndroidJNIHelper.debug"/> is on; the
        /// Editor prints nothing.</summary>
        [Unbound.EditorFaithful("the Editor prints no JNI trace")]
        protected void DebugPrint(string msg) { }

        [Unbound.EditorFaithful("the Editor prints no JNI trace")]
        protected void DebugPrint(string call, string methodName, string signature, object[] args) { }

        protected void _Call(string methodName, params object[] args)
        {
            args ??= s_NoArgs;
            IntPtr methodID = AndroidJNIHelper.GetMethodID(m_jclass, methodName, args, false);
            _Call(methodID, args);
        }

        protected void _Call(IntPtr methodID, params object[] args)
        {
            args ??= s_NoArgs;
            jvalue[] jniArgs = AndroidJNIHelper.CreateJNIArgArray(args);
            try { AndroidJNISafe.CallVoidMethod(m_jobject, methodID, jniArgs); }
            finally { AndroidJNIHelper.DeleteJNIArgArray(args, jniArgs); }
        }

        protected ReturnType _Call<ReturnType>(string methodName, params object[] args)
        {
            args ??= s_NoArgs;
            IntPtr methodID = AndroidJNIHelper.GetMethodID<ReturnType>(m_jclass, methodName, args, false);
            return _Call<ReturnType>(methodID, args);
        }

        protected ReturnType _Call<ReturnType>(IntPtr methodID, params object[] args)
        {
            args ??= s_NoArgs;
            jvalue[] jniArgs = AndroidJNIHelper.CreateJNIArgArray(args);
            try { return JavaCall.Invoke<ReturnType>(m_jobject, methodID, jniArgs, false); }
            finally { AndroidJNIHelper.DeleteJNIArgArray(args, jniArgs); }
        }

        protected FieldType _Get<FieldType>(string fieldName) =>
            _Get<FieldType>(AndroidJNIHelper.GetFieldID<FieldType>(m_jclass, fieldName, false));

        protected FieldType _Get<FieldType>(IntPtr fieldID) => JavaCall.GetField<FieldType>(m_jobject, fieldID, false);

        protected void _Set<FieldType>(string fieldName, FieldType val) =>
            _Set(AndroidJNIHelper.GetFieldID<FieldType>(m_jclass, fieldName, false), val);

        protected void _Set<FieldType>(IntPtr fieldID, FieldType val) => JavaCall.SetField(m_jobject, fieldID, val, false);

        protected void _CallStatic(string methodName, params object[] args)
        {
            args ??= s_NoArgs;
            IntPtr methodID = AndroidJNIHelper.GetMethodID(m_jclass, methodName, args, true);
            _CallStatic(methodID, args);
        }

        protected void _CallStatic(IntPtr methodID, params object[] args)
        {
            args ??= s_NoArgs;
            jvalue[] jniArgs = AndroidJNIHelper.CreateJNIArgArray(args);
            try { AndroidJNISafe.CallStaticVoidMethod(m_jclass, methodID, jniArgs); }
            finally { AndroidJNIHelper.DeleteJNIArgArray(args, jniArgs); }
        }

        protected ReturnType _CallStatic<ReturnType>(string methodName, params object[] args)
        {
            args ??= s_NoArgs;
            IntPtr methodID = AndroidJNIHelper.GetMethodID<ReturnType>(m_jclass, methodName, args, true);
            return _CallStatic<ReturnType>(methodID, args);
        }

        protected ReturnType _CallStatic<ReturnType>(IntPtr methodID, params object[] args)
        {
            args ??= s_NoArgs;
            jvalue[] jniArgs = AndroidJNIHelper.CreateJNIArgArray(args);
            try { return JavaCall.Invoke<ReturnType>(m_jclass, methodID, jniArgs, true); }
            finally { AndroidJNIHelper.DeleteJNIArgArray(args, jniArgs); }
        }

        protected FieldType _GetStatic<FieldType>(string fieldName) =>
            _GetStatic<FieldType>(AndroidJNIHelper.GetFieldID<FieldType>(m_jclass, fieldName, true));

        protected FieldType _GetStatic<FieldType>(IntPtr fieldID) => JavaCall.GetField<FieldType>(m_jclass, fieldID, true);

        protected void _SetStatic<FieldType>(string fieldName, FieldType val) =>
            _SetStatic(AndroidJNIHelper.GetFieldID<FieldType>(m_jclass, fieldName, true), val);

        protected void _SetStatic<FieldType>(IntPtr fieldID, FieldType val) => JavaCall.SetField(m_jclass, fieldID, val, true);

        internal static AndroidJavaObject AndroidJavaObjectDeleteLocalRef(IntPtr jobject)
        {
            try { return new AndroidJavaObject(jobject); }
            finally { AndroidJNISafe.DeleteLocalRef(jobject); }
        }

        internal static AndroidJavaClass AndroidJavaClassDeleteLocalRef(IntPtr jclass)
        {
            try { return new AndroidJavaClass(jclass); }
            finally { AndroidJNISafe.DeleteLocalRef(jclass); }
        }

        internal static ReturnType FromJavaArrayDeleteLocalRef<ReturnType>(IntPtr jobject)
        {
            try { return FromJavaArray<ReturnType>(jobject); }
            finally { AndroidJNISafe.DeleteLocalRef(jobject); }
        }

        internal static ReturnType FromJavaArray<ReturnType>(IntPtr jobject) =>
            jobject == IntPtr.Zero ? default : AndroidJNIHelper.ConvertFromJNIArray<ReturnType>(jobject);

        protected IntPtr _GetRawObject() => m_jobject;
        protected IntPtr _GetRawClass() => m_jclass;
    }

    public partial class AndroidJavaClass : AndroidJavaObject
    {
        public AndroidJavaClass(string className)
        {
            DebugPrint("Creating AndroidJavaClass from " + className);
            IntPtr jclass = AndroidJNISafe.FindClass(className.Replace('.', '/'));
            m_jclass = new GlobalJavaObjectRef(jclass);
            m_jobject = new GlobalJavaObjectRef(IntPtr.Zero);
            AndroidJNISafe.DeleteLocalRef(jclass);
        }

        internal AndroidJavaClass(IntPtr jclass)
        {
            if (jclass == IntPtr.Zero) throw new Exception("JNI: Init'd AndroidJavaClass with null ptr!");
            m_jclass = new GlobalJavaObjectRef(jclass);
            m_jobject = new GlobalJavaObjectRef(IntPtr.Zero);
        }
    }

    public sealed partial class AndroidJavaException : Exception
    {
        readonly string m_JavaStackTrace;

        internal AndroidJavaException(string message, string javaStackTrace) : base(message) => m_JavaStackTrace = javaStackTrace;

        public override string StackTrace => m_JavaStackTrace + base.StackTrace;
    }

    public partial class AndroidJavaProxy
    {
        public readonly AndroidJavaClass javaInterface;
        internal IntPtr proxyObject;

        public AndroidJavaProxy(string javaInterface) : this(new AndroidJavaClass(javaInterface)) { }

        public AndroidJavaProxy(AndroidJavaClass javaInterface) => this.javaInterface = javaInterface;

        ~AndroidJavaProxy()
        {
            if (proxyObject != IntPtr.Zero) AndroidJNI.DeleteWeakGlobalRef(proxyObject);
        }

        /// <summary>Runs the C# method of this name whose parameters fit the arguments; a missing
        /// method answers null (Unity reports it to the Java caller as an exception).</summary>
        public virtual AndroidJavaObject Invoke(string methodName, object[] args)
        {
            Exception error = null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var argTypes = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) argTypes[i] = args[i] == null ? typeof(AndroidJavaObject) : args[i].GetType();
            try
            {
                MethodInfo method = GetType().GetMethod(methodName, flags, null, argTypes, null);
                if (method != null) return JavaCall.Box(method.Invoke(this, args));
            }
            catch (TargetInvocationException invocationError) { error = invocationError.InnerException; }
            catch (Exception invocationError) { error = invocationError; }
            var names = new string[args.Length];
            for (int i = 0; i < argTypes.Length; i++) names[i] = argTypes[i].ToString();
            if (error != null)
                throw new TargetInvocationException(GetType() + "." + methodName + "(" + string.Join(",", names) + ")", error);
            return null;
        }

        public virtual AndroidJavaObject Invoke(string methodName, AndroidJavaObject[] javaArgs)
        {
            var args = new object[javaArgs.Length];
            for (int i = 0; i < javaArgs.Length; i++) args[i] = JavaCall.Unbox(javaArgs[i]);
            return Invoke(methodName, args);
        }

        public virtual IntPtr Invoke(string methodName, IntPtr javaArgs)
        {
            int length = AndroidJNI.GetArrayLength(javaArgs);
            var args = new AndroidJavaObject[length];
            for (int i = 0; i < length; i++)
            {
                IntPtr element = AndroidJNI.GetObjectArrayElement(javaArgs, i);
                args[i] = element == IntPtr.Zero ? null : new AndroidJavaObject(element);
            }
            using (AndroidJavaObject result = Invoke(methodName, args))
                return result == null ? IntPtr.Zero : AndroidJNI.NewLocalRef(result.GetRawObject());
        }

        public virtual bool equals(AndroidJavaObject obj) =>
            AndroidJNI.IsSameObject(proxyObject, obj == null ? IntPtr.Zero : obj.GetRawObject());

        public virtual int hashCode() => AndroidJNI.CallStaticIntMethod(IntPtr.Zero, IntPtr.Zero, new jvalue[1]);

        public virtual string toString() => ToString() + " <c# proxy java object>";

        internal AndroidJavaObject GetProxyObject() => new AndroidJavaObject(GetRawProxy());

        internal IntPtr GetRawProxy()
        {
            if (proxyObject == IntPtr.Zero) proxyObject = AndroidJNIHelper.CreateJavaProxy(this);
            return proxyObject;
        }
    }

    internal partial class AndroidJavaRunnableProxy : AndroidJavaProxy
    {
        readonly AndroidJavaRunnable m_Runnable;

        public AndroidJavaRunnableProxy(AndroidJavaRunnable runnable) : base("java/lang/Runnable") => m_Runnable = runnable;

        public void run() => m_Runnable();

        public override IntPtr Invoke(string methodName, IntPtr javaArgs)
        {
            if (methodName == "run") { run(); return IntPtr.Zero; }
            return base.Invoke(methodName, javaArgs);
        }
    }

    /// <summary>AndroidJNI calls that turn a pending Java exception into a C# one.</summary>
    internal partial class AndroidJNISafe
    {
        public static void CheckException()
        {
            IntPtr jthrowable = AndroidJNI.ExceptionOccurred();
            if (jthrowable == IntPtr.Zero) return;
            AndroidJNI.ExceptionClear();
            AndroidJNI.DeleteLocalRef(jthrowable);
            throw new AndroidJavaException("java.lang.Throwable", "");
        }

        public static void DeleteLocalRef(IntPtr localref)
        {
            if (localref != IntPtr.Zero) AndroidJNI.DeleteLocalRef(localref);
        }

        public static IntPtr FindClass(string name)
        {
            try { return AndroidJNI.FindClass(name); }
            finally { CheckException(); }
        }

        public static IntPtr GetObjectClass(IntPtr ptr)
        {
            try { return AndroidJNI.GetObjectClass(ptr); }
            finally { CheckException(); }
        }

        public static IntPtr NewObject(IntPtr clazz, IntPtr methodID, jvalue[] args)
        {
            try { return AndroidJNI.NewObject(clazz, methodID, args); }
            finally { CheckException(); }
        }

        public static void CallVoidMethod(IntPtr obj, IntPtr methodID, jvalue[] args)
        {
            try { AndroidJNI.CallVoidMethod(obj, methodID, args); }
            finally { CheckException(); }
        }

        public static void CallStaticVoidMethod(IntPtr clazz, IntPtr methodID, jvalue[] args)
        {
            try { AndroidJNI.CallStaticVoidMethod(clazz, methodID, args); }
            finally { CheckException(); }
        }
    }

    /// <summary>The typed JNI calls behind <c>Call&lt;T&gt;</c>, <c>Get&lt;T&gt;</c> and
    /// <c>Set&lt;T&gt;</c>: primitives, strings, Java objects and classes, and arrays.</summary>
    static class JavaCall
    {
        static Exception Unsupported(Type t) => new Exception("JNI: Unknown generic argument type '" + t + "'");

        static ReturnType Checked<ReturnType>(ReturnType value)
        {
            AndroidJNISafe.CheckException();
            return value;
        }

        public static ReturnType Invoke<ReturnType>(IntPtr target, IntPtr methodID, jvalue[] args, bool isStatic)
        {
            Type t = typeof(ReturnType);
            object result;
            if (t == typeof(int)) result = isStatic ? AndroidJNI.CallStaticIntMethod(target, methodID, args) : AndroidJNI.CallIntMethod(target, methodID, args);
            else if (t == typeof(bool)) result = isStatic ? AndroidJNI.CallStaticBooleanMethod(target, methodID, args) : AndroidJNI.CallBooleanMethod(target, methodID, args);
            else if (t == typeof(sbyte)) result = isStatic ? AndroidJNI.CallStaticSByteMethod(target, methodID, args) : AndroidJNI.CallSByteMethod(target, methodID, args);
            else if (t == typeof(short)) result = isStatic ? AndroidJNI.CallStaticShortMethod(target, methodID, args) : AndroidJNI.CallShortMethod(target, methodID, args);
            else if (t == typeof(long)) result = isStatic ? AndroidJNI.CallStaticLongMethod(target, methodID, args) : AndroidJNI.CallLongMethod(target, methodID, args);
            else if (t == typeof(float)) result = isStatic ? AndroidJNI.CallStaticFloatMethod(target, methodID, args) : AndroidJNI.CallFloatMethod(target, methodID, args);
            else if (t == typeof(double)) result = isStatic ? AndroidJNI.CallStaticDoubleMethod(target, methodID, args) : AndroidJNI.CallDoubleMethod(target, methodID, args);
            else if (t == typeof(char)) result = isStatic ? AndroidJNI.CallStaticCharMethod(target, methodID, args) : AndroidJNI.CallCharMethod(target, methodID, args);
            else if (t == typeof(string)) result = isStatic ? AndroidJNI.CallStaticStringMethod(target, methodID, args) : AndroidJNI.CallStringMethod(target, methodID, args);
            else
            {
                IntPtr jobject = isStatic ? AndroidJNI.CallStaticObjectMethod(target, methodID, args) : AndroidJNI.CallObjectMethod(target, methodID, args);
                AndroidJNISafe.CheckException();
                return FromObject<ReturnType>(jobject);
            }
            return Checked((ReturnType)result);
        }

        public static FieldType GetField<FieldType>(IntPtr target, IntPtr fieldID, bool isStatic)
        {
            Type t = typeof(FieldType);
            object result;
            if (t == typeof(int)) result = isStatic ? AndroidJNI.GetStaticIntField(target, fieldID) : AndroidJNI.GetIntField(target, fieldID);
            else if (t == typeof(bool)) result = isStatic ? AndroidJNI.GetStaticBooleanField(target, fieldID) : AndroidJNI.GetBooleanField(target, fieldID);
            else if (t == typeof(sbyte)) result = isStatic ? AndroidJNI.GetStaticSByteField(target, fieldID) : AndroidJNI.GetSByteField(target, fieldID);
            else if (t == typeof(short)) result = isStatic ? AndroidJNI.GetStaticShortField(target, fieldID) : AndroidJNI.GetShortField(target, fieldID);
            else if (t == typeof(long)) result = isStatic ? AndroidJNI.GetStaticLongField(target, fieldID) : AndroidJNI.GetLongField(target, fieldID);
            else if (t == typeof(float)) result = isStatic ? AndroidJNI.GetStaticFloatField(target, fieldID) : AndroidJNI.GetFloatField(target, fieldID);
            else if (t == typeof(double)) result = isStatic ? AndroidJNI.GetStaticDoubleField(target, fieldID) : AndroidJNI.GetDoubleField(target, fieldID);
            else if (t == typeof(char)) result = isStatic ? AndroidJNI.GetStaticCharField(target, fieldID) : AndroidJNI.GetCharField(target, fieldID);
            else if (t == typeof(string)) result = isStatic ? AndroidJNI.GetStaticStringField(target, fieldID) : AndroidJNI.GetStringField(target, fieldID);
            else
            {
                IntPtr jobject = isStatic ? AndroidJNI.GetStaticObjectField(target, fieldID) : AndroidJNI.GetObjectField(target, fieldID);
                AndroidJNISafe.CheckException();
                return FromObject<FieldType>(jobject);
            }
            return Checked((FieldType)result);
        }

        public static void SetField<FieldType>(IntPtr target, IntPtr fieldID, FieldType val, bool isStatic)
        {
            object v = val;
            switch (v)
            {
                case int i: if (isStatic) AndroidJNI.SetStaticIntField(target, fieldID, i); else AndroidJNI.SetIntField(target, fieldID, i); break;
                case bool z: if (isStatic) AndroidJNI.SetStaticBooleanField(target, fieldID, z); else AndroidJNI.SetBooleanField(target, fieldID, z); break;
                case sbyte b: if (isStatic) AndroidJNI.SetStaticSByteField(target, fieldID, b); else AndroidJNI.SetSByteField(target, fieldID, b); break;
                case short s: if (isStatic) AndroidJNI.SetStaticShortField(target, fieldID, s); else AndroidJNI.SetShortField(target, fieldID, s); break;
                case long j: if (isStatic) AndroidJNI.SetStaticLongField(target, fieldID, j); else AndroidJNI.SetLongField(target, fieldID, j); break;
                case float f: if (isStatic) AndroidJNI.SetStaticFloatField(target, fieldID, f); else AndroidJNI.SetFloatField(target, fieldID, f); break;
                case double d: if (isStatic) AndroidJNI.SetStaticDoubleField(target, fieldID, d); else AndroidJNI.SetDoubleField(target, fieldID, d); break;
                case char c: if (isStatic) AndroidJNI.SetStaticCharField(target, fieldID, c); else AndroidJNI.SetCharField(target, fieldID, c); break;
                case string str: if (isStatic) AndroidJNI.SetStaticStringField(target, fieldID, str); else AndroidJNI.SetStringField(target, fieldID, str); break;
                default:
                {
                    IntPtr jobject = v switch
                    {
                        null => IntPtr.Zero,
                        AndroidJavaObject o => o.GetRawObject(),
                        Array a => AndroidJNIHelper.ConvertToJNIArray(a),
                        _ => throw Unsupported(typeof(FieldType)),
                    };
                    if (isStatic) AndroidJNI.SetStaticObjectField(target, fieldID, jobject); else AndroidJNI.SetObjectField(target, fieldID, jobject);
                    break;
                }
            }
            AndroidJNISafe.CheckException();
        }

        static T FromObject<T>(IntPtr jobject)
        {
            Type t = typeof(T);
            if (t == typeof(AndroidJavaObject))
                return jobject == IntPtr.Zero ? default : (T)(object)AndroidJavaObject.AndroidJavaObjectDeleteLocalRef(jobject);
            if (t == typeof(AndroidJavaClass))
                return jobject == IntPtr.Zero ? default : (T)(object)AndroidJavaObject.AndroidJavaClassDeleteLocalRef(jobject);
            if (t.IsArray) return AndroidJavaObject.FromJavaArrayDeleteLocalRef<T>(jobject);
            throw Unsupported(t);
        }

        /// <summary>A C# value as the Java object a proxy method returns.</summary>
        public static AndroidJavaObject Box(object obj)
        {
            switch (obj)
            {
                case null: return null;
                case AndroidJavaObject o: return o;
                case AndroidJavaProxy p: return p.GetProxyObject();
                case int i: return AndroidJavaObject.AndroidJavaObjectDeleteLocalRef(AndroidJNIHelper.Box(i));
                case bool z: return AndroidJavaObject.AndroidJavaObjectDeleteLocalRef(AndroidJNIHelper.Box(z));
                case sbyte b: return AndroidJavaObject.AndroidJavaObjectDeleteLocalRef(AndroidJNIHelper.Box(b));
                case short s: return AndroidJavaObject.AndroidJavaObjectDeleteLocalRef(AndroidJNIHelper.Box(s));
                case long j: return AndroidJavaObject.AndroidJavaObjectDeleteLocalRef(AndroidJNIHelper.Box(j));
                case float f: return AndroidJavaObject.AndroidJavaObjectDeleteLocalRef(AndroidJNIHelper.Box(f));
                case double d: return AndroidJavaObject.AndroidJavaObjectDeleteLocalRef(AndroidJNIHelper.Box(d));
                case char c: return AndroidJavaObject.AndroidJavaObjectDeleteLocalRef(AndroidJNIHelper.Box(c));
                case string str: return AndroidJavaObject.AndroidJavaObjectDeleteLocalRef(AndroidJNI.NewString(str));
                case Array a: return AndroidJavaObject.AndroidJavaObjectDeleteLocalRef(AndroidJNIHelper.ConvertToJNIArray(a));
                default: throw new Exception("JNI: Unknown type '" + obj.GetType() + "' to box");
            }
        }

        /// <summary>A Java argument of a proxy call as the C# value it carries: boxed primitives and
        /// strings unwrap, other objects stay wrapped.</summary>
        public static object Unbox(AndroidJavaObject obj) => obj;
    }

    public static partial class AndroidJNIHelper
    {
        /// <summary>The JNI call trace switch; the Editor keeps it off whatever is set.</summary>
        [Unbound.EditorFaithful("measured: tests/projects/platform")]
        public static bool debug
        {
            get => false;
            set { }
        }

        public static IntPtr GetConstructorID(IntPtr javaClass) => GetConstructorID(javaClass, "");

        public static IntPtr GetConstructorID(IntPtr javaClass, string signature)
        {
            IntPtr member = AndroidReflection.GetConstructorMember(javaClass, signature);
            try { return AndroidJNI.FromReflectedMethod(member); }
            finally { AndroidJNISafe.DeleteLocalRef(member); }
        }

        public static IntPtr GetMethodID(IntPtr javaClass, string methodName) => GetMethodID(javaClass, methodName, "", false);
        public static IntPtr GetMethodID(IntPtr javaClass, string methodName, string signature) => GetMethodID(javaClass, methodName, signature, false);

        public static IntPtr GetMethodID(IntPtr javaClass, string methodName, string signature, bool isStatic)
        {
            IntPtr member = AndroidReflection.GetMethodMember(javaClass, methodName, signature, isStatic);
            try { return AndroidJNI.FromReflectedMethod(member); }
            finally { AndroidJNISafe.DeleteLocalRef(member); }
        }

        public static IntPtr GetFieldID(IntPtr javaClass, string fieldName) => GetFieldID(javaClass, fieldName, "", false);
        public static IntPtr GetFieldID(IntPtr javaClass, string fieldName, string signature) => GetFieldID(javaClass, fieldName, signature, false);

        /// <summary>Looks the field up by reflection, then by the signature reflection reports;
        /// fails with Unity's message (naming that reported signature) when there is none.</summary>
        public static IntPtr GetFieldID(IntPtr javaClass, string fieldName, string signature, bool isStatic)
        {
            IntPtr member = AndroidReflection.GetFieldMember(javaClass, fieldName, signature, isStatic);
            try
            {
                IntPtr fieldClass = AndroidReflection.GetFieldClass(member);
                signature = AndroidReflection.GetFieldSignature(member);
                IntPtr field = isStatic
                    ? AndroidJNI.GetStaticFieldID(fieldClass, fieldName, signature)
                    : AndroidJNI.GetFieldID(fieldClass, fieldName, signature);
                AndroidJNISafe.DeleteLocalRef(fieldClass);
                if (field == IntPtr.Zero) throw new Exception(string.Format("Field {0} or type signature {1} not found", fieldName, signature));
                return field;
            }
            finally { AndroidJNISafe.DeleteLocalRef(member); }
        }

        public static IntPtr CreateJavaRunnable(AndroidJavaRunnable jrunnable) => CreateJavaProxy(new AndroidJavaRunnableProxy(jrunnable));

        public static IntPtr CreateJavaProxy(AndroidJavaProxy proxy) =>
            AndroidReflection.NewProxyInstance(IntPtr.Zero, IntPtr.Zero, proxy.javaInterface.GetRawClass());

        public static IntPtr ConvertToJNIArray(Array array) => IntPtr.Zero;

        public static jvalue[] CreateJNIArgArray(object[] args)
        {
            var jniArgs = new jvalue[args.Length];
            CreateJNIArgArray(args, jniArgs);
            return jniArgs;
        }

        public static void CreateJNIArgArray(object[] args, Span<jvalue> jniArgs)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case null: jniArgs[i].l = IntPtr.Zero; break;
                    case bool z: jniArgs[i].z = z; break;
                    case byte b: jniArgs[i].b = (sbyte)b; break;
                    case sbyte b: jniArgs[i].b = b; break;
                    case char c: jniArgs[i].c = c; break;
                    case short s: jniArgs[i].s = s; break;
                    case int n: jniArgs[i].i = n; break;
                    case long j: jniArgs[i].j = j; break;
                    case float f: jniArgs[i].f = f; break;
                    case double d: jniArgs[i].d = d; break;
                    case string str: jniArgs[i].l = AndroidJNI.NewString(str); break;
                    case AndroidJavaClass c: jniArgs[i].l = c.GetRawClass(); break;
                    case AndroidJavaObject o: jniArgs[i].l = o.GetRawObject(); break;
                    case Array a: jniArgs[i].l = ConvertToJNIArray(a); break;
                    case AndroidJavaProxy p: jniArgs[i].l = p.GetRawProxy(); break;
                    case AndroidJavaRunnable r: jniArgs[i].l = CreateJavaRunnable(r); break;
                    default: throw new Exception("JNI; Unknown argument type '" + args[i].GetType() + "'");
                }
            }
        }

        public static void DeleteJNIArgArray(object[] args, jvalue[] jniArgs) => DeleteJNIArgArray(args, jniArgs.AsSpan());

        public static void DeleteJNIArgArray(object[] args, Span<jvalue> jniArgs)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i] is string || args[i] is AndroidJavaRunnable || args[i] is Array)
                    AndroidJNISafe.DeleteLocalRef(jniArgs[i].l);
        }

        public static IntPtr GetConstructorID(IntPtr jclass, object[] args) => GetConstructorID(jclass, GetSignature(args));

        public static IntPtr GetMethodID(IntPtr jclass, string methodName, object[] args, bool isStatic) =>
            GetMethodID(jclass, methodName, GetSignature(args), isStatic);

        /// <summary>The JNI type signature of a value, or of a type given as a <see cref="Type"/>.</summary>
        public static string GetSignature(object obj)
        {
            if (obj == null) return "Ljava/lang/Object;";
            Type type = obj is Type t ? t : obj.GetType();
            if (type.IsPrimitive)
            {
                if (type == typeof(int)) return "I";
                if (type == typeof(bool)) return "Z";
                if (type == typeof(byte))
                {
                    Debug.LogWarning("AndroidJNIHelper.GetSignature: using Byte parameters is obsolete, use SByte parameters instead");
                    return "B";
                }
                if (type == typeof(sbyte)) return "B";
                if (type == typeof(short)) return "S";
                if (type == typeof(long)) return "J";
                if (type == typeof(float)) return "F";
                if (type == typeof(double)) return "D";
                if (type == typeof(char)) return "C";
            }
            else if (type == typeof(string)) return "Ljava/lang/String;";
            else if (obj is AndroidJavaProxy proxy)
            {
                using (var javaObject = new AndroidJavaObject(proxy.javaInterface.GetRawClass()))
                    return "L" + javaObject.Call<string>("getName") + ";";
            }
            else if (obj == (object)typeof(AndroidJavaRunnable) || obj is AndroidJavaRunnable) return "Ljava/lang/Runnable;";
            else if (typeof(AndroidJavaClass).IsAssignableFrom(type)) return "Ljava/lang/Class;";
            else if (typeof(AndroidJavaObject).IsAssignableFrom(type))
            {
                if (obj == (object)type) return "Ljava/lang/Object;";
                using (AndroidJavaObject javaClass = ((AndroidJavaObject)obj).Call<AndroidJavaObject>("getClass"))
                    return "L" + javaClass.Call<string>("getName") + ";";
            }
            else if (type.IsArray && type.GetArrayRank() == 1)
                return "[" + GetSignature(type.GetElementType());
            throw new Exception("JNI: Unknown signature for type '" + type + "' (obj = " + obj + ") " + ((object)type == obj ? "equal" : "instance"));
        }

        public static string GetSignature(object[] args)
        {
            var sb = new StringBuilder("(");
            foreach (object arg in args) sb.Append(GetSignature(arg));
            return sb.Append(")V").ToString();
        }

        [Unbound.EditorFaithful("measured: tests/projects/platform")]
        public static ArrayType ConvertFromJNIArray<ArrayType>(IntPtr array) => default;

        public static IntPtr GetMethodID<ReturnType>(IntPtr jclass, string methodName, object[] args, bool isStatic) =>
            GetMethodID(jclass, methodName, GetSignature<ReturnType>(args), isStatic);

        public static IntPtr GetFieldID<FieldType>(IntPtr jclass, string fieldName, bool isStatic) =>
            GetFieldID(jclass, fieldName, GetSignature(typeof(FieldType)), isStatic);

        public static string GetSignature<ReturnType>(object[] args)
        {
            var sb = new StringBuilder("(");
            foreach (object arg in args) sb.Append(GetSignature(arg));
            return sb.Append(')').Append(GetSignature(typeof(ReturnType))).ToString();
        }

        public static IntPtr Box(sbyte value) => IntPtr.Zero;
        public static IntPtr Box(short value) => IntPtr.Zero;
        public static IntPtr Box(int value) => IntPtr.Zero;
        public static IntPtr Box(long value) => IntPtr.Zero;
        public static IntPtr Box(float value) => IntPtr.Zero;
        public static IntPtr Box(double value) => IntPtr.Zero;
        public static IntPtr Box(char value) => IntPtr.Zero;
        public static IntPtr Box(bool value) => IntPtr.Zero;
        public static void Unbox(IntPtr obj, out sbyte value) => value = default;
        public static void Unbox(IntPtr obj, out short value) => value = default;
        public static void Unbox(IntPtr obj, out int value) => value = default;
        public static void Unbox(IntPtr obj, out long value) => value = default;
        public static void Unbox(IntPtr obj, out float value) => value = default;
        public static void Unbox(IntPtr obj, out double value) => value = default;
        public static void Unbox(IntPtr obj, out char value) => value = default;
        public static void Unbox(IntPtr obj, out bool value) => value = default;
    }

    /// <summary>Java reflection through the JNI; the Editor has no VM, so nothing is found.</summary>
    [Unbound.EditorFaithful("the Editor on a desktop has no Java VM: nothing is found")]
    internal partial class AndroidReflection
    {
        public static IntPtr GetConstructorMember(IntPtr jclass, string signature) => IntPtr.Zero;
        public static IntPtr GetMethodMember(IntPtr jclass, string methodName, string signature, bool isStatic) => IntPtr.Zero;
        public static IntPtr GetFieldMember(IntPtr jclass, string fieldName, string signature, bool isStatic) => IntPtr.Zero;
        public static IntPtr GetFieldClass(IntPtr field) => IntPtr.Zero;
        public static string GetFieldSignature(IntPtr field) => null;
        public static IntPtr NewProxyInstance(IntPtr player, IntPtr delegateHandle, IntPtr interfaze) => IntPtr.Zero;
    }
}
