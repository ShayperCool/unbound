"""Float32 replay of the strip's tangent arithmetic on saved lighting controls.

Only diagnostics; it builds no runtime and never consumes Native tangent values
when constructing a tangent. Unaffected streams are retained verbatim.
"""
import json, math
from pathlib import Path
import numpy as np
F=np.float32
def dot(a,b):return F(F(F(a[0]*b[0])+F(a[1]*b[1]))+F(a[2]*b[2]))
def mag(a):return F(math.sqrt(dot(a,a)))
def norm(a):
 m=mag(a);return a/m if m>1e-5 else np.zeros(3,dtype='f')
def cos(a):return F(math.cos(a))
def sin(a):return F(math.sin(a))
def acos(a):return F(math.acos(float(np.clip(a,-1,1))))
def cross(a,b):return np.cross(a,b).astype('f')
def effective(points,width,loop=False):
 ds=np.diff(points,axis=0);lens=np.array([mag(d) for d in ds],dtype='f');zero=np.array([dot(d,d)<1e-5 for d in ds]);dirs=[d/l if l else np.zeros(3,dtype='f') for d,l in zip(ds,lens)]
 def short(j):return F(np.clip(F(F(2)*width/lens[j]-F(1)),0,1)) if j<len(ds) and not zero[j] else F(j<len(ds))
 def ahead(j):return np.zeros(3,dtype='f') if j>=len(ds) else dirs[j] if not zero[j] else dirs[j+1] if j+1<len(ds) else dirs[j-1] if j else np.zeros(3,dtype='f')
 if loop:
  return [next((dirs[(j+k)%len(ds)] for k in range(len(ds)) if not zero[(j+k)%len(ds)]),dirs[j]) for j in range(len(ds))]
 eff=[];chain=np.zeros(3,dtype='f')
 for j in range(len(ds)):
  chain=(eff[j-1] if j else chain) if zero[j] else dirs[0] if j==0 else norm(dirs[j]+chain*short(j))
  e=chain+ahead(j+1)*short(j+1);e=norm(e) if dot(e,e)>0 else eff[j-1] if j else ahead(j+1)
  eff.append(e)
 for j in range(len(ds)-2,-1,-1):
  if dot(eff[j],eff[j])==0:eff[j]=eff[j+1]
 return eff

def tangent_model(points,face,campos,forward,widths,corners,caps,loop=False,normalized=False,transform_z=False):
 points=np.array(points,dtype='f');face=np.array(face,dtype='f');campos=np.array(campos,dtype='f');forward=np.array(forward,dtype='f')
 eff=effective(points,F(max(abs(w) for w in widths)),loop)
 out=[];witness=[]
 def side(d,i):
  s=cross(d,face if transform_z else campos-points[i]);
  if not transform_z:s-=forward*dot(s,forward)
  return norm(s)
 for i,p in enumerate(points):
  hasin=i>0 or loop;hasout=i<len(eff) or loop
  din=eff[i-1] if hasin else np.zeros(3,dtype='f');dout=eff[i%len(eff)] if hasout else np.zeros(3,dtype='f')
  if hasin and hasout and not loop:
   c=dot(din,dout)
   if c>F(.99):
    weight=F(F(1)-c)/F(F(1)-F(.99));e=din*F(F(1)-weight)+dout*weight;dout=norm(e) if dot(e,e)>0 else dout
  if not(hasin and hasout):out.extend([cross(face,side(dout if hasout else din,i))]*2);continue
  s1,s2=side(din,i),side(dout,i)
  if not corners:out.extend([cross(face,s2)]*2);continue
  c=dot(s1,s2);angle=acos(c)
  perp=s2-s1*c;perp=norm(perp) if dot(perp,perp)>F(1e-12) else din
  # The candidate only changes tangent angle; positions keep the measured side arc.
  denom=F(math.sqrt(F(dot(s1,s1)*dot(s2,s2))))
  target=acos(c/denom) if normalized and denom>0 else angle
  witness.append(dict(point=i,din=din.tolist(),dout=dout.tolist(),side_cos=float(c),denominator=float(denom),old_angle=float(angle),new_angle=float(target)))
  for k in range(corners if loop and i==0 else 0,corners+1):
   a=F(target*F(F(k)/F(corners)));r=s1*cos(a)+perp*sin(a);out.extend([F(widths[i])*cross(face,r)]*2)
 if caps:out=[out[0]]*(caps+3)+out+[out[-1]]*(caps+3)
 return np.column_stack((out,np.ones(len(out),dtype='f'))).reshape(-1),witness

