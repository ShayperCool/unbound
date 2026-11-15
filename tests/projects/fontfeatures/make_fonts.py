#!/usr/bin/env python3
"""Make small renamed derivatives of the licensed probe fonts, with their layout tables."""
from pathlib import Path
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
ROOT = Path(__file__).resolve().parent
for name, source in [('Liberation', '/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf'),
                     ('DejaVu', '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf')]:
    font = TTFont(source)
    options = subset.Options()
    options.layout_features = ['*']
    options.name_IDs = ['*']
    sub = subset.Subsetter(options=options)
    sub.populate(text=' AafilstAVTo\u0300\u0301\u0302\u0308\u0323\u0327\ufb01\ufb02')
    sub.subset(font)
    for n in font['name'].names:
        if n.nameID in (1, 3, 4, 6, 16): n.string = ('UnboundLayoutSans' if name == 'Liberation' else 'UnboundLayoutExtended').encode(n.getEncoding())
    font.save(ROOT/'Assets/Fonts'/f'{name}.ttf')
font = TTFont(ROOT/'Assets/Fonts/Liberation.ttf')
for table in ('GSUB', 'GPOS', 'GDEF', 'kern'):
    if table in font: del font[table]
addOpenTypeFeaturesFromString(font, '''
languagesystem DFLT dflt;
lookup Other { sub A by V; } Other;
lookup First { sub f f i by fi; sub f i by fi; sub f l by fl; sub A V by fi; } First;
lookup Second useExtension { sub f i by fl; sub A V by fl; } Second;
feature liga { lookup First; } liga;
feature dlig { lookup Second; } dlig;
'''.replace(' fi', ' ' + font.getBestCmap()[0xfb01]).replace(' fl', ' ' + font.getBestCmap()[0xfb02]))
for n in font['name'].names:
    if n.nameID in (1, 3, 4, 6, 16): n.string = 'UnboundLayoutControl'.encode(n.getEncoding())
font.save(ROOT/'Assets/Fonts/Controlled.ttf')
