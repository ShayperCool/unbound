set pagination off
set confirm off
set debuginfod enabled off
set disable-randomization off
set print thread-events off
handle SIGPWR nostop noprint pass
handle SIGXCPU nostop noprint pass
handle SIG35 nostop noprint pass
handle SIG36 nostop noprint pass
handle SIG37 nostop noprint pass
handle SIG38 nostop noprint pass
python
import gdb, os, json, struct
from pathlib import Path
editor=Path(os.environ['DYNAMICS_TRACE_EDITOR'])
gdb.execute('file '+json.dumps(str(editor.with_name('Unity_s.debug'))))
gdb.execute('exec-file '+json.dumps(str(editor)))
out=open(os.environ['DYNAMICS_TRACE_OUT'],'w')
steps={}
def memory(p, fmt):
 return struct.unpack('<'+fmt, gdb.selected_inferior().read_memory(p,struct.calcsize('<'+fmt)))[0]
def reg(s): return int(gdb.parse_and_eval('$'+s))
def ptr(p): return memory(p,'Q')
def body(p):
 return dict(ptr=hex(p),type=memory(p,'i'),index=memory(p+8,'i'),x=memory(p+12,'f'),y=memory(p+16,'f'))
current=None
class Step(gdb.Breakpoint):
 def stop(self):
  global current
  current=reg('rdi');steps[current]=steps.get(current,0)+1
  return False
class Island(gdb.Breakpoint):
 def stop(self):
  p=reg('rdi');nb=memory(p+0x40,'i');nc=memory(p+0x48,'i')
  assert 0<=nb<1000 and 0<=nc<1000
  bodies=[body(ptr(ptr(p+0x18)+8*i)) for i in range(nb)]
  contacts=[]
  for i in range(nc):
   c=ptr(ptr(p+0x20)+8*i);a=ptr(c+0x60);b=ptr(c+0x68)
   contacts.append(dict(ptr=hex(c),fixtureA=hex(a),fixtureB=hex(b),bodyA=body(ptr(a+0x20)),bodyB=body(ptr(b+0x20)),points=memory(c+0xbc,'i')))
  out.write(json.dumps(dict(world=hex(current),step=steps[current],bodies=bodies,contacts=contacts))+'\n');out.flush()
  return False
Step('_ZN5Box2D7b2World4StepEfiiii',internal=True)
Island('_ZN5Box2D8b2Island5SolveERKNS_10b2TimeStepERKNS_6b2Vec2Eb',internal=True)
end
run
