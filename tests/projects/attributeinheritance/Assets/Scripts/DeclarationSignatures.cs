using System;
using System.Linq;
using System.Reflection;

public static class DeclarationSignatures
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    static string TypeKey(Type type)
    {
        if (type.IsByRef) return TypeKey(type.GetElementType());
        if (type.IsArray) return TypeKey(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsPointer) return TypeKey(type.GetElementType()) + "*";
        if (type.IsGenericParameter) return (type.DeclaringMethod == null ? "!" : "!!") + type.GenericParameterPosition;
        if (!type.IsGenericType) return type.FullName;
        Type definition = type.GetGenericTypeDefinition();
        Type[] arguments = type.GetGenericArguments();
        string prefix = definition.FullName;
        return prefix + "<" + string.Join(",", arguments.Select(TypeKey).ToArray()) + ">";
    }
    static string Parameters(ParameterInfo[] parameters)
    {
        return string.Join(",", parameters.Select(p => (p.ParameterType.IsByRef ? p.IsOut ? "out " : p.IsIn ? "in " : "ref " : "") + TypeKey(p.ParameterType)).ToArray());
    }
    public static bool IsReadOnly(MemberInfo member) => member.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
    public static string Key(MemberInfo member)
    {
        if (member is MethodBase)
        {
            var method = (MethodBase)member;
            var function = member as MethodInfo;
            return "M:" + (method.IsStatic ? "static " : "") + method.Name + "`" + (method.IsGenericMethod ? method.GetGenericArguments().Length : 0) + "(" + Parameters(method.GetParameters()) + "):" + (function == null ? "System.Void" : TypeKey(function.ReturnType));
        }
        if (member is PropertyInfo)
        {
            var property = (PropertyInfo)member;
            var accessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
            return "P:" + (accessor.IsStatic ? "static " : "") + (property.GetIndexParameters().Length == 0 ? property.Name : "this[]") + "(" + Parameters(property.GetIndexParameters()) + "):" + TypeKey(property.PropertyType);
        }
        if (member is FieldInfo)
        {
            var field = (FieldInfo)member;
            return "F:" + (field.IsStatic ? "static " : "") + (field.IsLiteral ? "const " : "") + field.Name + ":" + TypeKey(field.FieldType);
        }
        if (member is EventInfo)
        {
            var evt = (EventInfo)member;
            return "E:" + (evt.GetAddMethod(true).IsStatic ? "static " : "") + evt.Name + ":" + TypeKey(evt.EventHandlerType);
        }
        throw new InvalidOperationException("Unsupported declaration member " + member);
    }
    public static MemberInfo Find(string typeName, string module, string key)
    {
        Type type = Type.GetType(typeName + ", " + module, true);
        return key == "type" ? (MemberInfo)type : type.GetMembers(All).Where(m => m is MethodBase || m is PropertyInfo || m is FieldInfo || m is EventInfo).Single(m => Key(m) == key);
    }
}
