#!/usr/bin/env python3
"""Generate a compact class table representing 100 million nonzero glyph pairs."""
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
root = Path(__file__).resolve().parent
font = TTFont(root/'Assets/Fonts/Neither.ttf')
order = list(font.getGlyphOrder())
left = ['largeL'+str(i) for i in range(10000)]
right = ['largeR'+str(i) for i in range(10000)]
for glyph in left+right:
    font['glyf'][glyph] = TTGlyphPen(None).glyph()
    font['hmtx'][glyph] = (500, 0)
font.setGlyphOrder(order+left+right)
addOpenTypeFeaturesFromString(font, 'languagesystem DFLT dflt; @L=['+' '.join(left)+']; @R=['+' '.join(right)+']; feature kern { pos @L @R -7; } kern;')
for record in font['name'].names:
    if record.nameID in (1,3,4,6,16):
        record.string = 'UnboundLayoutLargeClasses'.encode(record.getEncoding())
out = root/'Fixtures/LargeClasses.ttf'
out.parent.mkdir(exist_ok=True)
font.save(out)
print(out, out.stat().st_size)
