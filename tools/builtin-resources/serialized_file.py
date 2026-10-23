off = 48
def r(fmt):
    global off
    v = struct.unpack_from('<' + fmt, data, off); off += struct.calcsize(fmt); return v if len(v) > 1 else v[0]
def cstr():
    global off
    end = data.index(b'\0', off); s = data[off:end].decode('utf8', 'replace'); off = end + 1; return s
data_offset = struct.unpack_from('>Q', data, 32)[0]
unity = cstr(); platform = r('i'); tree = data[off]; off += 1
ntypes = r('i'); types = []
for t in range(ntypes):
    cid = r('i'); off += 1; r('h')
    if cid == 114 or cid < 0: off += 16
    off += 16
    nn = r('i'); sb = r('i'); nodes = []
    for i in range(nn):
        ver, lvl, flags, toff, noff, size, idx, mflag, refh = struct.unpack_from('<HBBIIiiiQ', data, off); off += 32
        nodes.append({'lvl': lvl, 'flags': flags, 'size': size, 'meta': mflag, 'noff': noff, 'children': []})
    strs = data[off:off+sb]; off += sb
    dc = r('i'); off += 4 * dc
    stack = []
    for n in nodes:
        while stack and stack[-1]['lvl'] >= n['lvl']: stack.pop()
        if stack: stack[-1]['children'].append(n)
        stack.append(n)
        n['name'] = strs[n['noff']:strs.index(b'\0', n['noff'])].decode() if not (n['noff'] & 0x80000000) else '#%d' % (n['noff'] & 0x7fffffff)
    types.append((cid, nodes[0]))
nobj = r('i')
while off % 4: off += 1
objs = []
for i in range(nobj):
    while off % 4: off += 1
    pid, start, size, tidx = struct.unpack_from('<qqIi', data, off); off += 24
    objs.append((pid, start, size, tidx))

def walk(node, pos, out, path):
    kids = node['children']
    if node['flags'] & 1:  # array: children = [size, data]
        n = struct.unpack_from('<i', data, pos)[0]; pos += 4
        elem = kids[1]
        if not elem['children'] and elem['size'] == 1:
            raw = data[pos:pos+n]; pos += n
            out.append((path, raw))
        else:
            for i in range(n):
                pos = walk(elem, pos, out, path + '[%d]' % i)
    elif kids:
        if len(kids) == 1 and kids[0]['flags'] & 1 and not kids[0]['children'][1]['children'] and kids[0]['children'][1]['size'] == 1:
            n = struct.unpack_from('<i', data, pos)[0]; pos += 4
            out.append((path + '/' + node['name'], data[pos:pos+n].decode('utf8', 'replace'))); pos += n
            if kids[0]['meta'] & 0x4000 or node['meta'] & 0x4000:
                while pos % 4: pos += 1
            return pos
        for k in kids:
            pos = walk(k, pos, out, path + '/' + node['name'])
    else:
        pos += max(node['size'], 0)
    if node['meta'] & 0x4000:
        while pos % 4: pos += 1
    return pos
