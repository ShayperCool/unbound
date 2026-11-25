using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    [Serializable] public class TypeTextBox<T> { public T value; }

    /// <summary>The type entry of a [SerializeReference] object of a generic class, as the Editor
    /// writes it (tests/projects/scenesave generic-refs), read back to the same type.</summary>
    public class ManagedReferenceTypeTextTests
    {
        static readonly string Assembly = typeof(ManagedReferenceTypeTextTests).Assembly.GetName().Name;

        [Fact]
        public void AGenericClassIsWrittenWithItsArgumentsAndReadBack()
        {
            Assert.Equal("{class: 'TypeTextBox`1[[System.Int32, mscorlib]]', ns: UnityEngine.Tests, asm: " + Assembly + "}",
                ManagedReferenceIds.TypeText(typeof(TypeTextBox<int>)));
            Assert.Equal("{class: 'TypeTextBox`1[[System.Collections.Generic.List`1[[System.String, mscorlib]], mscorlib]]', ns: UnityEngine.Tests, asm: " + Assembly + "}",
                ManagedReferenceIds.TypeText(typeof(TypeTextBox<List<string>>)));
            Assert.Equal("{class: 'TypeTextBox`1[[UnityEngine.Vector3, UnityEngine.CoreModule]]', ns: UnityEngine.Tests, asm: " + Assembly + "}",
                ManagedReferenceIds.TypeText(typeof(TypeTextBox<Vector3>)));
            Assert.Equal(typeof(TypeTextBox<int>), TypeResolver.Find("TypeTextBox`1[[System.Int32, mscorlib]]", "UnityEngine.Tests", Assembly));
            Assert.Equal(typeof(TypeTextBox<List<string>>),
                TypeResolver.Find("TypeTextBox`1[[System.Collections.Generic.List`1[[System.String, mscorlib]], mscorlib]]", "UnityEngine.Tests", Assembly));
        }

        [Fact]
        public void ClassLibraryArgumentsOutsideTheCoreLibraryAreRefusedLoudly()
        {
            var e = Assert.Throws<NotSupportedException>(() => ManagedReferenceIds.TypeText(typeof(TypeTextBox<HashSet<int>>)));
            Assert.Contains("class library", e.Message);
            Assert.Throws<NotSupportedException>(() => ManagedReferenceIds.TypeText(typeof(TypeTextBox<Guid>)));
            // An engine type of another Unity module (AudioClip: UnityEngine.AudioModule) and arrays.
            Assert.Contains("module", Assert.Throws<NotSupportedException>(() => ManagedReferenceIds.TypeText(typeof(TypeTextBox<AudioClip>))).Message);
            Assert.Throws<NotSupportedException>(() => ManagedReferenceIds.TypeText(typeof(TypeTextBox<int[]>)));
            Assert.Throws<NotSupportedException>(() => ManagedReferenceIds.TypeText(typeof(TypeTextBox<TypeTextBox<int>[]>)));
            // A plain class is written as before.
            Assert.Equal("{class: ManagedReferenceTypeTextTests, ns: UnityEngine.Tests, asm: " + Assembly + "}",
                ManagedReferenceIds.TypeText(typeof(ManagedReferenceTypeTextTests)));
        }
    }
}
