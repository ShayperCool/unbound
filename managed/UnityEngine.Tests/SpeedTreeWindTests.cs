using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The SpeedTree 9 wind's rules (tests/projects/speedtree9wind measures them in the
    /// Editor): the calm start, the eased direction and strength, the clamped strength, the parts a
    /// configuration turns off, the previous frame's constants and the zone direction through a
    /// mirrored parent.</summary>
    public class SpeedTreeWindTests
    {
        const float Dt = 1f / 30f;

        static unsafe SpeedTreeWindConfig9 Config(bool shared = true, bool branch1 = true, bool branch2 = true, bool ripple = true)
        {
            var config = new SpeedTreeWindConfig9
            {
                strengthResponse = 3f,
                directionResponse = 1.5f,
                treeExtentX = 2f,
                treeExtentY = 4f,
                treeExtentZ = 3f,
                sharedHeightStart = 0.25f,
                branch1StretchLimit = 0.4f,
                branch2StretchLimit = 0.6f,
                windIndependence = 0.7f,
                importScale = 1f,
                independenceShared = 0.1f,
                doShared = shared ? 1 : 0,
                doBranch1 = branch1 ? 1 : 0,
                doBranch2 = branch2 ? 1 : 0,
                doRipple = ripple ? 1 : 0,
            };
            for (int i = 0; i < 20; i++)
            {
                // Speeds rise along the curve; the bend is the sample index.
                config.speedShared[i] = config.speedBranch1[i] = config.speedBranch2[i] = config.speedRipple[i] = 1f + i;
                config.bendShared[i] = i;
            }
            return config;
        }

        static Vector4 Value(SpeedTreeWindState wind, int index) => (Vector4)wind.Values[index];

        [Fact]
        public void ACalmWindKeepsZeroWindAndTheTreesShape()
        {
            var wind = new SpeedTreeWindState();
            var config = Config();
            for (int frame = 1; frame <= 3; frame++) wind.Update(frame * Dt, Vector3.zero, 0f, ref config);
            Assert.Equal(Vector4.zero, Value(wind, 0));
            Assert.Equal(new Vector4(2f, 4f, 3f, 0.25f), Value(wind, 1));
            Assert.Equal(new Vector4(0.4f, 0.6f, 0.7f, 1f), Value(wind, 2));
            Assert.Equal(new Vector4(0f, 0f, 0f, 0.1f), Value(wind, 3));
        }

        [Fact]
        public void AWindAfterACalmTurnsAtOnceAndGrowsFromZero()
        {
            var wind = new SpeedTreeWindState();
            var config = Config();
            wind.Update(Dt, Vector3.zero, 0f, ref config);
            var east = new Vector3(1f, 0f, 0f);
            float time = 11f * Dt;
            wind.Update(time, east, 0.6f, ref config);
            // The calm left the clock at zero: the direction has eased since then; the strength
            // starts now, from zero.
            var vector = Value(wind, 0);
            Assert.Equal(1f, vector.x, 6);
            Assert.Equal(0f, vector.w);
            // The noise moved against the wind for the whole time since zero, at the first speed.
            Assert.Equal(-time * 1f, Value(wind, 3).x, 6);
            for (int frame = 12; frame < 200; frame++) wind.Update(frame * Dt, east, 0.6f, ref config);
            Assert.Equal(0.6f, Value(wind, 0).w, 5);
        }

        [Fact]
        public void ASettledWindKeepsItsDirectionAndNoiseAndAReturningWindJumpsTheNoise()
        {
            var wind = new SpeedTreeWindState();
            var config = Config();
            int frame = 1;
            for (; frame < 60; frame++) wind.Update(frame * Dt, Vector3.right, 0.5f, ref config);
            // The wind goes and settles over the strength response.
            for (; frame < 400; frame++) wind.Update(frame * Dt, Vector3.zero, 0f, ref config);
            var settled = Value(wind, 0);
            var noise = Value(wind, 3);
            Assert.Equal(new Vector4(1f, 0f, 0f, 0f), settled);
            Assert.Equal(0f, Value(wind, 4).x);
            wind.Update(frame++ * Dt, Vector3.zero, 0f, ref config);
            Assert.Equal(noise, Value(wind, 3));
            // A wind again: the settled wind's clock stood at zero, so the noise moves by the
            // whole time since then at once.
            float time = frame * Dt;
            wind.Update(time, Vector3.right, 0.5f, ref config);
            Assert.Equal(noise.x - time * 1f, Value(wind, 3).x, 4);
        }

        [Fact]
        public void AStrengthOverOneReadsTheLastCurveSample()
        {
            var wind = new SpeedTreeWindState();
            var config = Config();
            for (int frame = 1; frame < 400; frame++) wind.Update(frame * Dt, Vector3.forward, 2.5f, ref config);
            Assert.Equal(1f, Value(wind, 0).w);
            Assert.Equal(19f, Value(wind, 4).x);
        }

        [Fact]
        public void APartTurnedOffKeepsZeroConstants()
        {
            var wind = new SpeedTreeWindState();
            var config = Config(branch1: false, ripple: false);
            for (int frame = 1; frame < 30; frame++) wind.Update(frame * Dt, Vector3.forward, 0.8f, ref config);
            Assert.Equal(Vector4.zero, Value(wind, 5));
            Assert.Equal(Vector4.zero, Value(wind, 6));
            Assert.Equal(Vector4.zero, Value(wind, 9));
            Assert.Equal(Vector4.zero, Value(wind, 10));
            Assert.NotEqual(Vector4.zero, Value(wind, 7));
        }

        [Fact]
        public void TheHistoryHoldsThePreviousFrame()
        {
            var wind = new SpeedTreeWindState();
            var config = Config();
            for (int frame = 1; frame < 20; frame++) wind.Update(frame * Dt, Vector3.forward, 0.8f, ref config);
            var vector = Value(wind, 0);
            var noise = Value(wind, 3);
            wind.Update(20f * Dt, Vector3.forward, 0.8f, ref config);
            Assert.Equal(vector, Value(wind, SpeedTreeWindState.HistoryOffset));
            Assert.Equal(noise, Value(wind, SpeedTreeWindState.HistoryOffset + 3));
            Assert.NotEqual(noise, Value(wind, 3));
        }

        [Fact]
        public void ATurnedAroundWindStaysFiniteAndArrives()
        {
            var wind = new SpeedTreeWindState();
            var config = Config();
            int frame = 1;
            for (; frame < 100; frame++) wind.Update(frame * Dt, Vector3.forward, 0.5f, ref config);
            for (; frame < 300; frame++)
            {
                wind.Update(frame * Dt, Vector3.back, 0.5f, ref config);
                var v = Value(wind, 0);
                Assert.False(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z));
            }
            Assert.Equal(-1f, Value(wind, 0).z, 6);
        }

        sealed class Warnings : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Messages = new System.Collections.Generic.List<string>();
            public void LogException(System.Exception exception, Object context) => Messages.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Messages.Add(logType + ": " + string.Format(format, args));
        }

        /// <summary>A spherical zone over a tree: the draw that finds it takes the asset's wind and
        /// gives the tree a local wind, which then eases toward the zone's wind (away from the
        /// zone) on top of the directional wind; a renderer without a Tree is left alone. Once the
        /// zone is gone the local wind returns to the asset's wind and is given up.</summary>
        [Fact]
        public void ASphericalZoneOverATreeGivesItALocalWind()
        {
            var previous = Debug.unityLogger.logHandler;
            var warnings = new Warnings();
            var tree = new GameObject("WindTree");
            var plain = new GameObject("PlainRenderer");
            var zone = new GameObject("Sphere").AddComponent<WindZone>();
            var directional = new GameObject("Directional").AddComponent<WindZone>();
            SpeedTreeWindAsset asset = null;
            float time = (float)Time.s_Time;
            try
            {
                Debug.unityLogger.logHandler = warnings;
                tree.AddComponent<MeshFilter>().sharedMesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
                var renderer = tree.AddComponent<MeshRenderer>();
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                asset = new SpeedTreeWindAsset(1, Config());
                var component = tree.AddComponent<Tree>();
                component.windAsset = asset;
                var plainRenderer = plain.AddComponent<MeshRenderer>();
                directional.windMain = 0.5f;
                zone.mode = WindZoneMode.Spherical;
                zone.radius = 5f;
                zone.windMain = 1f;
                // The zone blows from x = -3 toward the tree.
                zone.transform.position = new Vector3(-3f, 0.25f, 0f);
                for (int frame = 1; frame <= 3; frame++)
                {
                    Time.s_Time = frame * Dt;
                    SpeedTreeWindSystem.Update();
                }
                using (Unbound.Runtime.DrawPropertyPool.Begin())
                {
                    var drawn = SpeedTreeWindSystem.Apply(renderer, null);
                    Assert.Equal(SpeedTreeWindState.ConstantCount, drawn.Count);
                    Assert.Equal((Vector4)asset.Wind.Values[0], (Vector4)drawn[Shader.PropertyToID("_ST_WindVector")]);
                    // Camera motion vectors: no history.
                    Assert.Equal(Vector4.zero, (Vector4)drawn[Shader.PropertyToID("_ST_HistoryTreeExtents_SharedHeightStart")]);
                    Assert.Null(SpeedTreeWindSystem.Apply(plainRenderer, null));
                }
                Assert.NotNull(component.m_LocalWind);
                for (int frame = 4; frame <= 200; frame++)
                {
                    Time.s_Time = frame * Dt;
                    SpeedTreeWindSystem.Update();
                }
                var local = (Vector4)component.m_LocalWind.Wind.Values[0];
                // The directional wind blows along +z; the zone pushes the tree along +x.
                Assert.True(local.x > 0.5f, local.ToString());
                Assert.True(local.w > 0.5f, local.ToString());
                Assert.True(((Vector4)asset.Wind.Values[0]).x < 1e-4f);
                Assert.Empty(warnings.Messages);

                // The zone goes: the local wind returns to the asset's wind and is given up.
                zone.gameObject.SetActive(false);
                for (int frame = 201; frame <= 900 && component.m_LocalWind != null; frame++)
                {
                    Time.s_Time = frame * Dt;
                    SpeedTreeWindSystem.Update();
                }
                Assert.Null(component.m_LocalWind);
            }
            finally
            {
                Time.s_Time = time;
                Debug.unityLogger.logHandler = previous;
                Object.DestroyImmediate(tree);
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(zone.gameObject);
                Object.DestroyImmediate(directional.gameObject);
                if (asset != null) Object.DestroyImmediate(asset);
                SpeedTreeWindSystem.Update();
            }
        }

        /// <summary>The spherical wind at bounds: zones out of reach take no part; one in reach
        /// blows from its position toward the center raised by a quarter of the height.</summary>
        [Fact]
        public void TheSphericalWindPointsFromTheZoneAtItsMainWind()
        {
            var near = new GameObject("Near").AddComponent<WindZone>();
            var far = new GameObject("Far").AddComponent<WindZone>();
            try
            {
                near.mode = far.mode = WindZoneMode.Spherical;
                near.radius = 10f;
                near.windMain = 2f;
                near.transform.position = new Vector3(0f, 1f, -4f);
                far.radius = 1f;
                far.transform.position = new Vector3(0f, 0f, 50f);
                SpeedTreeWindSystem.Update();
                // Center (0, 0, 0), height 4: the point is (0, 1, 0), straight along +z from the zone.
                var wind = SpeedTreeWindSystem.SphericalWind(new Bounds(Vector3.zero, new Vector3(2f, 4f, 2f)));
                Assert.Equal(0f, wind.x, 6);
                Assert.Equal(0f, wind.y, 6);
                Assert.Equal(2f, wind.z, 6);
            }
            finally
            {
                Object.DestroyImmediate(near.gameObject);
                Object.DestroyImmediate(far.gameObject);
                SpeedTreeWindSystem.Update();
            }
        }

        /// <summary>The GPU Resident Drawer's buffer (tests/projects/residentdrawer speedtree):
        /// zeros while the world has no wind or outside Play mode; then each tree's wind constants
        /// (the previous frame's for history, zeros there for camera motion vectors) and zeros for
        /// the parameters the SpeedTree 9 layout does not use; zeros for an instance that is not
        /// (or no longer) a tree with wind. Nothing outside the wind parameters is written.</summary>
        const int Stride = 80;

        /// <summary>Parameter p's uint offset in an instance: the first eight uints are not wind.</summary>
        static int Offset(int p) => 4 * (p + 2);

        static unsafe Vector4 Param(uint* buffer, int instance, int p) => *(Vector4*)(buffer + Stride * instance + Offset(p));

        [Fact]
        public unsafe void TheDrawerBufferGetsEachTreesWind()
        {
            const int Instances = 3;
            var objects = new[] { new GameObject("ObjectMotion"), new GameObject("CameraMotion"), new GameObject("Plain") };
            var asset = new SpeedTreeWindAsset(1, Config());
            WindZone zone = null;
            bool playing = Unbound.Runtime.EditorSession.IsPlaying;
            var data = new uint[Stride * Instances];
            fixed (uint* buffer = data)
            {
                var iterator = new UnityEngine.Rendering.SpeedTreeWindParamsBufferIterator
                {
                    bufferPtr = (System.IntPtr)buffer, uintStride = Stride, elementsCount = Instances,
                };
                for (int p = 0; p < 16; p++) iterator.uintParamOffsets[p] = Offset(p);
                var ids = new int[Instances];
                void Fill(uint value)
                {
                    for (int i = 0; i < data.Length; i++) data[i] = value;
                }
                try
                {
                    for (int i = 0; i < Instances; i++)
                    {
                        var renderer = objects[i].AddComponent<MeshRenderer>();
                        renderer.motionVectorGenerationMode = i == 0 ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera;
                        if (i < 2) objects[i].AddComponent<Tree>().windAsset = asset;
                        ids[i] = renderer.GetInstanceID();
                    }
                    Assert.True(SpeedTreeWindSystem.HasTree(objects[0].GetComponent<MeshRenderer>()));
                    Assert.False(SpeedTreeWindSystem.HasTree(objects[2].GetComponent<MeshRenderer>()));

                    // No wind: every listed instance's 16 parameters are zeroed, nothing else.
                    SpeedTreeWindSystem.Update();
                    Fill(7);
                    SpeedTreeWindSystem.WriteBuffer(ids, in iterator, false);
                    for (int i = 0; i < Instances; i++)
                    {
                        Assert.Equal(7u, data[Stride * i]);
                        for (int p = 0; p < 16; p++) Assert.Equal(Vector4.zero, Param(buffer, i, p));
                    }

                    zone = new GameObject("Wind").AddComponent<WindZone>();
                    zone.windMain = 0.5f;
                    SpeedTreeWindSystem.Update();
                    Fill(7);
                    SpeedTreeWindSystem.WriteBuffer(ids, in iterator, false);
                    for (int i = 0; i < 2; i++)
                    {
                        Assert.Equal(new Vector4(2f, 4f, 3f, 0.25f), Param(buffer, i, 1));
                        for (int p = 0; p < SpeedTreeWindState.HistoryOffset; p++) Assert.Equal((Vector4)asset.Wind.Values[p], Param(buffer, i, p));
                        // The SpeedTree 9 layout's 11 parameters, zeros after them.
                        for (int p = SpeedTreeWindState.HistoryOffset; p < 16; p++) Assert.Equal(Vector4.zero, Param(buffer, i, p));
                        Assert.Equal(7u, data[Stride * i]);
                    }
                    for (int p = 0; p < 16; p++) Assert.Equal(Vector4.zero, Param(buffer, 2, p));

                    // A tree whose wind asset is removed after the drawer took it: zeros.
                    objects[1].GetComponent<Tree>().windAsset = null;
                    Fill(7);
                    SpeedTreeWindSystem.WriteBuffer(ids, in iterator, false);
                    Assert.Equal(Vector4.zero, Param(buffer, 1, 1));
                    Assert.Equal(new Vector4(2f, 4f, 3f, 0.25f), Param(buffer, 0, 1));
                    objects[1].GetComponent<Tree>().windAsset = asset;

                    Fill(7);
                    SpeedTreeWindSystem.WriteBuffer(ids, in iterator, true);
                    for (int p = 0; p < SpeedTreeWindState.HistoryOffset; p++)
                        Assert.Equal((Vector4)asset.Wind.Values[SpeedTreeWindState.HistoryOffset + p], Param(buffer, 0, p));
                    for (int p = 0; p < 16; p++) Assert.Equal(Vector4.zero, Param(buffer, 1, p));
                    Assert.Equal(Vector4.zero, Param(buffer, 2, 0));

                    // Outside Play mode: zeros.
                    Unbound.Runtime.EditorSession.IsPlaying = false;
                    Fill(7);
                    SpeedTreeWindSystem.WriteBuffer(ids, in iterator, false);
                    Assert.Equal(Vector4.zero, Param(buffer, 0, 1));
                }
                finally
                {
                    Unbound.Runtime.EditorSession.IsPlaying = playing;
                    foreach (var o in objects) Object.DestroyImmediate(o);
                    if (zone != null) Object.DestroyImmediate(zone.gameObject);
                    Object.DestroyImmediate(asset);
                    SpeedTreeWindSystem.Update();
                }
            }
        }

        [Fact]
        public void AZoneUnderAMirroredParentBlowsMirrored()
        {
            var parent = new GameObject("Mirror").transform;
            var zone = new GameObject("Zone").AddComponent<WindZone>();
            try
            {
                parent.localScale = new Vector3(-1f, 1f, 1f);
                zone.transform.SetParent(parent, false);
                zone.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
                var direction = zone.SpeedTreeDirection();
                Assert.Equal(-0.5f, direction.x, 6);
                Assert.Equal(0f, direction.y, 6);
                Assert.Equal(0.8660254f, direction.z, 6);
            }
            finally
            {
                Object.DestroyImmediate(zone.gameObject);
                Object.DestroyImmediate(parent.gameObject);
            }
        }
    }
}
