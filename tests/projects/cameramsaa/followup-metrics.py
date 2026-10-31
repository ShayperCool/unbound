#!/usr/bin/env python3
"""Explain every strict failure and quantify presentation changes without exclusions."""
from pathlib import Path
import argparse
import hashlib
import json
import sys
import numpy as np
from PIL import Image
import compare
from recordings import external_reference

fixture=Path(__file__).resolve().parent
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, run_root
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('stage', nargs='?', default='current')
parser.add_argument('--before-root', type=Path)
parser.add_argument('--actual-root', type=Path)
parser.add_argument('--regress-before', type=Path)
parser.add_argument('--regress-actual', type=Path)
parser.add_argument('--out', type=Path)
args=parser.parse_args()
stage=args.stage
out=args.out or run_root(fixture,stage)/'metrics'
out.mkdir(parents=True,exist_ok=True)
before_root=args.before_root or run_root(fixture,'before')
actual_root=args.actual_root or run_root(fixture,stage)
result={}
for space in ('linear','gamma'):
    native=reference_root(fixture)/space.title()
    before=before_root/space.title()
    final=actual_root/space.title()
    a,b=compare.values(native),compare.values(final)
    details={}
    categories={}
    for key,path in compare.buffers(native).items():
        x=compare.pixels(path,a[key+'.pixels']);y=compare.pixels(compare.buffers(final)[key],a[key+'.pixels'])
        delta=np.abs(x-y)
        ok=(x==y)|(np.isfinite(x)&np.isfinite(y)&(delta<=np.maximum(2e-4,2e-4*np.maximum(np.abs(x),np.abs(y)))))
        centers={}
        for label,root in [('native',native),('before',before),('final',final)]:
            data=compare.pixels(compare.buffers(root)[key],a[key+'.pixels']).reshape(-1,4)
            # Source descriptors supply non-square viewport dimensions.
            size=a.get(key+'.size')
            width=size[0] if size else 128
            height=len(data)//width
            centers[label]=data[(height//2)*width+width//2].tolist()
        category='matching'
        if not ok.all():
            if key in ['ARGB32.resolve','ARGBFloat.resolve'] or key.endswith('.resolveSentinelInactive'):
                category='Native rejects inactive resolve; output is not a defined source resolve'
            elif key.startswith('boundary.screenPublic.'):
                category='Editor system framebuffer differs from engine internal screen; compare active attachment separately'
            elif key.startswith(('screen.','boundary.screen.')):
                category='Editor GameView byte attachment differs from engine linear screen read source'
            else:
                category='remaining comparable rendering/readback discrepancy'
            categories[category]=categories.get(category,0)+1
        details[key]={'matching_channels':int(ok.sum()),'channels':len(x),'max_delta':float(np.max(delta)),
                      'category':category,'center':centers}
    unequal=[{'key':key,'native':value,'engine':b.get(key,'MISSING')} for key,value in a.items() if key not in b or not compare.equal(value,b[key])]
    result[space]={'observations':len(a),'matching_observations':len(a)-len(unequal),'unequal_observations':unequal,
                   'unexpected_observations':sorted(b.keys()-a.keys()),'failing_image_categories':categories,'images':details}
(out/('complete-'+stage+'-metrics.json')).write_text(json.dumps(result,indent=2)+'\n')
print({space:{'matching_observations':v['matching_observations'],'failing_image_categories':v['failing_image_categories']} for space,v in result.items()})

pixels={}
for project in ('graphics','resources','builtinrp','builtinfx','uitk'):
    before=(args.regress_before or before_root/'regress')/project
    final=(args.regress_actual or actual_root/'regress')/project
    reference=external_reference(fixture.parent/project)
    assert before.exists() and final.exists(), f'missing regression captures: {before}, {final}'
    assert before.is_file()==final.is_file(), f'capture kinds differ: {before}, {final}'
    detail={}
    for path in sorted(before.rglob('*.png')):
        relative=path.relative_to(before);changed=path.read_bytes()!=(final/relative).read_bytes()
        if not changed:continue
        item={'native_image_available':(reference/relative).exists(),'before_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
              'final_sha256':hashlib.sha256((final/relative).read_bytes()).hexdigest()}
        if (reference/relative).exists():
            n=np.array(Image.open(reference/relative).convert('RGBA'),dtype=np.int16)
            for phase,root in [('before',before),('final',final)]:
                a=np.array(Image.open(root/relative).convert('RGBA'),dtype=np.int16)
                d=np.abs(n-a);item[phase]={'mean_max_channel_error':float(d.max(axis=2).mean()),'max_error':int(d.max()),'differing_channels':int((d!=0).sum())}
        detail[str(relative)]=item
    pixels[project]={'changed_images':len(detail),'images':detail}
    if before.is_file():
        pixels[project]['capture_file_changed']=before.read_bytes()!=final.read_bytes()
(out/('regression-'+stage+'-pixel-metrics.json')).write_text(json.dumps(pixels,indent=2)+'\n')
print({k:v['changed_images'] for k,v in pixels.items()})
