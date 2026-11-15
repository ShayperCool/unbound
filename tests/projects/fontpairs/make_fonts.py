#!/usr/bin/env python3
"""Build controlled derivatives of the licensed layout font for pair queries."""
from pathlib import Path
from fontTools.ttLib import TTFont,newTable
from fontTools.ttLib.tables._k_e_r_n import KernTable_format_0
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
root=Path(__file__).resolve().parent/'Assets/Fonts';root.mkdir(exist_ok=True)
for name in ['PairOnly','KernOnly','Both','Neither']:
    font=TTFont(root.parents[2]/'fontfeatures/Assets/Fonts/Liberation.ttf')
    for table in ['GPOS','GSUB','GDEF','kern']:
        if table in font:del font[table]
    if name in ['PairOnly','Both']:
        addOpenTypeFeaturesFromString(font, '''
        languagesystem DFLT dflt;
        lookup Single { pos f <1 2 3 4>; } Single;
        lookup Pair { pos A <10 20 -30 40> V <-11 -21 31 -41>; pos T V -54; pos A a 0; } Pair;
        @Left=[A T]; @Right=[V a];
        lookup Classes { pos @Left @Right -63; pos [i l] [f t] -91; } Classes;
        lookup Extended useExtension { pos A V -72; } Extended;
        feature kern { lookup Single; lookup Pair; lookup Classes; lookup Extended; } kern;
        ''')
    if name in ['KernOnly','Both']:
        font['kern']=newTable('kern');font['kern'].version=0
        table=KernTable_format_0();table.version=0;table.coverage=1
        table.kernTable={('A','V'):-177,('T','V'):283,('A','a'):0}
        font['kern'].kernTables=[table]
    for n in font['name'].names:
        if n.nameID in (1,3,4,6,16):n.string=('UnboundLayout'+name).encode(n.getEncoding())
    font.save(root/(name+'.ttf'))
    print(name, 'lookups',len(font['GPOS'].table.LookupList.Lookup) if 'GPOS' in font else 0)
font=TTFont(root/'KernOnly.ttf')
font['kern'].kernTables[0].kernTable.update({('V','A'):199,('A','A'):-33})
for n in font['name'].names:
    if n.nameID in (1,3,4,6,16):n.string='UnboundLayoutReverse'.encode(n.getEncoding())
font.save(root/'Reverse.ttf')
from fontTools.ttLib.tables.otBase import ValueRecord
from fontTools.ttLib.tables.otTables import ClassRangeRecord
from types import MethodType

def encode_class_ranges(definition, font):
    # Keep an explicit zero-class range in the format-2 control.
    definition.Format = 2
    records = []
    for glyph, value in sorted(definition.classDefs.items(), key=lambda item: font.getGlyphID(item[0])):
        record = ClassRangeRecord()
        record.Start = glyph
        record.End = glyph
        record.Class = value
        records.append(record)
    return {'ClassRangeRecord': records}

for name in ['ClassZero','ClassRange']:
    font=TTFont(root/'PairOnly.ttf')
    pair=font['GPOS'].table.LookupList.Lookup[2].SubTable[0]
    if name=='ClassZero':
        pair.ClassDef2.classDefs['o']=0
        pair.ClassDef2.preWrite=MethodType(encode_class_ranges,pair.ClassDef2)
    else:pair.ClassDef2.classDefs={'V':1,'a':2,'f':1,'i':2,'l':1,'o':0,'s':2,'t':1}
    value=ValueRecord(4);value.XAdvance=-123
    pair.Class1Record[0].Class2Record[0].Value1=value
    for n in font['name'].names:
        if n.nameID in (1,3,4,6,16):n.string=('UnboundLayout'+name).encode(n.getEncoding())
    font.save(root/(name+'.ttf'))
font=TTFont(root/'Both.ttf')
font['kern'].kernTables[0].kernTable[('V','T')]=155
for n in font['name'].names:
    if n.nameID in (1,3,4,6,16):n.string='UnboundLayoutFallback'.encode(n.getEncoding())
font.save(root/'Fallback.ttf')
font=TTFont(root/'KernOnly.ttf')
addOpenTypeFeaturesFromString(font, 'languagesystem DFLT dflt; feature kern { pos f <1 2 3 4>; } kern;')
for n in font['name'].names:
    if n.nameID in (1,3,4,6,16):n.string='UnboundLayoutKernWithSingle'.encode(n.getEncoding())
font.save(root/'KernWithSingle.ttf')
