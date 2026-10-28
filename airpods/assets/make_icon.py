"""Draws the AirPodsWin app icon: black squircle, a white four-pane window with the
top of an AirPods case (lid closed) sitting on it. Writes AirPodsWin.ico (16-256 px)
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

    # Four-pane window
    pane, gap = 380, 44
    left = (S - (2 * pane + gap)) // 2
    top = 772
    for row in range(2):
        for col in range(2):
            x = left + col * (pane + gap)
            y = top + row * (pane + gap)
            d.rounded_rectangle((x, y, x + pane, y + pane), radius=56, fill=WHITE)

    # Upper part of an AirPods case resting on the window: wide and low, flat top with
    # rounded corners, lid seam and status light. A dark outline separates it from the panes.
    cx, cw = S // 2, 980
    case_top, case_cut, outline = 470, 860, 44
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    ld.rounded_rectangle((cx - cw // 2 - outline, case_top - outline, cx + cw // 2 + outline, case_top + 900),
                         radius=190 + outline, fill=bg)
    ld.rounded_rectangle((cx - cw // 2, case_top, cx + cw // 2, case_top + 900), radius=190, fill=WHITE)
    ld.rectangle((0, case_cut + outline, S, S), fill=(0, 0, 0, 0))
    ld.rectangle((cx - cw // 2, case_cut, cx + cw // 2, case_cut + outline), fill=bg)
    ld.rectangle((cx - cw // 2, case_top + 205, cx + cw // 2, case_top + 229), fill=bg)  # lid seam
    ld.ellipse((cx - 22, case_top + 290, cx + 22, case_top + 334), fill=bg)  # status light
    icon.alpha_composite(layer)

    final = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    final.alpha_composite(icon.resize((920, 920), Image.LANCZOS), (52, 52))
    final.save(HERE / "AirPodsWin.png")
    final.save(HERE / "AirPodsWin.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])


if __name__ == "__main__":
    main()
