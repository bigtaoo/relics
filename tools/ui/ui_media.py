# Review media for the UI slice (design/08 §3) from Automatic.Editor.Batch.UiSlice output
# (artifacts/ui/): the stills per aspect ratio as JPEG, the hu and battle-start sequences as
# GIFs and contact sheets with timestamps, all into art/ui/.
# Usage: python tools/ui/ui_media.py [artifacts/ui] [art/ui]
import pathlib
import subprocess
import sys

from PIL import Image, ImageDraw

FPS = 30
SHEET_COLS, SHEET_ROWS, THUMB_W = 4, 3, 480
START_AT = 4.4 + 1 / FPS  # the battle start continues the hu timeline (UiSlice.End)


def main(src="artifacts/ui", dst="art/ui"):
    src, dst = pathlib.Path(src), pathlib.Path(dst)
    dst.mkdir(parents=True, exist_ok=True)
    for still in sorted(src.glob("*.png")):
        Image.open(still).convert("RGB").save(dst / f"shop_{still.stem}.jpg", quality=88)
    sequence(src / "hu", dst / "hu_show.gif", dst / "hu_sheet.jpg", 0)
    sequence(src / "start", dst / "battle_start.gif", dst / "start_sheet.jpg", START_AT)
    print("->", dst)


def sequence(seq, gif, sheet_file, offset):
    graph = ("fps=20,scale=800:-1:flags=lanczos,split[a][b];"
             "[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS), "-i", str(seq / "frame_%03d.png"),
                    "-filter_complex", graph, "-loop", "0", str(gif)], check=True)
    frames = sorted(seq.glob("frame_*.png"))
    n = SHEET_COLS * SHEET_ROWS
    picks = [frames[round(i * (len(frames) - 1) / (n - 1))] for i in range(n)]
    th = round(Image.open(picks[0]).height * THUMB_W / Image.open(picks[0]).width)
    sheet = Image.new("RGB", (SHEET_COLS * THUMB_W, SHEET_ROWS * th))
    draw = ImageDraw.Draw(sheet)
    for i, p in enumerate(picks):
        x, y = i % SHEET_COLS * THUMB_W, i // SHEET_COLS * th
        sheet.paste(Image.open(p).convert("RGB").resize((THUMB_W, th), Image.LANCZOS), (x, y))
        draw.rectangle((x, y, x + 64, y + 22), fill="black")
        draw.text((x + 6, y + 5), f"{offset + int(p.stem.split('_')[1]) / FPS:.2f}s", fill="white")
    sheet.save(sheet_file, quality=88)


if __name__ == "__main__":
    main(*sys.argv[1:])
