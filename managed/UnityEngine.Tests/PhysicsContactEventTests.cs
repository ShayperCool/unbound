using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Xunit;

namespace UnityEngine.Tests
{
    public class PhysicsContactEventTests : IDisposable
    {
        readonly List<GameObject> objects = new List<GameObject>();
        readonly List<Physics.ContactEventDelegate> handlers = new List<Physics.ContactEventDelegate>();
        public PhysicsContactEventTests()
        {
            Physics.simulationMode=SimulationMode.Script;
            Physics.gravity=Vector3.zero;
            Physics.invokeCollisionCallbacks=true;
            Physics.autoSyncTransforms=false;
        }
        Collider Box(string name, Vector3 position, bool dynamic)
        {
            var go=new GameObject(name);objects.Add(go);go.transform.position=position;
            if(dynamic){var rb=go.AddComponent<Rigidbody>();rb.useGravity=false;}
            var collider=go.AddComponent<BoxCollider>();collider.providesContacts=true;return collider;
        }
        void Subscribe(Physics.ContactEventDelegate handler){handlers.Add(handler);Physics.ContactEvent+=handler;}
        public void Dispose()
        {
            foreach(var handler in handlers)Physics.ContactEvent-=handler;
            foreach(var go in objects)Object.DestroyImmediate(go);
            Physics.invokeCollisionCallbacks=true;
        }
        [Fact]
        public void LayoutMatchesInstalledUnity()
        {
            Assert.Equal(40,Marshal.SizeOf<ContactPairHeader>());
            Assert.Equal(40,Marshal.SizeOf<ContactPair>());
            Assert.Equal(48,Marshal.SizeOf<ContactPairPoint>());
            Assert.Equal(8,Marshal.OffsetOf<ContactPairHeader>("m_StartPtr").ToInt32());
            Assert.Equal(24,Marshal.OffsetOf<ContactPair>("m_ImpulseSum").ToInt32());
            Assert.Equal(32,Marshal.OffsetOf<ContactPairPoint>("m_Impulse").ToInt32());
        }
        [Fact]
        public void DefaultIndexedAccessThrows()
        {
            Assert.Throws<IndexOutOfRangeException>(()=>default(ContactPairHeader).GetContactPair(0));
            Assert.Throws<IndexOutOfRangeException>(()=>default(ContactPair).GetContactPoint(0));
        }
        [Fact]
        public void PublicReportHasActualIdentityAndSdkPoints()
        {
            var a=Box("contactA",Vector3.zero,true);var b=Box("contactB",Vector3.right*.75f,false);
            int reports=0;bool checkedReport=false;
            Subscribe((scene,headers)=>
            {
                reports++;
                Assert.Equal(Physics.defaultPhysicsScene,scene);
                for(int i=0;i<headers.Length;i++)
                {
                    ref readonly var header=ref headers.UnsafeElementAt(i);
                    for(int j=0;j<header.pairCount;j++)
                    {
                        ref readonly var pair=ref header.GetContactPair(j);
                        if(pair.collider!=a && pair.otherCollider!=a)continue;
                        checkedReport=true;
                        Assert.Equal(a.attachedRigidbody.GetInstanceID(),header.bodyInstanceID);
                        Assert.Same(a.attachedRigidbody,header.body);
                        Assert.Equal(0,header.otherBodyInstanceID);
                        Assert.Equal(pair.collider.GetInstanceID(),pair.colliderInstanceID);
                        Assert.Equal(pair.otherCollider.GetInstanceID(),pair.otherColliderInstanceID);
                        Assert.True(pair.isCollisionEnter);Assert.False(pair.isCollisionStay);Assert.False(pair.isCollisionExit);
                        Assert.Equal(4,pair.contactCount);
                        var sum=Vector3.zero;
                        using var copy=new NativeArray<ContactPairPoint>(pair.contactCount,Allocator.Temp);
                        pair.CopyToNativeArray(copy);
                        for(int p=0;p<pair.contactCount;p++)
                        {
                            ref readonly var point=ref pair.GetContactPoint(p);
                            Assert.Equal(point,copy[p]);Assert.Equal(uint.MaxValue,pair.GetContactPointFaceIndex(p));
                            Assert.InRange(point.normal.magnitude,.99999f,1.00001f);
                            Assert.InRange(point.separation,-.25001f,-.24999f);
                            sum+=point.impulse;
                        }
                        Assert.Equal(sum,pair.impulseSum);
                        Assert.True(pair.impulseSum.sqrMagnitude>0);
                    }
                }
            });
            Physics.Simulate(.02f);Assert.Equal(1,reports);Assert.True(checkedReport);
        }
        [Fact]
        public void BorrowedArrayIsInvalidAfterSimulate()
        {
            Box("contactA",Vector3.zero,true);Box("contactB",Vector3.right*.75f,false);
            NativeArray<ContactPairHeader>.ReadOnly saved=default;bool seen=false;
            Subscribe((scene,headers)=>{saved=headers;seen=true;Assert.True(headers.IsCreated);Assert.True(headers[0].pairCount>0);});
            Physics.Simulate(.02f);Assert.True(seen);Assert.True(saved.IsCreated);Assert.True(saved.Length>0);
            Assert.Throws<ObjectDisposedException>(()=>saved[0]);
        }
        [Fact]
        public void InvokeCollisionCallbacksDoesNotSuppressContactEvent()
        {
            Box("contactA",Vector3.zero,true);Box("contactB",Vector3.right*.75f,false);
            Physics.invokeCollisionCallbacks=false;int calls=0;
            Subscribe((scene,headers)=>calls++);Physics.Simulate(.02f);Assert.Equal(1,calls);
        }
        [Fact]
        public void CompoundShapesShareAnActorHeader()
        {
            var a=Box("contactA",Vector3.zero,true);Box("contactB",Vector3.right*.75f,false);
            var child=new GameObject("contactChild");child.transform.SetParent(a.transform,false);child.transform.localPosition=Vector3.up*.2f;
            var extra=child.AddComponent<BoxCollider>();extra.providesContacts=true;bool seen=false;
            Subscribe((scene,headers)=>
            {
                for(int i=0;i<headers.Length;i++)
                {
                    var h=headers[i];if(h.body!=a.attachedRigidbody && h.otherBody!=a.attachedRigidbody)continue;
                    seen=true;Assert.Equal(2,h.pairCount);
                    var shapes=new HashSet<Collider>();for(int j=0;j<h.pairCount;j++){var p=h.GetContactPair(j);shapes.Add(p.collider);shapes.Add(p.otherCollider);}
                    Assert.Contains(a,shapes);Assert.Contains(extra,shapes);
                }
            });
            Physics.Simulate(.02f);Assert.True(seen);
        }
        [Fact]
        public void ShortCopyTruncatesAndLongCopyLeavesTail()
        {
            Box("contactA",Vector3.zero,true);Box("contactB",Vector3.right*.75f,false);bool seen=false;
            Subscribe((scene,headers)=>
            {
                for(int i=0;i<headers.Length;i++)for(int j=0;j<headers[i].pairCount;j++)
                {
                    var p=headers[i].GetContactPair(j);if(p.contactCount==0)continue;seen=true;
                    using var shortCopy=new NativeArray<ContactPairPoint>(1,Allocator.Temp);
                    using var longCopy=new NativeArray<ContactPairPoint>(p.contactCount+1,Allocator.Temp);
                    p.CopyToNativeArray(shortCopy);p.CopyToNativeArray(longCopy);
                    Assert.Equal(p.GetContactPoint(0),shortCopy[0]);Assert.Equal(default(ContactPairPoint),longCopy[p.contactCount]);
                }
            });
            Physics.Simulate(.02f);Assert.True(seen);
        }
    }
}
