#!/usr/bin/env python3
"""Build the controlled attachment font from the shared licensed font subset."""
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
ROOT = Path(__file__).resolve().parent
font = TTFont(ROOT.parent/'fontfeatures/Assets/Fonts/Liberation.ttf')
for tag in ('GPOS', 'GDEF', 'kern'):
    if tag in font: del font[tag]
fea = '''languagesystem DFLT dflt;
markClass ACUTE <anchor -50 100> @TOP;
markClass GRAVE <anchor 20 120> @TOP;
markClass CEDILLA <anchor 0 -80> @BOTTOM;
lookup Base { pos base A <anchor 300 700> mark @TOP <anchor 200 -100> mark @BOTTOM;
pos base f <anchor 100 600> mark @TOP; } Base;
lookup BaseTwo useExtension { pos base A <anchor 333 777> mark @TOP; } BaseTwo;
lookup Mark useExtension { pos mark ACUTE <anchor -20 350> mark @TOP; pos mark GRAVE <anchor 30 400> mark @TOP; } Mark;
feature mark { lookup Base; lookup BaseTwo; } mark;
feature mkmk { lookup Mark; } mkmk;
'''
for token, code in [('ACUTE', 0x301), ('GRAVE', 0x300), ('CEDILLA', 0x327)]:
    fea = fea.replace(token, font.getBestCmap()[code])
addOpenTypeFeaturesFromString(font, fea, tables=['GPOS', 'GDEF'])
for n in font['name'].names:
    if n.nameID in (1, 3, 4, 6, 16): n.string = 'UnboundLayoutMarks'.encode(n.getEncoding())
font.save(ROOT/'Assets/Fonts/Marks.ttf')
