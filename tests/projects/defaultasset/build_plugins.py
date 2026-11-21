#!/usr/bin/env python3
"""Rebuilds the probe's precompiled managed plug-in Assets/Plugins/ScriptPlugin.dll from
PluginSources/ScriptPlugin.cs (it is committed; run this only after changing the source). Uses the
C# compiler of the .NET SDK with the Editor's netstandard 2.1 and UnityEngine.CoreModule reference
assemblies, deterministically."""
from pathlib import Path
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1]))
import probe_paths  # noqa: E402


def editor_data():
    editor = probe_paths.editor_path()
    return editor.parents[1] if sys.platform == 'darwin' else editor.parent/'Data'


def csc():
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    sdk = sorted((Path(dotnet).resolve().parent/'sdk').glob('10.*'))[-1]
    return [dotnet, str(sdk/'Roslyn/bincore/csc.dll')]


def main():
    data = editor_data()
    output = HERE/'Assets/Plugins/ScriptPlugin.dll'
    output.parent.mkdir(parents=True, exist_ok=True)
    args = csc() + ['-nologo', '-noconfig', '-nostdlib', '-deterministic', '-optimize+', '-debug-',
                    '-target:library', f'-out:{output}',
                    f'-r:{data/"NetStandard/ref/2.1.0/netstandard.dll"}',
                    f'-r:{data/"Managed/UnityEngine/UnityEngine.CoreModule.dll"}',
                    str(HERE/'PluginSources/ScriptPlugin.cs')]
    subprocess.run(args, check=True)


if __name__ == '__main__':
    main()
