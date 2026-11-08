#!/usr/bin/env python3
"""Author pivot-free static FBX axis/unit controls and explicit Unity importer settings."""
import hashlib
import importlib.util
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[1]
PROJECTS = ROOT.parent
spec = importlib.util.spec_from_file_location('uv', PROJECTS/'lightmapuvs/Tools/make_project.py')
uv = importlib.util.module_from_spec(spec)
spec.loader.exec_module(uv)

def guid(name):
    return hashlib.md5(('modelaxis/'+name).encode()).hexdigest()

def fbx(up, unit, front_sign=-1):
    header = '''; FBX 7.4.0 project file
FBXHeaderExtension: {
 FBXHeaderVersion: 1003
 FBXVersion: 7400
}
GlobalSettings: {
 Version: 1000
 Properties70: {
'''
    axes = dict(UpAxis=up, UpAxisSign=1, FrontAxis=2 if up == 1 else 1,
                FrontAxisSign=front_sign, CoordAxis=0, CoordAxisSign=1)
    header += ''.join(f'  P: "{k}", "int", "Integer", "",{v}\n' for k,v in axes.items())
    header += f'  P: "UnitScaleFactor", "double", "Number", "",{unit}\n }}\n}}\n'
    geometry = '''Geometry: 100, "Geometry::Wedge", "Mesh" {
 Vertices: *12 { a: 0,0,0, 2,0,0, 0,3,0, 0,0,4 }
 PolygonVertexIndex: *12 { a: 0,2,-2, 0,1,-4, 0,3,-3, 1,2,-4 }
 GeometryVersion: 124
}
'''
    # No pivots, geometric transforms, pre/post rotations, animation or skinning.
    nodes = [('Root', 'Null', (5,7,11), (0,30,0), (1,1,1)),
             ('Branch', 'Null', (2,3,5), (20,0,0), (2,3,4)),
             ('Tip', 'Mesh', (1,2,3), (0,0,40), (0.5,1.5,2))]
    objects = geometry
    for i,(name,kind,p,r,s) in enumerate(nodes):
        objects += f'Model: {300+i}, "Model::{name}", "{kind}" {{\n Version: 232\n Properties70: {{\n'
        for prop,value in [('Lcl Translation',p),('Lcl Rotation',r),('Lcl Scaling',s)]:
            objects += f'  P: "{prop}", "{prop}", "", "A",'+','.join(map(str,value))+'\n'
        objects += ' }\n Shading: T\n Culling: "CullingOff"\n}\n'
    definitions = 'Definitions: {\n Version: 100\n Count: 4\n ObjectType: "Model" { Count: 3 }\n ObjectType: "Geometry" { Count: 1 }\n}\n'
    connections = 'Connections: {\n C: "OO",300,0\n C: "OO",301,300\n C: "OO",302,301\n C: "OO",100,302\n}\n'
    return (header+definitions+'Objects: {\n'+objects+'}\n'+connections).replace('{ ', '{\n ').replace(' }','\n }')

def main():
    for part in ('Packages','ProjectSettings'):
        shutil.copytree(PROJECTS/'modelimport'/part, ROOT/part, dirs_exist_ok=True)
    scene = (PROJECTS/'modelimport/Assets/Scenes/Probe.unity').read_text()
    start = scene.index('  models:\n')
    end = scene.index('--- !u!4',start)
    scene = scene[:start]+scene[end:]
    scene = scene.replace('0acbd5123db444107984dbacd494d206', guid('AxisProbe.cs'))
    (ROOT/'Assets/Scenes/Probe.unity').write_text(scene)
    dest = ROOT/'Assets/Resources/Axis'
    dest.mkdir(parents=True,exist_ok=True)
    for up in (1,2):
      for unit in (1,100):
       for bake in (0,1):
        for convert in (0,1):
         for preserve in (0,1):
            name = f'U{up}C{unit}B{bake}F{convert}H{preserve}'
            path = dest/(name+'.fbx')
            path.write_text(fbx(up,unit))
            settings = dict(uv.DEFAULTS, **uv.RAW, normalImportMode=2)
            meta = uv.META.format(guid=guid(name),**settings)
            for key,value in dict(bakeAxisConversion=bake,useFileScale=convert,preserveHierarchy=preserve,globalScale=2.5).items():
                import re
                meta = re.sub(r'(?m)^(    '+key+r': ).*',lambda m:m[1]+str(value),meta)
            Path(str(path)+'.meta').write_text(meta)
    signed = ROOT/'Assets/Resources/Signed'
    signed.mkdir(parents=True,exist_ok=True)
    for up in (1,2):
     for bake in (0,1):
      for preserve in (0,1):
        name = f'U{up}S1B{bake}H{preserve}'
        path = signed/(name+'.fbx')
        path.write_text(fbx(up,100,front_sign=1))
        settings = dict(uv.DEFAULTS, **uv.RAW, normalImportMode=2)
        meta = uv.META.format(guid=guid(name),**settings)
        for key,value in dict(bakeAxisConversion=bake,preserveHierarchy=preserve,globalScale=2.5).items():
            import re
            meta = re.sub(r'(?m)^(    '+key+r': ).*',lambda m:m[1]+str(value),meta)
        Path(str(path)+'.meta').write_text(meta)
    write_missing_metas()

def write_missing_metas():
    for path in sorted((ROOT/'Assets').rglob('*')):
        if path.suffix == '.meta': continue
        meta = Path(str(path)+'.meta')
        if meta.exists(): continue
        if path.is_dir(): text = uv.FOLDER.format(guid=guid(str(path.relative_to(ROOT))))
        elif path.suffix == '.cs': text = 'fileFormatVersion: 2\nguid: '+guid(path.name)+'\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n'
        else: text = 'fileFormatVersion: 2\nguid: '+guid(str(path.relative_to(ROOT)))+'\nDefaultImporter:\n  externalObjects: {}\n'
        meta.write_text(text)

if __name__ == '__main__': main()
