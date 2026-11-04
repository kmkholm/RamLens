# Draws the RamLens app icon (memory chip under a magnifying lens) into src/app.ico.
from PIL import Image, ImageDraw
import os

S = 1024
img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# rounded tile with a vertical navy-to-blue gradient
tile = Image.new('RGBA', (S, S))
td = ImageDraw.Draw(tile)
top, bot = (22, 33, 62), (47, 111, 237)
for y in range(S):
    t = y / (S - 1)
    td.line([(0, y), (S, y)], fill=tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)) + (255,))
mask = Image.new('L', (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], radius=210, fill=255)
img.paste(tile, (0, 0), mask)

# memory chip
cx0, cy0, cx1, cy1 = 230, 300, 700, 700
d.rounded_rectangle([cx0, cy0, cx1, cy1], radius=40, fill=(255, 255, 255, 255))
for i in range(6):
    x = cx0 + 45 + i * 75
    d.rectangle([x, cy1, x + 36, cy1 + 70], fill=(255, 255, 255, 255))
for i, c in enumerate([(228, 87, 46), (142, 68, 173), (79, 163, 224)]):
    y = cy0 + 70 + i * 100
    d.rounded_rectangle([cx0 + 50, y, cx1 - 50, y + 55], radius=18, fill=c + (255,))

# magnifying lens
r = 170
lx, ly = 690, 610
d.ellipse([lx - r, ly - r, lx + r, ly + r], fill=(255, 255, 255, 90), outline=(255, 214, 10, 255), width=56)
d.line([(lx + 115, ly + 115), (lx + 270, ly + 270)], fill=(255, 214, 10, 255), width=95)

out = os.path.join(os.path.dirname(__file__), '..', 'src', 'app.ico')
img.save(out, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
img.resize((256, 256), Image.LANCZOS).save(os.path.join(os.path.dirname(__file__), '..', 'docs', 'icon.png'))
print('ok')
