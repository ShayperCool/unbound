# Diagnostic only: Unity 6000.0.71f1 Linux x86_64, BuildID
# 9a4f6559dbeb9b8aa47d7c970b8858ddda4f717c. Requires Unity_s.debug.
# Offsets were verified against its local disassembly, not assumed from our SDK.
# Stops at constructors and final ShapeInteraction arguments; reads registers and
# memory only. ActorCore+0x2c is the native rigid-body flag word for dynamic actors.
# Canonical continuation/original oracle values must remain exact before using this trace.
set pagination off
set confirm off
set debuginfod enabled off
set print thread-events off
set disable-randomization off
handle SIGSEGV nostop noprint pass
handle SIGPIPE nostop noprint pass
handle SIGPWR nostop noprint pass
handle SIGXCPU nostop noprint pass
handle SIG32 SIG33 SIG34 SIG35 SIG36 SIG37 SIG38 SIG39 SIG40 SIG41 SIG42 SIG43 SIG44 SIG45 SIG46 SIG47 SIG48 SIG49 SIG50 SIG51 SIG52 SIG53 SIG54 SIG55 SIG56 SIG57 SIG58 SIG59 SIG60 SIG61 SIG62 SIG63 SIG64 nostop noprint pass
python
import gdb,struct
seq=0
step=0
def addr(reg): return int(gdb.parse_and_eval('$'+reg))
def u64(p): return struct.unpack('<Q',gdb.selected_inferior().read_memory(p,8))[0]
def u32(p): return struct.unpack('<I',gdb.selected_inferior().read_memory(p,4))[0]
def u8(p): return bytes(gdb.selected_inferior().read_memory(p,1))[0]
def actor(sim):
 core=u64(sim+0x50)
 kind=u8(core+0xd)
 return 'sim=%x rigid=%d kind=%d k=%d' % (sim,u32(sim+0x58),kind,(u32(core+0x2c)&1) if kind==1 else 0)
def shape(s): return 'shape=%x elem=%d %s' % (s,u32(s+0x10)&0x7fffffff,actor(u64(s+8)))
class Trace(gdb.Breakpoint):
 def __init__(self,expr,op,reg=None):
  super().__init__(expr,internal=True);self.op=op;self.reg=reg
 def stop(self):
  global seq,step
  try:
   seq+=1
   if self.op=='pair': msg='shape0 '+shape(addr('r12'))+' shape1 '+shape(addr('r13'))
   elif self.op=='step':
    step+=1;msg='scene=%x'%addr('rdi')
   elif self.op in ['create','destroy']:msg=actor(addr(self.reg))
   elif self.op=='flags-before': msg=shape(addr(self.reg))+' old=%d new=%d'%(u8(addr('rsi')),u8(u64(addr(self.reg)+0x40)+0x40))
   else: msg=shape(addr(self.reg))
   gdb.write('KD_NATIVE %d step=%d %s %s\n'%(seq,step,self.op,msg))
  except Exception as e:gdb.write('KD_TRACE_ERROR '+str(e)+'\n')
  return False
Trace('*_ZN5physx2Sc8RigidSimC1ERNS0_5SceneERNS0_9RigidCoreE+0x43','create','r14')
Trace('_ZN5physx2Sc8RigidSimD1Ev','destroy','rdi')
Trace('_ZN5physx2Sc8ShapeSim34initSubsystemsDependingOnElementIDEv','element','rdi')
Trace('*_ZN5physx2Sc10NPhaseCore22createShapeInteractionERNS0_8ShapeSimES3_NS_7PxFlagsINS_10PxPairFlag4EnumEtEEPNS_17PxsContactManagerEPNS0_16ShapeInteractionE+0x15a','pair')
Trace("_ZN5physx7NpScene8simulateEfPNS_10PxBaseTaskEPvjb","step")
Trace('_ZN5physx2Sc8ShapeSim12onFlagChangeENS_7PxFlagsINS_11PxShapeFlag4EnumEhEE','flags-before','rdi')
Trace('*_ZN5physx2Sc8ShapeSim12onFlagChangeENS_7PxFlagsINS_11PxShapeFlag4EnumEhEE+0x1b3','flags-after','rbp')
end
run
