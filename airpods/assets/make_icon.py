"""Draws the AirPodsWin app icon (graphite squircle, two white pods) and writes
AirPodsWin.ico (16-256 px) and AirPodsWin.png next to this script.
Requires Pillow: python make_icon.py"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

S = 2048  # drawn large, then downsampled for clean edges
HERE = Path(__file__).parent


def squircle_mask(size, radius):
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return m


def gradient(size, top, bottom):
    g = Image.new("RGB", (1, size))
    for y in range(size):
        t = y / (size - 1)
        g.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    return g.resize((size, size))


def pod(mirror):
    """One AirPod (head + stem) on a transparent layer, left-ear shape unless mirrored."""
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    shade = (196, 196, 202, 255)
    white = (255, 255, 255, 255)
    # stem, dropping from the inner side of the head
    d.rounded_rectangle((858, 700, 1018, 1540), radius=80, fill=shade)
    d.rounded_rectangle((850, 700, 998, 1530), radius=74, fill=white)
    d.ellipse((868, 1462, 1010, 1536), fill=(178, 178, 186, 255))  # stem tip
    # head
    d.ellipse((600, 520, 1030, 900), fill=shade)
    d.ellipse((590, 505, 1012, 878), fill=white)
    # speaker mesh, facing the other pod
    d.ellipse((880, 620, 990, 720), fill=(40, 40, 44, 255))
    layer = layer.rotate(9, resample=Image.BICUBIC, center=(900, 900))
    return layer.transpose(Image.FLIP_LEFT_RIGHT) if mirror else layer


def main():
    bg = gradient(S, (72, 72, 78), (12, 12, 14)).convert("RGBA")
    # soft highlight at the top, like Apple's glassy icons
    hl = Image.new("L", (S, S), 0)
    ImageDraw.Draw(hl).ellipse((-400, -1500, S + 400, 700), fill=34)
    bg.paste((255, 255, 255, 255), (0, 0), hl.filter(ImageFilter.GaussianBlur(220)))

    pods = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    left, right = pod(False), pod(True)
    pods.alpha_composite(left, (-260, 60))
    pods.alpha_composite(right, (260, 60))

    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    shadow.putalpha(pods.getchannel("A").point(lambda a: a * 0.55))
    shadow = shadow.filter(ImageFilter.GaussianBlur(40))
    bg.alpha_composite(shadow, (0, 50))
    bg.alpha_composite(pods)

    icon = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    icon.paste(bg, (0, 0), squircle_mask(S, int(S * 0.225)))

    # 1024 canvas with a small margin, the way macOS/iOS icons sit in their grid
    final = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    final.alpha_composite(icon.resize((920, 920), Image.LANCZOS), (52, 52))
    final.save(HERE / "AirPodsWin.png")
    final.save(HERE / "AirPodsWin.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])


if __name__ == "__main__":
    main()
