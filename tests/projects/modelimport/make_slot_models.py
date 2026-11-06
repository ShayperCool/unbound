#!/usr/bin/env python3
"""Small authored face/material controls. Does not require a DCC application."""
import importlib.util
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('uv_models', ROOT.parent/'lightmapuvs/Tools/make_project.py')
uv = importlib.util.module_from_spec(spec)
spec.loader.exec_module(uv)
DEST = ROOT/'Assets/Resources/Slots'
MATERIALS = [('Red', (1, 0, 0)), ('Green', (0, 1, 0)), ('Blue', (0, 0, 1)), ('Unused', (1, 1, 0))]
ORDERS = {'Declared': [0, 1, 2, 0], 'FirstUse': [2, 0, 1, 2], 'Sparse': [2, 0, 2, 0]}


def fbx(order, shared=False, duplicate=False):
    vertices = ','.join(str(c) for i in range(4) for p in
                        [(i*1.5, 0, 0), (i*1.5+1, 0, 0), (i*1.5+1, 1, 0), (i*1.5, 1, 0)] for c in p)
    indices = ','.join(str(i*4+j if j < 3 else -(i*4+j+1)) for i in range(4) for j in range(4))
    objects = [f'''Geometry: 100, "Geometry::Panels", "Mesh" {{
 Vertices: *48 {{ a: {vertices} }}
 PolygonVertexIndex: *16 {{ a: {indices} }}
 GeometryVersion: 124
 LayerElementMaterial: 0 {{
  Version: 101
  Name: ""
  MappingInformationType: "ByPolygon"
  ReferenceInformationType: "IndexToDirect"
  Materials: *4 {{ a: {','.join(map(str, order))} }}
 }}
 Layer: 0 {{ Version: 100
  LayerElement: {{ Type: "LayerElementMaterial"
   TypedIndex: 0 }}
 }}
}}''']
    connections = []
    for i, (name, color) in enumerate(MATERIALS):
        if duplicate and i < 2:
            name = 'Shared'
        objects.append(f'''Material: {200+i}, "Material::{name}", "" {{
 Version: 102
 ShadingModel: "phong"
 MultiLayer: 0
 Properties70: {{
  P: "DiffuseColor", "Color", "", "A",{','.join(map(str, color))}
  P: "DiffuseFactor", "Number", "", "A",1
 }}
}}''')
    for n in range(2 if shared else 1):
        objects.append(f'''Model: {300+n}, "Model::Panels{n}", "Mesh" {{
 Version: 232
 Properties70: {{
  P: "Lcl Translation", "Lcl Translation", "", "A",0,{n*2},0
  P: "Lcl Scaling", "Lcl Scaling", "", "A",1,1,1
 }}
 Shading: T
 Culling: "CullingOff"
}}''')
        connections += [f'C: "OO",{300+n},0', f'C: "OO",100,{300+n}']
        # The second instance has the same geometry and a different binding for each slot.
        connections += [f'C: "OO",{200+i},{300+n}' for i in ([1, 2, 0, 3] if n else range(4))]
    header = '''; FBX 7.4.0 project file
FBXHeaderExtension: { FBXHeaderVersion: 1003
 FBXVersion: 7400
}
GlobalSettings: { Version: 1000
 Properties70: {
  P: "UpAxis", "int", "Integer", "",1
  P: "UpAxisSign", "int", "Integer", "",1
  P: "FrontAxis", "int", "Integer", "",2
  P: "FrontAxisSign", "int", "Integer", "",-1
  P: "CoordAxis", "int", "Integer", "",0
  P: "CoordAxisSign", "int", "Integer", "",1
  P: "UnitScaleFactor", "double", "Number", "",100
 }
}
'''
    counts = {'Model': 2 if shared else 1, 'Geometry': 1, 'Material': 4}
    definitions = 'Definitions: {\nVersion: 100\nCount: '+str(sum(counts.values()))+'\n'
    definitions += '\n'.join(f'ObjectType: "{k}" {{\nCount: {v}\n}}' for k, v in counts.items())+'\n}\n'
    text = header + definitions + 'Objects: {\n' + '\n'.join(objects) + '\n}\nConnections: {\n' + '\n'.join(connections) + '\n}\n'
    return text.replace('{ ', '{\n ').replace(' }', '\n }')


