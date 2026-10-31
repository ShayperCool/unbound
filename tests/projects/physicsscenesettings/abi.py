#!/usr/bin/env python3
"""Exercise the private shim's creation contract, not a substitute for Native motion oracles."""
import ctypes,os,pathlib
class V3(ctypes.Structure): _fields_=[(n,ctypes.c_float) for n in ['x','y','z']]
class Scene(ctypes.Structure): _fields_=[(n,ctypes.c_int) for n in ['solver','friction','pairs','determinism','pcm','improved']]
lib=ctypes.CDLL(str(pathlib.Path(os.environ['UNBOUND_MANAGED_DIR'])/'libunbound_physx.so'))
assert lib.ua3_version()==7
assert ctypes.sizeof(Scene)==24
lib.ua3_world_create.argtypes=[V3,ctypes.c_float,ctypes.c_float,ctypes.c_int,ctypes.POINTER(Scene)]
lib.ua3_world_create.restype=ctypes.c_uint32
lib.ua3_world_destroy.argtypes=[ctypes.c_uint32]
count=0
for solver in [0,1]:
    for friction in [0,1,2]:
        for pairs in [0,1,2,3]:
            for det in [0,1]:
                scene=Scene(solver,friction,pairs,det,1,0)
                world=lib.ua3_world_create(V3(0,-9.81,0),2,1,1,ctypes.byref(scene))
                assert world,tuple(getattr(scene,n) for n,_ in Scene._fields_)
                lib.ua3_world_destroy(world);count+=1
for scene in [Scene(2,0,0,0,1,0),Scene(0,3,0,0,1,0),Scene(0,0,4,0,1,0)]:
    assert lib.ua3_world_create(V3(0,-9.81,0),2,1,1,ctypes.byref(scene))==0
print(f'Shim 7, scene struct 24 bytes, {count} valid combinations created/destroyed; 3 invalid enums rejected')
