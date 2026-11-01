"""The shared strip/cap arithmetic on the eight authored controls, not an engine capture."""
import argparse, json, math, sys
from pathlib import Path
import numpy as np
import tangent_projection as model
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--controls',type=Path,required=True)
parser.add_argument('--out',type=Path,required=True)
args=parser.parse_args()
sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path
P=cache_artifact_path(args.out.resolve());P.mkdir(parents=True,exist_ok=True)
F,dot,mag,norm,cross=model.F,model.dot,model.mag,model.norm,model.cross
controls=json.loads(args.controls.read_text())
inputs={x['k'][:-7]:x['v'] for x in controls if x['k'].endswith('.inputs')};results=[]
for row in controls:
 if not row['k'].endswith('.mesh'):continue
 key=row['k'][:-5];i=inputs[key];trail='.trail.' in key;tz=i['alignment']=='Local';caps=i['caps'];p=np.array(i['p'],dtype='f').reshape(-1,3)
 if trail:p=np.array([i['objectPosition'],*p[::-1]],dtype='f')
 dirs=model.effective(p,F(0 if trail else i['width']));dist=[F(0)]
 for d in np.diff(p,axis=0):dist.append(F(dist[-1]+mag(d)))
 width=F(0 if trail else i['width']);v=[];t=[];uv=[];indices=[]
 sides=[]
 for j,point in enumerate(p):
  d=dirs[min(j,len(dirs)-1)]
  side=cross(d,np.array([0,0,-1],dtype='f') if tz else np.array(i['cameraPosition'],dtype='f')-point)
  if not tz:side[2]=0
  sides.append(norm(side))
 def add(pos,tan,u,vv):v.append(pos);t.append([*tan,F(1)]);uv.append([u,vv])
 def cap(j,start,reverse):
  at=len(v);center=p[j];radius=sides[j]*F(width*F(.5));r=mag(radius);out=norm(cross(np.array([0,0,1],dtype='f'),radius))
  if dot(out,dirs[0 if start else -1])<0:out=-out
  if reverse:out=-out
  back=out*F(-r if start else r);tan=cross(np.array([0,0,-1],dtype='f'),sides[j]);u=F(dist[j]/dist[-1])
  add(center,tan,u,F(.5))
  for k in range(caps+2):
   angle=F(F(F(math.pi)*F(k))/F(caps+1));add(center+radius*F(math.cos(angle))+back*F(math.sin(angle)),tan,u,F(F(k)/F(caps+1)))
  for k in range(caps+1):indices.extend([at,at+2+k,at+1+k] if start!=reverse else [at,at+1+k,at+2+k])
 cap(0,True,trail and tz);first=len(v)
 for j,point in enumerate(p):
  tan=cross(np.array([0,0,-1],dtype='f'),sides[j]);u=F(dist[j]/dist[-1]);h=F(width*F(.5));add(point-sides[j]*h,tan,u,F(1));add(point+sides[j]*h,tan,u,F(0))
 for j in range(1,len(p)):
  at=first+2*j-2;indices.extend([at,at+1,at+2,at+1,at+3,at+2])
 cap(-1,False,trail and tz)
 va=np.array(v,dtype='f');low,high=va.min(axis=0),va.max(axis=0)
 # Layout and streams are those written unconditionally by the shared source builder.
 pred=dict(vc=len(v),sub=1,attrs=['Position:Float32:3:0','Normal:Float32:3:0','Tangent:Float32:4:0','Color:UNorm8:4:0','TexCoord0:Float32:2:0'],v=va.reshape(-1).tolist(),n=np.tile([0,0,-1],len(v)).tolist(),t=np.array(t,dtype='f').reshape(-1).tolist(),c=[1]*len(v)*4,uv0=np.array(uv,dtype='f').reshape(-1).tolist(),idx=[dict(topo='Triangles',i=indices)],bounds=[((low+high)*F(.5)).tolist(),((high-low)*F(.5)).tolist()])
 def leaves(x,path=''):
  if isinstance(x,dict):
   for k,vv in x.items():yield from leaves(vv,path+'.'+k)
  elif isinstance(x,list):
   for j,vv in enumerate(x):yield from leaves(vv,path+f'[{j}]')
  else:yield path,x
 a,b=dict(leaves(row['v'])),dict(leaves(pred));errors=[]
 for path in a.keys()|b.keys():
  x,y=a.get(path),b.get(path);isnum=isinstance(x,(int,float)) and isinstance(y,(int,float));tol=1e-5+1e-5*abs(x) if isnum else None
  if path not in a or path not in b or (abs(x-y)>tol if isnum else x!=y):errors.append(dict(path=path,reference=x,prepared=y,tolerance=tol))
 results.append(dict(key=row['k'],inputs=i,reference=row['v'],prepared=pred,failures=errors))
(P/'new-control-projection.json').write_text(json.dumps(results,indent=1)+'\n');print('New controls predicted:',len(results),'meshes,',sum(len(x['failures']) for x in results),'component failures')
for r in results:
 if r['failures']:print(r['key'],r['failures'][:3])
