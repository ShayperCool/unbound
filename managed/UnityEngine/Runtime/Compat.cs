using System;
using System.Globalization;

namespace Unbound.Runtime
{
    /// <summary>
    /// Behaviour that differs between Unity releases, chosen by the project's release (the managed
    /// side of the compatibility profile). Each answer names what was measured. Releases are grouped
    /// by family (MAJOR.MINOR, <see cref="Family"/>): an answer measured on 6000.0.71f1 serves every
    /// release of the 6000.0 family, and "other releases" below are the other families.
    /// </summary>
    internal static class Compat
    {
        // Property-editor window default measured by the defaultstate 6000.0.71f1 controls.
        internal const float PropertyEditorMinimumWidth = 275f;

        // Opaque stream ABI recorded by editorapi on 6000.0.71f1: 96 bytes, eight-byte alignment.
        // Payload production/access and other releases remain unsupported or unmeasured.
        internal const int ObjectChangeStreamStorageWords = 12;

        // The engine classes the Gizmos menu lists, with their AnnotationUtility flags (1 icon, 2
        // gizmo), measured on 6000.0.71f1 in a project with the default modules
        // (tests/projects/gameinterop annotation.builtin).
        internal static bool AnnotationTableMeasured => Family == ReleaseFamily.Unity6000_0;

        // The documents the Editor writes for new objects and new scenes (a scene save's starting
        // point), measured on 6000.0.71f1 (tests/projects/scenesave defaults.unity).
        internal static bool NewObjectDocumentsMeasured => Family == ReleaseFamily.Unity6000_0;
        internal static readonly (int ClassId, int Flags)[] BuiltinAnnotations = { (20, 3), (41, 2), (58, 2), (59, 2), (60, 2), (61, 2), (64, 2), (65, 2), (66, 2), (68, 2), (70, 2), (82, 3), (95, 2), (108, 3), (111, 2), (119, 3), (123, 1), (135, 2), (136, 2), (137, 2), (143, 2), (144, 2), (145, 2), (146, 2), (153, 2), (167, 2), (182, 3), (183, 2), (192, 2), (193, 2), (195, 2), (198, 1), (205, 2), (208, 2), (215, 3), (220, 1), (223, 2), (224, 2), (231, 2), (232, 2), (233, 2), (234, 2), (235, 2), (251, 2), (253, 2), (254, 2), (255, 2), (256, 2), (257, 2), (259, 3), (330, 3), (19719996, 2), (156049354, 2), (893571522, 2), (2083052967, 3) };

        /// <summary>Editor name lookup uses the class table measured for the release's family
        /// (exactly its measured release's): another family's table must not supply its answers.</summary>
        internal static System.Collections.Generic.SortedDictionary<int, ClassCoverage.UnityClass> EditorClassTable(string release)
            => ClassCoverage.UnityTable(MeasuredRelease(FamilyOf(release)), exact: true)
                ?? throw new NotSupportedException("UnityType metadata has not been measured for Unity " + release + ": it is measured only for " + MeasuredFamilies + ".");

        /// <summary>Managed argument and cast messages measured by editortuples and editorapi on
        /// 6000.0.71f1. Other releases retain their previous unmeasured runtime messages.</summary>
        internal static bool EditorManagedResourceErrors => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Play/Pause/Step menu availability in Play mode, editorapi 6000.0.71f1.
        /// Other lifecycle phases and releases remain unsupported.</summary>
        internal static bool EditorPlayMenusMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Service-owned preference retention measured by editorservices and analyticssession on 6000.0.71f1.
        /// Other releases keep their previous complete DeleteAll behavior, unmeasured.</summary>
        internal static bool ServicePreferencesPreserved => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Material scalar validation measured by assetadapters on 6000.0.71f1.
        /// Other releases retain the previous, unmeasured value storage.</summary>
        internal static bool PhysicsMaterialValuesMeasured => Family == ReleaseFamily.Unity6000_0;
        // Attribute URL observed by attributeinheritance on 6000.0.71f1.
        internal static string RayTracingShaderHelpURL => Family == ReleaseFamily.Unity6000_0
            ? "https://docs.unity3d.com//6000.0/Documentation/ScriptReference/Rendering.RayTracingShader.html"
            : throw new NotSupportedException("RayTracingShader help URL has not been measured for this Unity release.");
        /// <summary>Drag boundary events reach ancestor trickle callbacks in uitkruntime on
        /// 6000.0.71f1. Other releases use the documented target-only propagation, unmeasured here.</summary>
        internal static bool DragBoundaryEventsTrickle => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Pointer angle conversion and lazy reads measured by defaultstate on 6000.0.71f1.
        /// Other releases retain their previous independent angle values.</summary>
        internal static bool PointerAnglesMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>JSON string transfer measured by defaultstate on 6000.0.71f1.
        /// Other releases retain their previous embedded-null handling.</summary>
        internal static bool JsonStringTransferMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>The USS importer checks values against the release's property syntaxes and the
        /// style pass checks var() substitutions against them (measured for the 6000.0 family:
        /// tests/projects/ussvalidation); other families get no importer check and keep the earlier
        /// colour check of var() values, unmeasured.</summary>
        internal static bool UssValuesValidated => Family == ReleaseFamily.Unity6000_0;
        /// <summary>UxmlSerializedData metadata helpers measured by tests/projects/uxmlserializeddata
        /// on 6000.0.71f1: UxmlObjectReferenceAttribute(string) rejects the literal "null" (the
        /// two-argument overload accepts it) and UxmlAttributeNames normalizes a null obsoleteNames
        /// array to an empty array. Other families retain their unmeasured constructor values.</summary>
        internal static bool UxmlSerializedDataMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Multi-object Vector4 SerializedProperty binding measured by
        /// tests/projects/vector4binding on 6000.0.71f1: when hasMultipleDifferentValues is true,
        /// target[0].z is compared against the new w component. Other families compare target[0].w
        /// against the new w component.</summary>
        internal static bool Vector4MultiEditComparesZToW => Family == ReleaseFamily.Unity6000_0;
        /// <summary>GradientField value equality measured by tests/projects/gradientbinding
        /// (standalone.equality_and_null) on 6000.0.71f1: key-time-only edits are treated as equal
        /// to the current gradient and fire no change event. Other families compare the current
        /// and incoming key times.</summary>
        internal static bool GradientFieldIgnoresKeyTimeInEquality => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Uncreated object wrappers measured by defaultstate on 6000.0.71f1.
        /// Other releases retain their previous unmeasured wrapper state.</summary>
        internal static bool UncreatedObjectWrappersMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Removed window settings measured by defaultstate on 6000.0.71f1.
        /// Other releases retain their previous unmeasured setting storage.</summary>
        internal static bool RemovedEditorSettingsMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Managed constructor defaults measured by defaultstate on 6000.0.71f1.
        /// Other releases retain their previous, unmeasured initialization.</summary>
        internal static bool DefaultConstructorStateMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Local file response status measured by defaultstate on 6000.0.71f1.
        /// Other releases retain their previous, unmeasured status reporting.</summary>
        internal static bool LocalFileRequestStatusMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Collider-owned material copies measured by defaultstate on 6000.0.71f1.
        /// Other releases retain the previous, unmeasured reuse across colliders.</summary>
        internal static bool PhysicsMaterialInstancesMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Removed query settings ignore writes in the defaultstate 6000.0.71f1 reference.
        /// Other releases retain their previous, unmeasured per-command storage.</summary>
        internal static bool RemovedPhysicsSettingsMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Invalid 3D callback payload access measured by defaultstate on 6000.0.71f1.
        /// Other releases retain the previous, unmeasured null fallback.</summary>
        internal static bool PhysicsCallbackPayloadsMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>A scene loaded from a scene asset keeps its local physics scenes valid and
        /// empty after its unload (Simulate does nothing, queries find nothing), where a created
        /// scene's become invalid: measured by localphysics on 6000.0.71f1 (loaded.*, stale.*).
        /// Other releases follow the documented lifetime ("the same as the Scene") for both.</summary>
        internal static bool LoadedScenePhysicsOutlivesUnload => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Joint-drive input ranges measured by defaultstate on 6000.0.71f1.
        /// Other releases retain their previous, unmeasured stored drive values.</summary>
        internal static bool PhysicsJointDrivesMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Removed reverb settings and warnings measured by defaultstate on 6000.0.71f1.
        /// Other releases retain the previous, unmeasured stub response.</summary>
        internal static bool RemovedAudioSettingsMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Particle module properties without an owner, measured by defaultstate on 6000.0.71f1.
        /// Other releases retain their previous unmeasured missing-owner behavior.</summary>
        internal static bool ParticleModuleHandlesMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Removed particle members reject access in the defaultstate 6000.0.71f1 reference.
        /// Other releases retain their previous unmeasured compatibility paths.</summary>
        internal static bool RemovedParticleMembersMeasured => Family == ReleaseFamily.Unity6000_0;
        // Shared result code of Unity exception constructors in the defaultstate 6000.0.71f1 reference.
        internal const int UnityExceptionResult = -2147467261;
        /// <summary>Refresh-rate fractions, invalid-rate ordering and rounded integer conversion,
        /// measured by defaultstate on 6000.0.71f1. Other releases keep their unmeasured previous rules.</summary>
        internal static bool RefreshRateFractionsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>The errors of scene loads the build list cannot serve, measured by apierrors
        /// on 6000.0.71f1: the build profile wording, and an empty name or a negative index with
        /// their own error. Other releases keep the earlier build settings texts (releases before
        /// Unity 6 have no build profiles; later ones are not measured).</summary>
        internal static bool SceneLoadErrorsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Component factory eligibility measured by defaultstate on 6000.0.71f1.
        /// Other releases retain their previous, unmeasured creation rules.</summary>
        internal static bool ComponentFactoryEligibilityMeasured => Family == ReleaseFamily.Unity6000_0;
        internal static bool NativeComponentIsAbstract(Type type) => ComponentFactoryEligibilityMeasured
            && (type == typeof(UnityEngine.Behaviour) || type == typeof(UnityEngine.Renderer)
                || type == typeof(UnityEngine.Collider2D) || type == typeof(UnityEngine.Joint2D)
                || type == typeof(UnityEngine.AnchoredJoint2D) || type == typeof(UnityEngine.Effector2D)
                || type == typeof(UnityEngine.PhysicsUpdateBehaviour2D)
                || type == typeof(UnityEngine.Collider) || type == typeof(UnityEngine.Joint)
                || type == typeof(UnityEngine.AudioBehaviour) || type == typeof(UnityEngine.GridLayout));

