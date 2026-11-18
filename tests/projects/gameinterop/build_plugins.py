#!/usr/bin/env python3
"""Rebuilds the probe's precompiled plugins in Assets/Plugins from PluginSources/ (they are
committed; run this only after changing a source). ShapeCore.dll is version 2 of the library;
ShapeExtras.dll is built against version 1 and ShapeLater.dll against version 3, which are not
part of the project. Assets/RspPlugins/RspRef.dll has Auto Reference off: only Assets/csc.rsp
references it. Uses the C# compiler of the .NET SDK and the Editor's netstandard 2.1
reference assembly, deterministically."""
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1]))
import probe_paths  # noqa: E402


def netstandard():
    editor = probe_paths.editor_path()
    root = editor.parents[1] if sys.platform == 'darwin' else editor.parent/'Data'
    return root/'NetStandard/ref/2.1.0/netstandard.dll'


def csc():
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    sdk = sorted((Path(dotnet).resolve().parent/'sdk').glob('10.*'))[-1]
    return [dotnet, str(sdk/'Roslyn/bincore/csc.dll')]


def build(source, output, references=()):
    args = csc() + ['-nologo', '-noconfig', '-nostdlib', '-deterministic', '-optimize+', '-debug-',
                    '-target:library', f'-out:{output}', f'-r:{netstandard()}']
    args += [f'-r:{r}' for r in references] + [str(source)]
    subprocess.run(args, check=True)


def main():
    src, plugins = HERE/'PluginSources', HERE/'Assets/Plugins'
    with tempfile.TemporaryDirectory() as tmp:
        for version in ('v1', 'v3'):
            (Path(tmp)/version).mkdir()
            build(src/f'ShapeCore.{version}.cs', Path(tmp)/version/'ShapeCore.dll')
        build(src/'ShapeCore.v2.cs', plugins/'ShapeCore.dll')
        build(src/'ShapeExtras.cs', plugins/'ShapeExtras.dll', [Path(tmp)/'v1/ShapeCore.dll'])
        build(src/'ShapeLater.cs', plugins/'ShapeLater.dll', [Path(tmp)/'v3/ShapeCore.dll'])
        build(src/'RspRef.cs', HERE/'Assets/RspPlugins/RspRef.dll')


if __name__ == '__main__':
    main()
