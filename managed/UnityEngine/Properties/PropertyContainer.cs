using System;
using System.Collections.Generic;
using Unity.Properties.Internal;
using UnityEngine.Pool;

namespace Unity.Properties
{
    /// <summary>
    /// Visiting containers, and getting and setting values by property path (values convert with
    /// <see cref="TypeConversion"/>). The Try forms report a <see cref="VisitReturnCode"/>; the
    /// others throw the matching exception.
    /// </summary>
    public static partial class PropertyContainer
    {
        sealed class GetPropertyVisitor : PathVisitor
        {
            internal static readonly ObjectPool<GetPropertyVisitor> Pool = new ObjectPool<GetPropertyVisitor>(() => new GetPropertyVisitor(), null, v => v.Reset());
            internal IProperty Property;

            public override void Reset()
            {
                base.Reset();
                Property = null;
                ReadonlyVisit = true;
            }

            protected override void VisitPath<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value) => Property = property;
        }

        sealed class GetValueVisitor<TSrcValue> : PathVisitor
        {
            internal static readonly ObjectPool<GetValueVisitor<TSrcValue>> Pool = new ObjectPool<GetValueVisitor<TSrcValue>>(() => new GetValueVisitor<TSrcValue>(), null, v => v.Reset());
            internal TSrcValue Value;

            public override void Reset()
            {
                base.Reset();
                Value = default;
                ReadonlyVisit = true;
            }

