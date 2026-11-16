using System;
using System.IO;
using System.Linq;
using Unbound.ApiTools;
using Xunit;

namespace Unbound.ApiTools.Tests;

/// <summary>The checked-member list: bindings that pass their own object to
/// ThrowHelper.ThrowNullReferenceException, and members reaching one through their own object.</summary>
public class CheckedMembersTests
{
    const string Source = @"
namespace UnityEngine.Bindings
{
    public static class ThrowHelper { public static void ThrowNullReferenceException(object o) { } }
}
namespace UnityEngine
{
    public class Object
    {
        public static class MarshalledUnityObject { public static System.IntPtr MarshalNotNull<T>(T o) where T : class => System.IntPtr.Zero; }
    }
    public class Body : Object
    {
        static float get_mass_Injected(System.IntPtr p) => 0;
        // The binding pattern.
        public float mass { get { var p = MarshalledUnityObject.MarshalNotNull(this); if (p == System.IntPtr.Zero) UnityEngine.Bindings.ThrowHelper.ThrowNullReferenceException(this); return get_mass_Injected(p); } }
        // Decides something of its own before it reaches its object.
        public float Guarded(Body other) { if (other == null) throw new System.ArgumentNullException(""other""); return mass; }
        // A managed overload reaching the binding through its own object.
        public float Twice() => mass * 2;
        public T Same<T>(T value) { _ = mass; return value; }
        public void Scale(float factor, System.Collections.Generic.List<int> list) { _ = Twice(); }
        // Managed members that do not touch the binding, and a call on another object.
        public float Plain() => 1;
        // Parameter types as both sides write them (Unbound.Host's LifetimeChecks tests the same text).
        public void Mixed(in int x, int[,] a, System.Collections.Generic.List<int>[] b, ref float f) { _ = mass; }
        public float Other(Body other) => other.mass;
        public static float Static(Body b) => b.mass;
    }
    // A class with its own engine pointer, not an engine object.
    public class Handle
    {
        System.IntPtr m_Ptr;
        public int count { get { if (m_Ptr == System.IntPtr.Zero) UnityEngine.Bindings.ThrowHelper.ThrowNullReferenceException(this); return 0; } }
    }
}";

    static string[] Compute()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var path = TestAssemblies.Compile(root, "Bodies", Source);
            return CheckedMembers.Compute(new[] { File.ReadAllBytes(path) }).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BindingsAndTheirOwnObjectsCallersAreChecked()
    {
        var list = Compute();
        Assert.Contains("UnityEngine.Body::get_mass()", list);
        Assert.Contains("UnityEngine.Body::Twice()", list);
        Assert.Contains("UnityEngine.Body::Same`1(!!0)", list);
        Assert.Contains("UnityEngine.Body::Scale(System.Single,System.Collections.Generic.List`1<System.Int32>)", list);
        Assert.DoesNotContain("UnityEngine.Body::Plain()", list);
        Assert.DoesNotContain("UnityEngine.Body::Guarded(UnityEngine.Body)", list);
        Assert.Contains("UnityEngine.Body::Mixed(System.Int32&,System.Int32[,],System.Collections.Generic.List`1<System.Int32>[],System.Single&)", list);
        Assert.DoesNotContain("UnityEngine.Body::Other(UnityEngine.Body)", list);
        Assert.DoesNotContain(list, line => line.Contains("::Static("));
        Assert.DoesNotContain("UnityEngine.Handle::get_count()", list);
    }

    /// <summary>The installed release, if any (UNBOUND_TEST_UNITY_MANAGED_DIR or the Hub's default
    /// Linux place).</summary>
    static string? UnityManagedEngineDir()
    {
        var dir = Environment.GetEnvironmentVariable("UNBOUND_TEST_UNITY_MANAGED_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Unity/Hub/Editor/6000.0.71f1/Editor/Data/Managed/UnityEngine");
        return File.Exists(Path.Combine(dir, "UnityEngine.CoreModule.dll")) ? dir : null;
    }

    [Fact]
    public void TheReleasesBindingsAreRead()
    {
        if (UnityManagedEngineDir() is not { } unity)
            return; // no Unity install on this machine
        var list = CheckedMembers.Compute(UnityModules.Files(unity).Select(File.ReadAllBytes)).Split('\n');
        foreach (var member in new[]
                 {
                     "UnityEngine.Object::get_name()", "UnityEngine.Object::get_hideFlags()", "UnityEngine.Component::get_transform()",
                     "UnityEngine.GameObject::GetComponent`1()", "UnityEngine.Transform::get_position()",
                     "UnityEngine.Rigidbody::get_mass()", "UnityEngine.Texture::get_width()",
                 })
            Assert.Contains(member, list);
        foreach (var member in new[] { "UnityEngine.Object::GetInstanceID()", "UnityEngine.Object::Equals(System.Object)", "UnityEngine.Object::ToString()",
                     "UnityEditor.SerializedObject::FindProperty(System.String)",
                     // Unity's own answers come first: its message, the routine check, the name check.
                     "UnityEngine.MonoBehaviour::get_destroyCancellationToken()",
                     "UnityEngine.MonoBehaviour::StartCoroutine(System.Collections.IEnumerator)", "UnityEngine.Transform::Find(System.String)" })
            Assert.DoesNotContain(member, list);
    }
}
