#!/usr/bin/env python3
"""Archive every record and every non-exact value, including passing rounding differences."""
import hashlib,json,os,pathlib,shutil,subprocess,sys,re
from compare import read,matches
from run import PROFILES,ROOT
work=pathlib.Path(os.environ['SCENE_WORK'])
summary=[]; hashes={}; transitions=[]
profiles = [(name,'') for name in PROFILES]
profiles += [(name,'contacts-') for name in ['default','pairs-kk','pairs-ks','pairs-all']]
profiles += [(name,'islands-') for name in ['default','determinism','tgs','tgs-det']]
for profile,prefix in profiles:
    reference=work/(prefix+'native1')/(profile+'.jsonl')
    if not reference.exists(): continue
    ref=read(reference)
    refdir=ROOT/'Reference'/prefix.rstrip('-') if prefix else ROOT/'Reference'
    refdir.mkdir(exist_ok=True)
    shutil.copy2(reference,refdir/reference.name)
    record={'profile':prefix+profile,'records':len(ref)}
    candidates={}
    for phase in ['native1','native2','baseline','final']:
        source=work/(prefix+phase)/(profile+'.jsonl')
        if not source.exists():continue
        dest=ROOT/'Evidence'/(prefix+phase);dest.mkdir(parents=True,exist_ok=True)
        shutil.copy2(source,dest/source.name)
        settings=source.with_suffix('.jsonl.settings')
        if settings.exists():shutil.copy2(settings,dest/settings.name)
        candidate=read(source); keys=sorted(ref.keys()|candidate.keys())
        candidates[phase]=candidate
        diff=[{'key':key,'reference':ref.get(key),'candidate':candidate.get(key)} for key in keys if ref.get(key)!=candidate.get(key)]
        (dest/(profile+'-all-differences.json')).write_text(json.dumps(diff,indent=2)+'\n')
        with (dest/(profile+'-comparison.txt')).open('w') as f:
            result=subprocess.run(['python3',str(ROOT/'compare.py'),str(reference),str(source)],stdout=f).returncode
        summary_line=re.search(r'(\d+)/(\d+) records match; (\d+) differ', (dest/(profile+'-comparison.txt')).read_text())
        record[phase]={'exact':len(keys)-len(diff),'different':len(diff),'matched':int(summary_line[1]),'failed':int(summary_line[3]),'comparison_exit':result}
        hashes[f'{prefix}{phase}/{source.name}']=hashlib.sha256(source.read_bytes()).hexdigest()
    summary.append(record)
    if 'baseline' in candidates and 'final' in candidates:
        baseline,final=candidates['baseline'],candidates['final']
        for key,value in ref.items():
            before,after=baseline.get(key),final.get(key)
            bg,fg=matches(key,value,before),matches(key,value,after)
            if bg != fg or (not fg and before != after):
                transitions.append(dict(profile=prefix+profile,key=key,
                    kind='resolved' if fg else 'introduced' if bg else 'retained-with-changed-values',
                    reference=value,baseline=before,final=after))
(ROOT/'Evidence/summary.json').write_text(json.dumps(summary,indent=2)+'\n')
(ROOT/'Evidence/failure-transitions.json').write_text(json.dumps(transitions,indent=2)+'\n')
for p in (ROOT/'Profiles').glob('*.asset'): hashes['Profiles/'+p.name]=hashlib.sha256(p.read_bytes()).hexdigest()
(ROOT/'Evidence/sha256.json').write_text(json.dumps(hashes,indent=2,sort_keys=True)+'\n')
print(json.dumps(summary,indent=2))

if '--complete' in sys.argv:
    assert len(summary)==len(profiles), 'missing profile recordings'
    for row in summary:
        assert row['records']==(299 if row['profile'].startswith('contacts-') else 227), row
        assert all(phase in row for phase in ['native1','native2','baseline','final']), row
        assert row['native2']['comparison_exit']==0, row
