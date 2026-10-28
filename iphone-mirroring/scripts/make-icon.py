# Usage: python scripts/make-icon.py assets/app   (writes app.png + app.ico)
# Apple-style (Big Sur squircle) original icon: phone with a mirrored screen.
from PIL import Image, ImageDraw, ImageFilter
import sys
S = 1024
def grad(size, top, bottom):
    g = Image.new("RGBA", size)
    d = ImageDraw.Draw(g)
    for y in range(size[1]):
        t = y / (size[1] - 1)
        d.line([(0, y), (size[0], y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    return g
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
# shadow
m = 100
sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(sh).rounded_rectangle([m, m + 20, S - m, S - m + 20], radius=185, fill=(0, 0, 0, 90))
img = Image.alpha_composite(img, sh.filter(ImageFilter.GaussianBlur(22)))
# squircle background
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([m, m, S - m, S - m], radius=185, fill=255)
bg = grad((S, S), (94, 200, 255), (10, 102, 230))
img.paste(bg, (0, 0), mask)
d = ImageDraw.Draw(img)
# monitor-ish window behind (Windows PC)
def layer(fn):
    global img
    l = Image.new("RGBA", (S, S), (0, 0, 0, 0)); fn(ImageDraw.Draw(l)); img = Image.alpha_composite(img, l)
layer(lambda q: q.rounded_rectangle([200, 290, 720, 650], radius=40, fill=(255, 255, 255, 80)))
layer(lambda q: q.rounded_rectangle([200, 290, 720, 350], radius=40, fill=(255, 255, 255, 60)))
d = ImageDraw.Draw(img)
# phone body
px0, py0, px1, py1 = 470, 230, 790, 800
sh2 = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(sh2).rounded_rectangle([px0 + 6, py0 + 22, px1 + 6, py1 + 22], radius=70, fill=(0, 30, 90, 110))
img = Image.alpha_composite(img, sh2.filter(ImageFilter.GaussianBlur(18)))
d = ImageDraw.Draw(img)
d.rounded_rectangle([px0, py0, px1, py1], radius=70, fill=(255, 255, 255, 255))
# screen
s0 = (px0 + 26, py0 + 26, px1 - 26, py1 - 26)
scr = grad((s0[2] - s0[0], s0[3] - s0[1]), (40, 44, 60), (14, 16, 26))
smask = Image.new("L", scr.size, 0)
ImageDraw.Draw(smask).rounded_rectangle([0, 0, scr.size[0] - 1, scr.size[1] - 1], radius=48, fill=255)
img.paste(scr, s0[:2], smask)
d = ImageDraw.Draw(img)
# dynamic island
cx = (px0 + px1) // 2
d.rounded_rectangle([cx - 52, py0 + 48, cx + 52, py0 + 80], radius=16, fill=(0, 0, 0, 255))
# app grid on screen
cols = [(255, 95, 87), (255, 189, 46), (40, 205, 65), (10, 132, 255), (191, 90, 242), (255, 159, 10)]
k = 0
for r in range(3):
    for c in range(3):
        x = s0[0] + 34 + c * 82; y = s0[1] + 100 + r * 92
        d.rounded_rectangle([x, y, x + 58, y + 58], radius=15, fill=cols[k % len(cols)] + (255,)); k += 1
# pointer (AssistiveTouch-like cursor ring)
layer(lambda q: q.ellipse([566, 606, 646, 686], fill=(255, 255, 255, 110), outline=(255, 255, 255, 235), width=7))
img.save(sys.argv[1] + ".png")
img.resize((256, 256), Image.LANCZOS).save(sys.argv[1] + ".ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
