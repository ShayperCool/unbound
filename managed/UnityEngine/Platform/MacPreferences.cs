using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// The macOS Editor's prefs store: the user's preferences domain of an application id
    /// (<c>unity.&lt;company&gt;.&lt;product&gt;</c> for PlayerPrefs, <c>com.unity3d.UnityEditor5.x</c> for
    /// EditorPrefs), read and written through CFPreferences as Unity does, so the values are shared
    /// with the Editor and the preferences daemon's cache stays right. Ints are CFNumber integers,
    /// floats CFNumber reals, strings CFString.
    /// </summary>
    internal static unsafe class MacPreferences
    {
        const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const int kCFNumberSInt32Type = 3;
        const int kCFNumberSInt64Type = 4;
        const int kCFNumberFloat32Type = 5;
        const int kCFNumberFloat64Type = 6;

        [StructLayout(LayoutKind.Sequential)]
        struct CFRange
        {
            public nint location;
            public nint length;
        }

        [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr cf);
        [DllImport(CoreFoundation)] static extern nuint CFGetTypeID(IntPtr cf);
        [DllImport(CoreFoundation)] static extern nuint CFStringGetTypeID();
        [DllImport(CoreFoundation)] static extern nuint CFNumberGetTypeID();
        [DllImport(CoreFoundation)] static extern IntPtr CFStringCreateWithCharacters(IntPtr alloc, char* chars, nint length);
        [DllImport(CoreFoundation)] static extern nint CFStringGetLength(IntPtr str);
        [DllImport(CoreFoundation)] static extern void CFStringGetCharacters(IntPtr str, CFRange range, char* buffer);
        [DllImport(CoreFoundation)] static extern IntPtr CFNumberCreate(IntPtr alloc, int type, void* value);
        [DllImport(CoreFoundation)] static extern byte CFNumberGetValue(IntPtr number, int type, void* value);
        [DllImport(CoreFoundation)] static extern byte CFNumberIsFloatType(IntPtr number);
        [DllImport(CoreFoundation)] static extern nint CFArrayGetCount(IntPtr array);
        [DllImport(CoreFoundation)] static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
        [DllImport(CoreFoundation)] static extern IntPtr CFPreferencesCopyKeyList(IntPtr app, IntPtr user, IntPtr host);
        [DllImport(CoreFoundation)] static extern IntPtr CFPreferencesCopyAppValue(IntPtr key, IntPtr app);
        [DllImport(CoreFoundation)] static extern void CFPreferencesSetAppValue(IntPtr key, IntPtr value, IntPtr app);
        [DllImport(CoreFoundation)] static extern byte CFPreferencesAppSynchronize(IntPtr app);

        static IntPtr s_CurrentUser, s_AnyHost;

        /// <summary>CoreFoundation's <c>kCFPreferencesCurrentUser</c> / <c>kCFPreferencesAnyHost</c>.</summary>
        static IntPtr Constant(string name, ref IntPtr cache)
        {
            if (cache != IntPtr.Zero) return cache;
            var library = NativeLibrary.Load(CoreFoundation);
            return cache = *(IntPtr*)NativeLibrary.GetExport(library, name);
        }

        static IntPtr CreateString(string s)
        {
            fixed (char* chars = s) return CFStringCreateWithCharacters(IntPtr.Zero, chars, s.Length);
        }

        static string ReadString(IntPtr str)
        {
            var length = CFStringGetLength(str);
            var buffer = new char[length];
            fixed (char* chars = buffer) CFStringGetCharacters(str, new CFRange { location = 0, length = length }, chars);
            return new string(buffer);
        }

        /// <summary>The domain's ints, floats and strings (values of other types are skipped).</summary>
        internal static Dictionary<string, object> Read(string app)
        {
            var values = new Dictionary<string, object>();
            var appRef = CreateString(app);
            try
            {
                CFPreferencesAppSynchronize(appRef);
                var keys = CFPreferencesCopyKeyList(appRef, Constant("kCFPreferencesCurrentUser", ref s_CurrentUser), Constant("kCFPreferencesAnyHost", ref s_AnyHost));
                if (keys == IntPtr.Zero) return values;
                try
                {
                    var count = CFArrayGetCount(keys);
                    for (nint i = 0; i < count; i++)
                    {
                        var key = CFArrayGetValueAtIndex(keys, i);
                        var value = CFPreferencesCopyAppValue(key, appRef);
                        if (value == IntPtr.Zero) continue;
                        try
                        {
                            var converted = ToManaged(value);
                            if (converted != null) values[ReadString(key)] = converted;
                        }
                        finally
                        {
                            CFRelease(value);
                        }
                    }
                }
                finally
                {
                    CFRelease(keys);
                }
            }
            finally
            {
                CFRelease(appRef);
            }
            return values;
        }

        static object ToManaged(IntPtr value)
        {
            var type = CFGetTypeID(value);
            if (type == CFStringGetTypeID()) return ReadString(value);
            if (type != CFNumberGetTypeID()) return null;
            if (CFNumberIsFloatType(value) != 0)
            {
                double d;
                CFNumberGetValue(value, kCFNumberFloat64Type, &d);
                return (float)d;
            }
            long l;
            CFNumberGetValue(value, kCFNumberSInt64Type, &l);
            return unchecked((int)l);
        }

        /// <summary>Makes the domain hold exactly <paramref name="values"/>: sets each value, removes
        /// the domain's other keys, and writes the domain out.</summary>
        internal static void Write(string app, Dictionary<string, object> values)
        {
            var stale = Read(app);
            var appRef = CreateString(app);
            try
            {
                foreach (var key in stale.Keys)
                    if (!values.ContainsKey(key)) Set(appRef, key, IntPtr.Zero);
                foreach (var pair in values)
                {
                    var value = pair.Value switch
                    {
                        int i => CFNumberCreate(IntPtr.Zero, kCFNumberSInt32Type, &i),
                        float f => CFNumberCreate(IntPtr.Zero, kCFNumberFloat32Type, &f),
                        string s => CreateString(s),
                        _ => IntPtr.Zero,
                    };
                    if (value == IntPtr.Zero) continue;
                    try
                    {
                        Set(appRef, pair.Key, value);
                    }
                    finally
                    {
                        CFRelease(value);
                    }
                }
                if (CFPreferencesAppSynchronize(appRef) == 0) throw new InvalidOperationException($"CFPreferences could not write {app}");
            }
            finally
            {
                CFRelease(appRef);
            }
        }

        static void Set(IntPtr appRef, string key, IntPtr value)
        {
            var keyRef = CreateString(key);
            try
            {
                CFPreferencesSetAppValue(keyRef, value, appRef);
            }
            finally
            {
                CFRelease(keyRef);
            }
        }
    }
}
