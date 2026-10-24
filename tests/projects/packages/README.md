# Packages probe

A Unity 6000.0.71f1 project with every runtime-relevant package the Editor ships offline (its
`Resources/PackageManager/Editor/*.tgz`), URP, Shader Graph, VFX Graph and all built-in modules: the
measure of how much of the API Unity's packages use Unbound provides.

Resolve the packages (first time; needs the Editor, which unpacks them into `Library/PackageCache`):

    DISPLAY=:0 ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit \
        -projectPath tests/projects/packages -logFile /tmp/packages.log

Then:

    unbound graph -p tests/projects/packages --compare-bee tests/projects/packages/Library/Bee/*-inputdata.json
    unbound compile -p tests/projects/packages --editor-assemblies
    unbound api-gaps -p tests/projects/packages --top 100
