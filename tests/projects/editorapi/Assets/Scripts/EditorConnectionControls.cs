using System;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine.Events;
using Message = UnityEngine.Networking.PlayerConnection.MessageEventArgs;
public static class EditorConnectionControls
{
    public class Receiver : UnityEngine.ScriptableObject
    {
        public int hits;
        public void Message(Message data) { hits++; }
        public int states;
        public void State(int value) { states+=value; }
    }
    static void StaticMessage(Message data) { }
    static int staticStates;
    static void StaticState(int value) { staticStates+=value; }
    public static string BeforePlay()
    {
        var receiver=UnityEngine.ScriptableObject.CreateInstance<Receiver>();
        try
        {
            var evt=new UnityEngine.Events.UnityEvent<int>();
            var method=evt.GetType().GetMethod("AddPersistentListener",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(UnityAction<int>),typeof(UnityEngine.Events.UnityEventCallState)},null);
            method.Invoke(evt,new object[]{(UnityAction<int>)receiver.State,UnityEngine.Events.UnityEventCallState.RuntimeOnly});
            evt.Invoke(3);int runtimeOnly=receiver.states;
            evt.SetPersistentListenerState(0,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            evt.Invoke(5);return runtimeOnly+":"+receiver.states;
        }
        finally {UnityEngine.Object.DestroyImmediate(receiver);}
    }
    public static void Run(Action<string,Func<object>> check)
    {
        var c=EditorConnection.instance;
        Guid id=new Guid("1030abdd-f7d1-4e36-a3c7-ae59f8b36b9d"), cold=new Guid("010899a6-c1ef-40be-955b-9a3ea8c89122");
        UnityAction<Message> handler=m=>{}; UnityAction<int> state=i=>{};
        check("editorConnection.peers",()=>c.ConnectedPlayers.Count);
        if(c.ConnectedPlayers.Count!=0)throw new InvalidOperationException("No-player control has an attached peer");
        check("editorConnection.singleton",()=>ReferenceEquals(c,EditorConnection.instance));
        check("editorConnection.initialize",()=>{c.Initialize();return "ok";});
        check("editorConnection.register",()=>{c.Register(id,handler);return "ok";});
        check("editorConnection.registerNull",()=>{c.Register(id,null);return "ok";});
        check("editorConnection.registerEmpty",()=>{c.Register(Guid.Empty,handler);return "ok";});
        check("editorConnection.send",()=>{c.Send(id,new byte[]{1,2});return "ok";});
        check("editorConnection.sendNull",()=>{c.Send(id,null);return "ok";});
        check("editorConnection.trySend",()=>c.TrySend(id,new byte[]{1,2}));
        check("editorConnection.trySendNull",()=>c.TrySend(id,null));
        check("editorConnection.trySendEmpty",()=>c.TrySend(Guid.Empty,null));
        check("editorConnection.subscribe",()=>{c.RegisterConnection(state);c.RegisterDisconnection(state);return "ok";});
        check("editorConnection.unregister",()=>{c.Unregister(id,handler);return "ok";});
        check("editorConnection.unregisterNull",()=>{c.Unregister(id,null);return "ok";});
        check("editorConnection.unregisterUnknown",()=>{c.Unregister(cold,handler);return "ok";});
        check("editorConnection.unsubscribe",()=>{c.UnregisterConnection(state);c.UnregisterDisconnection(state);return "ok";});
        check("editorConnection.unsubscribeNull",()=>{c.UnregisterConnection(null);return "ok";});
        var t=new System.Threading.Thread(()=>{
            check("editorConnection.thread.initialize",()=>{c.Initialize();return "ok";});
            check("editorConnection.thread.send",()=>{c.Send(id,null);return "ok";});
            check("editorConnection.thread.trySend",()=>c.TrySend(id,null));
            check("editorConnection.thread.disconnect",()=>{c.DisconnectAll();return "ok";});
            check("editorConnection.thread.registerCold",()=>{c.Register(cold,handler);return "ok";});
        });t.Start();t.Join();
        var receiver=UnityEngine.ScriptableObject.CreateInstance<Receiver>();
        Guid proper=new Guid("bbf7907d-4567-4139-aa4e-4569f4c3951d");
        var scalar=new UnityEngine.Events.UnityEvent<int>();
        var addPersistent=scalar.GetType().GetMethod("AddPersistentListener",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(UnityAction<int>),typeof(UnityEngine.Events.UnityEventCallState)},null);
        check("editorConnection.persistent.instance",()=>{
            addPersistent.Invoke(scalar,new object[]{(UnityAction<int>)receiver.State,UnityEngine.Events.UnityEventCallState.EditorAndRuntime});
            var group=typeof(UnityEngine.Events.UnityEventBase).GetField("m_PersistentCalls",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(scalar);
            var entry=group.GetType().GetMethod("GetListener").Invoke(group,new object[]{0});
            entry.GetType().GetField("m_TargetAssemblyTypeName",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(entry,null);
            int before=receiver.states;scalar.Invoke(9);return scalar.GetPersistentEventCount()+":"+(scalar.GetPersistentTarget(0)==receiver)+":"+(receiver.states-before);
        });
        check("editorConnection.persistent.static",()=>{
            var evt=new UnityEngine.Events.UnityEvent<int>();addPersistent.Invoke(evt,new object[]{(UnityAction<int>)StaticState,UnityEngine.Events.UnityEventCallState.EditorAndRuntime});int before=staticStates;evt.Invoke(4);return evt.GetPersistentEventCount()+":"+(evt.GetPersistentTarget(0)==null)+":"+(staticStates-before);
        });
        check("editorConnection.player.default",()=>{var p=new ConnectedPlayer();return p.playerId+":"+(p.name??"<null>");});
        check("editorConnection.player.id",()=>{var p=new ConnectedPlayer(17);return p.playerId+":"+(p.name??"<null>");});
        check("editorConnection.player.named",()=>{var p=new ConnectedPlayer(18,"peer");return p.playerId+":"+p.name;});
        check("editorConnection.connectedReplay",()=>{
            c.ConnectedPlayers.Add(new ConnectedPlayer(3,"control"));
            c.ConnectedPlayers.Add(new ConnectedPlayer(7,"control"));
            try{int before=receiver.states;c.RegisterConnection(receiver.State);return receiver.states-before;}
            finally{c.ConnectedPlayers.Clear();c.UnregisterConnection(receiver.State);}
        });
        check("editorConnection.object.register",()=>{c.Register(proper,receiver.Message);return "ok";});
        var incoming=typeof(EditorConnection).GetMethod("MessageCallbackInternal",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        Func<object> deliver=()=>{int before=receiver.hits;incoming.Invoke(null,new object[]{IntPtr.Zero,(ulong)0,(ulong)1,proper.ToString()});return receiver.hits-before;};
        check("editorConnection.object.deliverOne",deliver);
        check("editorConnection.object.registerDuplicate",()=>{c.Register(proper,receiver.Message);return "ok";});
        check("editorConnection.object.deliverDuplicate",deliver);
        check("editorConnection.object.unregister",()=>{c.Unregister(proper,receiver.Message);return "ok";});
        check("editorConnection.object.deliverAfterOneUnregister",deliver);
        check("editorConnection.object.unregisterAgain",()=>{c.Unregister(proper,receiver.Message);return "ok";});
        check("editorConnection.object.deliverAfterAllUnregister",deliver);
        check("editorConnection.static.register",()=>{c.Register(proper,StaticMessage);return "ok";});
        check("editorConnection.static.unregister",()=>{c.Unregister(proper,StaticMessage);return "ok";});
        check("editorConnection.object.subscribe",()=>{c.RegisterConnection(receiver.State);c.RegisterDisconnection(receiver.State);return "ok";});
        check("editorConnection.object.unsubscribe",()=>{c.UnregisterConnection(receiver.State);c.UnregisterDisconnection(receiver.State);return "ok";});
        check("editorConnection.static.subscribe",()=>{c.RegisterConnection(StaticState);c.RegisterDisconnection(StaticState);return "ok";});
        check("editorConnection.static.unsubscribe",()=>{c.UnregisterConnection(StaticState);c.UnregisterDisconnection(StaticState);return "ok";});
        UnityEngine.Object.DestroyImmediate(receiver);
        check("editorConnection.persistent.destroyed",()=>{int before=receiver.states;scalar.Invoke(7);return receiver.states-before;});
        check("editorConnection.disconnect",()=>{c.DisconnectAll();return c.ConnectedPlayers.Count;});
    }
}
