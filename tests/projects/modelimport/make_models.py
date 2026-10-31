"""Generates the probe's FBX models and textures with Blender's own FBX exporter.

    blender -b --factory-startup --python tests/projects/modelimport/make_models.py

Blender writes the absolute texture path next to the relative one, so the models are exported
from a neutral staging directory and copied into Assets/Models afterwards.
"""
import os
import shutil
import sys

import bpy

PROBE = os.path.dirname(os.path.abspath(__file__))
STAGE = '/tmp/unbound-modelimport/Assets/Models'


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def image(name, size, pixel, folder):
    img = bpy.data.images.new(name, size, size, alpha=True)
    pixels = []
    for y in range(size):
        for x in range(size):
            pixels.extend(pixel(x, y))
    img.pixels = pixels
    os.makedirs(folder, exist_ok=True)
    img.filepath_raw = os.path.join(folder, name + '.png')
    img.file_format = 'PNG'
    img.save()
    return img


def checker(x, y):
    on = ((x // 8) + (y // 8)) % 2
    return (0.9, 0.9, 0.2, 1.0) if on else (0.1, 0.3, 0.8, 1.0)


def bumps(x, y):
    return (0.5 + 0.3 * ((x // 4) % 2 - 0.5), 0.5, 1.0, 1.0)


def glow(x, y):
    return (1.0, 0.4 * (x % 2), 0.0, 1.0)


def material(name, color=(0.8, 0.8, 0.8, 1.0), alpha=1.0, roughness=0.5, metallic=0.0,
             emission=None, strength=0.0, base_tex=None, normal_tex=None, emit_tex=None):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    tree = mat.node_tree
    bsdf = next(n for n in tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = color
    bsdf.inputs['Alpha'].default_value = alpha
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = metallic
    if emission is not None:
        bsdf.inputs['Emission Color'].default_value = emission
        bsdf.inputs['Emission Strength'].default_value = strength
    if base_tex is not None:
        node = tree.nodes.new('ShaderNodeTexImage')
        node.image = base_tex
        tree.links.new(node.outputs['Color'], bsdf.inputs['Base Color'])
    if normal_tex is not None:
        node = tree.nodes.new('ShaderNodeTexImage')
        node.image = normal_tex
        normal = tree.nodes.new('ShaderNodeNormalMap')
        tree.links.new(node.outputs['Color'], normal.inputs['Color'])
        tree.links.new(normal.outputs['Normal'], bsdf.inputs['Normal'])
    if emit_tex is not None:
        node = tree.nodes.new('ShaderNodeTexImage')
        node.image = emit_tex
        tree.links.new(node.outputs['Color'], bsdf.inputs['Emission Color'])
        bsdf.inputs['Emission Strength'].default_value = strength
    return mat


def cube(name, location, materials=(), size=1.0, parent=None):
    bpy.ops.mesh.primitive_cube_add(size=size, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    for mat in materials:
        obj.data.materials.append(mat)
    if len(materials) > 1:
        # One face per extra slot so each material has triangles.
        for i, poly in enumerate(obj.data.polygons):
            poly.material_index = i % len(materials)
    if parent is not None:
        obj.parent = parent
    return obj


def empty(name, location=(0, 0, 0), parent=None):
    obj = bpy.data.objects.new(name, None)
    obj.location = location
    obj.parent = parent
    bpy.context.scene.collection.objects.link(obj)
    return obj


def export(path, embed=False):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, path_mode='COPY' if embed else 'RELATIVE',
                             embed_textures=embed, use_selection=False, object_types={'MESH', 'EMPTY'})


def materials_scene():
    reset()
    textures = os.path.join(STAGE, 'Textures')
    base = image('Checker', 32, checker, textures)
    normal = image('Bumps', 16, bumps, textures)
    emit = image('Glow', 8, glow, textures)
    red = material('M_Red', color=(0.8, 0.1, 0.1, 1.0), roughness=0.4)
    cube('Cube_Red', (0, 0, 0), [red])
    cube('Cube_Shared', (2, 0, 0), [red])
    cube('Cube_Tex', (4, 0, 0), [material('M_Tex', roughness=0.3, base_tex=base, normal_tex=normal)])
    alpha = material('M_Alpha', color=(0.2, 0.4, 0.9, 1.0), alpha=0.4, roughness=0.9)
    cube('Cube_Alpha', (6, 0, 0), [alpha])
    cube('Cube_Emit', (8, 0, 0), [material('M_Emit', color=(0.1, 0.1, 0.1, 1.0),
                                           emission=(1.0, 0.5, 0.0, 1.0), strength=2.0)])
    cube('Cube_EmitTex', (10, 0, 0), [material('M_EmitTex', emit_tex=emit, strength=1.5)])
    cube('Cube_Metal', (12, 0, 0), [material('M_Metal', color=(0.7, 0.7, 0.75, 1.0),
                                             roughness=0.2, metallic=0.8)])
    cube('Cube_Multi', (14, 0, 0), [red, alpha])
    cube('Cube_NoMat', (16, 0, 0))
    export(os.path.join(STAGE, 'Materials.fbx'))


def embedded_scene():
    reset()
    textures = '/tmp/unbound-modelimport/Embedded'
    tex = image('EmbeddedChecker', 16, checker, textures)
    tex.pack()
    cube('Cube_Embedded', (0, 0, 0), [material('M_Embedded', base_tex=tex)])
    export(os.path.join(STAGE, 'Embedded.fbx'), embed=True)


def lods_scene():
    reset()
    red = material('M_Lod', color=(0.3, 0.8, 0.3, 1.0))
    tree = empty('Tree')
    cube('Tree_LOD0', (0, 0, 0), [red], 1.0, tree)
    cube('Tree_LOD1', (0, 0, 0), [red], 0.8, tree)
    cube('Tree_LOD2', (0, 0, 0), [red], 0.6, tree)
    cube('Rock_LOD0', (3, 0, 0), [red], 1.0)
    cube('Rock_LOD1', (3, 0, 0), [red], 0.5)
    cube('Stone_lod0', (6, 0, 0), [red], 1.0)
    cube('Stone_lod1', (6, 0, 0), [red], 0.5)
    cube('Bush_LOD1', (9, 0, 0), [red], 1.0)
    cube('Bush_LOD2', (9, 0, 0), [red], 0.5)
    cube('Plain', (12, 0, 0), [red])
    export(os.path.join(STAGE, 'Lods.fbx'))


def single_lods_scene():
    """LOD nodes directly under the file's root: the importer's root takes the group."""
    reset()
    red = material('M_Lod', color=(0.8, 0.3, 0.3, 1.0))
    cube('Crate_LOD0', (0, 0, 0), [red], 1.0)
    cube('Crate_LOD1', (0, 0, 0), [red], 0.7)
    export(os.path.join(STAGE, 'CrateLods.fbx'))


def many_lods_scene():
    """Six levels, and a group whose levels skip numbers."""
    reset()
    red = material('M_Lod', color=(0.3, 0.3, 0.8, 1.0))
    for i in range(6):
        cube(f'Tower_LOD{i}', (0, 0, 0), [red], 1.0 - 0.1 * i)
    cube('Gap_LOD0', (0, 0, 4), [red], 2.0)
    cube('Gap_LOD3', (0, 0, 4), [red], 1.0)
    export(os.path.join(STAGE, 'ManyLods.fbx'))


def grouped_lods_scene():
    """Level nodes without a mesh of their own, their meshes in children."""
    reset()
    red = material('M_Lod', color=(0.6, 0.6, 0.3, 1.0))
    near = empty('Bundle_LOD0')
    cube('Part_A', (0, 0, 0), [red], 1.0, near)
    cube('Part_B', (0, 2, 0), [red], 1.0, near)
    far = empty('Bundle_LOD1')
    cube('Part_C', (0, 1, 0), [red], 1.5, far)
    export(os.path.join(STAGE, 'GroupedLods.fbx'))


def rig_scenes():
    """A skinned mesh without animation, and a mesh with a clip: which get an Animator."""
    reset()
    bpy.ops.object.armature_add(location=(0, 0, 0))
    rig = bpy.context.active_object
    rig.name = 'Rig'
    mesh = cube('Skinned', (0, 0, 0.5), [material('M_Skin', color=(0.5, 0.5, 0.9, 1.0))])
    group = mesh.vertex_groups.new(name='Bone')
    group.add(list(range(len(mesh.data.vertices))), 1.0, 'REPLACE')
    mesh.parent = rig
    modifier = mesh.modifiers.new('Armature', 'ARMATURE')
    modifier.object = rig
    bpy.ops.export_scene.fbx(filepath=os.path.join(STAGE, 'Skinned.fbx'), path_mode='RELATIVE',
                             object_types={'MESH', 'ARMATURE', 'EMPTY'}, bake_anim=False)
    reset()
    moving = cube('Moving', (0, 0, 0), [material('M_Move', color=(0.9, 0.9, 0.5, 1.0))])
    moving.keyframe_insert('location', frame=1)
    moving.location = (0, 0, 2)
    moving.keyframe_insert('location', frame=20)
    export(os.path.join(STAGE, 'Animated.fbx'))


# Search.fbx points at SearchTex/<name>.png; none is copied there, each lies elsewhere.
SEARCH_PLACES = {
    'Local': ['Models/Search/Local.png'],
    'Up': ['Models/Textures/Up.png'],
    'Faraway': ['Elsewhere/Deep/Faraway.png'],
    'Twin': ['Elsewhere/Twin.png', 'Models/Search/Textures/Twin.png'],
}


def search_scene():
    reset()
    folder = os.path.join(STAGE, 'SearchTex')
    for i, name in enumerate(SEARCH_PLACES):
        tex = image(name, 4 * (i + 1), checker, folder)
        cube('Cube_' + name, (2 * i, 0, 0), [material('M_' + name, base_tex=tex)])
    export(os.path.join(STAGE, 'Search', 'Search.fbx'))


VARIANTS = ['MaterialsNone.fbx', 'MaterialsLegacy.fbx', 'Moved/MaterialsMoved.fbx',
            'External/MaterialsExternal.fbx', 'External/ByTexture/MaterialsByTexture.fbx',
            'External/ByModel/MaterialsByModel.fbx', 'External/ByTextureOrModel/MaterialsByTextureOrModel.fbx',
            'External/Deeper/MaterialsUp.fbx', 'Far/MaterialsEverywhere.fbx', 'Local/MaterialsLocal.fbx']
# Copies with an Avatar created from the model (their .meta: avatarSetup 1).
AVATAR_VARIANTS = {'Animated.fbx': 'AnimatedAvatar.fbx', 'CrateLods.fbx': 'CrateAvatar.fbx'}


def main():
    shutil.rmtree('/tmp/unbound-modelimport', ignore_errors=True)
    materials_scene()
    embedded_scene()
    lods_scene()
    single_lods_scene()
    many_lods_scene()
    grouped_lods_scene()
    rig_scenes()
    search_scene()
    assets = os.path.join(PROBE, 'Assets')
    for root, _, files in os.walk(STAGE):
        for name in files:
            source = os.path.join(root, name)
            relative = os.path.relpath(source, STAGE)
            if relative.startswith('SearchTex'):
                places = SEARCH_PLACES[os.path.splitext(name)[0]]
            else:
                places = [os.path.join('Models', relative)]
            for place in places:
                dest = os.path.join(assets, place)
                os.makedirs(os.path.dirname(dest), exist_ok=True)
                shutil.copyfile(source, dest)
    # The same file under importer settings that differ only in their .meta.
    for variant in VARIANTS:
        shutil.copyfile(os.path.join(assets, 'Models', 'Materials.fbx'), os.path.join(assets, 'Models', variant))
    for source, variant in AVATAR_VARIANTS.items():
        shutil.copyfile(os.path.join(assets, 'Models', source), os.path.join(assets, 'Models', variant))
    shutil.rmtree('/tmp/unbound-modelimport', ignore_errors=True)


main()
sys.exit(0)
