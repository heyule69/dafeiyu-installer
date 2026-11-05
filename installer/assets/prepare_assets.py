# 生成安装器用的素材（嵌入 exe）：
#   mascot.png  大肥鱼形象，去白底（窗口左侧）
#   panda.png   熊猫背景形象，去白底、缩到 520px 高（卡片右上角探头）
#   app.ico     程序图标，取大肥鱼头部（16~256px）
# 用法：python prepare_assets.py
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
MARK = (255, 0, 255)


def cutout(src, thresh, shrink, feather):
    # 从四角漫水填充，只去掉和外框相连的白底；描边内的白色不受影响
    im = Image.open(src).convert("RGB")
    w, h = im.size
    flood = im.copy()
    for corner in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]:
        if flood.getpixel(corner) != MARK:
            ImageDraw.floodfill(flood, corner, MARK, thresh=thresh)
    diff = ImageChops.difference(flood, Image.new("RGB", (w, h), MARK)).convert("L")
    mask = diff.point(lambda v: 255 if v else 0)
    # 收掉一圈杂边，再轻微羽化
    mask = mask.filter(ImageFilter.MinFilter(shrink)).filter(ImageFilter.GaussianBlur(feather))
    rgba = im.convert("RGBA")
    rgba.putalpha(mask)
    return rgba.crop(rgba.getbbox())


def resized(img, size):
    # 预乘 alpha 后缩放，避免边缘发白
    return img.convert("RGBa").resize(size, Image.LANCZOS).convert("RGBA")


mascot = cutout(HERE / "mascot-source.jpg", thresh=40, shrink=3, feather=0.8)
mascot.save(HERE / "mascot.png", optimize=True)

panda = cutout(HERE / "panda-source.png", thresh=10, shrink=5, feather=1.2)
panda = resized(panda, (round(panda.width * 520 / panda.height), 520))
panda.save(HERE / "panda.png", optimize=True)

# 图标：以脸为中心裁一个正方形（头饰到下巴）
side = int(mascot.width * 0.78)
left = (mascot.width - side) // 2
top = int(mascot.height * 0.04)
square = mascot.crop((left, top, left + side, top + side))
resized(square, (256, 256)).save(
    HERE / "app.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
)

print("mascot.png", mascot.size)
print("panda.png", panda.size)
print("app.ico ok")
