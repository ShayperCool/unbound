#!/usr/bin/env python3
"""Make a controlled OFL single-positioning derivative of the shared font subset."""
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
ROOT = Path(__file__).resolve().parent
font = TTFont(ROOT.parent/'fontfeatures/Assets/Fonts/Liberation.ttf')
for tag in ('GPOS', 'GDEF', 'kern'):
    if tag in font: del font[tag]
addOpenTypeFeaturesFromString(font, '''languagesystem DFLT dflt;
lookup Single {pos A <11 -12 13 -14>; pos f <-21 22 -23 24>;} Single;
lookup Uniform useExtension {pos [i l] <7 8 9 10>;} Uniform;
lookup Zero {pos t <0 0 0 0>;} Zero;
lookup Pair {pos A V -20;} Pair;
feature dist {lookup Single;lookup Uniform;lookup Zero;} dist;
feature kern {lookup Pair;} kern;
''', tables=['GPOS', 'GDEF'])
for n in font['name'].names:
    if n.nameID in (1, 3, 4, 6, 16): n.string = 'UnboundLayoutSingles'.encode(n.getEncoding())
font.save(ROOT/'Assets/Fonts/Singles.ttf')
