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
        readonly List<Exception> callbackFailures = new List<Exception>();
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
        void Subscribe(Physics.ContactEventDelegate handler)
        {
            Physics.ContactEventDelegate checkedHandler = (scene, headers) =>
            { try { handler(scene, headers); } catch (Exception e) { callbackFailures.Add(e); } };
            handlers.Add(checkedHandler);Physics.ContactEvent+=checkedHandler;
        }
        public void Dispose()
        {
            foreach(var handler in handlers)Physics.ContactEvent-=handler;
            foreach(var go in objects)Object.DestroyImmediate(go);
            Physics.invokeCollisionCallbacks=true;
            Assert.Empty(callbackFailures);
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
        [Fact]
        public void DefaultCapsuleUsesMeasuredComponentHeight()
        {
            var go=new GameObject("capsuleDefault");objects.Add(go);
            var c=go.AddComponent<CapsuleCollider>();
            Assert.Equal(1f,c.height);Assert.Equal(.5f,c.radius);Assert.Equal(1,c.direction);
            var primitive=GameObject.CreatePrimitive(PrimitiveType.Capsule);objects.Add(primitive);
            Assert.Equal(2f,primitive.GetComponent<CapsuleCollider>().height);
            var cylinder=GameObject.CreatePrimitive(PrimitiveType.Cylinder);objects.Add(cylinder);
            Assert.Equal(2f,cylinder.GetComponent<CapsuleCollider>().height);
        }
        [Theory]
        [InlineData(0,1f)] [InlineData(1,1f)] [InlineData(2,1f)]
        [InlineData(0,2f)] [InlineData(1,2f)] [InlineData(2,2f)]
        public void CapsuleKeepsMeasuredEndpointsAndSdkPointOrder(int axis,float height)
        {
            var a=new GameObject("capsuleA");objects.Add(a);
            var c=a.AddComponent<CapsuleCollider>();c.direction=axis;c.height=height;c.providesContacts=true;
            var direction=axis==0 ? Vector3.right : axis==1 ? Vector3.up : Vector3.forward;
            Physics.SyncTransforms();
            Assert.True(c.Raycast(new Ray(direction*3,-direction),out var hit,10));
            float half=height==1f ? .000005f : .5f;
            Assert.InRange(hit.point[axis],.5f+half-1e-6f,.5f+half+1e-6f);
            var b=new GameObject("capsuleB");objects.Add(b);
            b.transform.position=(axis==0 ? Vector3.forward : Vector3.right)*.75f;
            b.AddComponent<Rigidbody>().useGravity=false;
            var d=b.AddComponent<CapsuleCollider>();d.direction=axis;d.height=height;d.providesContacts=true;
            bool seen=false;
            Subscribe((scene,headers)=>
            {
                for(int i=0;i<headers.Length;i++)for(int j=0;j<headers[i].pairCount;j++)
                {
                    var pair=headers[i].GetContactPair(j);
                    if(pair.collider!=c && pair.otherCollider!=c)continue;
                    seen=true;Assert.Equal(height==1f ? 1 : axis==0 ? 4 : 2,pair.contactCount);
                    float first=axis==0 ? half : -half;
                    Assert.InRange(pair.GetContactPoint(0).position[axis],first-1e-6f,first+1e-6f);
                }
            });
            Physics.Simulate(.02f);Assert.True(seen);
        }
        public sealed class DestroyReceiver : MonoBehaviour
        {
            internal static bool Requested,Alive;
            void OnCollisionEnter(Collision c)
            {
                if(!Requested)return;
                Object.DestroyImmediate(gameObject);Alive=gameObject!=null;
            }
        }
        [Theory] [InlineData(true)] [InlineData(false)]
        public void ImmediateGameObjectDestructionIsRejectedDuringContactCallbacks(bool publicCallback)
        {
            Box("guardA",Vector3.zero,false);var b=Box("guardB",Vector3.right*.75f,true);
            b.gameObject.AddComponent<DestroyReceiver>();DestroyReceiver.Requested=!publicCallback;DestroyReceiver.Alive=false;
            bool publicAlive=false;
            if(publicCallback)Subscribe((scene,headers)=>{Object.DestroyImmediate(b.gameObject);publicAlive=b.gameObject!=null;});
            try
            {
                Physics.Simulate(.02f);Assert.True(b!=null);
                Assert.True(publicCallback ? publicAlive : DestroyReceiver.Alive);
            }
            finally { DestroyReceiver.Requested=false; }
        }
        [Fact]
        public void DefaultSceneMatchesMeasuredValueSemantics()
        {
            Assert.Equal(default(PhysicsScene),Physics.defaultPhysicsScene);
            Assert.True(Physics.defaultPhysicsScene.IsValid());
            Assert.Equal("PhysicsScene(Index: 0, Version: 0)",Physics.defaultPhysicsScene.ToString());
            Assert.Equal(default(PhysicsScene).GetHashCode(),Physics.defaultPhysicsScene.GetHashCode());
        }
        public sealed class EmptyCollisionReceiver : MonoBehaviour { internal static int Calls; void OnCollisionEnter(Collision c) { Calls++; } }
        [Theory]
        [InlineData(false,false,false)] [InlineData(true,false,true)]
        [InlineData(false,true,true)] [InlineData(true,true,true)]
        public void ReportsRequireProvidedContactsOrCollisionMessages(bool provides,bool receiver,bool expected)
        {
            var a=Box("selectionA",Vector3.zero,false);var b=Box("selectionB",Vector3.right*.75f,true);
            a.providesContacts=provides;b.providesContacts=false;
            if(receiver)b.gameObject.AddComponent<EmptyCollisionReceiver>();
            bool seen=false;Subscribe((scene,headers)=>
            {
                for(int i=0;i<headers.Length;i++)for(int j=0;j<headers[i].pairCount;j++)
                { var pair=headers[i].GetContactPair(j);if(pair.collider==a || pair.otherCollider==a)seen=true; }
            });
            Physics.Simulate(.02f);Assert.Equal(expected,seen);
        }
        [Theory] [InlineData(true)] [InlineData(false)]
        public void UncreatedCopiesOnlyAccessSafetyWhenWriting(bool disposed)
        {
            Box("copyA",Vector3.zero,false);Box("copyB",Vector3.right*.75f,true);bool seen=false;
            Subscribe((scene,headers)=>
            {
                ContactPair pair=default;
                for(int i=0;i<headers.Length;i++)for(int j=0;j<headers[i].pairCount;j++)
                {var candidate=headers[i].GetContactPair(j);if(candidate.contactCount>0)pair=candidate;}
                if(pair.contactCount==0)return;
                seen=true;
                NativeArray<ContactPairPoint> buffer=default;
                if(disposed){buffer=new NativeArray<ContactPairPoint>(pair.contactCount,Allocator.Temp);buffer.Dispose();}
                if(disposed)Assert.Throws<ObjectDisposedException>(()=>pair.CopyToNativeArray(buffer));
                else pair.CopyToNativeArray(buffer);
            });
            Physics.Simulate(.02f);Assert.True(seen);
        }
        [Theory] [InlineData(true)] [InlineData(false)]
        public void SubscriberExceptionStopsMulticastButPreservesLegacyCallbacks(bool throwing)
        {
            Box("throwA",Vector3.zero,false);var b=Box("throwB",Vector3.right*.75f,true);
            b.gameObject.AddComponent<EmptyCollisionReceiver>();EmptyCollisionReceiver.Calls=0;
            bool first=false,second=false;
            Physics.ContactEventDelegate one=(scene,headers)=>{first=true;if(throwing)throw new InvalidOperationException("measured sentinel");};
            Physics.ContactEventDelegate two=(scene,headers)=>second=true;
            handlers.Add(one);handlers.Add(two);Physics.ContactEvent+=one;Physics.ContactEvent+=two;
            Physics.Simulate(.02f);Assert.True(first);Assert.Equal(!throwing,second);Assert.Equal(1,EmptyCollisionReceiver.Calls);
        }
        [Fact]
        public void CapsuleSweepKeepsMeasuredParallelFeatureWitness()
        {
            var go=new GameObject("sweepTarget");objects.Add(go);
            var c=go.AddComponent<CapsuleCollider>();c.height=3f;
            Physics.SyncTransforms();var a=new Vector3(-10,-1,.2f);
            Assert.True(Physics.CapsuleCast(a,a+Vector3.up*.8f,.25f,Vector3.right,out var hit,30));
            Assert.Same(c,hit.collider);Assert.InRange(hit.point.y,-.200001f,-.199999f);
        }
    }
}
