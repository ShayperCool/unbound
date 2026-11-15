using System;
using System.Reflection;
using UnityEditor;

public static class MenuControls
{
    const string Script = "Tools/Registry probe/Validated command";
    const string Dynamic = "Tools/Registry probe/Dynamic command";
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    const string Hotkey = "Tools/Registry probe/Hotkey command";
    static int hotkeyCalls, hotkeyValidations;
    [MenuItem(Hotkey + " _x")] static void HotkeyExecute() { hotkeyCalls++; }
    [MenuItem(Hotkey, true)] static bool HotkeyValidate() { hotkeyValidations++;return false; }
    static bool enabled;
    static int calls, validations;
    [MenuItem(Script)] static void Execute() { calls++; }
    [MenuItem(Script, true)] static bool Validate() { validations++; return enabled; }
    public static string BuiltinState() => Menu.GetEnabled("Edit/Play")+":"+Menu.GetEnabled("Edit/Pause")+":"+Menu.GetEnabled("Edit/Step");
    public static void Run(Action<string,Func<object>> check)
    {
        check("menus.builtin.playing",()=>BuiltinState());
        check("menus.builtin.checked",()=>Menu.GetChecked("Edit/Play")+":"+Menu.GetChecked("Edit/Pause")+":"+Menu.GetChecked("Edit/Step"));
        check("menus.hotkey.enabled",()=>Menu.GetEnabled(Hotkey));
        check("menus.hotkey.execute",()=>EditorApplication.ExecuteMenuItem(Hotkey)+":"+hotkeyCalls+":"+hotkeyValidations);
        check("menus.script.disabled", () => Menu.GetEnabled(Script));
        check("menus.script.validates", () => validations > 0);
        check("menus.script.enabled", () => { enabled = true; return Menu.GetEnabled(Script); });
        check("menus.script.execEnabled", () => { int before=calls;return EditorApplication.ExecuteMenuItem(Script)+":"+(calls-before); });
        check("menus.script.disabledAgain", () => { enabled=false;return Menu.GetEnabled(Script); });
        check("menus.script.execDisabled", () => { int before=calls;return EditorApplication.ExecuteMenuItem(Script)+":"+(calls-before); });
        check("menus.script.checkDisabled", () => { Menu.SetChecked(Script,true);return Menu.GetChecked(Script); });
        check("menus.unknown", () => Menu.GetEnabled("Tools/Registry probe/No such command"));
        check("menus.empty", () => Menu.GetEnabled(""));
        check("menus.null", () => Menu.GetEnabled(null));
        MethodInfo add=typeof(Menu).GetMethod("AddMenuItem",All), remove=typeof(Menu).GetMethod("RemoveMenuItem",All);
        int dynamicCalls=0, dynamicValidations=0;bool dynamicEnabled=false;
        Action exec=()=>dynamicCalls++;
        Func<bool> validate=()=>{dynamicValidations++;return dynamicEnabled;};
        check("menus.dynamic.add", () => {add.Invoke(null,new object[]{Dynamic,"",true,200,exec,validate});return "ok";});
        check("menus.dynamic.checked", () => Menu.GetChecked(Dynamic));
        check("menus.dynamic.disabled", () => Menu.GetEnabled(Dynamic));
        check("menus.dynamic.execDisabled", () => {int before=dynamicCalls;return EditorApplication.ExecuteMenuItem(Dynamic)+":"+(dynamicCalls-before);});
        check("menus.dynamic.enabled", () => {dynamicEnabled=true;return Menu.GetEnabled(Dynamic);});
        check("menus.dynamic.exec", () => EditorApplication.ExecuteMenuItem(Dynamic)+":"+dynamicCalls);
        check("menus.dynamic.validates", () => dynamicValidations>0);
        check("menus.dynamic.parent", () => Menu.GetEnabled("Tools/Registry probe"));
        check("menus.dynamic.parentChecked", () => {Menu.SetChecked("Tools/Registry probe",true);return Menu.GetChecked("Tools/Registry probe");});
        check("menus.dynamic.replace", () => {add.Invoke(null,new object[]{Dynamic,"",false,200,(Action)(()=>dynamicCalls+=10),null});int before=dynamicCalls;return Menu.GetChecked(Dynamic)+":"+EditorApplication.ExecuteMenuItem(Dynamic)+":"+(dynamicCalls-before);});
        check("menus.dynamic.throwingValidator", () => {add.Invoke(null,new object[]{Dynamic,"",false,200,exec,(Func<bool>)(()=>throw new InvalidOperationException("validation-control"))});return Menu.GetEnabled(Dynamic);});
        check("menus.dynamic.remove", () => {remove.Invoke(null,new object[]{Dynamic});return Menu.GetChecked(Dynamic);});
        check("menus.dynamic.removedEnabled", () => Menu.GetEnabled(Dynamic));
        check("menus.dynamic.removedExec", () => {int before=dynamicCalls;return EditorApplication.ExecuteMenuItem(Dynamic)+":"+(dynamicCalls-before);});
        check("menus.dynamic.freshThrowingValidator",()=>{add.Invoke(null,new object[]{Dynamic+" throw","",false,200,exec,(Func<bool>)(()=>throw new InvalidOperationException("validation-control"))});try{return Menu.GetEnabled(Dynamic+" throw");}finally{remove.Invoke(null,new object[]{Dynamic+" throw"});}});
        check("menus.dynamic.removeUnknown", () => {remove.Invoke(null,new object[]{Dynamic});return "ok";});
        check("menus.tree.add",()=>{add.Invoke(null,new object[]{"Ephemeral probe/Branch/Command","",false,200,exec,null});return Menu.GetEnabled("Ephemeral probe/Branch");});
        check("menus.tree.removeLeaf",()=>{remove.Invoke(null,new object[]{"Ephemeral probe/Branch/Command"});return Menu.GetEnabled("Ephemeral probe/Branch");});
        check("menus.tree.readd",()=>{add.Invoke(null,new object[]{"Ephemeral probe/Branch/Command","",false,200,exec,null});return "ok";});
        check("menus.tree.removeParent",()=>{remove.Invoke(null,new object[]{"Ephemeral probe/Branch"});return Menu.GetEnabled("Ephemeral probe/Branch/Command");});
        remove.Invoke(null,new object[]{"Ephemeral probe"});
        int beforeThreadCalls=calls;
        var thread=new System.Threading.Thread(()=>{
            check("menus.thread.execute",()=>EditorApplication.ExecuteMenuItem(Script));
            check("menus.thread.getEnabled",()=>Menu.GetEnabled(Script));
            check("menus.thread.add",()=>{add.Invoke(null,new object[]{Dynamic,"",false,200,exec,null});return "ok";});
            check("menus.thread.remove",()=>{remove.Invoke(null,new object[]{Script});return "ok";});
        });thread.Start();thread.Join();
        check("menus.thread.unchanged",()=>Menu.GetChecked(Script)+":"+Menu.GetChecked(Dynamic));
        check("menus.thread.executeUnchanged",()=>calls==beforeThreadCalls);
        Menu.SetChecked(Script,false);
    }
}
