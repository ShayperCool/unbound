"""Writes the probe's ASCII FBX 7.4 models into Assets/Resources/Models.

    python3 tests/projects/modeltoproot/make_models.py

Every model has a single top-level node `Top` with two children, `ChildA` and `ChildB`, whose
translations are animated over one take "Take 001" of one second. The models differ in what the
importer may use to decide whether `Top` becomes the model's root: its node attribute (none, Null
or Skeleton), its `DefaultAttributeIndex` (absent, -1 or 0), its rest rotation, the children's
attribute and the animation of `Top` (translation, or only a custom property as 3D packages
export). Y up, centimetres (UnitScaleFactor 1), so the importer scales positions by 0.01.
`*Avatar` copies are the same file; `run.py create` gives them an avatar in their `.meta`.
"""
import os

PROBE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(PROBE, 'Assets', 'Resources', 'Models')
KTIME = 46186158000  # FBX time units per second

# name: (top attribute, DefaultAttributeIndex, top rotation, children's attribute, top animation)
MODELS = {
    'NullTop': ('Null', None, (0, 0, 0), 'Null', None),
    'NullTopDai0': ('Null', 0, (0, 0, 0), 'Null', None),
    'NullTopAnim': ('Null', None, (0, 0, 0), 'Null', 'translation'),
    'BareTop': (None, None, (0, 0, 0), 'Null', None),
    'SkelTopNoDai': ('Skeleton', None, (0, 0, 0), 'Skeleton', None),
    'SkelTopDaiM1': ('Skeleton', -1, (0, 0, 0), 'Skeleton', None),
    'SkelTop': ('Skeleton', 0, (0, 0, 0), 'Skeleton', None),
    'SkelTopNullKids': ('Skeleton', 0, (0, 0, 0), 'Null', None),
    'SkelTopTurned': ('Skeleton', 0, (90, 0, 0), 'Skeleton', None),
    'SkelTopTurnedAvatar': ('Skeleton', 0, (90, 0, 0), 'Skeleton', None),
    'SkelTopAnim': ('Skeleton', 0, (0, 0, 0), 'Skeleton', 'translation'),
    'SkelTopCustom': ('Skeleton', 0, (90, 0, 0), 'Skeleton', 'custom'),
    'NullTopCustom': ('Null', None, (90, 0, 0), 'Null', 'custom'),
}


def curve(ids, objects, conns, value_a, value_b):
    cid = next(ids)
    objects.append(f'''	AnimationCurve: {cid}, "AnimCurve::", "" {{
		Default: {value_a}
		KeyVer: 4009
		KeyTime: *2 {{
			a: 0,{KTIME}
		}}
		KeyValueFloat: *2 {{
			a: {value_a},{value_b}
		}}
		KeyAttrFlags: *1 {{
			a: 8456
		}}
		KeyAttrDataFloat: *4 {{
			a: 0,0,218434821,0
		}}
		KeyAttrRefCount: *1 {{
			a: 2
		}}
	}}''')
    return cid


