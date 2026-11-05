using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Rigidbody2D.Slide, Physics2D.simulationLayers and 2D sub-stepping, from the Scripting API
    /// 6000.0 pages (no Unity reference capture exists for them). Assertions on documented
    /// behaviour say so; those on Unbound's choices (iteration details, tolerances, constructor
    /// defaults the pages leave open) check Unbound's implementation, not a Unity guarantee.
    /// Only members that exist before this change are used, so every case compiles against the
    /// stubbed source and fails there on its own assertion.
    /// </summary>
    public class Physics2DSlideAndLayersTests : IDisposable
    {
        readonly List<GameObject> objects = new List<GameObject>();

        public Physics2DSlideAndLayersTests()
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.autoSyncTransforms = false;
            Physics2D.gravity = Vector2.zero;
        }

        public void Dispose()
        {
            foreach (var o in objects) if (o) Object.DestroyImmediate(o);
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.simulationLayers = Physics2D.AllLayers;
            Physics2D.useSubStepping = false;
            Physics2D.useSubStepContacts = false;
            Physics2D.minSubStepFPS = 30f;
            Physics2D.maxSubStepCount = 4;
            Physics2D.IgnoreLayerCollision(0, 8, false);
            Physics2D.gravity = new Vector2(0, -9.81f);
            Physics2D.Simulate(.02f);
        }

        GameObject Box(string name, Vector2 position, Vector2 size, int layer = 0)
        {
            var go = new GameObject(name); objects.Add(go);
            go.layer = layer;
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = size;
            return go;
        }

        Rigidbody2D Body(string name, Vector2 position, RigidbodyType2D type, int layer = 0)
        {
            var go = Box(name, position, Vector2.one, layer);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = type;
            return rb;
        }

        /// <summary>A movement with no surface anchor and no gravity, so only the slide acts.</summary>
        static Rigidbody2D.SlideMovement Plain()
        {
            var movement = new Rigidbody2D.SlideMovement();
            movement.surfaceAnchor = Vector2.zero;
            movement.gravity = Vector2.zero;
            return movement;
        }

        // ------------------------------------------------------------------ Slide

        [Fact]
        public void SlideMovementHasTheDocumentedDefaults()
        {
            var movement = new Rigidbody2D.SlideMovement();
            Assert.Equal(Vector2.up, movement.surfaceUp);
            Assert.Equal(Vector2.down, movement.surfaceAnchor);
            Assert.Equal(new Vector2(0, -9.81f), movement.gravity);
            // Unbound's choice (undocumented): at least one iteration, so a default movement moves.
            Assert.True(movement.maxIterations >= 1);
        }

        [Fact]
        public void SetLayerMaskAndSetStartPositionAlsoSetTheirFlags()
        {
            var movement = new Rigidbody2D.SlideMovement();
            movement.SetLayerMask(1 << 8);
            movement.SetStartPosition(new Vector2(2, 3));
            Assert.True(movement.useLayerMask);
            Assert.Equal(1 << 8, movement.layerMask.value);
            Assert.True(movement.useStartPosition);
            Assert.Equal(new Vector2(2, 3), movement.startPosition);
        }

        [Fact]
        public void FreeSlideIntegratesTheVelocityAndMovesBodyAndTransformAtOnce()
        {
            var rb = Body("mover", new Vector2(3, 1), RigidbodyType2D.Kinematic);
            var results = rb.Slide(new Vector2(2, 0), .5f, Plain());
            Assert.Equal(0, results.iterationsUsed);
            Assert.False(results.slideHit);
            Assert.Equal(Vector2.zero, results.remainingVelocity);
            Assert.InRange(results.position.x, 3.999f, 4.001f);
            Assert.InRange(results.position.y, .999f, 1.001f);
            // Not a simulation move: the Rigidbody2D and the Transform are there now.
            Assert.InRange(rb.position.x, 3.999f, 4.001f);
            Assert.InRange(rb.transform.position.x, 3.999f, 4.001f);
        }

        [Fact]
        public void StartPositionOverridesTheBodyPosition()
        {
            var rb = Body("mover", Vector2.zero, RigidbodyType2D.Kinematic);
            var movement = Plain();
            movement.SetStartPosition(new Vector2(-4, 2));
            var results = rb.Slide(new Vector2(1, 0), 1f, movement);
            Assert.InRange(results.position.x, -3.001f, -2.999f);
            Assert.InRange(results.position.y, 1.999f, 2.001f);
        }

        [Fact]
        public void AWallSteeperThanTheSlideAngleStopsTheMovement()
        {
            var wall = Box("wall", new Vector2(3, 0), new Vector2(1, 4));
            var rb = Body("mover", Vector2.zero, RigidbodyType2D.Kinematic);
            // The wall is 90 degrees from surfaceUp; a 45 degree limit does not slide on it.
            var movement = Plain();
            movement.surfaceSlideAngle = 45f;
            var results = rb.Slide(new Vector2(10, 0), 1f, movement);
            Assert.Equal(1, results.iterationsUsed);
            Assert.Same(wall.GetComponent<BoxCollider2D>(), results.slideHit.collider);
            // The box's right face stops at the wall's left face (x = 2.5), within the contact gap.
            Assert.InRange(results.position.x, 1.9f, 2.0f);
            Assert.InRange(rb.position.x, 1.9f, 2.0f);
            Assert.Equal(Vector2.zero, results.remainingVelocity);
        }

        [Fact]
        public void AContactBelowTheSlideAngleTurnsTheMovementAlongTheSurface()
        {
            var floor = Box("floor", new Vector2(0, -1), new Vector2(40, 1));
            var rb = Body("mover", new Vector2(0, .5f), RigidbodyType2D.Kinematic);
            // Half a unit above the floor, moving (2, -2): it lands after (0.5, -0.5), then the
            // rest turns along the floor without friction (Rigidbody2D.Slide).
            var results = rb.Slide(new Vector2(2, -2), 1f, Plain());
            Assert.Equal(1, results.iterationsUsed);
            Assert.Same(floor.GetComponent<BoxCollider2D>(), results.slideHit.collider);
            Assert.InRange(results.position.x, 1.9f, 2.05f);
            Assert.InRange(results.position.y, -.01f, .05f);
        }

        [Fact]
        public void MaxIterationsLeavesTheRestOfTheVelocityUnused()
        {
            Box("floor", new Vector2(0, -1), new Vector2(40, 1));
            var rb = Body("mover", new Vector2(0, .5f), RigidbodyType2D.Kinematic);
            var movement = Plain();
            movement.maxIterations = 1;
            var results = rb.Slide(new Vector2(2, -2), 1f, movement);
            Assert.Equal(1, results.iterationsUsed);
            Assert.InRange(results.position.x, .45f, .55f);
            // The unused part, along the floor, per second (SlideResults.remainingVelocity).
            Assert.InRange(results.remainingVelocity.x, 1.45f, 1.55f);
            Assert.InRange(results.remainingVelocity.y, -.01f, .01f);
        }

        [Fact]
        public void GravityMovesToTheSurfaceAndReportsIt()
        {
            var floor = Box("floor", new Vector2(0, -1), new Vector2(40, 1));
            var rb = Body("mover", new Vector2(0, 2), RigidbodyType2D.Kinematic);
            var movement = new Rigidbody2D.SlideMovement();
            movement.gravity = new Vector2(0, -10);
            var results = rb.Slide(Vector2.zero, 1f, movement);
            Assert.Same(floor.GetComponent<BoxCollider2D>(), results.surfaceHit.collider);
            // The box's bottom rests on the floor's top (y = -0.5), within the contact gap.
            Assert.InRange(results.position.y, -.02f, .05f);
            Assert.InRange(results.position.x, -.001f, .001f);
        }

        [Fact]
        public void UseNoMoveCalculatesThePositionWithoutMoving()
        {
            var rb = Body("mover", Vector2.zero, RigidbodyType2D.Kinematic);
            var movement = Plain();
            movement.useNoMove = true;
            var results = rb.Slide(new Vector2(5, 0), 1f, movement);
            Assert.InRange(results.position.x, 4.999f, 5.001f);
            Assert.Equal(Vector2.zero, rb.position);
            Assert.Equal(Vector3.zero, rb.transform.position);
        }

        [Fact]
        public void UseSimulationMoveMovesInTheNextSimulationStep()
        {
            var rb = Body("mover", Vector2.zero, RigidbodyType2D.Kinematic);
            var movement = Plain();
            movement.useSimulationMove = true;
            var results = rb.Slide(new Vector2(5, 0), 1f, movement);
            Assert.InRange(results.position.x, 4.999f, 5.001f);
            Assert.Equal(Vector2.zero, rb.position);
            Physics2D.Simulate(.02f);
            Assert.InRange(rb.position.x, 4.999f, 5.001f);
        }

        [Fact]
        public void LayerMaskReplacesTheLayerCollisionMatrix()
        {
            Box("wall", new Vector2(3, 0), new Vector2(1, 4), layer: 8);
            Physics2D.IgnoreLayerCollision(0, 8, true);
            var rb = Body("mover", Vector2.zero, RigidbodyType2D.Kinematic);
            var movement = Plain();
            movement.useNoMove = true;
            movement.surfaceSlideAngle = 45f;
            // The matrix ignores layer 8: the slide passes the wall.
            Assert.InRange(rb.Slide(new Vector2(10, 0), 1f, movement).position.x, 9.999f, 10.001f);
            // An explicit mask selects layer 8 whatever the matrix says.
            movement.SetLayerMask(1 << 8);
            Assert.InRange(rb.Slide(new Vector2(10, 0), 1f, movement).position.x, 1.9f, 2.0f);
        }

        // ------------------------------------------------------------------ Slide serialization warning

        // Interim fail-loud control: the first Slide call in the process warns once that the
        // serialized SlideMovement/SlideResults names and the undocumented constructor defaults are
        // unverified. That is Unbound's notice, not Unity behaviour. The warning state is process
        // wide, so each case starts from "not warned yet" and puts the previous state back. The
        // test-only reset is reached by reflection so that the file still compiles where it does
        // not exist: there no warning is logged and each case fails on its own assertion.

        /// <summary>Runs <paramref name="body"/> with the Slide warning not yet logged and every
        /// warning or error report captured in order; restores the warning state and the logger.</summary>
        static void WithFirstSlideUse(Action<List<string>> body)
        {
            var reset = typeof(Rigidbody2D).GetMethod("ResetSlideWarningForTests",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var logger = Debug.unityLogger;
            var handler = logger.logHandler;
            bool enabled = logger.logEnabled;
            var filter = logger.filterLogType;
            var reports = new List<string>();
            object previous = reset?.Invoke(null, new object[] { false });
            try
            {
                logger.logHandler = new ReportCapture(handler, reports);
                logger.logEnabled = true;
                logger.filterLogType = LogType.Log;
                body(reports);
            }
            finally
            {
                logger.logHandler = handler;
                logger.logEnabled = enabled;
                logger.filterLogType = filter;
                if (reset != null) reset.Invoke(null, new object[] { previous });
            }
        }

        /// <summary>Records each warning or error and forwards every call unchanged.</summary>
        sealed class ReportCapture : ILogHandler
        {
            readonly ILogHandler inner;
            readonly List<string> reports;

            internal ReportCapture(ILogHandler inner, List<string> reports)
            {
                this.inner = inner;
                this.reports = reports;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type != LogType.Log)
                {
                    string message = args == null || args.Length == 0 ? format
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                    reports.Add($"{type}: {message}");
                }
                inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context)
            {
                if (exception != null) reports.Add($"Exception: {exception}");
                inner.LogException(exception, context);
            }
        }

        static bool IsSlideWarning(string report) =>
            report.StartsWith("Warning: ", StringComparison.Ordinal)
            && report.Contains("Rigidbody2D.Slide", StringComparison.Ordinal)
            && report.Contains("SlideMovement", StringComparison.Ordinal)
            && report.Contains("SlideResults", StringComparison.Ordinal)
            && report.Contains("unverified", StringComparison.Ordinal)
            && report.Contains("constructor defaults", StringComparison.Ordinal)
            && report.Contains("Inspector", StringComparison.Ordinal)
            && report.Contains("may not be preserved", StringComparison.Ordinal);

        [Fact]
        public void TheFirstSlideWithInitializedValuesWarnsAboutUnverifiedSerialization()
        {
            var rb = Body("mover", new Vector2(3, 1), RigidbodyType2D.Kinematic);
            WithFirstSlideUse(reports =>
            {
                var results = rb.Slide(new Vector2(2, 0), .5f, Plain());
                Assert.Single(reports);
                Assert.True(IsSlideWarning(reports[0]), reports[0]);
                // The notice claims neither a detection nor compatibility.
                Assert.DoesNotContain("detected", reports[0], StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("unknown", reports[0], StringComparison.OrdinalIgnoreCase);
                // The slide itself still runs.
                Assert.InRange(results.position.x, 3.999f, 4.001f);
                Assert.InRange(rb.position.x, 3.999f, 4.001f);
            });
        }

        [Fact]
        public void TheFirstSlideWithAllZeroValuesWarnsBeforeTheArgumentError()
        {
            var rb = Body("mover", new Vector2(3, 1), RigidbodyType2D.Kinematic);
            WithFirstSlideUse(reports =>
            {
                // default(SlideMovement): every member zero, so maxIterations 0 is invalid.
                var results = rb.Slide(new Vector2(2, 0), .5f, default(Rigidbody2D.SlideMovement));
                Assert.Equal(2, reports.Count);
                Assert.True(IsSlideWarning(reports[0]), reports[0]);
                // The existing argument error is kept, after the warning, and nothing moves.
                Assert.StartsWith("Error: Rigidbody2D.Slide: invalid argument", reports[1], StringComparison.Ordinal);
                Assert.Equal(0, results.iterationsUsed);
                Assert.Equal(new Vector2(2, 0), results.remainingVelocity);
                Assert.Equal(new Vector2(3, 1), rb.position);
            });
        }

        [Fact]
        public void LaterSlidesDoNotWarnAgain()
        {
            var rb = Body("mover", Vector2.zero, RigidbodyType2D.Kinematic);
            WithFirstSlideUse(reports =>
            {
                rb.Slide(new Vector2(1, 0), .5f, Plain());
                rb.Slide(new Vector2(1, 0), .5f, Plain());
                rb.Slide(new Vector2(1, 0), .5f, default(Rigidbody2D.SlideMovement));
                // One warning in all, then only the third call's own argument error.
                Assert.Equal(2, reports.Count);
                Assert.True(IsSlideWarning(reports[0]), reports[0]);
                Assert.StartsWith("Error: Rigidbody2D.Slide: invalid argument", reports[1], StringComparison.Ordinal);
            });
        }

        [Fact]
        public void TheWarningLeavesTheDefaultMovementUnchanged()
        {
            Box("floor", new Vector2(0, -1), new Vector2(40, 1));
            var rb = Body("mover", new Vector2(0, 2), RigidbodyType2D.Kinematic);
            // Control: passes with or without the warning. The default movement (surface anchor,
            // gravity) computed by the warning call equals the same call made after it.
            var movement = new Rigidbody2D.SlideMovement();
            movement.useNoMove = true;
            WithFirstSlideUse(reports =>
            {
                var first = rb.Slide(new Vector2(1, 0), .5f, movement);
                var again = rb.Slide(new Vector2(1, 0), .5f, movement);
                Assert.Equal(first.position, again.position);
                Assert.Equal(first.remainingVelocity, again.remainingVelocity);
                Assert.Equal(first.iterationsUsed, again.iterationsUsed);
                // Gravity lands it on the floor (Unbound's slide; the same before this notice).
                Assert.NotNull(first.surfaceHit.collider);
                Assert.Same(first.surfaceHit.collider, again.surfaceHit.collider);
                Assert.Equal(new Vector2(0, 2), rb.position);
                // The same movement, now moving, ends where the warning call computed.
                movement.useNoMove = false;
                var moved = rb.Slide(new Vector2(1, 0), .5f, movement);
                Assert.Equal(first.position, moved.position);
                Assert.InRange(rb.position.x, first.position.x - 1e-4f, first.position.x + 1e-4f);
                Assert.InRange(rb.position.y, first.position.y - 1e-4f, first.position.y + 1e-4f);
                Assert.DoesNotContain(reports, r => r.StartsWith("Error: ", StringComparison.Ordinal));
            });
        }

        // ------------------------------------------------------------------ simulation layers

        [Fact]
        public void SimulateWithALayerMaskLeavesOtherBodiesUnsimulated()
        {
            var simulated = Body("simulated", Vector2.zero, RigidbodyType2D.Dynamic);
            var held = Body("held", new Vector2(5, 0), RigidbodyType2D.Dynamic, layer: 8);
            simulated.linearVelocity = new Vector2(3, 0);
            held.linearVelocity = new Vector2(3, 0);
            Physics2D.Simulate(.1f, 1 << 0);
            Assert.True(simulated.position.x > .2f);
            // Not simulated: no movement. Unbound's choice: the velocity it had is kept.
            Assert.Equal(new Vector2(5, 0), held.position);
            Assert.Equal(new Vector2(3, 0), held.linearVelocity);
            Physics2D.Simulate(.1f);
            Assert.True(held.position.x > 5.2f);
        }

        [Fact]
        public void OnlyCollidersOnTheSimulatedLayersMakeContacts()
        {
            Physics2D.gravity = new Vector2(0, -10);
            Box("floor", new Vector2(0, -1), new Vector2(40, 1), layer: 8);
            var falling = Body("falling", Vector2.zero, RigidbodyType2D.Dynamic);
            for (int i = 0; i < 50; i++) Physics2D.Simulate(.02f, 1 << 0);
            // The floor is not on a simulated layer: no contact stops the body.
            Assert.True(falling.position.y < -1.5f);
        }

        [Fact]
        public void CollidersOnSimulatedLayersStillCollide()
        {
            Physics2D.gravity = new Vector2(0, -10);
            Box("floor", new Vector2(0, -1), new Vector2(40, 1), layer: 8);
            var falling = Body("falling", Vector2.zero, RigidbodyType2D.Dynamic);
            for (int i = 0; i < 50; i++) Physics2D.Simulate(.02f, (1 << 0) | (1 << 8));
            Assert.True(falling.position.y > -.1f);
        }

        [Fact]
        public void TheAutomaticModesSimulateThePhysics2DSimulationLayers()
        {
            Physics2D.gravity = new Vector2(0, -10);
            var held = Body("held", Vector2.zero, RigidbodyType2D.Dynamic, layer: 8);
            Physics2D.simulationLayers = 1 << 0;
            // The automatic step (as the player loop runs it) reads Physics2D.simulationLayers.
            Physics2DWorld.Simulate(.1f, Physics2D.AllLayers, fromLoop: true);
            Assert.Equal(Vector2.zero, held.position);
            // A script step takes its own mask: Physics2D.simulationLayers does not apply.
            Physics2D.Simulate(.1f);
            Assert.True(held.position.y < 0f);
        }

        // ------------------------------------------------------------------ sub-stepping

        Rigidbody2D Coasting(float linearDamping)
        {
            var rb = Body("coasting", Vector2.zero, RigidbodyType2D.Dynamic);
            rb.linearDamping = linearDamping;
            rb.linearVelocity = new Vector2(1, 0);
            return rb;
        }

        [Fact]
        public void SubSteppingBeyondMaxSubStepCountLosesTime()
        {
            Physics2D.useSubStepping = true;
            Physics2D.minSubStepFPS = 30f;
            Physics2D.maxSubStepCount = 4;
            var rb = Coasting(0f);
            // 2 FPS needs 15 sub-steps at 30 FPS; only 4 run, so 4/30 s of the 0.5 s is simulated.
            Physics2D.Simulate(.5f);
            Assert.InRange(rb.position.x, .13f, .137f);
        }

        [Fact]
        public void SubStepsIntegrateAtTheMinimumRate()
        {
            Physics2D.useSubStepping = true;
            Physics2D.minSubStepFPS = 30f;
            Physics2D.maxSubStepCount = 20;
            var rb = Coasting(2f);
            // 15 sub-steps of 1/30 s: Box2D's damping 1 / (1 + h c) per sub-step gives
            // sum(0.9375^k, k = 1..15) / 30 = 0.3101; one 0.5 s step would give 0.25.
            Physics2D.Simulate(.5f);
            Assert.InRange(rb.position.x, .305f, .315f);
        }

        [Fact]
        public void FixedUpdateModeDoesNotSubStep()
        {
            Physics2D.useSubStepping = true;
            Physics2D.maxSubStepCount = 4;
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            var rb = Coasting(0f);
            Physics2D.Simulate(.5f);
            Assert.InRange(rb.position.x, .499f, .501f);
        }

        [Fact]
        public void WithoutSubSteppingTheWholeTimeIsOneStep()
        {
            Physics2D.maxSubStepCount = 4;
            var rb = Coasting(2f);
            Physics2D.Simulate(.5f);
            Assert.InRange(rb.position.x, .249f, .251f);
        }
    }
}
