"""Readers of what the Editor's bake writes (Unity 6000.0.71f1): the unprocessed buffers of a
lightmap, and the bake input and lightmap requests as `UnityEditor.LightBaking.LightBaker`
serializes them (file version 202405161; the fields and their order are those of the built-in
package com.unity.path-tracing's `BakeInputSerialization.cs`, which reads the same files)."""
from array import array
import struct

VERSION = 202405161


def lz4_block(source, size):
    out = bytearray()
    i = 0
    while i < len(source):
        token = source[i]
        i += 1
        length = token >> 4
        if length == 15:
            while True:
                extra = source[i]
                i += 1
                length += extra
                if extra != 255:
                    break
        out += source[i:i + length]
        i += length
        if i >= len(source):
            break
        offset = source[i] | source[i + 1] << 8
        i += 2
        length = token & 15
        if length == 15:
            while True:
                extra = source[i]
                i += 1
                length += extra
                if extra != 255:
                    break
        length += 4
        start = len(out) - offset
        if offset >= length:
            out += out[start:start + length]
        else:
            for k in range(length):
                out.append(out[start + k])
    if len(out) != size:
        raise ValueError(f'a block of {len(out)} bytes, expected {size}')
    return bytes(out)


def read_r2d(path):
    """A `.r2d` buffer: (width, height, channels, floats row by row from the first row). The file
    is 'R2d!', the width and height, eight format bytes (bytes per texel, channels, ...), then
    chunks of rows (compressed flag, first row, rows, byte size), each LZ4-compressed and laid
    out as planes: for each channel, for each byte of its float, the rows."""
    data = open(path, 'rb').read()
    if data[:4] != b'R2d!':
        raise ValueError(f'{path}: not an r2d file')
    width, height = struct.unpack_from('<II', data, 4)
    texel, channels = data[12], data[13]
    if texel != 4 * channels:
        raise ValueError(f'{path}: {texel} bytes for {channels} channels')
    position, rows = 20, 0
    values = array('f')
    while rows < height:
        compressed, first, count, size = struct.unpack_from('<IIII', data, position)
        position += 16
        if first != rows:
            raise ValueError(f'{path}: a chunk at row {first}, expected {rows}')
        block = data[position:position + size]
        position += size
        if compressed == 1:
            block = lz4_block(block, count * width * texel)
        plane = count * width
        interleaved = bytearray(len(block))
        for channel in range(channels):
            for byte in range(4):
                interleaved[channel * 4 + byte::texel] = block[(channel * 4 + byte) * plane:(channel * 4 + byte + 1) * plane]
        values.frombytes(bytes(interleaved))
        rows += count
    return width, height, channels, values


def read_chart_indices(path):
    """`chartIndex<N>.int`: a 64-bit count, then each texel's chart (-1 for none)."""
    data = open(path, 'rb').read()
    count = struct.unpack_from('<q', data)[0]
    values = array('i')
    values.frombytes(data[8:])
    if len(values) != count:
        raise ValueError(f'{path}: {len(values)} values, {count} named')
    return values


class Reader:
    def __init__(self, data):
        self.data, self.position = data, 0

    def take(self, layout):
        values = struct.unpack_from('<' + layout, self.data, self.position)
        self.position += struct.calcsize('<' + layout)
        return values if len(values) > 1 else values[0]

    def boolean(self):
        return self.take('B') != 0

    def values(self, code, per=1):
        count = self.take('Q') * per
        values = array(code)
        size = count * values.itemsize
        values.frombytes(self.data[self.position:self.position + size])
        self.position += size
        return values

    def many(self, read):
        return [read(self) for _ in range(self.take('Q'))]

    def pairs(self, key, read):
        return [(self.take(key), read(self)) for _ in range(self.take('Q'))]

    def text(self):
        return bytes(self.values('B')).decode('ascii')


def _samples(r):
    return dict(zip(('direct', 'indirect', 'environment'), r.take('III')))


def _mesh(r):
    vertex = dict(count=r.take('I'), channels=r.take('i'), dimensions=r.values('I'), offsets=r.values('I'), stride=r.values('I'),
                  data=bytes(r.values('B')))
    return dict(vertex=vertex, indices=r.values('I'), submeshOffsets=r.values('I'), submeshCounts=r.values('I'),
                submeshBounds=r.values('f', 6), uvScaleOffset=r.take('4f'))


def channel(mesh, index):
    """A vertex channel of a bake input's mesh (0 positions, 1 normals, 2 UVs, 3 lightmap UVs):
    a tuple of floats per vertex."""
    vertex = mesh['vertex']
    size, offset, stride = vertex['dimensions'][index], vertex['offsets'][index], vertex['stride'][index]
    return [struct.unpack_from(f'<{size}f', vertex['data'], offset + k * stride) for k in range(vertex['count'])]


def _instance(r):
    return dict(mesh=r.take('i'), terrain=r.take('i'), transform=r.take('16f'), oddNegativeScale=r.boolean(), castShadows=r.boolean(),
                receiveShadows=r.boolean(), lodGroup=r.take('i'), lodMask=r.take('B'), materials=r.values('i'))


