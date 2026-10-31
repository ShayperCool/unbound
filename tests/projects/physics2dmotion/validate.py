#!/usr/bin/env python3
"""Remaining final checks with private paths and the unchanged managed test filter."""
import os,subprocess
from pathlib import Path
WORK=Path(os.environ['MOTION_WORK']); ROOT=Path(__file__).resolve().parents[3]
heavy=str(Path.home()/'.cache/compat/with-heavy-job')
env=dict(os.environ,LOADING_WORK=str(WORK/'loading'))
for order in ['transform-first','rigidbody-first']:
 subprocess.run(['python3',str(ROOT/'tests/projects/inactive3dloading/run.py'),'engine','--order',order,'--label','final'],env=env,check=True)
filter='|'.join('FullyQualifiedName~UnityEngine.Tests.'+x for x in ['PhysicsTests','Physics2DTests','UIElements','YogaLayout','CanvasOrderTests','CoreTests','Physics2DMotionParityTests'])
with (WORK/'tests-final.log').open('w') as f:
 subprocess.run([heavy,'dotnet','test',str(ROOT/'managed/UnityEngine.Tests/UnityEngine.Tests.csproj'),'-c','Release','-p:UnboundTarget='+str(WORK/'test-build')+'/', '-p:UnboundNativeDir='+os.environ['UnboundNativeDir'],'--disable-build-servers','-m:1','-p:UseSharedCompilation=false','/nodeReuse:false','--filter',filter,'--logger','trx;LogFileName=final.trx','--results-directory',str(WORK/'tests')],env=env,stdout=f,stderr=subprocess.STDOUT,check=True)
print('Final loading pair and integrated tests passed',flush=True)
