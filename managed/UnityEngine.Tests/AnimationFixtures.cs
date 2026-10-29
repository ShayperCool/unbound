namespace Unbound.Tests
{
    /// <summary>Trimmed copies of the test project's animation assets (the test game), with the
    /// controller's clip references made local so that one fixture holds both.</summary>
    internal static class AnimationFixtures
    {
        /// <summary>A button's controller and its show clip.</summary>
        internal const string PopupButton = """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1102 &-5843706674010299242
AnimatorState:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: PopupShow
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: []
  m_StateMachineBehaviours: []
  m_Position: {x: 50, y: 50, z: 0}
  m_IKOnFeet: 0
  m_WriteDefaultValues: 1
  m_Mirror: 0
  m_SpeedParameterActive: 0
  m_MirrorParameterActive: 0
  m_CycleOffsetParameterActive: 0
  m_TimeParameterActive: 0
  m_Motion: {fileID: 7400000}
  m_Tag:
  m_SpeedParameter:
  m_MirrorParameter:
  m_CycleOffsetParameter:
  m_TimeParameter:
--- !u!91 &9100000
AnimatorController:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: PopupButton
  serializedVersion: 5
  m_AnimatorParameters: []
  m_AnimatorLayers:
  - serializedVersion: 5
    m_Name: Base Layer
    m_StateMachine: {fileID: 2379091846958323130}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}
--- !u!1107 &2379091846958323130
AnimatorStateMachine:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: Base Layer
  m_ChildStates:
  - serializedVersion: 1
    m_State: {fileID: -5843706674010299242}
    m_Position: {x: 360, y: 120, z: 0}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_AnyStatePosition: {x: 50, y: 20, z: 0}
  m_EntryPosition: {x: 50, y: 120, z: 0}
  m_ExitPosition: {x: 800, y: 120, z: 0}
  m_ParentStateMachinePosition: {x: 800, y: 20, z: 0}
  m_DefaultState: {fileID: -5843706674010299242}
--- !u!74 &7400000
AnimationClip:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: PopupShow
  serializedVersion: 7
  m_Legacy: 0
  m_Compressed: 0
  m_UseHighQualityCurve: 1
  m_RotationCurves: []
  m_CompressedRotationCurves: []
  m_EulerCurves: []
  m_PositionCurves: []
  m_ScaleCurves:
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {x: 1, y: 1, z: 1}
        inSlope: {x: 0, y: 0, z: 0}
        outSlope: {x: 0, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
        inWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
        outWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
      - serializedVersion: 3
        time: 0.25
        value: {x: 1.0884, y: 1.0884, z: 1.0884}
        inSlope: {x: 0, y: 0, z: 0}
        outSlope: {x: 0, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
        inWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
        outWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
      - serializedVersion: 3
        time: 0.3
        value: {x: 1, y: 1, z: 1}
        inSlope: {x: 0, y: 0, z: 0}
        outSlope: {x: 0, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
        inWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
        outWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
      m_PreInfinity: 2
      m_PostInfinity: 2
      m_RotationOrder: 4
    path: ContinueText
  m_FloatCurves:
  - serializedVersion: 2
    curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: -69.3
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      - serializedVersion: 3
        time: 0.18333334
        value: 30
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      m_PreInfinity: 2
      m_PostInfinity: 2
      m_RotationOrder: 4
    attribute: m_AnchoredPosition.y
    path: ContinueText
    classID: 224
    script: {fileID: 0}
    flags: 0
  m_PPtrCurves: []
  m_SampleRate: 60
  m_WrapMode: 0
  m_Bounds:
    m_Center: {x: 0, y: 0, z: 0}
    m_Extent: {x: 0, y: 0, z: 0}
  m_ClipBindingConstant:
    genericBindings:
    - serializedVersion: 2
      path: 965236745
      attribute: 3
      script: {fileID: 0}
      typeID: 4
      customType: 0
      isPPtrCurve: 0
      isIntCurve: 0
      isSerializeReferenceCurve: 0
    - serializedVersion: 2
      path: 965236745
      attribute: 538195251
      script: {fileID: 0}
      typeID: 224
      customType: 28
      isPPtrCurve: 0
      isIntCurve: 0
      isSerializeReferenceCurve: 0
    pptrCurveMapping: []
  m_AnimationClipSettings:
    serializedVersion: 2
    m_AdditiveReferencePoseClip: {fileID: 0}
    m_AdditiveReferencePoseTime: 0
    m_StartTime: 0
    m_StopTime: 0.3
    m_OrientationOffsetY: 0
    m_Level: 0
    m_CycleOffset: 0
    m_HasAdditiveReferencePose: 0
    m_LoopTime: 0
    m_LoopBlend: 0
    m_LoopBlendOrientation: 0
    m_LoopBlendPositionY: 0
    m_LoopBlendPositionXZ: 0
    m_KeepOriginalOrientation: 0
    m_KeepOriginalPositionY: 1
    m_KeepOriginalPositionXZ: 0
    m_HeightFromFeet: 0
    m_Mirror: 0
  m_EditorCurves: []
  m_EulerEditorCurves: []
  m_HasGenericRootTransform: 0
  m_HasMotionFloatCurves: 0
  m_Events: []
""";

        /// <summary>A panel controller: an empty default state that leaves by exit
        /// time (0.75, fixed duration 0.25) for PanelShowClip.</summary>
        internal const string PanelController = """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1107 &-3811913567969900090
AnimatorStateMachine:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_Name: Base Layer
  m_ChildStates:
  - serializedVersion: 1
    m_State: {fileID: 924631915283591160}
    m_Position: {x: 320, y: 120, z: 0}
  - serializedVersion: 1
    m_State: {fileID: 7566612294110765730}
    m_Position: {x: 320, y: 230, z: 0}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_DefaultState: {fileID: 924631915283591160}
--- !u!1101 &-2803333540474118065
AnimatorStateTransition:
  m_ObjectHideFlags: 1
  m_Name:
  m_Conditions: []
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 7566612294110765730}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: 0.25
  m_TransitionOffset: 0
  m_ExitTime: 0.75
  m_HasExitTime: 1
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1
--- !u!91 &9100000
AnimatorController:
  m_ObjectHideFlags: 0
  m_Name: PanelController
  serializedVersion: 5
  m_AnimatorParameters: []
  m_AnimatorLayers:
  - serializedVersion: 5
    m_Name: Base Layer
    m_StateMachine: {fileID: -3811913567969900090}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}
--- !u!1102 &924631915283591160
AnimatorState:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_Name: New State
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions:
  - {fileID: -2803333540474118065}
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Mirror: 0
  m_SpeedParameterActive: 0
  m_MirrorParameterActive: 0
  m_CycleOffsetParameterActive: 0
  m_TimeParameterActive: 0
  m_Motion: {fileID: 0}
  m_Tag:
  m_SpeedParameter:
  m_MirrorParameter:
  m_CycleOffsetParameter:
  m_TimeParameter:
--- !u!1102 &7566612294110765730
AnimatorState:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_Name: PanelShowClip
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: []
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Mirror: 0
  m_SpeedParameterActive: 0
  m_MirrorParameterActive: 0
  m_CycleOffsetParameterActive: 0
  m_TimeParameterActive: 0
  m_Motion: {fileID: 7400000}
  m_Tag:
  m_SpeedParameter:
  m_MirrorParameter:
  m_CycleOffsetParameter:
  m_TimeParameter:
--- !u!74 &7400000
AnimationClip:
  m_ObjectHideFlags: 0
  m_Name: PanelShowClip
  serializedVersion: 7
  m_Legacy: 0
  m_RotationCurves: []
  m_CompressedRotationCurves: []
  m_EulerCurves: []
  m_PositionCurves: []
  m_ScaleCurves: []
  m_FloatCurves: []
  m_PPtrCurves: []
  m_SampleRate: 60
  m_WrapMode: 0
  m_AnimationClipSettings:
    serializedVersion: 2
    m_StartTime: 0
    m_StopTime: 1
    m_CycleOffset: 0
    m_LoopTime: 0
  m_Events: []
""";

        /// <summary>An idle clip's first circle, its alpha curve retargeted from
        /// uGUI's Image (a script) to a SpriteRenderer (class 212), plus an event.</summary>
        internal const string CircleIdle = """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!74 &7400000
AnimationClip:
  m_ObjectHideFlags: 0
  m_Name: Idle
  serializedVersion: 7
  m_Legacy: 0
  m_RotationCurves: []
  m_CompressedRotationCurves: []
  m_EulerCurves: []
  m_PositionCurves: []
  m_ScaleCurves:
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {x: 1, y: 1, z: 1}
        inSlope: {x: 0, y: 0, z: 0}
        outSlope: {x: 0, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
        inWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
        outWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
      - serializedVersion: 3
        time: 0.6666667
        value: {x: 1, y: 1, z: 1}
        inSlope: {x: -0, y: -0, z: -0}
        outSlope: {x: 0.07999993, y: 0.07999993, z: 0.07999993}
        tangentMode: 0
        weightedMode: 0
        inWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
        outWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
      - serializedVersion: 3
        time: 1.1666666
        value: {x: 1.04, y: 1.04, z: 1.04}
        inSlope: {x: 0.07999993, y: 0.07999993, z: 0.07999993}
        outSlope: {x: -0.11999987, y: -0.11999987, z: -0.11999987}
        tangentMode: 0
        weightedMode: 0
        inWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
        outWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
      - serializedVersion: 3
        time: 1.5
        value: {x: 1, y: 1, z: 1}
        inSlope: {x: -0.11999987, y: -0.11999987, z: -0.11999987}
        outSlope: {x: 0, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
        inWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
        outWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
      m_PreInfinity: 2
      m_PostInfinity: 2
      m_RotationOrder: 4
    path: Circle 1
  m_FloatCurves:
  - serializedVersion: 2
    curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 0
        inSlope: 0
        outSlope: 0
        tangentMode: 69
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      - serializedVersion: 3
        time: 0.6666667
        value: 0
        inSlope: -0
        outSlope: 0.37647066
        tangentMode: 69
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      - serializedVersion: 3
        time: 1.1666666
        value: 0.1882353
        inSlope: 0.37647066
        outSlope: -0.56470585
        tangentMode: 69
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      - serializedVersion: 3
        time: 1.5
        value: 0
        inSlope: -0.56470585
        outSlope: 0
        tangentMode: 69
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      m_PreInfinity: 2
      m_PostInfinity: 2
      m_RotationOrder: 4
    attribute: m_Color.a
    path: Circle 1
    classID: 212
    script: {fileID: 0}
    flags: 0
  m_PPtrCurves: []
  m_SampleRate: 60
  m_WrapMode: 0
  m_Bounds:
    m_Center: {x: 0, y: 0, z: 0}
    m_Extent: {x: 0, y: 0, z: 0}
  m_AnimationClipSettings:
    serializedVersion: 2
    m_StartTime: 0
    m_StopTime: 1.5
    m_CycleOffset: 0
    m_LoopTime: 1
    m_LoopBlend: 0
  m_Events:
  - time: 0.5
    functionName: OnPulse
    data: half
    objectReferenceParameter: {fileID: 0}
    floatParameter: 2.5
    intParameter: 7
    messageOptions: 0
""";

        /// <summary>A controller exercising parameters, conditions, exit time and Any State: Idle goes
        /// to Walk while Run is set; Walk returns at exit time 0.5 once Run is cleared; the Jump
        /// trigger leads anywhere to Jump, which returns to Idle at its end.</summary>
        internal const string Locomotion = """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!91 &9100000
AnimatorController:
  m_Name: Locomotion
  serializedVersion: 5
  m_AnimatorParameters:
  - m_Name: Run
    m_Type: 4
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
  - m_Name: Jump
    m_Type: 9
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
  - m_Name: Speed
    m_Type: 1
    m_DefaultFloat: 0.5
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
  m_AnimatorLayers:
  - serializedVersion: 5
    m_Name: Base Layer
    m_StateMachine: {fileID: 1107000001}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}
--- !u!1107 &1107000001
AnimatorStateMachine:
  serializedVersion: 6
  m_Name: Base Layer
  m_ChildStates:
  - serializedVersion: 1
    m_State: {fileID: 1102000001}
  - serializedVersion: 1
    m_State: {fileID: 1102000002}
  - serializedVersion: 1
    m_State: {fileID: 1102000003}
  m_ChildStateMachines: []
  m_AnyStateTransitions:
  - {fileID: 1101000003}
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_DefaultState: {fileID: 1102000001}
--- !u!1102 &1102000001
AnimatorState:
  serializedVersion: 6
  m_Name: Idle
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions:
  - {fileID: 1101000001}
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 7400001}
  m_Tag: Grounded
--- !u!1102 &1102000002
AnimatorState:
  serializedVersion: 6
  m_Name: Walk
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions:
  - {fileID: 1101000002}
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_SpeedParameterActive: 1
  m_SpeedParameter: Speed
  m_Motion: {fileID: 7400002}
  m_Tag: Grounded
--- !u!1102 &1102000003
AnimatorState:
  serializedVersion: 6
  m_Name: Jump
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions:
  - {fileID: 1101000004}
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 7400003}
  m_Tag:
--- !u!1101 &1101000001
AnimatorStateTransition:
  m_Name:
  m_Conditions:
  - m_ConditionMode: 1
    m_ConditionEvent: Run
    m_EventTreshold: 0
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 1102000002}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: 0
  m_TransitionOffset: 0
  m_ExitTime: 0.9
  m_HasExitTime: 0
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1
--- !u!1101 &1101000002
AnimatorStateTransition:
  m_Name:
  m_Conditions:
  - m_ConditionMode: 2
    m_ConditionEvent: Run
    m_EventTreshold: 0
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 1102000001}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: 0.2
  m_TransitionOffset: 0
  m_ExitTime: 0.5
  m_HasExitTime: 1
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1
--- !u!1101 &1101000003
AnimatorStateTransition:
  m_Name: ToJump
  m_Conditions:
  - m_ConditionMode: 1
    m_ConditionEvent: Jump
    m_EventTreshold: 0
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 1102000003}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: 0
  m_TransitionOffset: 0
  m_ExitTime: 0.75
  m_HasExitTime: 0
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 0
--- !u!1101 &1101000004
AnimatorStateTransition:
  m_Name:
  m_Conditions: []
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 1102000001}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: 0
  m_TransitionOffset: 0
  m_ExitTime: 1
  m_HasExitTime: 1
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1
--- !u!74 &7400001
AnimationClip:
  m_Name: IdleClip
  m_Legacy: 0
  m_PositionCurves:
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {x: 0, y: 0, z: 0}
        inSlope: {x: 0, y: 0, z: 0}
        outSlope: {x: 0, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
      - serializedVersion: 3
        time: 1
        value: {x: 0, y: 0, z: 0}
        inSlope: {x: 0, y: 0, z: 0}
        outSlope: {x: 0, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    path: Body
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 1
    m_LoopTime: 1
  m_Events: []
--- !u!74 &7400002
AnimationClip:
  m_Name: WalkClip
  m_Legacy: 0
  m_PositionCurves:
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {x: 0, y: 0, z: 0}
        inSlope: {x: 1, y: 0, z: 0}
        outSlope: {x: 1, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
      - serializedVersion: 3
        time: 1
        value: {x: 1, y: 0, z: 0}
        inSlope: {x: 1, y: 0, z: 0}
        outSlope: {x: 1, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    path: Body
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 1
    m_LoopTime: 1
  m_Events:
  - time: 0.25
    functionName: OnStep
    data:
    objectReferenceParameter: {fileID: 0}
    floatParameter: 0
    intParameter: 0
    messageOptions: 0
--- !u!74 &7400003
AnimationClip:
  m_Name: JumpClip
  m_Legacy: 0
  m_PositionCurves:
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {x: 0, y: 2, z: 0}
        inSlope: {x: 0, y: 0, z: 0}
        outSlope: {x: 0, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
      - serializedVersion: 3
        time: 0.5
        value: {x: 0, y: 2, z: 0}
        inSlope: {x: 0, y: 0, z: 0}
        outSlope: {x: 0, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    path: Body
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 0.5
    m_LoopTime: 0
  m_Events: []
""";

        /// <summary>Three layers: a 1D blend tree (Blend: 0 → x 0, 1 → x 10), an override layer at
        /// weight 0.5 whose Wave state sets y 4 (its rest state does not write defaults), and an
        /// additive layer adding z 2.</summary>
        internal const string Layers = """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!91 &9100000
AnimatorController:
  m_Name: Layers
  serializedVersion: 5
  m_AnimatorParameters:
  - m_Name: Blend
    m_Type: 1
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
  - m_Name: Wave
    m_Type: 4
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
  m_AnimatorLayers:
  - serializedVersion: 5
    m_Name: Base Layer
    m_StateMachine: {fileID: 1107000001}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}
  - serializedVersion: 5
    m_Name: Upper
    m_StateMachine: {fileID: 1107000002}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0.5
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}
  - serializedVersion: 5
    m_Name: Add
    m_StateMachine: {fileID: 1107000003}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 1
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 1
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}
--- !u!1107 &1107000001
AnimatorStateMachine:
  serializedVersion: 6
  m_Name: Base Layer
  m_ChildStates:
  - serializedVersion: 1
    m_State: {fileID: 1102000001}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_DefaultState: {fileID: 1102000001}
--- !u!1102 &1102000001
AnimatorState:
  serializedVersion: 6
  m_Name: Move
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: []
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 20600001}
  m_Tag: 
--- !u!206 &20600001
BlendTree:
  m_Name: Move Tree
  m_Childs:
  - serializedVersion: 2
    m_Motion: {fileID: 7400001}
    m_Threshold: 0
    m_Position: {x: 0, y: 0}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: Blend
    m_Mirror: 0
  - serializedVersion: 2
    m_Motion: {fileID: 7400002}
    m_Threshold: 1
    m_Position: {x: 0, y: 0}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: Blend
    m_Mirror: 0
  m_BlendParameter: Blend
  m_BlendParameterY: Blend
  m_MinThreshold: 0
  m_MaxThreshold: 1
  m_UseAutomaticThresholds: 1
  m_NormalizedBlendValues: 0
  m_BlendType: 0
--- !u!1107 &1107000002
AnimatorStateMachine:
  serializedVersion: 6
  m_Name: Upper
  m_ChildStates:
  - serializedVersion: 1
    m_State: {fileID: 1102000002}
  - serializedVersion: 1
    m_State: {fileID: 1102000003}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_DefaultState: {fileID: 1102000002}
--- !u!1102 &1102000002
AnimatorState:
  serializedVersion: 6
  m_Name: Rest
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: 
  - {fileID: 1101000001}
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 0
  m_Motion: {fileID: 0}
  m_Tag: 
--- !u!1102 &1102000003
AnimatorState:
  serializedVersion: 6
  m_Name: Wave
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: []
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 7400003}
  m_Tag: 
--- !u!1101 &1101000001
AnimatorStateTransition:
  m_Name: 
  m_Conditions: 
  - m_ConditionMode: 1
    m_ConditionEvent: Wave
    m_EventTreshold: 0
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 1102000003}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: 0
  m_TransitionOffset: 0
  m_ExitTime: 0
  m_HasExitTime: 0
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1
--- !u!1107 &1107000003
AnimatorStateMachine:
  serializedVersion: 6
  m_Name: Add
  m_ChildStates:
  - serializedVersion: 1
    m_State: {fileID: 1102000004}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_DefaultState: {fileID: 1102000004}
--- !u!1102 &1102000004
AnimatorState:
  serializedVersion: 6
  m_Name: Lean
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: []
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 7400004}
  m_Tag: 
--- !u!74 &7400001
AnimationClip:
  m_Name: Slow
  m_Legacy: 0
  m_FloatCurves:
  - serializedVersion: 2
    curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 0
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      - serializedVersion: 3
        time: 1
        value: 0
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    attribute: m_LocalPosition.x
    path: Body
    classID: 4
    script: {fileID: 0}
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 1
    m_LoopTime: 1
  m_Events: []
--- !u!74 &7400002
AnimationClip:
  m_Name: Fast
  m_Legacy: 0
  m_FloatCurves:
  - serializedVersion: 2
    curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 10
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      - serializedVersion: 3
        time: 2
        value: 10
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    attribute: m_LocalPosition.x
    path: Body
    classID: 4
    script: {fileID: 0}
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 2
    m_LoopTime: 1
  m_Events: []
--- !u!74 &7400003
AnimationClip:
  m_Name: WaveClip
  m_Legacy: 0
  m_FloatCurves:
  - serializedVersion: 2
    curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 4
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      - serializedVersion: 3
        time: 1
        value: 4
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    attribute: m_LocalPosition.y
    path: Body
    classID: 4
    script: {fileID: 0}
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 1
    m_LoopTime: 1
  m_Events: []
--- !u!74 &7400004
AnimationClip:
  m_Name: LeanClip
  m_Legacy: 0
  m_FloatCurves:
  - serializedVersion: 2
    curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 2
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      - serializedVersion: 3
        time: 1
        value: 2
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    attribute: m_LocalPosition.z
    path: Body
    classID: 4
    script: {fileID: 0}
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 1
    m_LoopTime: 1
  m_Events: []
""";

        /// <summary>A sub-state machine: Ground → Air (Rise → Fall → Exit) on the Jump trigger, and
        /// the parent's transition from Air back to Ground.</summary>
        internal const string Machines = """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!91 &9100000
AnimatorController:
  m_Name: Machines
  serializedVersion: 5
  m_AnimatorParameters:
  - m_Name: Jump
    m_Type: 9
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
  m_AnimatorLayers:
  - serializedVersion: 5
    m_Name: Base Layer
    m_StateMachine: {fileID: 1107000001}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}
--- !u!1107 &1107000001
AnimatorStateMachine:
  serializedVersion: 6
  m_Name: Base Layer
  m_ChildStates:
  - serializedVersion: 1
    m_State: {fileID: 1102000001}
  m_ChildStateMachines:
  - serializedVersion: 1
    m_StateMachine: {fileID: 1107000002}
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions:
  - first: {fileID: 1107000002}
    second:
    - {fileID: 1109000001}
  m_StateMachineBehaviours: []
  m_DefaultState: {fileID: 1102000001}
--- !u!1107 &1107000002
AnimatorStateMachine:
  serializedVersion: 6
  m_Name: Air
  m_ChildStates:
  - serializedVersion: 1
    m_State: {fileID: 1102000002}
  - serializedVersion: 1
    m_State: {fileID: 1102000003}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_DefaultState: {fileID: 1102000002}
--- !u!1102 &1102000001
AnimatorState:
  serializedVersion: 6
  m_Name: Ground
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: 
  - {fileID: 1101000001}
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 7400001}
  m_Tag: 
--- !u!1102 &1102000002
AnimatorState:
  serializedVersion: 6
  m_Name: Rise
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: 
  - {fileID: 1101000002}
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 7400002}
  m_Tag: 
--- !u!1102 &1102000003
AnimatorState:
  serializedVersion: 6
  m_Name: Fall
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: 
  - {fileID: 1101000003}
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 7400003}
  m_Tag: 
--- !u!1101 &1101000001
AnimatorStateTransition:
  m_Name: 
  m_Conditions: 
  - m_ConditionMode: 1
    m_ConditionEvent: Jump
    m_EventTreshold: 0
  m_DstStateMachine: {fileID: 1107000002}
  m_DstState: {fileID: 0}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: 0
  m_TransitionOffset: 0
  m_ExitTime: 0
  m_HasExitTime: 0
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1
--- !u!1101 &1101000002
AnimatorStateTransition:
  m_Name: 
  m_Conditions: []
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 1102000003}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: 0
  m_TransitionOffset: 0
  m_ExitTime: 1
  m_HasExitTime: 1
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1
--- !u!1101 &1101000003
AnimatorStateTransition:
  m_Name: 
  m_Conditions: []
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 0}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 1
  serializedVersion: 3
  m_TransitionDuration: 0
  m_TransitionOffset: 0
  m_ExitTime: 1
  m_HasExitTime: 1
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1
--- !u!1109 &1109000001
AnimatorTransition:
  m_Name: 
  m_Conditions: []
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 1102000001}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 1
--- !u!74 &7400001
AnimationClip:
  m_Name: GroundClip
  m_Legacy: 0
  m_FloatCurves:
  - serializedVersion: 2
    curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 0
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      - serializedVersion: 3
        time: 1
        value: 0
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    attribute: m_LocalPosition.y
    path: Body
    classID: 4
    script: {fileID: 0}
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 1
    m_LoopTime: 1
  m_Events: []
--- !u!74 &7400002
AnimationClip:
  m_Name: RiseClip
  m_Legacy: 0
  m_FloatCurves:
  - serializedVersion: 2
    curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 1
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      - serializedVersion: 3
        time: 0.5
        value: 1
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    attribute: m_LocalPosition.y
    path: Body
    classID: 4
    script: {fileID: 0}
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 0.5
    m_LoopTime: 0
  m_Events: []
--- !u!74 &7400003
AnimationClip:
  m_Name: FallClip
  m_Legacy: 0
  m_FloatCurves:
  - serializedVersion: 2
    curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 2
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      - serializedVersion: 3
        time: 0.5
        value: 2
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    attribute: m_LocalPosition.y
    path: Body
    classID: 4
    script: {fileID: 0}
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 0.5
    m_LoopTime: 0
  m_Events: []
""";

        /// <summary>Root motion: the default state Empty has no motion; Move's clip moves the
        /// root along x (0 to 2 over a second), turns it about y (0 to 90) and raises Child.</summary>
        internal const string RootMotion = """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!91 &9100000
AnimatorController:
  m_Name: RootMotion
  serializedVersion: 5
  m_AnimatorParameters: []
  m_AnimatorLayers:
  - serializedVersion: 5
    m_Name: Base Layer
    m_StateMachine: {fileID: 1107000001}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}
--- !u!1107 &1107000001
AnimatorStateMachine:
  serializedVersion: 6
  m_Name: Base Layer
  m_ChildStates:
  - serializedVersion: 1
    m_State: {fileID: 1102000001}
  - serializedVersion: 1
    m_State: {fileID: 1102000002}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_DefaultState: {fileID: 1102000001}
--- !u!1102 &1102000001
AnimatorState:
  serializedVersion: 6
  m_Name: Empty
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: []
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 0}
  m_Tag: 
--- !u!1102 &1102000002
AnimatorState:
  serializedVersion: 6
  m_Name: Move
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: []
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 7400001}
  m_Tag: 
--- !u!74 &7400001
AnimationClip:
  m_Name: MoveClip
  m_Legacy: 0
  m_PositionCurves:
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {x: 0, y: 0, z: 0}
        inSlope: {x: 2, y: 0, z: 0}
        outSlope: {x: 2, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
      - serializedVersion: 3
        time: 1
        value: {x: 2, y: 0, z: 0}
        inSlope: {x: 2, y: 0, z: 0}
        outSlope: {x: 2, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    path: 
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {x: 0, y: 0, z: 0}
        inSlope: {x: 0, y: 3, z: 0}
        outSlope: {x: 0, y: 3, z: 0}
        tangentMode: 0
        weightedMode: 0
      - serializedVersion: 3
        time: 1
        value: {x: 0, y: 3, z: 0}
        inSlope: {x: 0, y: 3, z: 0}
        outSlope: {x: 0, y: 3, z: 0}
        tangentMode: 0
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    path: Child
  m_EulerCurves:
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {x: 0, y: 0, z: 0}
        inSlope: {x: 0, y: 90, z: 0}
        outSlope: {x: 0, y: 90, z: 0}
        tangentMode: 0
        weightedMode: 0
      - serializedVersion: 3
        time: 1
        value: {x: 0, y: 90, z: 0}
        inSlope: {x: 0, y: 90, z: 0}
        outSlope: {x: 0, y: 90, z: 0}
        tangentMode: 0
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
      m_RotationOrder: 4
    path: 
  m_FloatCurves: []
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 1
    m_LoopTime: 0
  m_Events: []
""";
    }
}