        /// <summary>Runtime compressed channel formats and readback controls measured by
        /// tests/projects/texturecompression on 6000.0.71f1. Other releases keep their previous formats.</summary>
        internal static bool RuntimeTextureCompressionMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Runtime NavMesh one-cell region merging, measured by tests/projects/navmesh
        /// on 6000.0.71f1. Other releases retain the previous, unmeasured zero-cell threshold.</summary>
        internal static bool NavMeshRegionMergingMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Neighbour candidates cover the radius and predicted travel; nonfinite
        /// radius and velocity assignments are rejected. Measured by tests/projects/navmesh
        /// and avoidance on 6000.0.71f1. Other releases retain the previous, unmeasured
        /// point insertion and input handling.</summary>
        internal static bool NavMeshPredictionBoundsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Local heading, the movement threshold and up-axis alignment, measured by
        /// tests/projects/navlinks on 6000.0.71f1. Other releases retain the previous,
        /// unmeasured quaternion interpolation.</summary>
        internal static bool NavMeshOrientationMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>The current corridor reports its endpoint independently of hasPath; hasPath and
        /// steeringTarget report the corners found after each step, the corridor's length and the
        /// link state. Measured by tests/projects/navlinks and navmesh on 6000.0.71f1. Other
        /// releases retain the previous, unmeasured arrival-dependent reports.</summary>
        internal static bool NavMeshCorridorReportingMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Stopping distance selects planned speed before braking, measured by
        /// tests/projects/navlinks on 6000.0.71f1. Other releases retain their previous,
        /// unmeasured immediate stop.</summary>
        internal static bool NavMeshStoppingDistanceMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>A runtime point link's start and end each attach to the one polygon nearest to
        /// them among all the agent type's NavMesh data (within the agent type's climb), measured by
        /// tests/projects/navpath `--profile layers` on 6000.0.71f1. Other releases keep attaching
        /// to every tile over them, unmeasured.</summary>
        internal static bool NavMeshLinkBindingMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Straight paths keep points that are not exactly equal (a path's end 1e-7 m from
        /// its start is a second corner), measured by tests/projects/navarrival `query.*` on
        /// 6000.0.71f1. Other releases retain Detour's colocation threshold, unmeasured.</summary>
        internal static bool NavMeshStraightPathExactMergeMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>A braking agent arrives on the step that leaves at most
        /// <see cref="NavMeshBrakingArrivalDistance"/> to the end, measured by tests/projects/navarrival
        /// `lane.*` on 6000.0.71f1 (0.0000992 arrives, 0.000131 keeps moving). Other releases
        /// retain the previous, unmeasured arrival within a millimetre before the step.</summary>
        internal static bool NavMeshBrakingArrivalMeasured => Family == ReleaseFamily.Unity6000_0;

        internal const float NavMeshBrakingArrivalDistance = 1e-4f;

        /// <summary>Braking prepares and reports the next velocity from the steering plan,
        /// measured by tests/projects/navbaked on 6000.0.71f1. Other releases retain
        /// their previous, unmeasured use of the avoidance sample while braking.</summary>
        internal static bool NavMeshBrakingPlanMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>An arrived agent retains its destination request and resumes it when moved
        /// away, measured by tests/projects/navlinks on 6000.0.71f1. Other releases retain the
        /// previous, unmeasured cancellation of the completed request.</summary>
        internal static bool NavMeshDestinationRetentionMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>A surface move without a destination renews the standing corridor's endpoint
        /// and stale version, measured by tests/projects/navlinks on 6000.0.71f1. Other releases
        /// retain the previous, unmeasured corridor and staleness until the next update.</summary>
        internal static bool NavMeshIdleMoveMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Enabling an agent starts its reported velocity at zero, measured by
        /// tests/projects/navlinks on 6000.0.71f1. Other releases retain the previous,
        /// unmeasured velocity report until the next update.</summary>
        internal static bool NavMeshEnableVelocityMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Link activation updates query access immediately and invalidates an agent's
        /// corridor at its next update (Unity 6000.0.71f1, tests/projects/navlinks). Other releases
        /// retain the previous immediate mesh rebuild.</summary>
        internal static bool NavMeshLinkActivationMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Script code evaluates float32 expressions with double intermediates and rounds them
        /// where its IL stores a float32 (locals, arguments, fields, array elements, boxes, call
        /// arguments and returns), as the Editor's Mono does: measured by tests/projects/floatrounding
        /// on 6000.0.71f1 in both code optimization modes. Off unless <see cref="MonoFloats"/> opts in
        /// (user decision 2026-11-13: an artefact of the Editor's Mono runtime that IL2CPP builds do not
        /// share), and then only for the measured release; otherwise game code keeps CoreCLR's float32
        /// evaluation.</summary>
        internal static bool ScriptFloatsWide => MonoFloats && Family == ReleaseFamily.Unity6000_0;

        /// <summary>The opt-in to the Editor's Mono float evaluation of game code: the environment
        /// variable UNBOUND_MONO_FLOATS set to 1, true or on when the engine starts.</summary>
        internal static bool MonoFloats = OptedIn(Environment.GetEnvironmentVariable("UNBOUND_MONO_FLOATS"));

        internal static bool OptedIn(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value.Equals("off", StringComparison.OrdinalIgnoreCase))
                return false;
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase))
                return true;
            Console.Error.WriteLine($"unbound: UNBOUND_MONO_FLOATS={value} is not 1, true, on, 0, false or off; the Mono float opt-in stays off");
            return false;
        }

        /// <summary>Quaternion API accumulation and axis-conversion intermediates, measured by
        /// tests/projects/quaternioneuler on 6000.0.71f1. Other releases retain their unmeasured
        /// single-precision arithmetic.</summary>
        internal static bool QuaternionApiPrecisionMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Font asset ligature imports stop the batch at a duplicate in
        /// tests/projects/fontlayout, Unity 6000.0.71f1. Other releases skip duplicates.</summary>
        internal static bool FontLigatureDuplicateBatchMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Ligature character information uses the last consumed source element in
        /// tests/projects/fontlayout, Unity 6000.0.71f1. Other releases retain the starting span.</summary>
        internal static bool FontLigatureSourceSpanMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Preferred text size applies mark tables independently of feature switches
        /// in tests/projects/fontlayout, Unity 6000.0.71f1. Other releases honor the switches.</summary>
        internal static bool FontPreferredMarksMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Null mark matrix anchors retain header coordinates in the measured queries.
        /// tests/projects/fontmarks, Unity 6000.0.71f1.</summary>
        internal static bool FontMarkNullAnchorMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>The measured font feature lookup-index queries return records only for -1.
        /// tests/projects/fontfeatures, Unity 6000.0.71f1.</summary>
        internal static bool FontFeatureLookupIndexQueriesMeasured => Family == ReleaseFamily.Unity6000_0;
        internal static bool FontPairQueriesMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Panel atlas packing, texture ownership and dirty/reset transitions measured
        /// in tests/projects/uitkvector, Unity 6000.0.71f1. Other releases keep direct sampling,
        /// whose seams and texture lifetime remain unmeasured.</summary>
        internal static bool DynamicPanelAtlasMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Vector and rounded stencil shapes stay with an element's retained geometry
        /// until repaint. Measured in tests/projects/uitkvector on Unity 6000.0.71f1.
        /// Other releases retain per-paint mask generation, unmeasured.</summary>
        internal static bool RetainedStencilShapesMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Legacy generator pivot translation uses updated bounds, and primes the base
        /// space separately. Measured in tests/projects/textcolor on Unity 6000.0.71f1.
        /// Other releases retain generation-extents translation and batched glyph requests, unmeasured.</summary>
        internal static bool LegacyGeneratorBoundsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Legacy dynamic fonts: per-character OS fallback through the font names and the fallback families, and the built-in font without font data and Font.HasCharacter of dynamic fonts, measured in tests/projects/glyphraster.</summary>
        internal static bool LegacyGlyphRasterMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Reading an existing dynamic glyph keeps it for this frame's atlas rebuild.
        /// Measured in tests/projects/textcolor on Unity 6000.0.71f1.</summary>
        internal static bool LegacyFontReadKeepsGlyph => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Property scope labels, notifications and GUI state measured in Layout and
        /// Repaint by tests/projects/imguiskin on Unity 6000.0.71f1. Other families are unmeasured.</summary>
        internal static bool EditorPropertyScopeMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Numeric GUILayoutUtility.GetRect results in Layout and Used events,
        /// measured in tests/projects/imguiskin on Unity 6000.0.71f1.</summary>
        internal static bool NumericGUILayoutRectMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Consumed events use empty layout groups and do not advance arranged rects
        /// in Unity 6000.0.71f1 (tests/projects/imguiskin and editortogglegroup).</summary>
        internal static bool UsedGUILayoutMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Explicit GUI texture draws keep their tint alpha inside disabled groups,
        /// measured in tests/projects/editordrawrect on Unity 6000.0.71f1.</summary>
        internal static bool ExplicitGuiTextureAlphaMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>EditorGUILayout.GetControlRect sizing and default margins measured in
        /// tests/projects/imguiskin on Unity 6000.0.71f1.</summary>
        internal static bool EditorControlRectMeasured => Family == ReleaseFamily.Unity6000_0;
        internal const float EditorControlLabelGap = 5f;
        static UnityEngine.GUIStyle s_EditorControlRectStyle;
        internal static UnityEngine.GUIStyle EditorControlRectStyle => s_EditorControlRectStyle ??= new UnityEngine.GUIStyle
        {
            margin = new UnityEngine.RectOffset(3, 3, 2, 2),
            fixedHeight = UnityEditor.EditorGUIUtility.singleLineHeight,
            stretchWidth = true,
        };

        /// <summary>Inspector style defaults in the Game view, measured in
        /// tests/projects/editorstyles on Unity 6000.0.71f1.</summary>
        internal static bool InspectorGuiStylesMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>IMGUI buttons and toggles submit on an unmodified key event with a key code
        /// in Unity 6000.0.71f1 (tests/projects/editortogglegroup).</summary>
        internal static bool GuiControlKeySubmitMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Mouse-button events and explicit mouse capture across GUI group clips in
        /// Unity 6000.0.71f1 (tests/projects/editortogglegroup).</summary>
        internal static bool GuiMouseButtonClipMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Explicit group size options keep the opposite axis's outer child margins
        /// inside its measured size (Unity 6000.0.71f1, tests/projects/imguiskin).</summary>
        internal static bool OppositeLayoutSizeKeepsMarginsInside => Family == ReleaseFamily.Unity6000_0;

        // An unstyled horizontal group's height uses the tallest child. Child margins still
        // offset the children and the smallest margins are passed to the parent.
        // Measured by tests/projects/editortogglegroup.
        internal static bool UnstyledHorizontalHeightUsesChildMaximum => Family == ReleaseFamily.Unity6000_0;

        /// <summary>ReorderableList geometry defaults and height calculation measured in Play mode
        /// by tests/projects/reorderablelist on Unity 6000.0.71f1. Other families are unmeasured.</summary>
        internal static bool EditorReorderableListLayoutMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Querying the owning editor control's focus outside OnGUI throws on
        /// Unity 6000.0.71f1 (tests/projects/imguiskin). Other families are unmeasured.</summary>
        internal static bool EditorKeyFocusRequiresGUI => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Popup content defaults and window lifecycle measured by tests/projects/popupcontent
        /// on Unity 6000.0.71f1. Other families retain their unmeasured answers.</summary>
        internal static bool EditorPopupWindowMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Initial Analytics settings from tests/projects/analyticssettings on Unity
        /// 6000.0.71f1. The build-facing answers are measured for Linux, Android and iOS.</summary>
        internal static bool EditorAnalyticsSettingsMeasured => Family == ReleaseFamily.Unity6000_0;

        internal static bool EditorAnalyticsBuildTargetMeasured(UnityEditor.BuildTarget target) =>
            target is UnityEditor.BuildTarget.StandaloneLinux64 or UnityEditor.BuildTarget.Android or UnityEditor.BuildTarget.iOS;

        /// <summary>Runtime Analytics results with configured legacy Analytics in an unbound
        /// project, measured by tests/projects/editorservices on Unity 6000.0.71f1.</summary>
        internal static bool OfflineLegacyAnalyticsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Dynamic Font texture requests and queries share a 500-pixel maximum on
        /// Unity 6000.0.71f1 (tests/projects/textcolor). Other families are unmeasured.</summary>
        internal static int LegacyFontAtlasPixelSize(int pixels)
        {
            const int MaximumMeasuredSize = 500;
            return Family == ReleaseFamily.Unity6000_0 ? Math.Min(pixels, MaximumMeasuredSize) : pixels;
        }
        /// <summary>Constructor, animation-job API contexts and component initialization defaults measured in tests/projects/threadapis.</summary>
        internal static bool MainThreadContextsMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Invoke deadlines and delayed-call points measured in tests/projects/invokesteps.</summary>
        internal static bool InvokeTimingMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Separate preparation and activation of scene operations, measured by
        /// tests/projects/editorcallbacks/SceneLoop on Unity 6000.0.71f1.</summary>
        internal static bool SceneAsyncPreparationMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Public root hide flag ownership and component propagation measured by
        /// tests/projects/hiddenflags on Unity 6000.0.71f1.</summary>
        internal static bool HideFlagsRootOwnershipMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Material reload wrapper identity and shared state, tests/projects/materialalias
        /// on Unity 6000.0.71f1. Other releases retain the unmeasured previous wrapper.</summary>
        internal static bool MaterialReloadWrappersMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Captured exception formatting, tests/projects/stackcapture on 6000.0.71f1.
        /// Other releases retain their unmeasured previous exception text.</summary>
        internal static bool CapturedStackTracesMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Created clip stream memory follows the creation option and two truncated
        /// buffers, tests/projects/clipmemory on 6000.0.71f1. Other releases remain unmeasured.</summary>
        internal static bool CreatedClipStreamMemoryMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Created text and binary payload allocation accounting, Unity 6000.0.71f1
        /// (tests/projects/textassetmemory).</summary>
        internal static bool TextAssetMemoryMeasured => Family == ReleaseFamily.Unity6000_0;


        /// <summary>CRT loading, callback delivery and render-cycle scheduling,
        /// tests/projects/crtload on 6000.0.71f1. Other releases retain unmeasured prior behavior.</summary>
        internal static bool CustomRenderTextureLifecycleMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Pending Starts without an earlier callback visit run at the PostLateUpdate
        /// scripting point, measured by editorcallbacks/SceneLoop on Unity 6000.0.71f1.</summary>
        internal static bool PendingStartsAfterLateUpdate => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Camera dependency repair measured by
        /// flarelayerdependency in Unity 6000.0.71f1.</summary>
        internal static bool FlareCameraRepairMeasured => Family == ReleaseFamily.Unity6000_0;
        internal static bool FlareCameraRepairMeasuredFor(string release) => FamilyOf(release) == ReleaseFamily.Unity6000_0;

        /// <summary>Deferred object lifetime at delayed scripting points, measured in
        /// tests/projects/destroyphases, plus activation destruction guards in hiddenflags,
        /// on Unity 6000.0.71f1.</summary>
        internal static bool DestroyPhasesMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Scene objects read from files use positive instance IDs, independently of asset
        /// persistence, measured by instanceids on 6000.0.71f1. Other releases are unmeasured.</summary>
        internal static bool SceneFileInstanceIdsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Automatic rendering follows the public player loop and precedes end-of-frame
        /// coroutine resumes. Its first Play frame renders without end-of-frame completion. Measured
        /// by destroyphases and eofstartup on 6000.0.71f1; other releases retain their prior timing.</summary>
        internal static bool RenderingAfterPublicLoop => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Simulation mode checks, contact refresh across sub-steps and contact payload
        /// sampling/impulse accumulation, measured by physics2dadvanced and physics2ddynamics
        /// on Unity 6000.0.71f1.</summary>
        internal static bool Physics2DSimulationMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Generated 2D collider frames, partitions, manual settings and outline point tests, measured by
        /// geometryframe2d on Unity 6000.0.71f1.</summary>
        internal static bool Physics2DGeneratedGeometryMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Cast polygon contact clipping and witness selection, measured by
        /// query2dparity on Unity 6000.0.71f1.</summary>
        internal static bool Physics2DCastContactsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Slide contact spacing, iteration accounting and argument validation,
        /// measured by physics2dadvanced on Unity 6000.0.71f1.</summary>
        internal static bool Physics2DSlideMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>The fixed scheduler uses the same whole-tick time domain as the frame clock,
        /// measured by physics2dadvanced automatic modes on Unity 6000.0.71f1.</summary>
        internal static bool FixedClockTicksMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>What a scene without lighting data stores and reflects: a zero ambient probe
        /// and a black cube, or the built-in lighting data when an earlier release saved its
        /// RenderSettings with a new scene's environment; measured in tests/projects/realtimegi
        /// (nodata*, legacy*) on Unity 6000.0.71f1. In other releases Unbound gives such a scene
        /// zero and black with a warning: releases before Unity 6 bake both by default (the
        /// Unity 6 upgrade guide), and later ones are not measured.</summary>
        internal static bool SceneWithoutLightingDataMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>The project's release's major version (2022, 6000, ...).</summary>
        internal static int Major
        {
            get
            {
                var v = UnityEngine.ProjectInfo.UnityVersion ?? "";
                int dot = v.IndexOf('.');
                return int.TryParse(dot < 0 ? v : v.Substring(0, dot), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : 6000;
            }
        }

        /// <summary>Old GUILayer documents are discarded on scene load in the measured release
        /// (tests/projects/classids). Other releases retain the unsupported-class diagnostic.</summary>
        internal static bool RemovesLegacyGuiLayer(string release) => FamilyOf(release) == ReleaseFamily.Unity6000_0;

        /// <summary>Asset resource ownership and clone diagnostics measured by assetinstantiate
        /// in 6000.0.71f1. Other releases require their own measurement.</summary>
        internal static bool AssetCloneResourcesMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Linux Editor display answers measured by inputplatform on 6000.0.71f1.
        /// Screen's resolution choices describe the Editor, while DisplayInfo comes from the OS.</summary>
        internal static bool EditorDisplayMeasured => OperatingSystem.IsLinux() && Family == ReleaseFamily.Unity6000_0;

        /// <summary>macOS Editor display answers measured by macdisplay on 6000.0.71f1: the
        /// usable area (the work area) as the current resolution and the display info's size, at
        /// the host's exact refresh ratio; the resolutions the display modes' sizes at that rate,
        /// the current mode's as the usable area, plus the desktop's size in points without a rate.
        /// Other releases keep the host's answers until measured.</summary>
        internal static bool MacEditorDisplayMeasured => OperatingSystem.IsMacOS() && Family == ReleaseFamily.Unity6000_0;

        // Standard sizes measured on a desktop larger than every size in the list. The smaller
        // captures establish that each dimension is filtered independently against the desktop.
        internal static readonly (int Width, int Height)[] EditorDisplaySizes =
        {
            (640, 480), (720, 480), (768, 480), (800, 480), (854, 480),
            (960, 540), (1024, 576), (800, 600), (1024, 600), (960, 640),
            (1024, 640), (1136, 640), (960, 720), (1152, 720), (1280, 720),
            (1024, 768), (1152, 768), (1280, 768), (1366, 768), (1280, 800),
            (1152, 864), (1280, 864), (1440, 900), (1600, 900), (1280, 960),
            (1440, 960), (1280, 1024), (1600, 1024), (1400, 1050), (1680, 1050),
            (1920, 1080), (2560, 1080), (2048, 1152), (1600, 1200), (1920, 1200),
            (1792, 1344), (1856, 1392), (1920, 1440), (2160, 1440), (2304, 1440),
            (2560, 1440), (3440, 1440), (2048, 1536), (2560, 1600), (2880, 1800),
            (3200, 1800), (2560, 1920), (2560, 2048), (3200, 2048), (3840, 2160),
            (4096, 2160), (5120, 2160), (3200, 2400), (3840, 2400), (3200, 2560),
            (4096, 2560), (5120, 2880), (4096, 3072), (5120, 3200), (5760, 3240),
            (5120, 4096), (6400, 4096), (7680, 4320), (6400, 4800), (7680, 4800),
            (8192, 5120),
        };

        /// <summary>Group input startup fades follow the mixer graph's processing age, including
        /// groups first used later (tests/projects/mixerduck, 6000.0.71f1).</summary>
        internal static bool MixerGraphStartupMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Static effect-slot Wet mixing measured on 6000.0.71f1 by the five
        /// audio effect probes. Other releases keep rejecting this unmeasured mechanism.</summary>
        internal static bool MixerStaticWetMeasured => Family == ReleaseFamily.Unity6000_0;


        /// <summary>The Editor starts with its saved mixer snapshot selection, measured on
        /// 6000.0.71f1 (tests/projects/mixersuspend). Other releases retain the start
        /// snapshot until their Editor selection behavior is measured.</summary>
        internal static bool MixerSnapshotSelectionMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Trailing attenuation submits gain directly; interior attenuation ramps per
        /// frame. Measured on 6000.0.71f1 (tests/projects/mixersuspend, mixerduck).
        /// Other releases retain their unmeasured gain ramp.</summary>
        internal static bool MixerParameterGainMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>AudioSettings.Reset invalidates generated clip data and reloads
        /// imported data once per shared resource. Measured on 6000.0.71f1
        /// (tests/projects/mixersuspend); clone ownership/loading uses AssetCloneResourcesMeasured.
        /// Other releases retain their existing clip data until measured.</summary>
        internal static bool AudioClipResetDataMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Closing offline capture for a reset processes the old output buffer,
        /// measured on 6000.0.71f1 by tests/projects/mixersuspend's reset delivery control.
        /// Other releases retain their unmeasured reset delivery.</summary>
        internal static bool AudioResetCaptureTailMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Script-created clip duration uses whole milliseconds, measured on
        /// 6000.0.71f1 (tests/projects/mixersuspend). Imported duration remains sample-based.
        /// Other releases retain sample-based duration until measured.</summary>
        internal static bool ScriptAudioClipDurationMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Decoded resampling uses a single-precision rate ratio and holds the final
        /// frame until non-looping playback ends,
        /// measured on 6000.0.71f1 (tests/projects/mixersuspend). Other releases retain
        /// a double rate ratio and zero padding until measured; looping still wraps.</summary>
        internal static bool DecodedAudioResamplingMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Destroy requests stop AudioSource voices before the deferred object
        /// destruction, including sources below a destroyed GameObject. Measured on
        /// 6000.0.71f1 (tests/projects/mixersuspend). Other releases retain end-of-frame cleanup.</summary>
        internal static bool AudioSourceDestroyRequestMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>A streamed clip's cached buffer is owned by the most recent voice
        /// that starts it; stopping its source releases the buffer. Measured on
        /// 6000.0.71f1 (tests/projects/mixersuspend). Other releases keep their previous
        /// loading and stop behavior until measured.</summary>
        internal static bool StreamingClipBufferLifetimeMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Send/Receive routing, startup and Duck Volume detector state, measured on
        /// 6000.0.71f1 (tests/projects/mixerduck). Other releases fail until measured.</summary>
        internal static bool MixerSidechainMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Linear scaled-time Send and Duck Volume snapshot changes, measured by
        /// tests/projects/mixerduck. Unscaled-time transitions remain unmeasured.</summary>
        internal static bool MixerTimedSidechainMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Normalized snapshot progress accumulation, measured by the live public
        /// parameter trace in tests/projects/audiodistortion. Other releases retain elapsed-time division.</summary>
        internal static bool MixerSnapshotProgressMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Single-snapshot scalar curves and linear weighted-array transitions,
        /// measured by tests/projects/mixerduck on 6000.0.71f1.</summary>
        internal static bool MixerSnapshotCurvesMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Default mixer Pitch Shifter DSP measured on 6000.0.71f1.
        /// Other releases fail until their kernels and state have been measured.</summary>
        internal static bool MixerPitchShifterMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Mixer Normalize's linked peak envelope, measured on 6000.0.71f1
        /// (tests/projects/audionormalize). Other releases fail until measured.</summary>
        internal static bool MixerNormalizeMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>The modulated Q14 delay line that mixer Chorus and Flange and AudioChorusFilter
        /// share, measured on 6000.0.71f1 (tests/projects/audiochorus,
        /// tests/projects/audioflange). Other releases fail until measured.</summary>
        internal static bool ModulatedDelayMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Compressor DSP parameter clamps, measured by tests/projects/audiopitch.</summary>
        internal static bool MixerCompressorParametersMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Speaker-wide effects, measured by tests/projects/audiopitch's surround profile.</summary>
        internal static bool AudioSurroundEffectsMeasured => Family == ReleaseFamily.Unity6000_0;
        /// <summary>Every decoded input of a clip of three to eight channels, 2D or spatial,
        /// streamed or not, to every output layout, measured by audiopitch/VOICE_INPUT.md,
        /// SIX_LAYOUTS.md, SIX_SPATIAL.md and INPUT_CONTEXTS.md. Wider clips, streamed downloads
        /// and spatial voices in Prologic output are rejected on this release.</summary>
        internal static bool AudioMultichannelVoicesMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Block-constant effect parameters applying the measured history rules,
        /// measured on 6000.0.71f1 by tests/projects/audiodistortion's live profile.</summary>
        internal static bool MixerEffectTransitionsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Shared effect-slot wet/dry gains over 64 frames, measured on 6000.0.71f1
        /// by tests/projects/audiodistortion's live profile. Other releases keep static slots.</summary>
        internal static bool MixerWetTransitionsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Component tails end one DSP buffer after the last voice's containing
        /// buffer, including partial device periods (tests/projects/audioreverb, 6000.0.71f1).
        /// Other releases retain their previous render-chunk frontier until measured.</summary>
        internal static bool AudioComponentTailMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Echo component delay converts to unsigned whole milliseconds before the shared
        /// mixer kernel's frame conversion (tests/projects/audiodistortion, 6000.0.71f1).
        /// Other releases retain their previous float setter and 10 ms DSP minimum until measured.</summary>
        internal static bool AudioComponentEchoDelayMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>AudioLowPassFilter's cutoff is a curve over the listener's distance: keys of
        /// (cutoff - 10) / 21990, a new filter's key 5000 / 22000, set curves scaled to end at
        /// one, older serialized versions in world units (tests/projects/audiolowpass, measured on
        /// 6000.0.71f1 for the 6000.0 family). Other releases keep a cutoff clamped on set
        /// (default 5000) and fail loudly for a custom cutoff curve until measured.</summary>
        internal static bool AudioLowPassCurveMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Listener component ordering and per-source bypass, measured on
        /// 6000.0.71f1 by tests/projects/audiodistortion. Other releases retain reverb-only
        /// listener support and their prior bypassEffects routing until measured.</summary>
        internal static bool AudioListenerChainMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Mono/stereo source mapping, gain ramps, distance curves, relative-velocity
        /// Doppler and interpolation lookahead measured by tests/projects/audiodistortion on
        /// 6000.0.71f1. Other releases retain the previous approximate matrix, sampled curves,
        /// inverse-source-velocity Doppler and max-distance clamp until measured.</summary>
        internal static bool AudioSpatialStereoMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Source/listener bypass activation, retained disabled units and source chain
        /// reconnection measured by tests/projects/audiodistortion live controls on
        /// 6000.0.71f1. Other releases keep the previous bypass and disabled-unit behavior.</summary>
        internal static bool AudioComponentActivationMeasured => Family == ReleaseFamily.Unity6000_0;

        // Per-voice zone send placement and completion (tests/projects/audioreverb).
        internal static bool AudioZoneVoiceSendMeasured => Family == ReleaseFamily.Unity6000_0;

        // First zone processing defaults (tests/projects/audioreverb).
        internal static bool AudioZoneStartupMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>The Distortion waveshaper of the mixer effect and AudioDistortionFilter, measured
        /// on 6000.0.71f1 (tests/projects/audiodistortion). Other releases fail until measured.</summary>
        internal static bool DistortionMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>AudioMixer.GetFloat after ClearFloat, before the next audio update, measured on
        /// 6000.0.71f1 (tests/projects/mixerparams): an override already submitted to audio
        /// still reads until that update. Every release reads the snapshot value for a clear of an
        /// unsubmitted override, as GetFloat documents; other releases read it for a submitted one
        /// too (the exception is unmeasured there; docs/COMPATIBILITY_GAPS.md).</summary>
        internal static bool MixerClearFloatSubmissionMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Where Input's errors point to its settings: 6000.0's Editor says (measured)
        /// "Edit -> Project Settings... -> Input Manager"; earlier releases "Edit -> Settings -> Input".</summary>
        internal static string InputSettingsMenu => Major >= 6000 ? "Edit -> Project Settings... -> Input Manager" : "Edit -> Settings -> Input";

        /// <summary>Start batches (a step's rate and burst births, and Emit(count)): their
        /// initialization, birth ages and removal of births that die at their start, measured
        /// by tests/projects/particlerandom and particlemeshshape texture/clip/* on
        /// 6000.0.71f1. Other releases keep the per-particle emission path until measured.</summary>
        internal static bool ParticleRateBatchesMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Combined shape modifiers measured by tests/projects/particlerandom.
        /// Other releases retain their existing sampling until measured.</summary>
        internal static bool ParticleShapeModifiersMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Batch physics queries use the last synchronized transforms even with
        /// autoSyncTransforms enabled (physicsbatch, 6000.0.71f1). Other releases retain automatic
        /// preparation until measured.</summary>
        internal static bool PhysicsBatchRetainsSyncedTransforms => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Terrain tree material bindings retain their creation material, measured in
        /// tests/projects/physicsbatch on 6000.0.71f1. Other releases keep assignment propagation
        /// until their binding lifetime is measured.</summary>
        internal static bool TerrainTreeMaterialRetainsCreationBinding => Family == ReleaseFamily.Unity6000_0;

        /// <summary>The family Unbound's rendering parity was measured for (tests/projects/*
        /// Reference/unity-6000.0.71f1). The rendering answers below give its measured behaviour
        /// to every release of the 6000.0 family; the other families' answers are listed in
        /// docs/COMPATIBILITY_GAPS.md ("Rendering rules measured on one release").</summary>
        internal static bool RenderingMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>DrawMeshNow and CommandBuffer.DrawMesh keep the submitted winding, including
        /// negative determinants (samplerstates StencilFaces/RendererFaces). Other releases retain
        /// the renderer-style determinant correction until recorded.</summary>
        internal static bool ImmediateMeshKeepsWinding => RenderingMeasured;

        /// <summary>Device-specific texture transfers measured by textureformats on NVIDIA.
        /// Other releases retain the earlier framebuffer conversion.</summary>
        internal static bool DeviceTextureReadback => RenderingMeasured;

        /// <summary>A render pass subpass with several color outputs of which exactly one clears
        /// (its load action, first use in the pass) clears every output it binds to that color on
        /// the Editor's OpenGL Core device; two or more clear each to its own color and loading
        /// ones are kept (tests/projects/urp subpass_mrt, subpass_mrt_cleared, subpass_mrt_mixed).
        /// Other releases: each clearing output to its own color, the others kept.</summary>
        internal static bool SingleSubpassClearFillsOutputs => RenderingMeasured;

        /// <summary>Light-probe gizmo scale observed outside repaint callbacks in renderapi.</summary>
        internal static float GizmoProbeSize => RenderingMeasured ? 0.299999982f : 0.3f;

        /// <summary>Standalone mesh-instancing batch limits measured on 6000.0.71f1 GLCore.
        /// x/y: transform capacities without/with uniform scaling; z: property-buffer bytes.
        /// Other releases keep sequential IDs until measured.</summary>
        internal static UnityEngine.Vector4 MeshInstanceBatchLimits => RenderingMeasured
            ? new UnityEngine.Vector4(511, 1023, 65536, 0) : UnityEngine.Vector4.zero;

        /// <summary>Frames after a CPU synchronisation GraphicsFence ran until it reports passed
        /// (measured on the 6000.0.71f1 OpenGLCore Editor: tests/projects/cmdbuffers fence.*).
        /// Other releases report it once the GPU has passed it.</summary>
        internal static int GraphicsFenceFrames => RenderingMeasured ? 2 : 0;

        /// <summary>Graphics.RenderPrimitivesIndexed with an instance count of zero draws one
        /// instance (measured on 6000.0.71f1 OpenGLCore: cmdbuffers render.indexed.instances.zero);
        /// other releases draw none, as the count says.</summary>
        internal static bool ZeroIndexedInstancesDrawOne => RenderingMeasured;

        /// <summary>The built-in pipeline's ambient projection of the sky leaves out the main
        /// light's colour (<c>_LightColor0</c> zero; the procedural sky's sun disk then adds
        /// nothing, while its scattering keeps the light's direction). Measured on 6000.0.71f1:
        /// procedural skies with no, simple, high-quality and zero-size disks give the same
        /// ambient probe. That the colour, not only the disk, is left out for every sky shader
        /// is a hypothesis: a sky tinted by <c>_LightColor0</c> is unmeasured
        /// (docs/COMPATIBILITY_GAPS.md). Other releases project the sky as drawn.</summary>
        internal static bool SkyAmbientWithoutLightColor => RenderingMeasured;

        /// <summary>The automatic Animator update split over two player-loop leaves (state machines,
        /// events and movement in DirectorUpdateAnimationBegin; pose writes and IK passes, layer by
        /// layer over all Animators, in DirectorUpdateAnimationEnd; the fixed-step leaves likewise),
        /// measured by tests/projects/animcallbacks on 6000.0.71f1
        /// (<see cref="AnimationPhases"/>). Other releases keep Unbound's earlier single pass in the
        /// Begin leaf (unmeasured there).</summary>
        internal static bool AnimationUpdateSplitMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Humanoid root motion composed by mixer trees (any input count and nesting, any
        /// input weight sum blended unnormalized, weights changed between evaluations or while
        /// culled), measured by animculling humanMixerMotion/humanMixerShape on
        /// 6000.0.71f1. Other releases keep the unsupported diagnostic.</summary>
        internal static bool HumanPlayableMixerMotionMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Humanoid root motion and bodies composed by AnimationLayerMixerPlayables over
        /// humanoid clips and mixers (override layers by weight through their masks' body parts,
        /// additive layers leaving the motion), measured by tests/projects/humanoid mixer.* on
        /// 6000.0.71f1. Other releases keep the unsupported diagnostic.</summary>
        internal static bool HumanPlayableLayerMixerMotionMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>After skipped updates, a visibility return replays the last event intervals of
        /// clips that a mixer or layer mixer combined (at any depth), but not of a clip that is the
        /// output's source or is reached only through passthrough playables (6000.0.71f1,
        /// animculling explicit/later and explicit/eventShape). Other releases fire no events on
        /// visibility return, as the culling documentation describes.</summary>
        internal static bool PlayableEventsReplayOnVisibility => Family == ReleaseFamily.Unity6000_0;

        /// <summary>A CullCompletely-culled Animator holds only the animation playables of its
        /// outputs' trees (and what is below them); plain and script playables above them keep the
        /// graph's clock. Measured by tests/projects/animculling clock/ on 6000.0.71f1.
        /// Other releases (unmeasured) hold each culled output's whole source tree, as Unbound did
        /// before, but with the measured hold itself: it wins over other outputs reaching a held
        /// playable and pauses held behaviours instead of preparing them with a zero step.
        /// The same measured release also re-blends a held clip/mixer tree whose weights changed
        /// while culled at the visibility return (animculling humanMixerShape/live,
        /// genericMixerSum/liveCulled); other releases replay the last blend and warn.</summary>
        internal static bool PlayableCullingHoldsAnimationPlayablesOnly => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Animator automatic Play-entry timing and startup controls measured by
        /// tests/projects/animstartup in 6000.0.71f1's Linux Editor, and the time of a state
        /// a Play or CrossFade request enters (held for the update it enters in: animstartup's Play
        /// offsets, humanoid playState and crossFade*). Other releases retain the existing behavior
        /// (the entered state advances) until measured.</summary>
        internal static bool AnimatorStartupMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Root-space skinned bounds, single-bone defaults and cached public world
        /// bounds measured by skinnedbounds in the Linux 6000.0.71f1 Editor.</summary>
        internal static bool SkinnedBoundsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Birth sub-emitter origins, emission intervals and ages measured by
        /// tests/projects/particlerandom (Simulate) and tests/projects/particlerender birthstart/*
        /// (the frame's update). Other releases retain their previous path.</summary>
        internal static bool ParticleBirthStepsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Sub-emitters of every type play from each update of their playing parent and
        /// stop at the end of a frame that leaves them empty, measured by
        /// tests/projects/particlerender birthstart/* and subemitters played/* on 6000.0.71f1.
        /// Other releases keep the previous rule (a child plays once it receives particles).</summary>
        internal static bool ParticleSubEmitterStartMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Prewarm simulates the longest start lifetime plus the sub-emitter chains'
        /// lifetimes in one call ending on a loop's end, keeps the clock where it ends and emits
        /// sub-emitter children within their lifetime of the end without aging them, measured by
        /// tests/projects/particlerender prewarm/* on 6000.0.71f1. Other releases keep the previous
        /// model (one longest lifetime from time 0, then time 0) until measured.</summary>
        internal static bool ParticlePrewarmMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Start lifetimes take a reciprocal estimate with two Newton corrections
        /// instead of a correctly rounded division, measured by tests/projects/particlerandom
        /// (birthclock/, model/clock/). Other releases keep the division until measured.</summary>
        internal static bool ParticleLifetimeReciprocalEstimate => Family == ReleaseFamily.Unity6000_0;

        /// <summary>BakeTexture of mesh particles whose bake has a 16-bit index buffer fills the
        /// indices texture with that buffer read as 32-bit words (two indices a texel), measured
        /// by tests/projects/particlerender meshindex/* and bake/mesh on 6000.0.71f1. Other
        /// releases write one index a texel, as for billboards, until measured.</summary>
        internal static bool ParticleMeshBakeTextureReadsIndexPairs => Family == ReleaseFamily.Unity6000_0;

        /// <summary>6000.0's BakeTrailsMesh joins ribbon strips in its index count and rejects
        /// the resulting out-of-bounds indices. Regular ribbon rendering is unaffected.</summary>
        internal static bool ParticleRibbonBakeRejectsMultipleStrips => Major >= 6000;

        /// <summary>6000.0.71f1 has no serialized PCM switch and ignores an injected m_EnablePCM
        /// (measured by physicsscenesettings). Older settings documents expose this legacy switch;
        /// their selectable SAT path is documented, but not yet measured by this fixture.</summary>
        internal static bool PhysicsPcmEnabled(bool projectValue) => Major >= 6000 || projectValue;

        /// <summary>The public physics material type was renamed in Unity 6.</summary>
        internal static string PhysicsMaterialPropertyTypeName => Major >= 6000 ? "PPtr<PhysicsMaterial>" : "PPtr<PhysicMaterial>";

        /// <summary>Contact point requests and compound collision-message routing, measured by
        /// contactmodify3d in 6000.0.71f1. Other releases retain unconditional point reporting and
        /// collider-plus-body delivery until
        /// measured (docs/COMPATIBILITY_GAPS.md).</summary>
        internal static bool SelectiveContactReportsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>ContinuousDynamic also sweeps Discrete dynamic targets, measured by
        /// physicsccdhinge geometry-controls in 6000.0.71f1. Other releases retain the previous
        /// exclusion until their pair eligibility is measured.</summary>
        internal static bool CcdDiscreteDynamicTargetsMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Constrained velocity assignments and dynamic pose publication,
        /// measured by physicsccdhinge on 6000.0.71f1. Other releases remain unmeasured.</summary>
        internal static bool ConstrainedBodyStateMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Convex collider-local cooking and pose separation measured by physicsccdhinge
        /// on 6000.0.71f1. Other releases retain body-frame cooking until measured.</summary>
        internal static bool ConvexColliderFrameMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Collider updates, attachment transfers, static mesh actor replacement and
        /// idle-scene lifetime measured by physicsccdhinge in 6000.0.71f1.</summary>
        internal static bool ColliderLifetimeMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>Hinge solver limit clamping and contact padding measured by physicsccdhinge in 6000.0.71f1.
        /// Other releases keep the previous unclamped limits and 0.01-radian default pad.</summary>
        internal static bool HingeLimitClampingMeasured => Family == ReleaseFamily.Unity6000_0;

        /// <summary>The project's release as numbers and stage letter (6000.0.71f1: 6000, 0, 71,
        /// 'f', 1); null when it cannot be read.</summary>
        internal static (int Major, int Minor, int Patch, char Stage, int Build)? Release => Current.Parsed;

        /// <summary>The measured release family of the project's release: every release of a
        /// family (MAJOR.MINOR; any patch, stage or build) gets the answers measured on the
        /// family's <see cref="MeasuredRelease"/>. Another family (6000.1, 6000.3) is another
        /// version and keeps the unmeasured behaviour until it is measured. The Rust side asks
        /// the same question (unbound_compat::MeasuredFamily).</summary>
        internal static ReleaseFamily Family => Current.Family;

        /// <summary>The measured family of <paramref name="release"/> (for a release other than
        /// the project's).</summary>
        internal static ReleaseFamily FamilyOf(string release) => FamilyOf(Parse(release ?? ""));

        static ReleaseFamily FamilyOf((int Major, int Minor, int Patch, char Stage, int Build)? release)
            => release is { Major: 6000, Minor: 0 } ? ReleaseFamily.Unity6000_0 : ReleaseFamily.Unmeasured;

        /// <summary>The Editor release a family's answers were measured on; null for
        /// <see cref="ReleaseFamily.Unmeasured"/>.</summary>
        internal static string MeasuredRelease(ReleaseFamily family) => family switch
        {
            ReleaseFamily.Unity6000_0 => Measured6000_0,
            _ => null,
        };

        /// <summary>The release the 6000.0 family's answers were measured on.</summary>
        internal const string Measured6000_0 = "6000.0.71f1";

        /// <summary>Names the measured families in messages of the paths other families lack.</summary>
        internal const string MeasuredFamilies = "the Unity 6000.0 release family (measured on 6000.0.71f1)";

        // Parsed once per version string: hot paths (per draw, per particle) ask. The cache is
        // replaced whole, so another thread sees a consistent entry.
        static ParsedRelease Current
        {
            get
            {
                var v = UnityEngine.ProjectInfo.UnityVersion ?? "";
                var cached = s_Release;
                if (cached != null && string.Equals(cached.Version, v, StringComparison.Ordinal)) return cached;
                var parsed = Parse(v);
                cached = new ParsedRelease(v, parsed, FamilyOf(parsed));
                s_Release = cached;
                return cached;
            }
        }

        sealed class ParsedRelease
        {
            internal readonly string Version;
            internal readonly (int Major, int Minor, int Patch, char Stage, int Build)? Parsed;
            internal readonly ReleaseFamily Family;
            internal ParsedRelease(string version, (int, int, int, char, int)? parsed, ReleaseFamily family)
            {
                Version = version;
                Parsed = parsed;
                Family = family;
            }
        }

        static ParsedRelease s_Release;

        static (int Major, int Minor, int Patch, char Stage, int Build)? Parse(string v)
        {
            var parts = v.Split('.');
            if (parts.Length != 3) return null;
            int stageAt = 0;
            while (stageAt < parts[2].Length && char.IsDigit(parts[2][stageAt])) stageAt++;
            if (stageAt == 0 || stageAt >= parts[2].Length - 1) return null;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
                || !int.TryParse(parts[2].Substring(0, stageAt), NumberStyles.None, CultureInfo.InvariantCulture, out var patch)
                || !int.TryParse(parts[2].Substring(stageAt + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var build))
                return null;
            return (major, minor, patch, parts[2][stageAt], build);
        }

        /// <summary>Whether the shader keyword registry and keyword types below are measured for the
        /// project's release family: 6000.0, measured on 6000.0.71f1 (tests/projects/shaderkeywords
        /// Reference/unity-6000.0.71f1.jsonl). The Rust side gives keyword-space rules to the same
        /// family only (unbound_compat::ShaderKeywordRules). Other families are not measured: they
        /// keep the name-based behaviour (every name is a global keyword) with a warning, not an
        /// extrapolated table.</summary>
        internal static bool ShaderKeywordsMeasured => RenderingMeasured;

        /// <summary>The standalone ray tracing shader's main object, measured for distinct source
        /// names in cmdbuffers on 6000.0.71f1. This import has no disk cache.</summary>
        internal static long RayTracingShaderFileId => RenderingMeasured ? 4807578003741378534L
            : throw new NotSupportedException("Ray tracing shader asset identifiers are not measured for this Unity release.");

        /// <summary>The engine's built-in global shader keywords: the ones that exist before any is
        /// made (canonical global.builtin.types and global.all.contains, 6000.0.71f1 Linux Editor).
        /// Registered, not enabled: which keywords start enabled depends on quality, pipeline and
        /// context (none did in the measured batchmode capture) and is not a rule of this table.
        /// Null for a release whose registry is not measured.</summary>
        internal static string[] BuiltinGlobalKeywords => ShaderKeywordsMeasured ? s_BuiltinGlobalKeywords6000_0_71f1 : null;

        static readonly string[] s_BuiltinGlobalKeywords6000_0_71f1 =
        {
            "BILLBOARD_FACE_CAMERA_POS", "DIRECTIONAL", "DIRECTIONAL_COOKIE", "DIRLIGHTMAP_COMBINED", "DOTS_INSTANCING_ON",
            "DYNAMICLIGHTMAP_ON", "EDITOR_VISUALIZATION", "EFFECT_BILLBOARD", "EFFECT_BUMP", "EFFECT_EXTRA_TEX",
            "EFFECT_HUE_VARIATION", "EFFECT_SUBSURFACE", "ETC1_EXTERNAL_ALPHA", "FOG_EXP", "FOG_EXP2", "FOG_LINEAR",
            "GEOM_TYPE_BRANCH", "GEOM_TYPE_BRANCH_DETAIL", "GEOM_TYPE_FROND", "GEOM_TYPE_LEAF", "GEOM_TYPE_MESH",
            "INSTANCING_ON", "LIGHTMAP_ON", "LIGHTMAP_SHADOW_MIXING", "LIGHTPROBE_SH", "LOD_FADE_CROSSFADE", "POINT",
            "POINT_COOKIE", "PROCEDURAL_INSTANCING_ON", "SHADOWS_CUBE", "SHADOWS_DEPTH", "SHADOWS_SCREEN",
            "SHADOWS_SHADOWMASK", "SHADOWS_SINGLE_CASCADE", "SHADOWS_SOFT", "SHADOWS_SPLIT_SPHERES", "SOFTPARTICLES_ON",
            "SPOT", "STEREO_CUBEMAP_RENDER_ON", "STEREO_INSTANCING_ON", "STEREO_MULTIVIEW_ON",
            "UNITY_DEVICE_SUPPORTS_INLINE_RAY_TRACING", "UNITY_DEVICE_SUPPORTS_NATIVE_16BIT",
            "UNITY_DEVICE_SUPPORTS_WAVE_128", "UNITY_DEVICE_SUPPORTS_WAVE_16", "UNITY_DEVICE_SUPPORTS_WAVE_32",
            "UNITY_DEVICE_SUPPORTS_WAVE_64", "UNITY_DEVICE_SUPPORTS_WAVE_8", "UNITY_DEVICE_SUPPORTS_WAVE_ANY",
            "UNITY_HDR_ON", "UNITY_SINGLE_PASS_STEREO", "VERTEXLIGHT_ON", "_EMISSION", "_WINDQUALITY_BEST",
            "_WINDQUALITY_BETTER", "_WINDQUALITY_FAST", "_WINDQUALITY_FASTEST", "_WINDQUALITY_NONE", "_WINDQUALITY_PALM",
        };

        /// <summary>The keywords of a shader's keyword space whose type is built-in (the four stereo
        /// keywords every space starts with; canonical *.space.keywords, 6000.0.71f1). Null for a
        /// release whose types are not measured: every keyword is then user-defined.</summary>
        internal static string[] ImplicitShaderKeywords => ShaderKeywordsMeasured ? s_ImplicitShaderKeywords6000_0_71f1 : null;

        static readonly string[] s_ImplicitShaderKeywords6000_0_71f1 =
        {
            "STEREO_INSTANCING_ON", "UNITY_SINGLE_PASS_STEREO", "STEREO_MULTIVIEW_ON", "STEREO_CUBEMAP_RENDER_ON",
        };
    }

    /// <summary>A Unity release family (MAJOR.MINOR) with measured answers (see
    /// <see cref="Compat.Family"/>).</summary>
    internal enum ReleaseFamily
    {
        /// <summary>No measured answers: every gate keeps its unmeasured behaviour.</summary>
        Unmeasured,
        /// <summary>Unity 6000.0, measured on 6000.0.71f1.</summary>
        Unity6000_0,
    }
}