def project_tangents(rows, controls, out):
 BY={x['key']:x for x in rows}
 reports=[]
 for row in controls:
  key=row['key'];m=row['raw']['final'];v=np.array(m['v'],dtype='f').reshape(-1,3)
  if not key.startswith('trail.'):
   if key in ('line.light.Tile.zigzag','line.light.local.L'):
    reports.append(dict(key=key,unaffected='no round corners',baseline_model_errors=[],old_bad=[],new_bad=[],introduced_vs_blend=[],candidate=m['t'],witness=[]));continue
   pts=dict(L=[[-1,-.5,0],[1,-.5,0],[1,1,0]],spatial=[[-1,-.5,0],[0,.5,1],[1,-.3,-.5],[1.5,.8,.5]],square=[[-1,-1,0],[1,-1,0],[1,1,0],[-1,1,0],[-1,-1,0]],reverse=[[-1,0,0],[1,0,0],[-1,0,0]])
   shape='spatial' if '.spatial.' in key else 'square' if '.square.' in key else 'reverse' if '.reverse.' in key else 'L'
   cp=np.array([1.5,1.2,-5] if shape=='spatial' else [0,0,-10],dtype='f');cf=norm(np.array([.3,.2,0],dtype='f')-cp) if shape=='spatial' else np.array([0,0,1],dtype='f')
   # LookAt's saved quaternion roundoff is below the numeric tolerance away from coincident directions.
   points=pts[shape];widths=[F(.4)]*len(points);caps=2 if shape in ('L','spatial') else 0;loop=shape=='square'
   opts=dict(loop=loop,transform_z='TransformZ' in key)
   old,w=tangent_model(points,-cf,cp,cf,widths,2,caps,**opts)
   new,wn=tangent_model(points,-cf,cp,cf,widths,2,caps,normalized=True,**opts)
   actual=np.array(m['t']);ref=np.array(row['raw']['reference']['t']);blend=np.array(row['raw']['blend']['t'])
   def bad(a,b):return np.flatnonzero(abs(a-b)>1e-5+1e-5*abs(a)).tolist()
   reports.append(dict(key=key,baseline_model_errors=bad(actual,old),old_bad=bad(ref,actual),new_bad=bad(ref,new),introduced_vs_blend=sorted(set(bad(ref,new))-set(bad(ref,blend))),candidate=new.tolist(),baseline=old.tolist(),witness=wn));continue
  state=BY[key[:-4]+'state']['raw']['final']['value'];p=np.array(state['p'],dtype='f').reshape(-1,3)
  points=np.array([v[0],*p[:0:-1],v[-5]],dtype='f')
  dist=[F(0)]
  for d in np.diff(points,axis=0):dist.append(F(dist[-1]+mag(d)))
  ts=np.array(dist,dtype='f')/dist[-1];widths=F(.3)*(F(1)+F(-.75)*(ts*ts*(F(3)-F(2)*ts)))
  old,w=tangent_model(points,[0,0,-1],[0,0,-10],[0,0,1],widths,2,2)
  new,wn=tangent_model(points,[0,0,-1],[0,0,-10],[0,0,1],widths,2,2,normalized=True)
  actual=np.array(m['t']);ref=np.array(row['raw']['reference']['t']);blend=np.array(row['raw']['blend']['t'])
  def bad(a,b):return np.flatnonzero(abs(a-b)>1e-5+1e-5*abs(a)).tolist()
  record=dict(key=key,baseline_model_errors=bad(actual,old),old_bad=bad(ref,actual),new_bad=bad(ref,new),introduced_vs_blend=sorted(set(bad(ref,new))-set(bad(ref,blend))),candidate=new.tolist(),baseline=old.tolist(),witness=wn)
  reports.append(record)
 print('baseline mismatches',sum(len(r['baseline_model_errors']) for r in reports),'oldbad',sum(len(r['old_bad']) for r in reports),'newbad',sum(len(r['new_bad']) for r in reports),'introduced',sum(len(r['introduced_vs_blend']) for r in reports))
 for r in reports:
  if r['baseline_model_errors'] or r['introduced_vs_blend']:print(r['key'],'model',r['baseline_model_errors'],'introduced',r['introduced_vs_blend'])
 (out/'tangent-model-results.json').write_text(json.dumps(reports,indent=2)+'\n')
 return reports