def model_file(top_attr, dai, top_rotation, child_attr, top_anim):
    ids = iter(range(1000, 100000))
    objects, conns = [], []
    counts = {}

    def count(kind):
        counts[kind] = counts.get(kind, 0) + 1

    def node(name, parent, translation, rotation, attr, index=None, custom=False):
        mid = next(ids)
        kind = 'LimbNode' if attr == 'Skeleton' else 'Null'
        extra = ''
        if index is not None:
            extra += f'\n\t\t\tP: "DefaultAttributeIndex", "int", "Integer", "",{index}'
        if custom:
            extra += '\n\t\t\tP: "lockInfluenceWeights", "Bool", "", "A+U",0'
        objects.append(f'''	Model: {mid}, "Model::{name}", "{kind}" {{
		Version: 232
		Properties70:  {{
			P: "Lcl Translation", "Lcl Translation", "", "A",{translation[0]},{translation[1]},{translation[2]}
			P: "Lcl Rotation", "Lcl Rotation", "", "A",{rotation[0]},{rotation[1]},{rotation[2]}
			P: "Lcl Scaling", "Lcl Scaling", "", "A",1,1,1{extra}
		}}
		Shading: Y
		Culling: "CullingOff"
	}}''')
        count('Model')
        conns.append(f'	C: "OO",{mid},{parent}')
        if attr is not None:
            aid = next(ids)
            objects.append(f'''	NodeAttribute: {aid}, "NodeAttribute::", "{kind}" {{
		TypeFlags: "{attr}"
	}}''')
            count('NodeAttribute')
            conns.append(f'	C: "OO",{aid},{mid}')
        return mid

    stack, layer = next(ids), next(ids)
    objects.append(f'''	AnimationStack: {stack}, "AnimStack::Take 001", "" {{
		Properties70:  {{
			P: "LocalStart", "KTime", "Time", "",0
			P: "LocalStop", "KTime", "Time", "",{KTIME}
			P: "ReferenceStart", "KTime", "Time", "",0
			P: "ReferenceStop", "KTime", "Time", "",{KTIME}
		}}
	}}''')
    objects.append(f'	AnimationLayer: {layer}, "AnimLayer::BaseLayer", "" {{\n\t}}')
    count('AnimationStack')
    count('AnimationLayer')
    conns.append(f'	C: "OO",{layer},{stack}')

    def animate(mid, prop, channels, a, b):
        cn = next(ids)
        values = '\n'.join(f'\t\t\tP: "d|{c}", "Number", "", "A",{v}' for c, v in zip(channels, a))
        objects.append(f'''	AnimationCurveNode: {cn}, "AnimCurveNode::{prop}", "" {{
		Properties70:  {{
{values}
		}}
	}}''')
        count('AnimationCurveNode')
        conns.append(f'	C: "OO",{cn},{layer}')
        conns.append(f'	C: "OP",{cn},{mid}, "{prop}"')
        for channel, va, vb in zip(channels, a, b):
            conns.append(f'	C: "OP",{curve(ids, objects, conns, va, vb)},{cn}, "d|{channel}"')
            count('AnimationCurve')

    top = node('Top', 0, (0, 0, 0), top_rotation, top_attr, dai, custom=top_anim == 'custom')
    child_a = node('ChildA', top, (100, 0, 0), (0, 0, 0), child_attr)
    child_b = node('ChildB', top, (0, 100, 0), (0, 0, 0), child_attr)
    if top_anim == 'translation':
        animate(top, 'Lcl Translation', 'XYZ', (0, 0, 0), (0, 0, 200))
    elif top_anim == 'custom':
        animate(top, 'lockInfluenceWeights', ['lockInfluenceWeights'], (0,), (0,))
    animate(child_a, 'Lcl Translation', 'XYZ', (100, 0, 0), (100, 50, 0))
    animate(child_b, 'Lcl Translation', 'XYZ', (0, 100, 0), (0, 100, 50))

    definitions = '\n'.join(f'\tObjectType: "{k}" {{\n\t\tCount: {v}\n\t}}' for k, v in counts.items())
    return f'''; FBX 7.4.0 project file
FBXHeaderExtension:  {{
	FBXHeaderVersion: 1003
	FBXVersion: 7400
	Creator: "Unbound modeltoproot probe"
}}
GlobalSettings:  {{
	Version: 1000
	Properties70:  {{
		P: "UpAxis", "int", "Integer", "",1
		P: "UpAxisSign", "int", "Integer", "",1
		P: "FrontAxis", "int", "Integer", "",2
		P: "FrontAxisSign", "int", "Integer", "",1
		P: "CoordAxis", "int", "Integer", "",0
		P: "CoordAxisSign", "int", "Integer", "",1
		P: "OriginalUpAxis", "int", "Integer", "",1
		P: "OriginalUpAxisSign", "int", "Integer", "",1
		P: "UnitScaleFactor", "double", "Number", "",1
		P: "OriginalUnitScaleFactor", "double", "Number", "",1
		P: "TimeMode", "enum", "", "",6
		P: "TimeSpanStart", "KTime", "Time", "",0
		P: "TimeSpanStop", "KTime", "Time", "",{KTIME}
		P: "CustomFrameRate", "double", "Number", "",-1
	}}
}}
Definitions:  {{
	Version: 100
	Count: {sum(counts.values())}
{definitions}
}}
Objects:  {{
{chr(10).join(objects)}
}}
Connections:  {{
{chr(10).join(conns)}
}}
Takes:  {{
	Current: "Take 001"
	Take: "Take 001" {{
		FileName: "Take_001.tak"
		LocalTime: 0,{KTIME}
		ReferenceTime: 0,{KTIME}
	}}
}}
'''


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, spec in MODELS.items():
        with open(os.path.join(OUT, name + '.fbx'), 'w', newline='\n') as f:
            f.write(model_file(*spec))
    print('wrote', len(MODELS), 'models to', OUT)


if __name__ == '__main__':
    main()
