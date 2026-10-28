"""Draws the AirPodsWin app icon: black squircle, a white window-style mark whose two
upper panes are replaced by an AirPods Pro case lid. Writes AirPodsWin.ico (16-256 px)
and AirPodsWin.png next to this script. Requires Pillow: python make_icon.py"""
from pathlib import Path
from PIL import Image, ImageDraw

S = 2048  # drawn large, then downsampled for clean edges
HERE = Path(__file__).parent
BLACK = (0, 0, 0, 255)
WHITE = (255, 255, 255, 255)


def main():
    icon = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(icon)
    d.rounded_rectangle((0, 0, S - 1, S - 1), radius=int(S * 0.225), fill=(16, 16, 18, 255))
    bg = (16, 16, 18, 255)

    # Window-style mark: the two lower panes stay, the two upper panes are replaced by
    # the lid of an AirPods Pro case (flat top, large rounded corners, flat bottom edge).
    pane, gap = 450, 52
    width = 2 * pane + gap
    lid_h = 350
    left = (S - width) // 2
    top = (S - (lid_h + gap + pane)) // 2

    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    ld.rounded_rectangle((left, top, left + width, top + 2 * lid_h), radius=220, fill=WHITE)
    ld.rectangle((0, top + lid_h, S, S), fill=(0, 0, 0, 0))
    dot = 30  # status light, centred on the case front just above the seam
    ld.ellipse((S // 2 - dot, top + lid_h - 110 - dot, S // 2 + dot, top + lid_h - 110 + dot), fill=bg)
    icon.alpha_composite(layer)

    y = top + lid_h + gap
    for col in range(2):
        x = left + col * (pane + gap)
        d.rounded_rectangle((x, y, x + pane, y + pane), radius=66, fill=WHITE)

    final = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    final.alpha_composite(icon.resize((920, 920), Image.LANCZOS), (52, 52))
    final.save(HERE / "AirPodsWin.png")
    final.save(HERE / "AirPodsWin.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])


if __name__ == "__main__":
    main()
