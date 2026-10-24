# Shader Graph probe

A sphere drawn by a Shader Graph shader (URP's Autodesk Interactive graph, a version-0 graph the
Shader Graph package upgrades and imports through its ScriptedImporter) beside one drawn by URP
Lit, in the URP probe's setup (same pipeline asset, light and camera), read back from a 320x180
render texture in the Editor and in Unbound. In Unbound the graph's generated ShaderLab is written
to `Library/Unbound/GeneratedShaders/` and compiled like any `.shader`.

`Editor/ProbeSetup.Create` made the materials and the scene. Record Unity's reference:

    SHADERGRAPH_PROBE_OUT=$PWD/tests/projects/shadergraph/Reference/unity-6000.0.71f1.png DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/shadergraph \
        -executeMethod ProbeSetup.Record -logFile /tmp/shadergraph.log

Run Unbound and compare (the URP probe's comparison: its "sphere" point is the graph's sphere,
"cube" the Lit one):

    SHADERGRAPH_PROBE_OUT=/tmp/shadergraph.png unbound run -p tests/projects/shadergraph --no-watch --frames 30 --size 320x180
    python3 tests/projects/urp/compare.py tests/projects/shadergraph/Reference/unity-6000.0.71f1.png /tmp/shadergraph.png

Mean difference 0.02/255; 13 edge pixels differ by more than 8.
