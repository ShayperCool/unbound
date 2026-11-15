// The Visual Effect module's enums, as the Unity 6000.0 reference assemblies declare them.
namespace UnityEngine.VFX
{
    /// <summary>The camera buffers an effect can ask a render pipeline for.</summary>
    [System.Flags]
    public enum VFXCameraBufferTypes
    {
        None = 0,
        Depth = 1,
        Color = 2,
        Normal = 4,
    }

    /// <summary>What an effect skips while no camera sees it.</summary>
    [System.Flags]
    internal enum VFXCullingFlags
    {
        CullNone = 0,
        CullSimulation = 1,
        CullBoundsUpdate = 2,
        CullDefault = 3,
    }

    /// <summary>The operations of a compiled graph's expressions.</summary>
    internal enum VFXExpressionOperation
    {
        None = 0,
        Value = 1,
        Combine2f = 2,
        Combine3f = 3,
        Combine4f = 4,
        ExtractComponent = 5,
        DeltaTime = 6,
        TotalTime = 7,
        SystemSeed = 8,
        LocalToWorld = 9,
        WorldToLocal = 10,
        FrameIndex = 11,
        PlayRate = 12,
        UnscaledDeltaTime = 13,
        ManagerMaxDeltaTime = 14,
        ManagerFixedTimeStep = 15,
        GameDeltaTime = 16,
        GameUnscaledDeltaTime = 17,
        GameSmoothDeltaTime = 18,
        GameTotalTime = 19,
        GameUnscaledTotalTime = 20,
        GameTotalTimeSinceSceneLoad = 21,
        GameTimeScale = 22,
        Sin = 23,
        Cos = 24,
        Tan = 25,
        ASin = 26,
        ACos = 27,
        ATan = 28,
        Abs = 29,
        Sign = 30,
        Saturate = 31,
        Ceil = 32,
        Round = 33,
        Frac = 34,
        Floor = 35,
        Log2 = 36,
        Mul = 37,
        Divide = 38,
        Add = 39,
        Subtract = 40,
        Min = 41,
        Max = 42,
        Pow = 43,
        ATan2 = 44,
        TRSToMatrix = 45,
        InverseMatrix = 46,
        InverseTRSMatrix = 47,
        TransposeMatrix = 48,
        ExtractPositionFromMatrix = 49,
        ExtractAnglesFromMatrix = 50,
        ExtractScaleFromMatrix = 51,
        TransformMatrix = 52,
        TransformPos = 53,
        TransformVec = 54,
        TransformDir = 55,
        TransformVector4 = 56,
        RowToMatrix = 57,
        ColumnToMatrix = 58,
        AxisToMatrix = 59,
        MatrixToRow = 60,
        MatrixToColumn = 61,
        MatrixToAxis = 62,
        SampleCurve = 63,
        SampleGradient = 64,
        SampleMeshVertexFloat = 65,
        SampleMeshVertexFloat2 = 66,
        SampleMeshVertexFloat3 = 67,
        SampleMeshVertexFloat4 = 68,
        SampleMeshVertexColor = 69,
        SampleMeshIndex = 70,
        VertexBufferFromMesh = 71,
        VertexBufferFromSkinnedMeshRenderer = 72,
        IndexBufferFromMesh = 73,
        MeshFromSkinnedMeshRenderer = 74,
        RootBoneTransformFromSkinnedMeshRenderer = 75,
        BakeCurve = 76,
        BakeGradient = 77,
        BitwiseLeftShift = 78,
        BitwiseRightShift = 79,
        BitwiseOr = 80,
        BitwiseAnd = 81,
        BitwiseXor = 82,
        BitwiseComplement = 83,
        CastUintToFloat = 84,
        CastIntToFloat = 85,
        CastFloatToUint = 86,
        CastIntToUint = 87,
        CastFloatToInt = 88,
        CastUintToInt = 89,
        CastIntToBool = 90,
        CastUintToBool = 91,
        CastFloatToBool = 92,
        CastBoolToInt = 93,
        CastBoolToUint = 94,
        CastBoolToFloat = 95,
        RGBtoHSV = 96,
        HSVtoRGB = 97,
        Condition = 98,
        Branch = 99,
        GenerateRandom = 100,
        GenerateFixedRandom = 101,
        ExtractMatrixFromMainCamera = 102,
        ExtractFOVFromMainCamera = 103,
        ExtractNearPlaneFromMainCamera = 104,
        ExtractFarPlaneFromMainCamera = 105,
        ExtractAspectRatioFromMainCamera = 106,
        ExtractPixelDimensionsFromMainCamera = 107,
        ExtractScaledPixelDimensionsFromMainCamera = 108,
        ExtractLensShiftFromMainCamera = 109,
        GetBufferFromMainCamera = 110,
        IsMainCameraOrthographic = 111,
        GetOrthographicSizeFromMainCamera = 112,
        LogicalAnd = 113,
        LogicalOr = 114,
        LogicalNot = 115,
        ValueNoise1D = 116,
        ValueNoise2D = 117,
        ValueNoise3D = 118,
        ValueCurlNoise2D = 119,
        ValueCurlNoise3D = 120,
        PerlinNoise1D = 121,
        PerlinNoise2D = 122,
        PerlinNoise3D = 123,
        PerlinCurlNoise2D = 124,
        PerlinCurlNoise3D = 125,
        CellularNoise1D = 126,
        CellularNoise2D = 127,
        CellularNoise3D = 128,
        CellularCurlNoise2D = 129,
        CellularCurlNoise3D = 130,
        VoroNoise2D = 131,
        MeshVertexCount = 132,
        MeshChannelOffset = 133,
        MeshChannelInfos = 134,
        MeshVertexStride = 135,
        MeshIndexCount = 136,
        MeshIndexFormat = 137,
        BufferStride = 138,
        BufferCount = 139,
        TextureWidth = 140,
        TextureHeight = 141,
        TextureDepth = 142,
        TextureFormat = 143,
        ReadEventAttribute = 144,
        SpawnerStateNewLoop = 145,
        SpawnerStateLoopState = 146,
        SpawnerStateSpawnCount = 147,
        SpawnerStateDeltaTime = 148,
        SpawnerStateTotalTime = 149,
        SpawnerStateDelayBeforeLoop = 150,
        SpawnerStateLoopDuration = 151,
        SpawnerStateDelayAfterLoop = 152,
        SpawnerStateLoopIndex = 153,
        SpawnerStateLoopCount = 154,
    }

