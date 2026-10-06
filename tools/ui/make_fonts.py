"""Static UI fonts from Noto Serif SC (SIL OFL 1.1), design/08 §3.

The system copy is a variable font whose default instance is ExtraLight (wght 200), which is
what Unity's font engine would load. This pins two weights and subsets them to GB2312 (6763
common hanzi) plus ASCII and CJK punctuation, so each file stays small enough to ship in a
hot-update bundle and still covers dynamic text such as player names.

    python tools/ui/make_fonts.py [path/to/NotoSerifSC-VF.ttf]
"""
import sys
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

SOURCE = Path(sys.argv[1] if len(sys.argv) > 1 else r"C:\Windows\Fonts\NotoSerifSC-VF.ttf")
OUT = Path(__file__).resolve().parents[2] / "client/Assets/HotRes/Ui/Fonts"
WEIGHTS = {"ui_serif_body": 600, "ui_serif_title": 900}


def charset() -> str:
    chars = {chr(c) for c in range(0x20, 0x7F)}
    chars |= set("，。、；：？！“”‘’（）《》【】〈〉「」『』…—～·￥×÷＋－％")
    for hi in range(0xA1, 0xF8):
        for lo in range(0xA1, 0xFF):
            try:
                chars.add(bytes([hi, lo]).decode("gb2312"))
            except UnicodeDecodeError:
                pass
    return "".join(sorted(chars))


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    text = charset()
    options = subset.Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]  # keep the copyright and OFL licence strings
    options.notdef_outline = True
    for name, weight in WEIGHTS.items():
        font = TTFont(SOURCE)
        sub = subset.Subsetter(options)
        sub.populate(text=text)
        sub.subset(font)
        font = instancer.instantiateVariableFont(font, {"wght": weight})
        path = OUT / f"{name}.ttf"
        font.save(path)
        print(f"{path.name}: wght {weight}, {len(text)} chars, {path.stat().st_size / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
