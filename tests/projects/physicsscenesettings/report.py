#!/usr/bin/env python3
"""Archive every record and every non-exact value, including passing rounding differences."""
import hashlib,json,os,pathlib,shutil,subprocess
from compare import read
from run import PROFILES,ROOT
work=pathlib.Path(os.environ['SCENE_WORK'])
summary=[]; hashes={}
for profile in PROFILES:
    reference=work/'native1'/(profile+'.jsonl')
    if not reference.exists(): continue
    ref=read(reference)
    (ROOT/'Reference').mkdir(exist_ok=True)
    shutil.copy2(reference,ROOT/'Reference'/reference.name)
    record={'profile':profile,'records':len(ref)}
    for phase in ['native1','native2','baseline','final']:
        source=work/phase/(profile+'.jsonl')
        if not source.exists():continue
        dest=ROOT/'Evidence'/phase;dest.mkdir(parents=True,exist_ok=True)
        shutil.copy2(source,dest/source.name)
        settings=source.with_suffix('.jsonl.settings')
        if settings.exists():shutil.copy2(settings,dest/settings.name)
        candidate=read(source); keys=sorted(ref.keys()|candidate.keys())
        diff=[{'key':key,'reference':ref.get(key),'candidate':candidate.get(key)} for key in keys if ref.get(key)!=candidate.get(key)]
        (dest/(profile+'-all-differences.json')).write_text(json.dumps(diff,indent=2)+'\n')
        with (dest/(profile+'-comparison.txt')).open('w') as f:
            result=subprocess.run(['python3',str(ROOT/'compare.py'),str(reference),str(source)],stdout=f).returncode
        record[phase]={'exact':len(keys)-len(diff),'different':len(diff),'comparison_exit':result}
        hashes[f'{phase}/{source.name}']=hashlib.sha256(source.read_bytes()).hexdigest()
    summary.append(record)
(ROOT/'Evidence/summary.json').write_text(json.dumps(summary,indent=2)+'\n')
for p in (ROOT/'Profiles').glob('*.asset'): hashes['Profiles/'+p.name]=hashlib.sha256(p.read_bytes()).hexdigest()
(ROOT/'Evidence/sha256.json').write_text(json.dumps(hashes,indent=2,sort_keys=True)+'\n')
print(json.dumps(summary,indent=2))
