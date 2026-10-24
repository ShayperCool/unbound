# Test Framework probe

Edit Mode and Play Mode tests run by com.unity.test-framework 1.6.0 (unmodified) in the Editor and
in Unbound, compared through the NUnit XML results the framework writes: passes, assertion
failures, exceptions, an ignored test, parameterized cases, [OneTimeSetUp]/[SetUp]/[TearDown]
order and their logs, LogAssert (expected and unexpected logs), editor coroutines ([UnityTest] in
Edit mode), Play mode coroutines over frames, fixed updates and seconds, MonoBehaviour lifecycles
in Edit and Play mode, [UnitySetUp]/[UnityTearDown], an exception thrown from Update.

Record Unity's results (the Editor exits with 2: some tests fail on purpose):

    DISPLAY=:0 ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/testrunner \
        -runTests -testPlatform EditMode -testResults $PWD/tests/projects/testrunner/Reference/editmode-6000.0.71f1.xml
    (the same with PlayMode and playmode-6000.0.71f1.xml)

Run Unbound (`unbound test` gives the framework the same command line; exit code as Unity's) and
compare:

    unbound test -p tests/projects/testrunner --platform EditMode --results /tmp/editmode.xml
    python3 tests/projects/testrunner/compare.py tests/projects/testrunner/Reference/editmode-6000.0.71f1.xml /tmp/editmode.xml

`compare.py` compares the test tree, results, labels, failure messages, outputs and counts; times,
ids, seeds, paths and stack traces are left out. Edit Mode matches entirely. In Play Mode the
output of the test that logs an exception from `Update` differs: Unity prints the stack trace in
its console form (`Thrower.Update () (at Assets/...:98)`), Unbound in .NET's.
