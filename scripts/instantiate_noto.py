#!/usr/bin/env python3
"""Instantiate static Noto Sans SC weights from the official variable font.

The upstream licence (google/fonts ofl/notosanssc/OFL.txt) is SIL OFL 1.1 with
"Reserved Font Name 'Source'". The generated static instances keep the family
name "Noto Sans SC", which does not contain the reserved name, so the OFL
reserved-name clause is respected. The family is never renamed to include
"Source". See src/CustomsClearanceConsole/fonts/FONTS.md for the full note.
"""

import argparse
import json
import os
import sys

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

WEIGHTS = [("Regular", 400), ("Medium", 500), ("Bold", 700)]


def verify(path, weight):
    font = TTFont(path)
    try:
        if "fvar" in font:
            raise SystemExit(f"{path}: still variable (fvar present)")
        actual = font["OS/2"].usWeightClass
        if actual != weight:
            raise SystemExit(f"{path}: usWeightClass={actual} expected {weight}")
        family = font["name"].getDebugName(1)
        subfamily = font["name"].getDebugName(2)
        if family is None or "Source" in family:
            raise SystemExit(f"{path}: family name '{family}' uses the OFL reserved name")
        return family, subfamily
    finally:
        font.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--variable", required=True, help="official variable TTF")
    parser.add_argument("--output", required=True, help="directory for static TTFs")
    parser.add_argument("--manifest", required=True, help="manifest JSON path")
    args = parser.parse_args()

    if not os.path.isfile(args.variable):
        raise SystemExit(f"variable font not found: {args.variable}")
    os.makedirs(args.output, exist_ok=True)

    source = TTFont(args.variable)
    try:
        if "fvar" not in source:
            raise SystemExit("input font is not variable (no fvar table)")
    finally:
        source.close()

    instances = []
    for name, weight in WEIGHTS:
        font = TTFont(args.variable)
        instancer.instantiateVariableFont(font, {"wght": weight}, inplace=True, updateFontNames=True)
        if "fvar" in font:
            font.close()
            raise SystemExit(f"{name}: instancing left an fvar table")
        out = os.path.join(args.output, f"NotoSansSC-{name}.ttf")
        font.save(out)
        font.close()
        family, subfamily = verify(out, weight)
        instances.append({
            "file": os.path.basename(out),
            "requestedWeight": weight,
            "usWeightClass": weight,
            "family": family,
            "subfamily": subfamily,
            "bytes": os.path.getsize(out),
        })
        print(f"instantiated {out} · {family}/{subfamily} · wght={weight}")

    with open(args.manifest, "w", encoding="utf-8") as handle:
        json.dump({
            "source": os.path.basename(args.variable),
            "tool": "fontTools.varLib.instancer",
            "reservedFontName": "Source (not used by the generated family name)",
            "instances": instances,
        }, handle, ensure_ascii=False, indent=2)
    print(json.dumps(instances, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    sys.exit(main())