    /// <summary>Why a compiled graph cannot be instanced.</summary>
    [System.Flags]
    internal enum VFXInstancingDisabledReason
    {
        None = 0,
        [System.ComponentModel.Description("A system is using indirect draw.")]
        IndirectDraw = 1,
        [System.ComponentModel.Description("The effect is using output events.")]
        OutputEvent = 2,
        [System.ComponentModel.Description("The effect is using GPU events.")]
        GPUEvent = 4,
        [System.ComponentModel.Description("An Initialize node has Bounds Mode set to 'Automatic'.")]
        AutomaticBounds = 8,
        [System.ComponentModel.Description("The effect contains a mesh output.")]
        MeshOutput = 16,
        [System.ComponentModel.Description("The effect has exposed texture, mesh or graphics buffer properties.")]
        ExposedObject = 32,
        [System.ComponentModel.Description("The effect uses Shader Keywords in particle output.")]
        ShaderKeyword = 64,
        [System.ComponentModel.Description("Unknown reason.")]
        Unknown = -1,
    }

    /// <summary>How an asset's effects are batched.</summary>
    internal enum VFXInstancingMode
    {
        Disabled = -1,
        [InspectorName("Automatic batch capacity")]
        Auto = 0,
        [InspectorName("Custom batch capacity")]
        Custom = 1,
    }

    /// <summary>Which camera supplies main camera buffers when the main camera has none.</summary>
    internal enum VFXMainCameraBufferFallback
    {
        NoFallback = 0,
        PreferMainCamera = 1,
        PreferSceneCamera = 2,
    }

