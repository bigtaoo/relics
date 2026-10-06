# Turns the frame sequences rendered by Automatic.Editor.Batch.FxSlice (artifacts/fx/<seq>/)
# into review media in art/fx/: a GIF per camera (ffmpeg, palette per clip) and a contact sheet
# of evenly spaced close-up frames with timestamps.
# Usage: python tools/art/fx_media.py [artifacts/fx] [art/fx]
import pathlib
import subprocess
import sys

from PIL import Image, ImageDraw

FPS = 30
SHEET_COLS, SHEET_ROWS, THUMB_W = 4, 3, 480


def gif(frames_dir, prefix, out, width, fps):
    pattern = str(frames_dir / f"{prefix}_%03d.png")
    graph = (f"fps={fps},scale={width}:-1:flags=lanczos,split[a][b];"
             "[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS), "-i", pattern,
                    "-filter_complex", graph, "-loop", "0", str(out)], check=True)


def sheet(frames_dir, out):
    frames = sorted(frames_dir.glob("close_*.png"))
    n = SHEET_COLS * SHEET_ROWS
    picks = [frames[round(i * (len(frames) - 1) / (n - 1))] for i in range(n)]
    first = Image.open(picks[0])
    th = round(first.height * THUMB_W / first.width)
    img = Image.new("RGB", (SHEET_COLS * THUMB_W, SHEET_ROWS * th), "black")
    draw = ImageDraw.Draw(img)
    for i, p in enumerate(picks):
        x, y = i % SHEET_COLS * THUMB_W, i // SHEET_COLS * th
        img.paste(Image.open(p).convert("RGB").resize((THUMB_W, th), Image.LANCZOS), (x, y))
        t = int(p.stem.split("_")[1]) / FPS
        draw.rectangle((x, y, x + 64, y + 22), fill="black")
        draw.text((x + 6, y + 5), f"{t:.2f}s", fill="white")
    img.save(out)


def main(src="artifacts/fx", dst="art/fx"):
    src, dst = pathlib.Path(src), pathlib.Path(dst)
    dst.mkdir(parents=True, exist_ok=True)
    for seq in sorted(p for p in src.iterdir() if p.is_dir()):
        gif(seq, "close", dst / f"{seq.name}_close.gif", 640, 30)
        gif(seq, "full", dst / f"{seq.name}_board.gif", 640, 15)
        sheet(seq, dst / f"{seq.name}_sheet.png")
        print(seq.name, "->", dst)


if __name__ == "__main__":
    main(*sys.argv[1:])
