#!/usr/bin/env python3
"""Build the synthetic COLRv0 fixture with fonttools 4.59.0."""
from pathlib import Path

from fontTools.colorLib.builder import buildCOLR, buildCPAL
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib.tables.ttProgram import Program

FIXTURE_TIMESTAMP = 3660681600  # 2020-01-01 in the TrueType epoch.


def outline(*points):
    pen = TTGlyphPen(None)
    if points:
        pen.moveTo(points[0])
        for point in points[1:]:
            pen.lineTo(point)
        pen.closePath()
    return pen.glyph()


def make_font(family, file, instructions):
    # A has two overlapping layers with an overhang and a transparent palette entry.
    # B has a regular outline but shares the face, metrics and colour atlas.
    glyphs = {
        ".notdef": outline(),
        "space": outline(),
        "A": outline((80, 0), (80, 700), (640, 700), (640, 0)),
        "B": outline((80, -40), (130, 640), (510, 580), (570, -40)),
        "base": outline((40, -80), (40, 720), (520, 720), (520, -80)),
        "overlay": outline((320, 120), (560, 750), (820, 270)),
    }
    # Move the first point two pixels down when hinting runs, including a point on
    # the bottom edge of a colour layer. The two raster modes can yield different bounds.
    if instructions:
        for glyph in ("B", "base"):
            program = Program()
            program.fromAssembly(["SVTCA[0]", "PUSHW[ ]", "0", "-128", "SHPIX[ ]"])
            glyphs[glyph].program = program
    builder = FontBuilder(1000, isTTF=True)
    builder.setupGlyphOrder(list(glyphs))
    builder.setupCharacterMap({32: "space", 65: "A", 66: "B"})
    builder.setupGlyf(glyphs)
    if instructions:
        builder.font["maxp"].maxStackElements = 16
        builder.font["maxp"].maxSizeOfInstructions = len(glyphs["B"].program.getBytecode())
    builder.setupHorizontalMetrics({
        glyph: (350 if glyph == "space" else 740, getattr(outline_glyph, "xMin", 0))
        for glyph, outline_glyph in glyphs.items()
    })
    builder.setupHorizontalHeader(ascent=800, descent=-200)
    builder.setupNameTable({
        "familyName": family,
        "styleName": "Regular",
        "uniqueFontIdentifier": f"{family} Regular",
        "fullName": f"{family} Regular",
        "psName": f"{file}-Regular",
    })
    builder.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    builder.setupPost()
    builder.font["COLR"] = buildCOLR({"A": [("base", 0), ("overlay", 1)]}, version=0)
    builder.font["CPAL"] = buildCPAL([[(0.9, 0.2, 0.1, 1.0), (0.1, 0.6, 0.9, 0.5)]])
    builder.font.recalcTimestamp = False
    builder.font["head"].created = builder.font["head"].modified = FIXTURE_TIMESTAMP
    builder.save(Path(__file__).resolve().parent / "Assets/Resources/Fonts" / f"{file}.ttf")


if __name__ == "__main__":
    make_font("Layer Probe", "LayerProbe", instructions=False)
    make_font("Hinted Layer Probe", "HintedLayerProbe", instructions=True)
