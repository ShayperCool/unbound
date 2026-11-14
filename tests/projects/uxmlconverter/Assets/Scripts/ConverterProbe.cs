using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class ConverterProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("ConverterProbe").AddComponent<ConverterProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    sealed class Stateful : UxmlAttributeConverter<int>
    {
        public int bias=17, calls;
        public override int FromString(string value) { calls++; if(value=="bad") throw new FormatException("rejected-control"); return bias+(value==null?0:int.Parse(value,CultureInfo.InvariantCulture)); }
        public override string ToString(int value) { calls++; return "write:"+bias+":"+value; }
    }
    abstract class Parent : UxmlAttributeConverter<int> { public int bias=23; public override int FromString(string value)=>bias+int.Parse(value,CultureInfo.InvariantCulture); }
    sealed class Hidden : Parent { public new int FromString(string value)=>991; }
    sealed class Strings : UxmlAttributeConverter<string> { public override string FromString(string value)=>value??"from-null"; }
    sealed class Doubles : UxmlAttributeConverter<double> { public override double FromString(string value)=>double.Parse(value,CultureInfo.InvariantCulture); }
    sealed class NullableInts : UxmlAttributeConverter<int?> { public override int? FromString(string value)=>value==null?(int?)null:int.Parse(value,CultureInfo.InvariantCulture); }
    Type contract;
    MethodInfo from,to;
    StreamWriter output;
    void Put(string key,object value)=>output.WriteLine(JsonUtility.ToJson(new Row{key=key,value=Convert.ToString(value,CultureInfo.InvariantCulture)}));
    object Call(string key,MethodInfo method,object target,params object[] arguments)
    {
        try {var result=method.Invoke(target,arguments);Put(key+".value",result??"<null>");Put(key+".type",result==null?"null":result.GetType().Name);return result;}
        catch(Exception ex){if(ex is TargetInvocationException wrapper && wrapper.InnerException!=null)ex=wrapper.InnerException;Put(key+".exception",ex.GetType().Name);return null;}
    }
    void Start()
    {
        string path=Environment.GetEnvironmentVariable("CONVERTER_OUT");if(string.IsNullOrEmpty(path))return;
        try {using(output=new StreamWriter(path)){Record();Put("complete",1);}EditorApplication.Exit(0);}
        catch(Exception ex){Debug.LogException(ex);output?.Dispose();EditorApplication.Exit(1);}
    }
    static CreationContext Context(VisualTreeAsset asset)
    {
        object boxed=default(CreationContext);
        foreach(var field in typeof(CreationContext).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            if(field.FieldType==typeof(VisualTreeAsset)) field.SetValue(boxed,asset);
        return (CreationContext)boxed;
    }
    void Record()
    {
        contract=typeof(UxmlAttributeConverter<>).Assembly.GetType("UnityEditor.UIElements.IUxmlAttributeConverter",true);
        from=contract.GetMethod("FromString");to=contract.GetMethod("ToString");
        Put("contract.internal",contract.IsNotPublic);Put("contract.methodCount",contract.GetMethods().Length);
        var first=ScriptableObject.CreateInstance<VisualTreeAsset>();first.name="contextA";
        var second=ScriptableObject.CreateInstance<VisualTreeAsset>();second.name="contextB";
        var converter=new Stateful();Put("state.interface",contract.IsInstanceOfType(converter));
        Call("state.fromA",from,converter,"4",Context(first));Put("state.callsA",converter.calls);
        converter.bias=29;Call("state.fromB",from,converter,"4",Context(second));Put("state.callsB",converter.calls);
        Call("state.fromNull",from,converter,null,Context(first));Put("state.callsNull",converter.calls);
        Call("state.fromBad",from,converter,"bad",Context(first));Put("state.callsBad",converter.calls);
        Call("state.toA",to,converter,7,first);Put("state.callsToA",converter.calls);
        converter.bias=41;Call("state.toB",to,converter,7,second);Put("state.callsToB",converter.calls);
        Call("state.toWrong",to,converter,"wrong",first);Put("state.callsWrong",converter.calls);
        Call("state.toNull",to,converter,null,first);Put("state.callsToNull",converter.calls);
        var hidden=new Hidden();Put("hidden.direct",hidden.FromString("7"));Put("hidden.typed",((UxmlAttributeConverter<int>)hidden).FromString("7"));
        Call("hidden.interface",from,hidden,"7",default(CreationContext));hidden.bias=47;Call("hidden.changed",from,hidden,"7",default(CreationContext));
        var strings=new Strings();Call("string.from",from,strings,"seeded",default(CreationContext));Call("string.fromNull",from,strings,null,default(CreationContext));
        Call("string.to",to,strings,"seeded",first);Call("string.toNull",to,strings,null,second);Call("string.toWrong",to,strings,17,first);
        var nullable=new NullableInts();Call("nullable.fromNull",from,nullable,null,default(CreationContext));Call("nullable.from",from,nullable,"37",default(CreationContext));
        Call("nullable.toNull",to,nullable,null,first);Call("nullable.to",to,nullable,37,second);Call("nullable.toWrong",to,nullable,37L,first);
        var previous=CultureInfo.CurrentCulture;
        try {foreach(var name in new[]{"en-US","fr-FR"}){CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(name);var doubles=new Doubles();Call("culture."+name+".from",from,doubles,"1.25",default(CreationContext));Call("culture."+name+".to",to,doubles,1.25,first);}}
        finally {CultureInfo.CurrentCulture=previous;}
        UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);
    }
}
