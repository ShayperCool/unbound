import array,json,math,re
from pathlib import Path
from compare import raw

def verify(root,release,rate):
    m=json.loads((root/'capture.json').read_text());assert m['release']==release and m['rate']==rate
    file=root.parent/'project/Assets/Scripts/LifecycleProbe.cs'
    if not file.exists():file=Path(__file__).parent/'Assets/Scripts/LifecycleProbe.cs'
    cases=re.findall(r'"([a-z0-9_]+)"',file.read_text().split('Cases = {')[1].split('};')[0])
    for case in cases:
        for suffix in ('','.master','.listener'):
            record=m[case+suffix];v=array.array('f');v.frombytes(raw(root,record))
            assert len(v)==record['frames']*2 and record['channels']==2 and all(map(math.isfinite,v)),case+suffix
            if suffix!='.listener': assert record['frames']==1024*1024,case+suffix
        assert m[case+'.other32'] and m[case+'.other400']
    for key,record in m.items():
        if isinstance(record,dict) and 'file' in record:
            assert len(raw(root,record))==record['frames']*record['channels']*4,key
    print('Complete reset capture:',root)
