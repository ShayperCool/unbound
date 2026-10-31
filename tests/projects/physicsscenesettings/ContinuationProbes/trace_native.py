#!/usr/bin/env python3
"""Build an opt-in ID trace without changing the SDK or tracked runtime source.

Pass matching existing PhysX/NvCloth static archives. The caller must use with-heavy-job.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--physx', type=Path, required=True)
p.add_argument('--nvcloth', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
root = Path(__file__).resolve().parents[4]
source = root / 'native/unbound_physx.cpp'
text = source.read_text()
headers = '''
#include "NpRigidDynamic.h"
#include "NpRigidStatic.h"
#include "ScRigidSim.h"
#include "ScElementSim.h"
'''
helper = '''
static void kd_trace(const char* operation, PxRigidActor* actor)
{
    if (!std::getenv("KD_TRACE") || !actor) return;
    Sc::RigidCore& core = actor->is<PxRigidDynamic>()
        ? static_cast<NpRigidDynamic*>(actor)->getScbBodyFast().getScRigidCore()
        : static_cast<NpRigidStatic*>(actor)->getScbRigidStaticFast().getScRigidCore();
    Sc::RigidSim* sim = core.getSim();
    PxVec3 p = actor->getGlobalPose().p;
    bool k = actor->is<PxRigidDynamic>() && actor->is<PxRigidDynamic>()->getRigidBodyFlags().isSet(PxRigidBodyFlag::eKINEMATIC);
    std::fprintf(stderr,"KD_ID %s actor=%p p=%.6g,%.6g,%.6g k=%d rigid=%u elements=", operation, actor, p.x,p.y,p.z,k,sim ? sim->getRigidID() : ~0u);
    if (sim) for (Sc::ElementSim* e=sim->getElements_(); e; e=e->mNextInActor)
        std::fprintf(stderr,"%u,",e->getElementID());
    std::fprintf(stderr,"\\n");
}
'''
text = text.replace('#include "PxPhysicsAPI.h"', '#include "PxPhysicsAPI.h"\n' + headers)
text = text.replace('struct ua3_scene_def', helper + '\nstruct ua3_scene_def', 1)
text = text.replace('world->scene->simulate( timeStep );', '''if (std::getenv("KD_TRACE")) std::fprintf(stderr,"KD_STEP\\n");
    for (auto actor : s_bodies) if (actor->getScene() == world->scene) kd_trace("step",actor);
    world->scene->simulate( timeStep );''')
text = text.replace('s_bodies.insert( actor );', 'kd_trace("create",actor);\n\ts_bodies.insert( actor );', 1)
text = text.replace('s_bodies.erase( actor );', 'kd_trace("destroy",actor);\n\ts_bodies.erase( actor );', 1)
text = text.replace('actor->attachShape( *shape );', 'actor->attachShape( *shape );\n\tkd_trace("attach",actor);', 1)
text = text.replace('body->setRigidBodyFlag( PxRigidBodyFlag::eKINEMATIC, type == 1 );', 'kd_trace("type-before",body);\n\tbody->setRigidBodyFlag( PxRigidBodyFlag::eKINEMATIC, type == 1 );\n\tkd_trace("type-after",body);', 1)
text = text.replace('void Events::onContact( const PxContactPairHeader& header, const PxContactPair* pairs, PxU32 count )\n{', '''void Events::onContact( const PxContactPairHeader& header, const PxContactPair* pairs, PxU32 count )
{
    kd_trace("contact0",header.actors[0]);
    kd_trace("contact1",header.actors[1]);''')
a.output.mkdir(parents=True, exist_ok=True)
cpp = a.output / 'unbound_physx-trace.cpp'
cpp.write_text(text)
sdk = root / 'native/physx'
includes = sorted(d for d in (sdk / 'source').rglob('*') if d.is_dir()) + [sdk / 'source', sdk / 'include', sdk / 'pxshared/include']
cmd = ['c++', '-std=c++14', '-O2', '-fPIC', '-shared', '-fno-rtti', '-fno-exceptions', '-fno-strict-aliasing', '-ffp-contract=off', '-fvisibility=hidden',
       '-DNDEBUG', '-DPX_SUPPORT_PVD=0', '-DPX_PHYSX_STATIC_LIB', '-DDISABLE_CUDA_PHYSX', '-DPX_NVTX=0', '-DPX_COOKING']
cmd += ['-I' + str(d) for d in includes]
# The cloth shim has no diagnostic changes, and links the unchanged NvCloth archive.
clothincludes = [root / 'native/nvcloth' / d for d in ['include', 'extensions/include', 'src', 'extensions/src']]
cmd += ['-I' + str(d) for d in clothincludes]
cmd += ['-DNV_CLOTH_ENABLE_CUDA=0', '-DNV_CLOTH_ENABLE_DX11=0', str(cpp), str(root / 'native/unbound_cloth.cpp'), str(a.physx), str(a.nvcloth), '-lpthread', '-ldl', '-lm', '-o', str(a.output / 'libunbound_physx.so')]
subprocess.run(cmd, check=True)
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
(a.output / 'build.json').write_text(json.dumps({'command': cmd, 'sha256': {str(x): sha(x) for x in [source, cpp, a.physx, a.nvcloth, a.output / 'libunbound_physx.so']}}, indent=2) + '\n')