def obj(order, multiple=False):
    lines = ['mtllib Slots.mtl']
    for n in range(2 if multiple else 1):
        lines += [f'o Panels{n}', f'g Panels{n}']
        for i in range(4):
            lines += [f'v {x} {y+n*2} 0' for x, y in
                      [(i*1.5, 0), (i*1.5+1, 0), (i*1.5+1, 1), (i*1.5, 1)]]
        lines += ['s off']
        for i, material in enumerate(order):
            lines += ['usemtl '+MATERIALS[material][0],
                      'f '+' '.join(str(n*16+i*4+j+1) for j in range(4))]
    return '\n'.join(lines)+'\n'


def main():
    if DEST.exists():
        shutil.rmtree(DEST)
    DEST.mkdir(parents=True, exist_ok=True)
    for folder in (DEST.parent, DEST):
        Path(str(folder)+'.meta').write_text(uv.FOLDER.format(guid=uv.guid(str(folder.relative_to(ROOT)))))
    for name, order in ORDERS.items():
        (DEST/(name+'.fbx')).write_text(fbx(order))
        (DEST/(name+'.obj')).write_text(obj(order))
    (DEST/'Instances.fbx').write_text(fbx(ORDERS['FirstUse'], shared=True))
    (DEST/'SharedNames.fbx').write_text(fbx(ORDERS['FirstUse'], duplicate=True))
    (DEST/'SharedNamesReverse.fbx').write_text(fbx([1, 2, 0, 1], duplicate=True))
    (DEST/'Objects.obj').write_text(obj(ORDERS['FirstUse'], multiple=True))
    (DEST/'Slots.mtl').write_text('\n'.join('newmtl '+name+'\nKd '+' '.join(map(str, c)) for name, c in MATERIALS)+'\n')
    # Explicit Unity remaps isolate OBJ slot assignment from MTL property conversion.
    template = (ROOT/'Assets/Models/Local/Materials/M_Red.mat').read_text()
    for name, color in MATERIALS:
        import re
        text = template.replace('m_Name: M_Red', 'm_Name: '+name)
        text = re.sub(r'- _Color:.*', '- _Color: {r: %s, g: %s, b: %s, a: 1}' % color, text)
        (DEST/(name+'.mat')).write_text(text)
        (DEST/(name+'.mat.meta')).write_text('fileFormatVersion: 2\nguid: '+uv.guid('Slots/'+name)+
            '\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n')
    shutil.copyfile(ROOT.parent/'lightmapuvs/Sources/Multi.fbx', DEST/'Multi.fbx')
    # Different file names allow Resources to distinguish the FBX and OBJ controls.
    for source in list(DEST.glob('*.obj')):
        source.rename(source.with_stem(source.stem+'Obj'))
    for source in sorted(DEST.iterdir()):
        if source.suffix not in ('.fbx', '.obj'):
            continue
        for mode, normal in [('Import', 0), ('Calculate', 1), ('None', 2)]:
            target = source.with_stem(source.stem+mode)
            shutil.copyfile(source, target)
            settings = dict(uv.DEFAULTS, **uv.RAW, normalImportMode=normal)
            meta = uv.META.format(guid=uv.guid(str(target.relative_to(ROOT))), **settings)
            meta = meta.replace('materialImportMode: 0', 'materialImportMode: 2')
            if source.suffix == '.obj':
                remaps = 'externalObjects:\n' + '\n'.join(
                    '  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n      name: '+name+
                    '\n    second: {fileID: 2100000, guid: '+uv.guid('Slots/'+name)+', type: 2}' for name, _ in MATERIALS)
                meta = meta.replace('externalObjects: {}', remaps)
            Path(str(target)+'.meta').write_text(meta)
        source.unlink()


if __name__ == '__main__':
    main()
