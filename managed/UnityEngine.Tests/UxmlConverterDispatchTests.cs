using System;
using System.Globalization;
using System.Reflection;
using Unbound.UIElements;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UxmlConverterDispatchTests
    {
        // The open generic type does not register a converter for built-in attribute types.
        sealed class Direct<T> : UxmlAttributeConverter<T>
        {
            internal Func<string, T> Read;
            internal Func<T, string> Write;
            internal int Calls;
            public override T FromString(string value) { Calls++; return Read(value); }
            public override string ToString(T value) { Calls++; return Write == null ? base.ToString(value) : Write(value); }
        }

        [Fact]
        public void UntypedBridgeUsesTheSameStateAndVirtualMethods()
        {
            int bias = 17;
            var typed = new Direct<int> { Read = text => bias + int.Parse(text, CultureInfo.InvariantCulture), Write = value => $"write:{bias}:{value}" };
            IUxmlAttributeConverter converter = typed;
            var first = ScriptableObject.CreateInstance<VisualTreeAsset>();
            var second = ScriptableObject.CreateInstance<VisualTreeAsset>();
            try
            {
                Assert.Equal(21, converter.FromString("4", default));
                bias = 29;
                Assert.Equal(33, converter.FromString("4", default));
                Assert.Equal("write:29:7", converter.ToString(7, first));
                bias = 41;
                Assert.Equal("write:41:7", converter.ToString(7, second));
                Assert.Equal(4, typed.Calls);
                Assert.Throws<InvalidCastException>(() => converter.ToString("wrong", first));
                Assert.Throws<NullReferenceException>(() => converter.ToString(null, first));
                Assert.Equal(4, typed.Calls);
                Assert.Throws<FormatException>(() => converter.FromString("bad", default));
                Assert.Equal(5, typed.Calls);
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Fact]
        public void ReferenceNullableAndDefaultFormattingKeepTheirTypedRules()
        {
            IUxmlAttributeConverter strings = new Direct<string> { Read = text => text ?? "from-null" };
            Assert.Equal("from-null", strings.FromString(null, default));
            Assert.Equal("", strings.ToString(null, null));
            Assert.Throws<InvalidCastException>(() => strings.ToString(17, null));
            IUxmlAttributeConverter nullable = new Direct<int?> { Read = text => text == null ? null : int.Parse(text, CultureInfo.InvariantCulture) };
            Assert.Null(nullable.FromString(null, default));
            Assert.Equal(37, nullable.FromString("37", default));
            Assert.Equal("", nullable.ToString(null, null));
            Assert.Equal("37", nullable.ToString(37, null));
            Assert.Throws<InvalidCastException>(() => nullable.ToString(37L, null));
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                IUxmlAttributeConverter doubles = new Direct<double>();
                Assert.Equal("1.25", doubles.ToString(1.25, null));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        public struct Token { public int Value; }
        public abstract class TokenParent : UxmlAttributeConverter<Token>
        {
            public static TokenParent Instance;
            public int Bias = 23;
            protected TokenParent() { Instance = this; }
            public override Token FromString(string value) => new Token { Value = Bias + int.Parse(value, CultureInfo.InvariantCulture) };
        }
        public sealed class TokenHidden : TokenParent
        {
            public new Token FromString(string value) => new Token { Value = 991 };
        }

        [Fact]
        public void AttributeReadingUsesInheritedVirtualDispatchAndCachedInstance()
        {
            Assert.Equal(typeof(TokenHidden), UxmlTypeIndex.ConverterFor(typeof(Token)));
            Assert.True(UxmlAttributeValues.TryRead("7", typeof(Token), null, null, out var first, out var error), error);
            Assert.Equal(30, Assert.IsType<Token>(first).Value);
            var instance = Assert.IsType<TokenHidden>(TokenParent.Instance);
            Assert.Equal(991, instance.FromString("7").Value);
            instance.Bias = 47;
            try
            {
                Assert.True(UxmlAttributeValues.TryRead("7", typeof(Token), null, null, out var changed, out error), error);
                Assert.Equal(54, Assert.IsType<Token>(changed).Value);
                Assert.Same(instance, TokenParent.Instance);
                Assert.False(UxmlAttributeValues.TryRead("bad", typeof(Token), null, null, out var failed, out error));
                Assert.Null(failed);
                Assert.Contains("FromString threw FormatException:", error);
            }
            finally { instance.Bias = 23; }
        }

        public struct FailedToken { }
        public sealed class FailingConstructor : UxmlAttributeConverter<FailedToken>
        {
            public FailingConstructor() => throw new InvalidOperationException("constructor-control");
            public override FailedToken FromString(string value) => default;
        }
        public struct WrappedToken { }
        public sealed class UserInvocationException : UxmlAttributeConverter<WrappedToken>
        {
            public override WrappedToken FromString(string value) => throw new TargetInvocationException("converter-control", new InvalidOperationException("inner-control"));
        }

        [Fact]
        public void ConstructionAndConverterErrorsKeepTheirOriginalExceptionKinds()
        {
            Assert.False(UxmlTypeIndex.TryConvert(typeof(FailingConstructor), "7", out var result, out var error));
            Assert.Null(result);
            Assert.Equal("FailingConstructor.FromString threw InvalidOperationException: constructor-control", error);
            Assert.False(UxmlTypeIndex.TryConvert(typeof(UserInvocationException), "7", out result, out error));
            Assert.Null(result);
            Assert.Equal("UserInvocationException.FromString threw TargetInvocationException: converter-control", error);
        }
    }
}
