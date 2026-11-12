using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEditor;

public sealed class DefaultStateProbe : MonoBehaviour
{
    [Serializable] public sealed class Candidate { public string type, module, category, status; }
    [Serializable] public sealed class Manifest { public Candidate[] types; }
    [Serializable] public sealed class Record { public string key, status, value; }
    sealed class Identity : IEqualityComparer<object>
    { public new bool Equals(object a, object b) { return ReferenceEquals(a,b); } public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); } }
    static StreamWriter output;
    static string progress;
    static Candidate[] candidates;
    static int index;
    static readonly Dictionary<Type,MemberInfo[]> members = new Dictionary<Type,MemberInfo[]>();
    static readonly Identity identity = new Identity();

    [RuntimeInitializeOnLoadMethod]
    static void Install()
    {
        var path = Environment.GetEnvironmentVariable("DEFAULT_STATE_OUT");
        if (string.IsNullOrEmpty(path)) return;
        output = new StreamWriter(path);
        progress = Environment.GetEnvironmentVariable("DEFAULT_STATE_PROGRESS");
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Environment.GetEnvironmentVariable("DEFAULT_STATE_MANIFEST")));
        candidates = manifest.types;
        new GameObject("DefaultStateRecorder").AddComponent<DefaultStateProbe>();
    }
    void Update()
    {
        for (int step=0;step<4 && index<candidates.Length;step++,index++) Capture(candidates[index]);
        if (index<candidates.Length) return;
        Capture(new Candidate { type=typeof(FreshStateControl).FullName, module=typeof(FreshStateControl).Assembly.GetName().Name,
            category="class", status="candidate" });
        RemovedGetterControls();
        Emit("complete","value","1"); output.Dispose(); output=null;
        EditorApplication.Exit(0);
    }
    static void Emit(string key,string status,string value)
    {
        output.WriteLine(JsonUtility.ToJson(new Record { key=key,status=status,value=value }));
        output.Flush();
    }
    static void RemovedGetterControls()
    {
        foreach (var populated in new[] { false, true })
        {
            var owner = new GameObject("RemovedGetterControl");
            try
            {
                if (populated) owner.AddComponent<Rigidbody>();
                var prefix = "RemovedGetterControl/" + (populated ? "populated" : "empty");
                Emit(prefix + "/GetComponent", "value", (owner.GetComponent<Rigidbody>() != null).ToString());
                foreach (var target in new object[] { owner, owner.transform })
                {
                    var type = target is GameObject ? typeof(GameObject) : typeof(Component);
                    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        var obsolete = (ObsoleteAttribute)Attribute.GetCustomAttribute(property, typeof(ObsoleteAttribute));
                        if (obsolete == null || property.PropertyType != typeof(Component)) continue;
                        var key = prefix + "/" + type.Name + "/" + property.Name;
                        try { property.GetValue(target, null); Emit(key, "value", "returned"); }
                        catch (Exception error)
                        {
                            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                            Emit(key, "getter-threw", error.GetType().FullName + ":" + error.Message);
                        }
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
    }
    static void Progress(string key)
    { if (!string.IsNullOrEmpty(progress)) File.WriteAllText(progress,key); }
    static string Id(Type type)
    {
        if(type.IsArray)return Id(type.GetElementType())+"["+new string(',',type.GetArrayRank()-1)+"]";
        if(type.IsGenericType)return type.GetGenericTypeDefinition().FullName+"<"+string.Join(",",type.GetGenericArguments().Select(Id))+">";
        return type.FullName??type.Name;
    }
    static string Error(Exception error)
    {
        while(error is TargetInvocationException && error.InnerException!=null)error=error.InnerException;
        return error.GetType().FullName;
    }
    static MemberInfo[] Members(Type type)
    {
        MemberInfo[] result;
        if(members.TryGetValue(type,out result))return result;
        result=type.GetFields(BindingFlags.Instance|BindingFlags.Public).Cast<MemberInfo>()
            .Concat(type.GetProperties(BindingFlags.Instance|BindingFlags.Public))
            .OrderBy(m=>Id(m.DeclaringType)+"/"+m.MemberType+"/"+m.Name,StringComparer.Ordinal).ToArray();
        members[type]=result;return result;
    }
    static object Read(MemberInfo member,object target)
    { return member is FieldInfo?((FieldInfo)member).GetValue(target):((PropertyInfo)member).GetValue(target,null); }
    static Type MemberType(MemberInfo member)
    { return member is FieldInfo?((FieldInfo)member).FieldType:((PropertyInfo)member).PropertyType; }
    static void Capture(Candidate candidate)
    {
        var factory=candidate.type+"/@factory";
        if(candidate.status!="candidate") { Emit(factory,"excluded",candidate.status);return; }
        Progress(factory);
        object value=null;GameObject owner=null;
        try
        {
            var type=Type.GetType(candidate.type+", "+candidate.module,false);
            if(type==null) { Emit(factory,"missing-type",candidate.module);return; }
            value=Create(type,candidate.category,out owner);
            if(ReferenceEquals(value,null)) { Emit(factory,"factory-null","");return; }
            Emit(factory,"value",candidate.category);
            foreach(var member in Members(type))
            {
                var key=candidate.type+"/"+member.MemberType+"/"+Id(member.DeclaringType)+"/"+member.Name;
                if(member is PropertyInfo indexed && indexed.GetIndexParameters().Length>0)
                    key+="("+string.Join(",",indexed.GetIndexParameters().Select(parameter=>Id(parameter.ParameterType)))+")";
                var property=member as PropertyInfo;
                if(property!=null && (property.GetGetMethod()==null || property.GetIndexParameters().Length!=0))
                { Emit(key,"not-readable",property.GetGetMethod()==null?"no-public-getter":"indexer-requires-arguments");continue; }
                if (type.FullName == "UnityEngine.ModifiableContactPair" && property != null
                    && (property.Name == "colliderInstanceID" || property.Name == "otherColliderInstanceID" || property.Name == "massProperties"))
                { Emit(key,"requires-context","a live contact-modification callback payload");continue; }
                if (type.FullName == "UnityEngine.Rendering.LocalKeyword" && property != null
                    && (property.Name == "isDynamic" || property.Name == "isOverridable" || property.Name == "type"))
                { Emit(key,"requires-context","a keyword initialized from a shader keyword space");continue; }
                if (type.FullName == "UnityEngine.Rendering.LocalKeywordSpace" && property != null)
                { Emit(key,"requires-context","a keyword space obtained from a shader or compute shader");continue; }
                if (property != null && IsIntegratedDescriptor(type))
                { Emit(key,"requires-context","an integrated subsystem descriptor returned by SubsystemManager");continue; }
                Progress(key);
                // Each readable member observes a new instance, so an allocating or stateful
                // getter cannot change the next member's creation-time observation.
                try
                {
                    if (ReferenceEquals(value,null)) value=Create(type,candidate.category,out owner);
                    if (ReferenceEquals(value,null)) { Emit(key,"factory-null","");continue; }
                    object answer;
                    try { answer=Read(member,value); }
                    catch(Exception error) { Emit(key,"getter-threw",Error(error));continue; }
                    try { Emit(key,"value",Format(answer,MemberType(member),new HashSet<object>(identity),0)); }
                    catch(Exception error) { Emit(key,"format-threw",Error(error)); }
                }
                catch(Exception error) { Emit(key,"factory-threw",Error(error)); }
                finally { Release(value,owner);value=null;owner=null; }

            }
        }
        catch(Exception error) { Emit(factory,"factory-threw",Error(error)); }
        finally { Release(value,owner); }
    }
    static object Create(Type type,string category,out GameObject owner)
    {
        owner=null;
        if(category=="component")
        {
            owner=new GameObject("default-"+type.FullName);
            return owner.AddComponent(type);
        }
        return category=="scriptable"?ScriptableObject.CreateInstance(type):Activator.CreateInstance(type);
    }
    static void Release(object value,GameObject owner)
    {
        if(owner!=null)UnityEngine.Object.DestroyImmediate(owner);
        else if(value is UnityEngine.Object)UnityEngine.Object.DestroyImmediate((UnityEngine.Object)value);
        else if(value is IDisposable)try { ((IDisposable)value).Dispose(); }catch(Exception){}
    }
    static bool IsIntegratedDescriptor(Type type)
    {
        for(var current=type;current!=null;current=current.BaseType)
            if(current.FullName=="UnityEngine.IntegratedSubsystemDescriptor")return true;
        return false;
    }
    static string MemberId(MemberInfo member)
    {
        var prefix=member.MemberType+":"+(member.DeclaringType==null?"":Id(member.DeclaringType)+".")+member.Name;
        var method=member as MethodBase;
        if(method!=null)
        {
            prefix+=(method.IsGenericMethod?"<"+string.Join(",",method.GetGenericArguments().Select(Id))+">":"")
                +"("+string.Join(",",method.GetParameters().Select(parameter=>Id(parameter.ParameterType)))+")";
            if(method is MethodInfo)prefix+=":"+Id(((MethodInfo)method).ReturnType);
        }
        return prefix;
    }
    static string Format(object value,Type declared,HashSet<object> seen,int depth)
    {
        if(ReferenceEquals(value,null))return "null";
        if(value is string)return "string:"+(string)value;
        if(value is bool)return (bool)value?"true":"false";
        if(value is float)return ((float)value).ToString("G9",CultureInfo.InvariantCulture);
        if(value is double)return ((double)value).ToString("G17",CultureInfo.InvariantCulture);
        var type=value.GetType();
        if(type.IsEnum)return "enum:"+((Enum)value).ToString("D");
        if(value is Type)return "type:"+Id((Type)value);
        if(value is MemberInfo)return MemberId((MemberInfo)value);
        if(value is Assembly)return "assembly:"+((Assembly)value).GetName().Name;
        if(value is Module)return "module:"+((Module)value).Name;
        if(value is DateTime)return "date:"+((DateTime)value).Ticks+":"+((DateTime)value).Kind;
        if(value is TimeSpan)return "duration:"+((TimeSpan)value).Ticks;
        if(value is Guid)return "guid:"+((Guid)value).ToString("D");
        if(value is IntPtr)return (IntPtr)value==IntPtr.Zero?"null-pointer":"non-null-pointer";
        if(value is UIntPtr)return (UIntPtr)value==UIntPtr.Zero?"null-pointer":"non-null-pointer";
        if(type.IsPrimitive || value is decimal)return Convert.ToString(value,CultureInfo.InvariantCulture);
        if(value is UnityEngine.Object)
        {
            var obj=(UnityEngine.Object)value;
            string name;
            try { name=obj.name; }catch(Exception e) { name="<"+Error(e)+">"; }
            return "object:"+Id(type)+":"+name;
        }
        if(depth>=5)return "<depth-limit:"+Id(declared)+">";
        if(!type.IsValueType && !seen.Add(value))return "<cycle:"+Id(declared)+">";
        try
        {
            if(value is Delegate)
            {
                return "delegate:["+string.Join(";",((Delegate)value).GetInvocationList().Select(callback=>
                    MemberId(callback.Method)+";target="+Format(callback.Target,callback.Target==null?typeof(object):callback.Target.GetType(),seen,depth+1)))+"]";
            }
            if(value is IEnumerable)
            {
                var text=new StringBuilder("[");bool first=true;
                foreach(var element in (IEnumerable)value)
                { if(!first)text.Append(',');first=false;text.Append(Format(element,element==null?typeof(object):element.GetType(),seen,depth+1)); }
                return text.Append(']').ToString();
            }
            var contract=declared.IsInterface?declared:type;
            var fields=contract.GetFields(BindingFlags.Public|BindingFlags.Instance);
            var allFields=contract.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            var selected=type.IsValueType&&fields.Length>0&&fields.Length==allFields.Length?fields.Cast<MemberInfo>().ToArray():Members(contract);
            var result=new StringBuilder(Id(declared)+"{");bool initial=true;
            foreach(var member in selected)
            {
                var property=member as PropertyInfo;
                if(property!=null&&(property.GetGetMethod()==null||property.GetIndexParameters().Length!=0))continue;
                if(!initial)result.Append(';');initial=false;result.Append(member.Name).Append('=');
                try { result.Append(Format(Read(member,value),MemberType(member),seen,depth+1)); }
                catch(Exception e) { result.Append('<').Append(Error(e)).Append('>'); }
            }
            return result.Append('}').ToString();
        }
        finally { if(!type.IsValueType)seen.Remove(value); }
    }
}

public sealed class FreshStateControl
{
    int state;
    public int field;
    public int AChanges => ++state;
    public int ZReads => state;
    public NestedStateDerived ZNested => new NestedStateDerived();
}

public class NestedStateBase
{
    int answer=7;
    public int Answer => answer;
}
public sealed class NestedStateDerived : NestedStateBase
{
    public int field=2;
}
