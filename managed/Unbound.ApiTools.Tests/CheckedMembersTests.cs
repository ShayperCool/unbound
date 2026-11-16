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
    public class Object { }
    public class Body : Object
    {
        static System.IntPtr Marshal(Body b) => System.IntPtr.Zero;
        static float get_mass_Injected(System.IntPtr p) => 0;
        // The binding pattern.
        public float mass { get { var p = Marshal(this); if (p == System.IntPtr.Zero) UnityEngine.Bindings.ThrowHelper.ThrowNullReferenceException(this); return get_mass_Injected(p); } }
        // A managed overload reaching the binding through its own object.
        public float Twice() => mass * 2;
        public T Same<T>(T value) { _ = mass; return value; }
        public void Scale(float factor, System.Collections.Generic.List<int> list) { _ = Twice(); }
        // Managed members that do not touch the binding, and a call on another object.
        public float Plain() => 1;
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
                     "UnityEngine.Transform::Translate(UnityEngine.Vector3)", "UnityEngine.Rigidbody::get_mass()",
                     "UnityEngine.Texture::get_width()",
                 })
            Assert.Contains(member, list);
        foreach (var member in new[] { "UnityEngine.Object::GetInstanceID()", "UnityEngine.Object::Equals(System.Object)", "UnityEngine.Object::ToString()",
                     "UnityEditor.SerializedObject::FindProperty(System.String)" })
            Assert.DoesNotContain(member, list);
    }
}
