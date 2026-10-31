#!/usr/bin/env python3
"""Check the selected hand-written build and regenerate stubs only in cache."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
from xml.sax.saxutils import escape
ROOT=Path(__file__).resolve().parent
REPO=ROOT.parents[2]
sys.path.insert(0,str(REPO/'tests'))
from probe_paths import editor_path
from artifact_paths import artifact_path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--unity-version',required=True)
p.add_argument('--runtime',type=Path,required=True,help='Directory containing the build made with UnboundNoStubs=true')
p.add_argument('--work',type=Path,required=True)
a=p.parse_args()
if not a.runtime.is_dir() or not (a.runtime/'UnityEngine.CoreModule.dll').is_file():
    p.error('--runtime must be a directory containing UnityEngine.CoreModule.dll')
artifact_path(a.work)
os.environ['UNBOUND_PROBE_UNITY_VERSION']=a.unity_version
if a.work.resolve().is_relative_to(REPO):p.error('--work must be outside repository')
if a.work.exists():p.error('refusing overwrite '+str(a.work))
a.work.mkdir(parents=True)
shutil.copy2(ROOT/'Sources/ContactApiCheck.cs',a.work/'Program.cs')
project=a.work/'ApiCheck.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="'+escape(str(REPO/'managed/Unbound.ApiTools/Unbound.ApiTools.csproj'))+'" /></ItemGroup></Project>')
heavy=str(Path.home()/'.cache/compat/with-heavy-job')
subprocess.run([heavy,'dotnet','build',str(project),'-c','Release','-o',str(a.work/'bin'),
                '-p:UnboundTarget='+str(a.work/'build')+'/', '--disable-build-servers','-m:1',
                '-p:UseSharedCompilation=false','/nodeReuse:false'],check=True)
subprocess.run([heavy,'dotnet',str(a.work/'bin/ApiCheck.dll'),str(REPO),str(a.runtime.resolve()),
                str(editor_path().parent),str(a.work.resolve())],check=True)
