"""Analyze saved line captures without building or running an engine or Editor.

This consumes the combined batch's raw union and arrays, never edits its inputs,
and writes diagnostics, not a replacement parity gate. See
docs/LINE_GEOMETRY_ATTRIBUTION.md for the source split and unresolved controls.
"""
from pathlib import Path
import argparse
import collections
import hashlib
import json
import math
import numpy as np

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--batch', type=Path, required=True, help='Saved combined report directory')
parser.add_argument('--preblend', type=Path, required=True, help='Saved pre-blend unbound capture directory')
parser.add_argument('--out', type=Path, required=True, help='Cache or Library/Unbound output directory')
args = parser.parse_args()
HERE = args.out.resolve()
HERE.mkdir(parents=True, exist_ok=True)
SAVED = args.batch.resolve()
COMP = SAVED / 'comparisons'
ROWS = json.loads((COMP / 'lines-full-raw-union.json').read_text())['rows']
BYKEY = {r['key']: r for r in ROWS}
DELTA = json.loads((COMP / 'lines-leaf-and-pixel-deltas.json').read_text())
BASES = ('reference', 'root', 'blend', 'final')

def save(name, value):
    (HERE / name).write_text(json.dumps(value, indent=2, allow_nan=False) + '\n')

def raw(key, base):
    return BYKEY[key]['raw'][base].get('value')

def leaves(x, path=''):
    if isinstance(x, dict):
        for k, v in x.items(): yield from leaves(v, path + '.' + k)
    elif isinstance(x, list):
        for i, v in enumerate(x): yield from leaves(v, path + f'[{i}]')
    else: yield path, x

def errors(a, b):
    # Same original tolerance, used only for analysis. No comparator changes.
    aa, bb = dict(leaves(a)), dict(leaves(b))
    bad = []
    for p in sorted(aa.keys() | bb.keys()):
        x, y = aa.get(p), bb.get(p)
        numeric = all(isinstance(t, (int, float)) and not isinstance(t, bool) for t in (x, y))
        if p not in aa or p not in bb or (abs(x-y) > 1e-5 + 1e-5*abs(x) if numeric else x != y):
            bad.append(dict(path=p, reference_present=p in aa, actual_present=p in bb, reference=x, actual=y))
    return bad

def raster(mesh, uv_shader=False):
    """Pixel-center, double precision diagnostic for the orthographic probe camera.

    It is NOT a replacement rasterizer or parity comparator. Boundary pixels are
    retained, not masked. Each saved array is compared to its diagnostic result.
    The shader is Cull Off/ZWrite Off/ZTest Always, output interpolated RGBA.
    """
    result = np.zeros((64, 96, 4))
    if not mesh or not mesh.get('v'): return result
    v = np.array(mesh['v']).reshape(-1, 3)
    c = np.array(mesh['c']).reshape(-1, 4)
    if uv_shader:
        uv=np.array(mesh['uv0']).reshape(-1,2)
        c=np.column_stack((uv,np.zeros(len(uv)),np.ones(len(uv))))
    screen = v[:, :2]*16 + [48,32]
    for indices in np.array(mesh['idx'][0]['i']).reshape(-1,3):
        t = screen[indices]
        a, b, d = t
        den = (b[1]-d[1])*(a[0]-d[0]) + (d[0]-b[0])*(a[1]-d[1])
        if den == 0: continue
        low = np.maximum(0, np.ceil(t.min(axis=0)-.5).astype(int))
        high = np.minimum([95,63], np.floor(t.max(axis=0)-.5).astype(int))
        for y in range(low[1],high[1]+1):
            for x in range(low[0],high[0]+1):
                p = np.array([x+.5,y+.5])
                u = ((b[1]-d[1])*(p[0]-d[0]) + (d[0]-b[0])*(p[1]-d[1]))/den
                w = ((d[1]-a[1])*(p[0]-d[0]) + (a[0]-d[0])*(p[1]-d[1]))/den
                z = 1-u-w
                if min(u,w,z) >= 0: result[y,x] = np.array([u,w,z]) @ c[indices]
    return result

def array(image, base):
    return np.load(COMP/'line-image-arrays'/f'{image}.{base}.npy').astype(float).reshape(64,96,4)

