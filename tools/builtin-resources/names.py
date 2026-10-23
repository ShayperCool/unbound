import struct, sys, json
path = sys.argv[1]
data = open(path, 'rb').read()
exec(open('' + __import__('os').path.join(__import__('os').path.dirname(__file__), 'serialized_file.py') + '').read())
res = []
for pid, start, size, tidx in objs:
    cid, root = types[tidx]
    out = []
    try: walk(root, data_offset + start, out, '')
    except Exception as e: pass
    strings = [(p, v) for p, v in out if isinstance(v, str)]
    name = ''
    for p, v in strings:
        if cid == 48 and p.endswith('m_ParsedForm/#427'): name = v; break
    if not name and strings: name = strings[0][1]
    res.append((pid, cid, name))
for x in res: print('%d\t%d\t%s' % x)
