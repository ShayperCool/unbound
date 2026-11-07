using System;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Jobs;
using Xunit;

namespace UnityEngine.Tests
{
    public partial class PhysicsCastBatchQueryTests
    {
        struct BatchCommandWriter : IJob
        {
            public NativeArray<RaycastCommand> Commands;
            public Vector3 Origin;
            public void Execute()
            {
                for(int i=0;i<Commands.Length;i++)
                    Commands[i]=new RaycastCommand(Origin+(i%3==1?Vector3.up*10:Vector3.zero),Vector3.forward,new QueryParameters(-1),20);
            }
        }
        struct BatchDistanceReader : IJob
        {
            [ReadOnly] public NativeArray<RaycastHit> Results;
            public NativeArray<float> Distances;
            public void Execute()
            {
                for(int i=0;i<Distances.Length;i++) Distances[i]=Results[i*2].distance;
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(2048)]
        [InlineData(int.MaxValue)]
        public void ScheduledBatchesRetainOwnershipAndRunDependentWritersAndReaders(int min)
        {
            var at=Origin+new Vector3(0,0,230);
            var target=Box("Scheduled",at+Vector3.forward*4);
            var commands=Native<RaycastCommand>(1031);
            var results=Native<RaycastHit>(commands.Length*2);
            var distances=Native<float>(commands.Length);
            var writer=new BatchCommandWriter{Commands=commands,Origin=at}.Schedule();
            var batch=RaycastCommand.ScheduleBatch(commands,results,min,2,writer);
            try
            {
                Assert.Throws<InvalidOperationException>(()=>{var value=commands[0];});
                Assert.Throws<InvalidOperationException>(()=>{var value=results[0];});
                Assert.Throws<InvalidOperationException>(()=>commands[0]=default);
                Assert.Throws<InvalidOperationException>(()=>RaycastCommand.ScheduleBatch(commands,results,min,2));
                var alias=results;
                Assert.Throws<InvalidOperationException>(()=>alias.Dispose());
                var reader=new BatchDistanceReader{Results=results,Distances=distances}.Schedule(batch);
                reader.Complete();
                for(int i=0;i<commands.Length;i++)
                {
                    Assert.Equal(i%3==1?null:target,results[i*2].collider);
                    Assert.Null(results[i*2+1].collider);
                    Assert.Equal(i%3==1?0:3.5f,distances[i]);
                }
                commands[0]=commands[2];
            }
            finally { batch.Complete(); }
        }

        [Fact]
        public void FinishingExecutionDoesNotReleaseArraySafetyUntilComplete()
        {
            var at=Origin+new Vector3(0,0,240);
            var target=Box("Finished",at+Vector3.forward*4);
            var commands=Native(new RaycastCommand(at,Vector3.forward,All,20));
            var results=Native<RaycastHit>(1);
            var handle=RaycastCommand.ScheduleBatch(commands,results,1);
            try
            {
                // Read-only command access is permitted when no command writer is pending.
                Assert.Equal(at,commands[0].from);
                Assert.True(SpinWait.SpinUntil(()=>handle.IsCompleted,TimeSpan.FromSeconds(20)));
                Assert.Throws<InvalidOperationException>(()=>{var value=results[0];});
                Assert.Throws<InvalidOperationException>(()=>commands[0]=default);
            }
            finally { handle.Complete(); }
            Assert.Same(target,results[0].collider);
        }

        struct QueryTransformWriter : IJobParallelForTransform
        {
            public Vector3 Position;
            public void Execute(int index, TransformAccess transform) { transform.position=Position; }
        }

        [Fact]
        public void BatchQueriesRetainTheLastSyncedPoseWithPendingTransformJobs()
        {
            var previous=ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion="6000.0.71f1";
            try
            {
                var at=Origin+new Vector3(0,0,255);
                var target=Box("TransformWriter",at+Vector3.forward*4);
                Physics.SyncTransforms();
                using var transforms=new TransformAccessArray(new[]{target.transform});
                var writer=new QueryTransformWriter{Position=at+Vector3.forward*6}.Schedule(transforms);
                var commands=Native(new RaycastCommand(at,Vector3.forward,All,20));
                var results=Native<RaycastHit>(1);
                var query=RaycastCommand.ScheduleBatch(commands,results,1);
                query.Complete();writer.Complete();
                Assert.Same(target,results[0].collider);
                Assert.Equal(3.5f,results[0].distance);
                Physics.SyncTransforms();
                RaycastCommand.ScheduleBatch(commands,results,1).Complete();
                Assert.Equal(5.5f,results[0].distance);
            }
            finally { ProjectInfo.UnityVersion=previous; }
        }

        [Fact]
        public void TerrainHeightHitsDoNotExposeMeshTriangleIndices()
        {
            var go=new GameObject("QueryHeightmap");m_Made.Add(go);
            go.transform.position=Origin+new Vector3(0,0,260);
            var terrain=go.AddComponent<TerrainCollider>();
            var data=new TerrainData{heightmapResolution=33,size=new Vector3(8,4,8)};
            terrain.terrainData=data;
            var at=go.transform.position+new Vector3(2,3,2);
            Physics.SyncTransforms();
            Assert.True(Physics.Raycast(at,Vector3.down,out var single,10));
            Assert.Same(terrain,single.collider);
            Assert.Equal(-1,single.triangleIndex);
            var commands=Native(new RaycastCommand(at,Vector3.down,All,10));
            var results=Native<RaycastHit>(1);
            RaycastCommand.ScheduleBatch(commands,results,1).Complete();
            Assert.Same(terrain,results[0].collider);
            Assert.Equal(-1,results[0].triangleIndex);
            Object.DestroyImmediate(data);
        }

        [Fact]
        public void IndependentReadersKeepCommandOwnershipUntilBothHandlesComplete()
        {
            var at=Origin+new Vector3(0,0,245);
            var target=Box("SharedCommands",at+Vector3.forward*4);
            var commands=Native(new RaycastCommand(at,Vector3.forward,All,20));
            var first=Native<RaycastHit>(1);
            var second=Native<RaycastHit>(1);
            var a=RaycastCommand.ScheduleBatch(commands,first,1);
            var b=RaycastCommand.ScheduleBatch(commands,second,1);
            try
            {
                b.Complete();
                Assert.Same(target,second[0].collider);
                Assert.Throws<InvalidOperationException>(()=>{var value=first[0];});
                Assert.Throws<InvalidOperationException>(()=>commands[0]=default);
            }
            finally { a.Complete();b.Complete(); }
            Assert.Same(target,first[0].collider);
            commands[0]=default;
        }

        [Fact]
        public void QueryReadsFinishBeforeGeometryIsRemovedAndKeepTheirResultIdentity()
        {
            var at=Origin+new Vector3(0,0,250);
            var target=Box("RemovedAfterSchedule",at+Vector3.forward*4);
            var commands=Native(new RaycastCommand(at,Vector3.forward,All,20));
            var results=Native<RaycastHit>(1);
            var handle=RaycastCommand.ScheduleBatch(commands,results,1);
            Object.DestroyImmediate(target.gameObject);
            Assert.Throws<InvalidOperationException>(()=>{var value=results[0];});
            handle.Complete();
            Assert.Null(results[0].collider);
            Assert.Equal(3.5f,results[0].distance);
        }
    }
}