def pixel_rows(image):
    z = np.load(COMP/'line-image-arrays'/f'{image}.root-to-final-errors.npz')
    affected = sorted(set(z['new_bad_pixels'].tolist()) | set(z['new_bad_channels'][:,0].tolist()))
    out=[]
    for i in affected:
        out.append(dict(linear_pixel=i, xy=[i%96,i//96],
                        newly_failing_pixel=bool(i in z['new_bad_pixels']),
                        newly_failing_channels=z['new_bad_channels'][z['new_bad_channels'][:,0]==i,1].tolist(),
                        raw={b:array(image,b).reshape(-1,4)[i].tolist() for b in BASES},
                        before_error=z['before_error'][i].tolist(),actual_error=z['actual_error'][i].tolist()))
    return out

def tilted_transformz_line():
    """Replay the source's L strip with a transform-forward render plane.

    Positions, cap/corner count, width and rotation are the authored fixture,
    not fitted values. Colors/UVs/indices are taken from the saved mesh because
    none depend on this plane. No Native image is used in the construction.
    """
    def norm(v): return v/np.linalg.norm(v)
    # Quaternion.Euler(30,20,0) rotates X then Y.
    x,y=map(math.radians,(30,20))
    forward=np.array([math.sin(y)*math.cos(x),-math.sin(x),math.cos(y)*math.cos(x)])
    points=np.array([[-1,-.5,0],[1,-.5,0],[1,1,0]])
    dirs=np.diff(points,axis=0)
    dirs=np.array([norm(d) for d in dirs])
    sides=np.array([norm(np.cross(d,-forward)) for d in dirs])
    m=norm(sides[0]+sides[1]); reach=.2/max(np.dot(m,sides[0]),1/3)
    ainner=np.dot(np.cross(*sides),-forward)>0
    inner=points[1]-m*reach if ainner else points[1]+m*reach
    angle=math.acos(np.clip(np.dot(*sides),-1,1))
    perp=norm(sides[1]-sides[0]*np.dot(*sides))
    pairs=[(points[0]-.2*sides[0],points[0]+.2*sides[0])]
    for k in range(3):
        radius=sides[0]*math.cos(angle*k/2)+perp*math.sin(angle*k/2)
        pairs.append((inner,inner+.4*radius) if ainner else (inner-.4*radius,inner))
    pairs.append((points[-1]-.2*sides[1],points[-1]+.2*sides[1]))
    def cap(center,pair,direction,start):
        radius=pair[1]-center; r=np.linalg.norm(radius)
        outward=norm(np.cross(forward,radius))
        if np.dot(outward,direction)<0: outward=-outward
        back=outward*(-r if start else r)
        return [center]+[center+radius*math.cos(math.pi*k/3)+back*math.sin(math.pi*k/3) for k in range(4)]
    vertices=cap(points[0],pairs[0],dirs[0],True)+[v for pair in pairs for v in pair]+cap(points[-1],pairs[-1],dirs[-1],False)
    mesh=json.loads(json.dumps(raw('line.TransformZ.L.c2cap2.color','final')))
    mesh['v']=np.array(vertices).reshape(-1).tolist()
    return mesh

def cap_diagnostics():
    controls=[]
    for row in ROWS:
        a=raw(row['key'],'reference'); b=raw(row['key'],'final')
        if not isinstance(b,dict) or not b.get('idx') or b['idx'][0]['i'][:3]!=[0,2,1]: continue
        if not isinstance(a,dict) or not a.get('v'): continue
        ix=b['idx'][0]['i']
        ntri=0
        for i in range(0,len(ix),3):
            if ix[i]!=0: break
            ntri+=1
        caps=ntri-1
        if caps<1: continue
        n=caps+3
        record=dict(key=row['key'],inferred_cap_vertices=caps,
            raw={base:raw(row['key'],base) for base in BASES},caps=[])
        for start in (True,False):
            at=0 if start else b['vc']-n
            va=np.array(a['v']).reshape(-1,3)[at:at+n]
            vb=np.array(b['v']).reshape(-1,3)[at:at+n]
            # Compare cap's interior offsets, not differently tessellated Root indices.
            center=vb[0]; radius=vb[1]-center
            plane=np.array([0,0,1.])
            sideways=np.cross(plane,radius)
            observed=vb[2]-center-radius*math.cos(math.pi/(caps+1))
            native=va[2]-va[0]-(va[1]-va[0])*math.cos(math.pi/(caps+1))
            record['caps'].append(dict(start=start,center=center.tolist(),radius=radius.tolist(),
                final_interior_offset=observed.tolist(),native_interior_offset=native.tolist(),
                same_direction=bool(np.dot(observed,native)>=0),
                original_tolerance_failures=errors(va.tolist(),vb.tolist())))
        controls.append(record)
    save('cap-controls.json',controls)
    print('cap controls',len(controls),'counts',dict(collections.Counter(x['inferred_cap_vertices'] for x in controls)))

def tangent_diagnostics():
    out=[]
    for row in ROWS:
        a=raw(row['key'],'reference');b=raw(row['key'],'blend');c=raw(row['key'],'final')
        if not isinstance(c,dict) or not c.get('t'): continue
        reconstruction=[]
        if row['key'].startswith('trail.'):
            # All saved lit trails are round: caps=2, corners=2, XY, positive width.
            # For a corner pair B-A is exactly the source's width*r, independently
            # recoverable from the saved position stream. Caps reuse the endpoint tangent.
            v=np.array(c['v']).reshape(-1,3);t=np.array(c['t']).reshape(-1,4)
            first,last=5,len(v)-5
            for at in range(first,last,2):
                side=v[at+1]-v[at]
                expected=np.cross([0,0,-1],side)
                if at==first or at==last-2: expected/=np.linalg.norm(expected)
                reconstruction.append(dict(vertex_pair=[at,at+1],predicted=expected.tolist(),
                    captured=t[at,:3].tolist(),max_error=float(max(abs(expected-t[at,:3])))))
        out.append(dict(key=row['key'],raw={base:raw(row['key'],base) for base in BASES},
            blend_tangent_failures=errors(a['t'],b['t']),final_tangent_failures=errors(a['t'],c['t']),
            cross_tangent_reconstruction=reconstruction,
            introduced=[x for x in DELTA['blend']['introduced_numeric_paths'] if x['key']==row['key']],
            worsenings=[x for x in DELTA['blend']['all_numeric_worsenings'] if x['key']==row['key']]))
    save('tangent-controls.json',out)
    print('tangent controls',len(out))

def projection():
    predicted=raster(tilted_transformz_line())
    summary={}
    for base in ('root','blend','final'):
        before={};after={}
        for row in json.loads((COMP/'lines-images-full-union.json').read_text()):
            key=row['key'][:-7]
            ref=array(key,'reference');old=array(key,'final');new=predicted if key=='img.TransformZ.L.c2cap2.color' else old
            be=abs(array(key,base)-ref);oe=abs(old-ref);ne=abs(new-ref)
            def metrics(e):
                return dict(bad_pixels=int(np.any(e>2/255,axis=2).sum()),bad_channels=int((e>2/255).sum()),
                    introduced_pixels=int((~np.any(be>2/255,axis=2)&np.any(e>2/255,axis=2)).sum()),
                    introduced_channels=int(((be<=2/255)&(e>2/255)).sum()),
                    numerical_error_increases=int((e>be).sum()))
            before[key]=metrics(oe);after[key]=metrics(ne)
        summary[base]=dict(before=before,prepared=after,
            totals={state:{metric:sum(x[metric] for x in images.values()) for metric in next(iter(images.values()))} for state,images in [('before',before),('prepared',after)]})
    save('offline-projection.json',dict(status='UNBUILT/UNACCEPTED',image_predictions=summary,
        full_union=1887,required_union=1870,whole_key_prediction=dict(before_fail=215,prepared_fail=214),
        numeric_prediction=dict(reason='BakeMesh paths unchanged; only world TransformZ LineRenderer collection plane changes',
            root=dict(introduced=60,changed_retained=4968,error_increases=1279),
            blend=dict(introduced=36,changed_retained=136,error_increases=480)),
        original_missing_extra_and_failing_values=str(COMP/'lines-full-raw-union.json'),
        disclaimer='Predicted arrays are CPU raster diagnostics at unchanged tolerances, not GPU or runtime acceptance.'))
    print('projection',{b:x['totals'] for b,x in summary.items()})

def trail_image():
    names=['basic','flat','api2','api2Emitting','round','far']
    names+=['tex.'+s for s in ['Stretch','Tile','DistributePerSegment','RepeatPerSegment','Static']]
    names+=['emit','stop.autodestruct','stop','clear','transformZ','api','apiEmitting','apiLong']
    out={}
    for base in BASES:
        composed=np.zeros((64,96,4));layers=[]
        for name in names:
            key='trail.f10.'+name+'.end.mesh'
            mesh=raw(key,base)
            image=raster(mesh,uv_shader=name.startswith('tex.'))
            covered=image[:,:,3]!=0
            composed[covered]=image[covered]
            layers.append((key,image))
        expected=array('img.trails.f10',base)
        e=abs(composed-expected)
        mismatch=np.flatnonzero(np.any(e>2/255,axis=2))
        witnesses=[]
        for w in pixel_rows('img.trails.f10'):
            i=w['linear_pixel'];y,x=divmod(i,96)
            witnesses.append(dict(linear_pixel=i,raw=expected[y,x].tolist(),prediction=composed[y,x].tolist(),
                layers=[dict(key=k,rgba=im[y,x].tolist()) for k,im in layers if im[y,x,3]!=0]))
        out[base]=dict(mismatch_count=len(mismatch),mismatch_pixels=mismatch.tolist(),max_error=float(e.max()),witnesses=witnesses)
        np.save(HERE/f'img.trails.f10.{base}.diagnostic-raster.npy',composed)
    save('trail-image-raster.json',out)
    print('trail rasters',{b:v['mismatch_count'] for b,v in out.items()})

def main():
    cap_diagnostics()
    tangent_diagnostics()
    trail_image()
    projection()
    preblend_rows={r['k']:r['v'] for r in map(json.loads,(args.preblend/'values.jsonl').read_text().splitlines())}
    groups={}
    for b in ('root','blend'):
        paths=DELTA[b]['introduced_numeric_paths']
        groups[b]=dict(path_count=len(paths), by_key=dict(collections.Counter(x['key'] for x in paths)), paths=paths)
        for witness in paths:
            old=dict(leaves(preblend_rows.get(witness['key'])))
            witness['preblend_present']=witness['path'] in old
            witness['preblend']=old.get(witness['path'])
            witness['source_hunk']='base rewrite Cap winding' if b=='root' else 'cross-product corner tangent'
    images={}
    for image, meshkey in [('img.TransformZ.L.c2cap2.color','line.TransformZ.L.c2cap2.color'),
                           ('img.mask.VisibleInsideMask','line.mask.VisibleInsideMask')]:
        record=dict(witnesses=pixel_rows(image), meshes={}, raster={})
        for b in BASES:
            m=raw(meshkey,b)
            record['meshes'][b]=m
            prediction=raster(m)
            # The engine stencil clips VisibleInsideMask to the square.
            if 'mask.' in image and b!='reference':
                yy,xx=np.mgrid[:64,:96]
                inside=(abs((xx+.5-48)/16)<=.75)&(abs((yy+.5-32)/16)<=.75)
                prediction[~inside]=0
            actual=array(image,b)
            e=abs(prediction-actual)
            mismatch=np.flatnonzero(np.any(e>2/255,axis=2))
            newpx=[r['linear_pixel'] for r in record['witnesses'] if r['newly_failing_pixel']]
            record['raster'][b]=dict(mismatch_pixels=mismatch.tolist(), mismatch_count=len(mismatch), max_error=float(e.max()),
                witnesses=[dict(linear_pixel=i,prediction=prediction.reshape(-1,4)[i].tolist(),
                    actual=actual.reshape(-1,4)[i].tolist()) for i in newpx])
            np.save(HERE/f'{image}.{b}.diagnostic-raster.npy',prediction)
        record['bake_failures']={b:errors(raw(meshkey,'reference'),raw(meshkey,b)) for b in BASES if b!='reference'}
        images[image]=record
    images['img.trails.f10']=dict(witnesses=pixel_rows('img.trails.f10'))
    for name,record in images.items():
        preblend=np.fromfile(args.preblend/(name+'.rgba'),dtype='<f4').reshape(-1,4)
        stage=[]
        for w in record['witnesses']:
            i=w['linear_pixel']
            w['preblend']=preblend[i].astype(float).tolist()
            w['preblend_error']=abs(preblend[i]-w['raw']['reference']).tolist()
            for c in w['newly_failing_channels']:
                x=dict(linear_pixel=i,channel=c,reference=w['raw']['reference'][c],root=w['raw']['root'][c],
                    preblend=float(preblend[i,c]),blend=w['raw']['blend'][c],final=w['raw']['final'][c])
                x['preblend_already_failed']=abs(x['preblend']-x['reference'])>2/255
                stage.append(x)
        record['new_channel_stage_attribution']=stage
    model=tilted_transformz_line()
    prediction=raster(model)
    actual=array('img.TransformZ.L.c2cap2.color','reference')
    e=abs(prediction-actual)
    mismatch=np.flatnonzero(np.any(e>2/255,axis=2))
    save('transformz-render-plane-hypothesis.json',dict(mesh=model,mismatch_pixels=mismatch.tolist(),
        mismatch_count=len(mismatch),max_error=float(e.max()),
        claim='Offline authored-plane hypothesis; not a runtime measurement'))
    np.save(HERE/'transformz-render-plane-hypothesis.npy',prediction)
    save('introduced-paths-and-image-witnesses.json',dict(numeric=groups,images=images))
    # Raw amounts remain distinct. Every original missing/extra/fail/change is retained by link.
    save('inputs.json',dict(original_report=str(SAVED/'REPORT.md'),
        full_raw_union=str(COMP/'lines-full-raw-union.json'),
        full_image_union=str(COMP/'lines-images-full-union.json'),
        source_deltas=str(COMP/'lines-leaf-and-pixel-deltas.json'),
        unchanged_tolerances=dict(absolute=1e-5,relative=1e-5,pixel=2/255),
        selected_input_hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in [
            COMP/'lines-full-raw-union.json',COMP/'lines-leaf-and-pixel-deltas.json',COMP/'lines-images-full-union.json',
            args.preblend/'values.jsonl', *[args.preblend/(name+'.rgba') for name in images]]}))
    print({k:{b:r['mismatch_count'] for b,r in v.get('raster',{}).items()} for k,v in images.items()})

if __name__=='__main__': main()
