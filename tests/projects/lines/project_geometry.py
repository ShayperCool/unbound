"""Project the prepared geometry into the immutable saved unions; no engine claims."""
import argparse, ast, copy, json, math, sys
from pathlib import Path
import numpy as np
import tangent_projection as model
import strip_projection

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--batch',type=Path,required=True)
parser.add_argument('--attribution',type=Path,required=True)
parser.add_argument('--out',type=Path,required=True)
parser.add_argument('--source-revision',required=True)
args=parser.parse_args()
REPO=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(REPO/'tests'))
from probe_paths import cache_artifact_path
HERE=cache_artifact_path(args.out.resolve());HERE.mkdir(parents=True,exist_ok=True)
BATCH=args.batch.resolve();ATTR=args.attribution.resolve()
ROWS=json.loads((BATCH/'comparisons/lines-full-raw-union.json').read_text())['rows']
BY={x['key']:x for x in ROWS}
controls=model.project_tangents(ROWS,json.loads((ATTR/'tangent-controls.json').read_text()),HERE)
F,dot,mag,norm,cross=model.F,model.dot,model.mag,model.norm,model.cross
# Reuse the existing diagnostic raster, without re-running attribution/audits.
tree=ast.parse((REPO/'tests/projects/lines/attribute_geometry.py').read_text())
exec(compile(ast.Module(body=[x for x in tree.body if isinstance(x,ast.FunctionDef) and x.name=='raster'],type_ignores=[]),'<saved raster>','exec'))

def save(name,data):
 (HERE/name).write_text(json.dumps(data,indent=1,allow_nan=False)+'\n')

def cap_projection(mesh,caps):
 out=copy.deepcopy(mesh);v=np.array(mesh['v'],dtype='f').reshape(-1,3);new=v.copy();tri=np.array(mesh['idx'][0]['i']).reshape(-1,3);newtri=tri.copy();baseline=[]
 for at in (0,len(v)-caps-3):
  center=v[at];radius=v[at+1]-center;r=mag(radius);outward=norm(cross(np.array([0,0,1],dtype='f'),radius))
  theta=F(F(F(math.pi)*F(1))/F(caps+1));offset=v[at+2]-center-radius*F(math.cos(theta))
  if dot(outward,offset)<0:outward=-outward
  back=outward*r
  for k in range(caps+2):
   angle=F(F(F(math.pi)*F(k))/F(caps+1));rad=radius*F(math.cos(angle));b=back*F(math.sin(angle))
   old=center+rad+b;new[at+1+k]=center+rad-b
   baseline.extend((old-v[at+1+k]).tolist())
  hit=tri[:,0]==at;newtri[hit]=tri[hit][:,[0,2,1]]
 out['v']=new.reshape(-1).tolist();out['idx'][0]['i']=newtri.reshape(-1).tolist()
 # The actual source recalculates bounds from every vertex.
 low,high=new.min(axis=0),new.max(axis=0)
 out['bounds']=[((low+high)*F(.5)).tolist(),((high-low)*F(.5)).tolist()]
 return out,baseline

cap_checks=[]
for control in json.loads((ATTR/'cap-controls.json').read_text()):
 k=control['key'];row=BY[k];m=row['raw']['final']['value']
 row['raw']['prepared']=copy.deepcopy(row['raw']['final'])
 if k.startswith('trail.') and '.transformZ.' in k:
  predicted,baseline=cap_projection(m,control['inferred_cap_vertices'])
  row['raw']['prepared']['value']=predicted
  cap_checks.append(dict(key=k,baseline_max_absolute_error=max(map(abs,baseline)),changed=True))
 else:cap_checks.append(dict(key=k,changed=False))
for c in controls:
 row=BY[c['key']];row['raw'].setdefault('prepared',copy.deepcopy(row['raw']['final']))
 if 'baseline' in c:
  actual=np.array(row['raw']['final']['value']['t']);delta=np.array(c['candidate'])-np.array(c['baseline'])
  row['raw']['prepared']['value']['t']=(actual+delta).tolist()
for row in ROWS:row['raw'].setdefault('prepared',copy.deepcopy(row['raw']['final']))
save('cap-projection-controls.json',cap_checks)
# Corner side/arc and per-segment width/gradient rules, each replay first reproducing the saved engine mesh.
strip_checks=strip_projection.project_rows(BY)
save('strip-projection-controls.json',strip_checks)

