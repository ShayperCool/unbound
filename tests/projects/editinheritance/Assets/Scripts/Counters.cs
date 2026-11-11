using System.Collections.Generic;
using UnityEngine;

public abstract class CallbackCounter : MonoBehaviour
{
    public readonly List<string> calls = new List<string>();
    protected void Awake() { calls.Add("Awake"); }
    protected void OnEnable() { calls.Add("OnEnable"); }
    protected void Update() { if (!calls.Contains("Update")) calls.Add("Update"); }
    protected void OnDisable() { calls.Add("OnDisable"); }
}
[ExecuteInEditMode] public abstract class EditBase : CallbackCounter { }
[ExecuteAlways] public abstract class AlwaysBase : CallbackCounter { }
[ExecuteInEditMode] public abstract class GenericBase<T> : CallbackCounter { }
public class EditDerived : EditBase { }
public class AlwaysDerived : AlwaysBase { }
public class GenericDerived : GenericBase<GenericDerived> { }
public class PlainCounter : CallbackCounter { }
[ExecuteAlways] public class DirectCounter : CallbackCounter { }
