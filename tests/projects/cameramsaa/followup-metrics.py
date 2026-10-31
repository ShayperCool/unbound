#!/usr/bin/env python3
"""Explain every strict failure and quantify presentation changes without exclusions."""
from pathlib import Path
import hashlib
import json
import sys
import numpy as np
from PIL import Image
import compare

fixture=Path(__file__).resolve().parent
cache=Path.home()/'.cache/compat'
out=fixture/'Evidence/Followup'
stage=sys.argv[1] if len(sys.argv)>1 else 'final3'
result={}
for space in ('linear','gamma'):
    native=cache/('camerafollowup-oracle-v3-'+space+'1')
    before=cache/('camerafollowup-before-v3-'+space)
    final=cache/('camerafollowup-'+stage+'-'+space)
    a,b=compare.values(native),compare.values(final)
    details={}
    categories={}
    for key,path in compare.buffers(native).items():
        x=compare.pixels(path,a[key+'.pixels']);y=compare.pixels(final/(key+'.rgba'),a[key+'.pixels'])
        delta=np.abs(x-y)
        ok=(x==y)|(np.isfinite(x)&np.isfinite(y)&(delta<=np.maximum(2e-4,2e-4*np.maximum(np.abs(x),np.abs(y)))))
        centers={}
        for label,root in [('native',native),('before',before),('final',final)]:
            data=np.fromfile(root/(key+'.rgba'),dtype='<f4').reshape(-1,4)
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
    before=cache/'camerafollowup-regress-before'/project
    final=cache/('camerafollowup-regress-'+stage)/project
    reference=fixture.parent/project/'Reference'
    detail={}
    for path in sorted(before.rglob('*.png')):
        relative=path.relative_to(before);changed=path.read_bytes()!=(final/relative).read_bytes()
        if not changed:continue
        item={'before_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
              'final_sha256':hashlib.sha256((final/relative).read_bytes()).hexdigest()}
        if (reference/relative).exists():
            n=np.array(Image.open(reference/relative).convert('RGBA'),dtype=np.int16)
            for phase,root in [('before',before),('final',final)]:
                a=np.array(Image.open(root/relative).convert('RGBA'),dtype=np.int16)
                d=np.abs(n-a);item[phase]={'mean_max_channel_error':float(d.max(axis=2).mean()),'max_error':int(d.max()),'differing_channels':int((d!=0).sum())}
        detail[str(relative)]=item
    pixels[project]={'changed_images':len(detail),'images':detail}
(out/('regression-'+stage+'-pixel-metrics.json')).write_text(json.dumps(pixels,indent=2)+'\n')
print({k:v['changed_images'] for k,v in pixels.items()})