# Full nine-image union. Keep the exact saved pixels where the raster does not change.
def image(key,base):return np.load(BATCH/'comparisons/line-image-arrays'/f'{key}.{base}.npy').astype(float).reshape(64,96,4)
def raw(key,base):return BY[key]['raw'][base].get('value')
names=['basic','flat','api2','api2Emitting','round','far']+['tex.'+x for x in ['Stretch','Tile','DistributePerSegment','RepeatPerSegment','Static']]+['emit','stop.autodestruct','stop','clear','transformZ','api','apiEmitting','apiLong']
def compose(base):
 canvas=np.zeros((64,96,4))
 for name in names:
  a=raster(raw('trail.f10.'+name+'.end.mesh',base),uv_shader=name.startswith('tex.'));covered=a[:,:,3]!=0;canvas[covered]=a[covered]
 return canvas
oldtrail=compose('final');newtrail=compose('prepared');trail=image('img.trails.f10','final')+(newtrail-oldtrail)
np.save(HERE/'trail-cap-prediction.npy',trail)
imgrows=json.loads((BATCH/'comparisons/lines-images-full-union.json').read_text())
metrics={b:dict(measured={},prepared={}) for b in ('root','blend','final')}
imgdata={};witness=[]
for row in imgrows:
 key=row['key'][:-7];ref=image(key,'reference');old=image(key,'final')
 predicted=np.load(ATTR/'transformz-render-plane-hypothesis.npy') if key=='img.TransformZ.L.c2cap2.color' else trail if key=='img.trails.f10' else old
 np.save(HERE/(key+'.prepared.npy'),predicted)
 imgdata[row['key']]=predicted
 for base in metrics:
  before=image(key,base);be=abs(before-ref)
  for label,actual in [('measured',old),('prepared',predicted)]:
   e=abs(actual-ref);bad=e>2/255;bb=be>2/255
   metrics[base][label][key]=dict(bad_pixels=int(bad.any(axis=2).sum()),bad_channels=int(bad.sum()),introduced_pixels=int((~bb.any(axis=2)&bad.any(axis=2)).sum()),introduced_channels=int((~bb&bad).sum()),raw_error_increases=int((e>be).sum()))
   if label=='prepared':
    np.savez_compressed(HERE/(key+'.'+base+'.errors.npz'),reference=ref,before=before,prepared=actual,before_error=be,prepared_error=e,new_bad_pixels=np.flatnonzero(~bb.any(axis=2)&bad.any(axis=2)),new_bad_channels=np.argwhere((~bb&bad).reshape(-1,4)),raw_error_increases=np.argwhere((e>be).reshape(-1,4)))
 if key=='img.trails.f10':
  z=np.load(BATCH/'comparisons/line-image-arrays'/f'{key}.root-to-final-errors.npz')
  for pix,ch in z['new_bad_channels']:
   y,x=divmod(int(pix),96);witness.append(dict(pixel=int(pix),channel=int(ch),reference=float(ref[y,x,ch]),root=float(image(key,'root')[y,x,ch]),measured=float(old[y,x,ch]),prepared=float(predicted[y,x,ch]),prepared_error=float(abs(ref[y,x,ch]-predicted[y,x,ch])),failing=bool(abs(ref[y,x,ch]-predicted[y,x,ch])>2/255)))
for base,m in metrics.items():
 m['totals']={label:{k:sum(x[k] for x in m[label].values()) for k in next(iter(m[label].values()))} for label in ('measured','prepared')}
save('projected-images.json',metrics);save('eight-trail-channel-disposition.json',witness)
# Every trail-image channel failing in the prediction but not in the measured final image.
ref=image('img.trails.f10','reference');old=image('img.trails.f10','final')
newly=np.argwhere((abs(trail-ref)>2/255)&~(abs(old-ref)>2/255))
save('trail-image-new-vs-measured.json',[dict(pixel=int(y*96+x),channel=int(c),reference=float(ref[y,x,c]),measured=float(old[y,x,c]),prepared=float(trail[y,x,c])) for y,x,c in newly])

# Preserve every row and every original raw value; add prediction, all component
# failures (including absent fields), and every numeric error increase.
def leaves(x,path=''):
 if isinstance(x,dict):
  for k,v in x.items():yield from leaves(v,path+'.'+k)
 elif isinstance(x,list):
  for i,v in enumerate(x):yield from leaves(v,path+f'[{i}]')
 else:yield path,x