            protected override void VisitPath<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value)
            {
                if (!TypeConversion.TryConvert(ref value, out Value)) ReturnCode = VisitReturnCode.InvalidCast;
            }
        }

        sealed class ValueAtPathVisitor : PathVisitor
        {
            internal static readonly ObjectPool<ValueAtPathVisitor> Pool = new ObjectPool<ValueAtPathVisitor>(() => new ValueAtPathVisitor(), null, v => v.Reset());
            internal IPropertyVisitor Visitor;

            public override void Reset()
            {
                base.Reset();
                Visitor = null;
                ReadonlyVisit = true;
            }

            protected override void VisitPath<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value) =>
                ((IPropertyAccept<TContainer>)property).Accept(Visitor, ref container);
        }

        sealed class ExistsAtPathVisitor : PathVisitor
        {
            internal static readonly ObjectPool<ExistsAtPathVisitor> Pool = new ObjectPool<ExistsAtPathVisitor>(() => new ExistsAtPathVisitor(), null, v => v.Reset());
            internal bool Exists;

            public override void Reset()
            {
                base.Reset();
                Exists = false;
                ReadonlyVisit = true;
            }

            protected override void VisitPath<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value) => Exists = true;
        }

        internal partial class SetValueVisitor<TSrcValue> : PathVisitor
        {
            public static readonly ObjectPool<SetValueVisitor<TSrcValue>> Pool = new ObjectPool<SetValueVisitor<TSrcValue>>(() => new SetValueVisitor<TSrcValue>(), null, v => v.Reset());
            public TSrcValue Value;

            public override void Reset()
            {
                base.Reset();
                Value = default;
            }

            protected override void VisitPath<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value)
            {
                if (property.IsReadOnly) ReturnCode = VisitReturnCode.AccessViolation;
                else if (TypeConversion.TryConvert<TSrcValue, TValue>(ref Value, out var converted)) property.SetValue(ref container, converted);
                else ReturnCode = VisitReturnCode.InvalidCast;
            }
        }

        // ------------------------------------------------------------------ visiting

        public static void Accept<TContainer>(IPropertyBagVisitor visitor, TContainer container, VisitParameters parameters = default) =>
            Accept(visitor, ref container, parameters);

        public static void Accept<TContainer>(IPropertyBagVisitor visitor, ref TContainer container, VisitParameters parameters = default)
        {
            var code = VisitReturnCode.Ok;
            try
            {
                if (TryAccept(visitor, ref container, out code, parameters)) return;
            }
            catch (Exception)
            {
                if ((parameters.IgnoreExceptions & VisitExceptionKind.Visitor) == 0) throw;
            }
            if ((parameters.IgnoreExceptions & VisitExceptionKind.Internal) != 0) return;
            switch (code)
            {
                case VisitReturnCode.Ok:
                case VisitReturnCode.InvalidContainerType:
                    return;
                case VisitReturnCode.NullContainer:
                    throw new ArgumentException("The given container was null. Visitation only works for valid non-null containers.");
                case VisitReturnCode.MissingPropertyBag:
                    throw new MissingPropertyBagException(container.GetType());
                default:
                    throw new Exception($"Unexpected {nameof(VisitReturnCode)}=[{code}]");
            }
        }

        public static bool TryAccept<TContainer>(IPropertyBagVisitor visitor, ref TContainer container, VisitParameters parameters = default) =>
            TryAccept(visitor, ref container, out _, parameters);

        public static bool TryAccept<TContainer>(IPropertyBagVisitor visitor, ref TContainer container, out VisitReturnCode returnCode, VisitParameters parameters = default)
        {
            if (!TypeTraits<TContainer>.IsContainer)
            {
                returnCode = VisitReturnCode.InvalidContainerType;
                return false;
            }
            if (TypeTraits<TContainer>.CanBeNull && EqualityComparer<TContainer>.Default.Equals(container, default))
            {
                returnCode = VisitReturnCode.NullContainer;
                return false;
            }
            if (!TypeTraits<TContainer>.IsValueType && typeof(TContainer) != container.GetType())
            {
                // Visited as what it is, not as the declared type.
                var actual = container.GetType();
                if (!TypeTraits.IsContainer(actual))
                {
                    returnCode = VisitReturnCode.InvalidContainerType;
                    return false;
                }
                var bag = PropertyBagStore.GetPropertyBag(actual);
                if (bag == null)
                {
                    returnCode = VisitReturnCode.MissingPropertyBag;
                    return false;
                }
                object boxed = container;
                bag.Accept(visitor, ref boxed);
                container = (TContainer)boxed;
            }
            else
            {
                var bag = PropertyBagStore.GetPropertyBag<TContainer>();
                if (bag == null)
                {
                    returnCode = VisitReturnCode.MissingPropertyBag;
                    return false;
                }
                PropertyBag.AcceptWithSpecializedVisitor(bag, visitor, ref container);
            }
            returnCode = VisitReturnCode.Ok;
            return true;
        }

        public static void Accept<TContainer>(IPropertyVisitor visitor, ref TContainer container, in PropertyPath path, VisitParameters parameters = default)
        {
            var atPath = ValueAtPathVisitor.Pool.Get();
            try
            {
                atPath.Path = path;
                atPath.Visitor = visitor;
                Accept(atPath, ref container, parameters);
                if ((parameters.IgnoreExceptions & VisitExceptionKind.Internal) != 0) return;
                switch (atPath.ReturnCode)
                {
                    case VisitReturnCode.Ok:
                        return;
                    case VisitReturnCode.InvalidPath:
                        throw new InvalidPathException($"Failed to Visit at Path=[{path}]");
                    default:
                        throw new Exception($"Unexpected {nameof(VisitReturnCode)}=[{atPath.ReturnCode}]");
                }
            }
            finally
            {
                ValueAtPathVisitor.Pool.Release(atPath);
            }
        }

        public static bool TryAccept<TContainer>(IPropertyVisitor visitor, ref TContainer container, in PropertyPath path, out VisitReturnCode returnCode, VisitParameters parameters = default)
        {
            var atPath = ValueAtPathVisitor.Pool.Get();
            try
            {
                atPath.Path = path;
                atPath.Visitor = visitor;
                return TryAccept(atPath, ref container, out returnCode, parameters);
            }
            finally
            {
                ValueAtPathVisitor.Pool.Release(atPath);
            }
        }

        // ------------------------------------------------------------------ properties

        public static IProperty GetProperty<TContainer>(TContainer container, in PropertyPath path) => GetProperty(ref container, path);

        public static IProperty GetProperty<TContainer>(ref TContainer container, in PropertyPath path)
        {
            if (TryGetProperty(ref container, path, out var property, out var code)) return property;
            throw code switch
            {
                VisitReturnCode.NullContainer => new ArgumentNullException("container"),
                VisitReturnCode.InvalidContainerType => new InvalidContainerTypeException(container.GetType()),
                VisitReturnCode.MissingPropertyBag => new MissingPropertyBagException(container.GetType()),
                VisitReturnCode.InvalidPath => new ArgumentException($"Failed to get property for path=[{path}]"),
                _ => new Exception($"Unexpected {nameof(VisitReturnCode)}=[{code}]"),
            };
        }

        public static bool TryGetProperty<TContainer>(TContainer container, in PropertyPath path, out IProperty property) =>
            TryGetProperty(ref container, path, out property, out _);

        public static bool TryGetProperty<TContainer>(ref TContainer container, in PropertyPath path, out IProperty property) =>
            TryGetProperty(ref container, path, out property, out _);

        public static bool TryGetProperty<TContainer>(ref TContainer container, in PropertyPath path, out IProperty property, out VisitReturnCode returnCode)
        {
            var visitor = GetPropertyVisitor.Pool.Get();
            try
            {
                visitor.Path = path;
                if (!TryAccept(visitor, ref container, out returnCode))
                {
                    property = null;
                    return false;
                }
                returnCode = visitor.ReturnCode;
                property = visitor.Property;
                return returnCode == VisitReturnCode.Ok;
            }
            finally
            {
                GetPropertyVisitor.Pool.Release(visitor);
            }
        }

        // ------------------------------------------------------------------ values

        public static TValue GetValue<TContainer, TValue>(TContainer container, string name) => GetValue<TContainer, TValue>(ref container, name);

        public static TValue GetValue<TContainer, TValue>(ref TContainer container, string name) =>
            GetValue<TContainer, TValue>(ref container, new PropertyPath(name));

        public static TValue GetValue<TContainer, TValue>(TContainer container, in PropertyPath path) => GetValue<TContainer, TValue>(ref container, path);

        public static TValue GetValue<TContainer, TValue>(ref TContainer container, in PropertyPath path)
        {
            if (path.IsEmpty) throw new InvalidPathException("The specified PropertyPath is empty.");
            if (TryGetValue<TContainer, TValue>(ref container, path, out var value, out var code)) return value;
            throw code switch
            {
                VisitReturnCode.NullContainer => new ArgumentNullException("container"),
                VisitReturnCode.InvalidContainerType => new InvalidContainerTypeException(container.GetType()),
                VisitReturnCode.MissingPropertyBag => new MissingPropertyBagException(container.GetType()),
                VisitReturnCode.InvalidCast => new InvalidCastException($"Failed to GetValue of Type=[{typeof(TValue).Name}] for property with path=[{path}]"),
                VisitReturnCode.InvalidPath => new InvalidPathException($"Failed to GetValue for property with Path=[{path}]"),
                _ => new Exception($"Unexpected {nameof(VisitReturnCode)}=[{code}]"),
            };
        }

        public static bool TryGetValue<TContainer, TValue>(TContainer container, string name, out TValue value) =>
            TryGetValue(ref container, name, out value);

        public static bool TryGetValue<TContainer, TValue>(ref TContainer container, string name, out TValue value) =>
            TryGetValue(ref container, new PropertyPath(name), out value, out _);

        public static bool TryGetValue<TContainer, TValue>(TContainer container, in PropertyPath path, out TValue value) =>
            TryGetValue(ref container, path, out value, out _);

        public static bool TryGetValue<TContainer, TValue>(ref TContainer container, in PropertyPath path, out TValue value) =>
            TryGetValue(ref container, path, out value, out _);

        public static bool TryGetValue<TContainer, TValue>(ref TContainer container, in PropertyPath path, out TValue value, out VisitReturnCode returnCode)
        {
            if (path.IsEmpty)
            {
                returnCode = VisitReturnCode.InvalidPath;
                value = default;
                return false;
            }
            var visitor = GetValueVisitor<TValue>.Pool.Get();
            visitor.Path = path;
            visitor.ReadonlyVisit = true;
            try
            {
                if (!TryAccept(visitor, ref container, out returnCode))
                {
                    value = default;
                    return false;
                }
                value = visitor.Value;
                returnCode = visitor.ReturnCode;
            }
            finally
            {
                GetValueVisitor<TValue>.Pool.Release(visitor);
            }
            return returnCode == VisitReturnCode.Ok;
        }

        public static bool IsPathValid<TContainer>(TContainer container, string path) => IsPathValid(ref container, new PropertyPath(path));

        public static bool IsPathValid<TContainer>(TContainer container, in PropertyPath path) => IsPathValid(ref container, path);

        public static bool IsPathValid<TContainer>(ref TContainer container, string path) => IsPathValid(ref container, new PropertyPath(path));

        public static bool IsPathValid<TContainer>(ref TContainer container, in PropertyPath path)
        {
            var visitor = ExistsAtPathVisitor.Pool.Get();
            try
            {
                visitor.Path = path;
                TryAccept(visitor, ref container);
                return visitor.Exists;
            }
            finally
            {
                ExistsAtPathVisitor.Pool.Release(visitor);
            }
        }

        public static void SetValue<TContainer, TValue>(TContainer container, string name, TValue value) => SetValue(ref container, name, value);

        public static void SetValue<TContainer, TValue>(ref TContainer container, string name, TValue value) =>
            SetValue(ref container, new PropertyPath(name), value);

        public static void SetValue<TContainer, TValue>(TContainer container, in PropertyPath path, TValue value) => SetValue(ref container, path, value);

        public static void SetValue<TContainer, TValue>(ref TContainer container, in PropertyPath path, TValue value)
        {
            if (path.Length == 0) throw new ArgumentNullException("path");
            if (TrySetValue(ref container, path, value, out var code)) return;
            throw code switch
            {
                VisitReturnCode.NullContainer => new ArgumentNullException("container"),
                VisitReturnCode.InvalidContainerType => new InvalidContainerTypeException(container.GetType()),
                VisitReturnCode.MissingPropertyBag => new MissingPropertyBagException(container.GetType()),
                VisitReturnCode.InvalidCast => new InvalidCastException($"Failed to SetValue of Type=[{typeof(TValue).Name}] for property with path=[{path}]"),
                VisitReturnCode.InvalidPath => new InvalidPathException($"Failed to SetValue for property with Path=[{path}]"),
                VisitReturnCode.AccessViolation => new AccessViolationException($"Failed to SetValue for read-only property with Path=[{path}]"),
                _ => new Exception($"Unexpected {nameof(VisitReturnCode)}=[{code}]"),
            };
        }

        public static bool TrySetValue<TContainer, TValue>(TContainer container, string name, TValue value) => TrySetValue(ref container, name, value);

        public static bool TrySetValue<TContainer, TValue>(ref TContainer container, string name, TValue value) =>
            TrySetValue(ref container, new PropertyPath(name), value);

        public static bool TrySetValue<TContainer, TValue>(TContainer container, in PropertyPath path, TValue value) => TrySetValue(ref container, path, value);

        public static bool TrySetValue<TContainer, TValue>(ref TContainer container, in PropertyPath path, TValue value) =>
            TrySetValue(ref container, path, value, out _);

        public static bool TrySetValue<TContainer, TValue>(ref TContainer container, in PropertyPath path, TValue value, out VisitReturnCode returnCode)
        {
            if (path.IsEmpty)
            {
                returnCode = VisitReturnCode.InvalidPath;
                return false;
            }
            var visitor = SetValueVisitor<TValue>.Pool.Get();
            visitor.Path = path;
            visitor.Value = value;
            try
            {
                if (!TryAccept(visitor, ref container, out returnCode)) return false;
                returnCode = visitor.ReturnCode;
            }
            finally
            {
                SetValueVisitor<TValue>.Pool.Release(visitor);
            }
            return returnCode == VisitReturnCode.Ok;
        }
    }
}