    /// <summary>Which frame of a skinned mesh an expression reads.</summary>
    internal enum VFXSkinnedMeshFrame
    {
        Current = 0,
        Previous = 1,
    }

    /// <summary>Which root bone transform of a skinned mesh an expression reads.</summary>
    internal enum VFXSkinnedTransform
    {
        LocalRootBoneTransform = 0,
        WorldRootBoneTransform = 1,
    }

    /// <summary>The space a spaceable value is expressed in.</summary>
    public enum VFXSpace
    {
        None = -1,
        Local = 0,
        World = 1,
    }

    /// <summary>The state of a spawn system's loop.</summary>
    public enum VFXSpawnerLoopState
    {
        Finished = 0,
        DelayingBeforeLoop = 1,
        Looping = 2,
        DelayingAfterLoop = 3,
    }

    /// <summary>The traits of a compiled system.</summary>
    internal enum VFXSystemFlag
    {
        SystemDefault = 0,
        SystemHasKill = 1,
        SystemHasIndirectBuffer = 2,
        SystemReceivedEventGPU = 4,
        SystemHasStrips = 8,
        SystemNeedsComputeBounds = 16,
        SystemAutomaticBounds = 32,
        SystemInWorldSpace = 64,
        SystemHasDirectLink = 128,
        SystemHasAttributeBuffer = 256,
        SystemUsesInstancedRendering = 512,
        SystemIsRayTraced = 1024,
    }

    /// <summary>The kinds of compiled systems.</summary>
    internal enum VFXSystemType
    {
        Spawner = 0,
        Particle = 1,
        Mesh = 2,
        OutputEvent = 3,
    }

    /// <summary>The kinds of tasks of a compiled system: the high bits name the stage.</summary>
    internal enum VFXTaskType
    {
        None = 0,
        Spawner = 268435456,
        Initialize = 536870912,
        Update = 805306368,
        Output = 1073741824,
        CameraSort = 805306369,
        PerCameraUpdate = 805306370,
        PerCameraSort = 805306371,
        PerOutputSort = 805306372,
        GlobalSort = 805306373,
        ParticlePointOutput = 1073741824,
        ParticleLineOutput = 1073741825,
        ParticleQuadOutput = 1073741826,
        ParticleHexahedronOutput = 1073741827,
        ParticleMeshOutput = 1073741828,
        ParticleTriangleOutput = 1073741829,
        ParticleOctagonOutput = 1073741830,
        ConstantRateSpawner = 268435456,
        BurstSpawner = 268435457,
        PeriodicBurstSpawner = 268435458,
        VariableRateSpawner = 268435459,
        CustomCallbackSpawner = 268435460,
        SetAttributeSpawner = 268435461,
        EvaluateExpressionsSpawner = 268435462,
    }

    /// <summary>How an asset's effects take their time step.</summary>
    [System.Flags]
    internal enum VFXUpdateMode
    {
        FixedDeltaTime = 0,
        DeltaTime = 1,
        IgnoreTimeScale = 2,
        ExactFixedTimeStep = 4,
        DeltaTimeAndIgnoreTimeScale = 3,
        FixedDeltaAndExactTime = 4,
        FixedDeltaAndExactTimeAndIgnoreTimeScale = 6,
    }

    /// <summary>The types of expression values.</summary>
    internal enum VFXValueType
    {
        None = 0,
        Float = 1,
        Float2 = 2,
        Float3 = 3,
        Float4 = 4,
        Int32 = 5,
        Uint32 = 6,
        Texture2D = 7,
        Texture2DArray = 8,
        Texture3D = 9,
        TextureCube = 10,
        TextureCubeArray = 11,
        CameraBuffer = 12,
        Matrix4x4 = 13,
        Curve = 14,
        ColorGradient = 15,
        Mesh = 16,
        Spline = 17,
        Boolean = 18,
        Buffer = 19,
        SkinnedMeshRenderer = 20,
    }
}