def leafmap(raw):return dict(leaves(raw['value'])) if raw['present'] else {}
def numeric(x):return isinstance(x,(float,int)) and not isinstance(x,bool)
def failure(a,b):
 if a['present']!=b['present']:return 'missing' if a['present'] else 'extra'
 if not a['present']:return None
 r=differ(a['value'],b['value'])
 return dict(path=r[0],description=r[1],magnitude=r[2] if math.isfinite(r[2]) else 'Infinity') if r else None
# Use the original whole-value comparator unchanged.
ABS=REL=1e-5
exec(compile(ast.Module(body=[x for x in ast.parse((REPO/'tests/projects/lines/compare.py').read_text()).body if isinstance(x,ast.FunctionDef) and x.name=='differ'],type_ignores=[]),'<original comparator>','exec'))
deltas={base:{state:dict(introduced_numeric_paths=[],changed_retained_numeric_paths=[],all_numeric_worsenings=[]) for state in ('final','prepared')} for base in ('root','blend','final')}
whole={base:{} for base in ('reference','root','blend','final','prepared')}
for row in ROWS:
 key=row['key'];maps={base:leafmap(v) for base,v in row['raw'].items()};ref=maps['reference'];pr=maps['prepared'];component_fail=[]
 for path in sorted(ref.keys()|pr.keys()):
  rp,ap=path in ref,path in pr;x,y=ref.get(path),pr.get(path)
  isnum=numeric(x) and numeric(y);tol=1e-5+1e-5*abs(x) if isnum else None
  if not rp or not ap or (abs(x-y)>tol if isnum else x!=y):component_fail.append(dict(path=path,reference_present=rp,prepared_present=ap,reference=x,prepared=y,tolerance=tol))
 row['prepared_component_failures']=component_fail
 for base in whole:
  failed=failure(row['raw']['reference'],row['raw'][base])
  if key.endswith('.pixels'):
   img=imgdata[key] if base=='prepared' else image(key[:-7],base);bad=np.any(abs(img-image(key[:-7],'reference'))>2/255,axis=2)
   failed={'bad_pixels':int(bad.sum())} if bad.any() else None
  if failed:whole[base][key]=failed
 for base in deltas:
  before=maps[base]
  for state in ('final','prepared'):
   actual=maps[state]
   for path in sorted(ref.keys() & before.keys() & actual.keys()):
    x,b,a=ref[path],before[path],actual[path]
    if not all(numeric(v) for v in (x,b,a)):continue
    be,ae=abs(x-b),abs(x-a);tol=1e-5+1e-5*abs(x)
    r=dict(key=key,path=path,reference=x,before=b,actual=a,before_error=be,actual_error=ae,tolerance=tol)
    if ae>be:deltas[base][state]['all_numeric_worsenings'].append(r)
    if ae>tol and be<=tol:deltas[base][state]['introduced_numeric_paths'].append(r)
    if ae>tol and be>tol and a!=b:deltas[base][state]['changed_retained_numeric_paths'].append(r)
summary=dict(status='UNBUILT/UNACCEPTED CPU prediction, not runtime/GPU closure',required_union=1870,complete_union=len(ROWS),canonical_rows=1869,source=args.source_revision,numeric={base:{state:{k:len(v) for k,v in z.items()} for state,z in states.items()} for base,states in deltas.items()},whole_key_failures={base:len(v) for base,v in whole.items()},whole_key_changes={base:dict(introduced=sorted(whole['prepared'].keys()-whole[base].keys()),resolved=sorted(whole[base].keys()-whole['prepared'].keys())) for base in ('root','blend','final')},images={base:m['totals'] for base,m in metrics.items()},unchanged_missing_extra='All original presence flags preserved in projected-full-raw-union.json',tangent_baseline_model_errors=sum(len(r['baseline_model_errors']) for r in controls),cap_baseline_model_max_error=max(x.get('baseline_max_absolute_error',0) for x in cap_checks),strip_replays=len(strip_checks),strip_changed=sum(x['changed'] for x in strip_checks),strip_baseline_errors=sum(len(x['baseline_errors']) for x in strip_checks))
save('projected-component-deltas.json',deltas);save('projected-whole-failures.json',whole);save('projected-full-raw-union.json',dict(summary=summary,rows=ROWS));save('projection-summary.json',summary)
print(json.dumps({k:summary[k] for k in ('numeric','whole_key_failures','images','tangent_baseline_model_errors','cap_baseline_model_max_error','strip_replays','strip_changed','strip_baseline_errors')},indent=1))