def _texture(r):
    width, height = r.take('II')
    return dict(width=width, height=height, data=r.values('f', 4))


def _light(r):
    return dict(color=r.take('3f'), indirectColor=r.take('3f'), orientation=r.take('4f'), position=r.take('3f'), range=r.take('f'),
                cookie=r.take('I'), cookieScale=r.take('f'), coneAngle=r.take('f'), innerConeAngle=r.take('f'), shape0=r.take('f'),
                shape1=r.take('f'), type=r.take('B'), mode=r.take('B'), falloff=r.take('B'), angularFalloff=r.take('B'),
                castsShadows=r.boolean(), shadowMaskChannel=r.take('I'))


def read_bake_input(path):
    r = Reader(open(path, 'rb').read())
    if r.take('Q') != VERSION:
        raise ValueError(f'{path}: not a bake input of version {VERSION}')
    out = dict(
        settings=dict(lightmap=_samples(r), probe=_samples(r), minBounces=r.take('I'), maxBounces=r.take('I'), bakeMode=r.take('i'),
                      mixedMode=r.take('i'), ao=r.boolean(), aoDistance=r.take('f')),
        meshes=r.many(_mesh),
        terrains=r.many(lambda r: dict(heightmap=r.take('I'), holes=r.take('i'), outputResolution=r.take('f'), heightmapScale=r.take('3f'),
                                       uvBounds=r.take('4f'))),
        holes=r.many(lambda r: dict(data=r.values('B'), resolution=r.take('H'))),
        heightmaps=r.many(lambda r: dict(data=r.values('h'), resolution=r.take('H'))),
        materials=r.many(lambda r: dict(doubleSidedGI=r.boolean(), transmissionType=r.take('i'), transmissionChannels=r.take('i'),
                                        alphaCutoff=r.take('f'))),
        instances=r.many(_instance), instanceTextures=r.values('I'), materialTransmissions=r.values('i'),
        albedo=r.many(_texture), emissive=r.many(_texture), transmission=r.many(_texture),
        transmissionProperties=r.many(lambda r: dict(wrapU=r.take('i'), wrapV=r.take('i'), filter=r.take('i'), scaleOffset=r.take('4f'))),
        cookies=r.many(lambda r: dict(width=r.take('I'), height=r.take('I'), pixelStride=r.take('I'), slices=r.take('I'),
                                      repeat=r.boolean(), data=r.values('B'))),
        lights=r.many(_light),
        environment=dict(cubeResolution=r.take('I'), cube=r.values('f', 4), importanceSampleCount=r.take('I'),
                         importanceIntegratedMetric=r.take('f'), importanceDirections=r.values('f', 4),
                         importanceWeightedIntensities=r.values('f', 4), importanceIntensities=r.values('f', 4)))
    if r.position != len(r.data):
        raise ValueError(f'{path}: {len(r.data) - r.position} bytes left')
    return out


def _atlas(r):
    return dict(hash=r.take('QQ'), scene=r.take('QQ'), id=r.take('i'), backfaceTolerance=r.take('f'), supersampling=r.take('i'),
                pushoff=r.take('f'), tag=r.take('i'), maxLightmapCount=r.take('i'))


def _gbuffer(r):
    return dict(instances=r.many(lambda r: dict(object=r.take('QQ'), geometry=r.take('QQ'), scaleOffset=r.take('4f'),
                                                transformIndex=r.take('i'), viewport=r.take('4f'))), atlas=r.take('i'))


def read_lightmap_requests(path):
    r = Reader(open(path, 'rb').read())
    if r.take('Q') != VERSION:
        raise ValueError(f'{path}: not lightmap requests of version {VERSION}')
    atlassing = dict(
        atlases=r.many(_atlas), lightmapped=r.many(_atlas), sizes=r.values('i', 2), atlasOfHash=r.pairs('QQ', lambda r: r.take('i')),
        gbufferOfHash=r.pairs('QQ', lambda r: r.take('QQ')), gbufferHashes=r.values('Q', 2),
        objectsOfHash=r.pairs('QQ', lambda r: r.values('Q', 2)), weights=r.pairs('QQ', lambda r: r.take('f')),
        gbuffers=r.pairs('QQ', _gbuffer),
        instances=r.pairs('QQ', lambda r: dict(atlas=r.take('i'), index=r.take('i'), scaleOffset=r.take('4f'), viewport=r.take('4f'),
                                               width=r.take('f'), height=r.take('f'))),
        offsets=r.values('i'), texels=r.take('d'))
    requests = r.many(lambda r: dict(outputs=r.take('I'), offset=r.take('I'), count=r.take('I'), tiling=r.take('B'), folder=r.text(),
                                     pushoff=r.take('f')))
    if r.position != len(r.data):
        raise ValueError(f'{path}: {len(r.data) - r.position} bytes left')
    return dict(atlassing=atlassing, requests=requests)
