from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "button-icons"
OUT.mkdir(parents=True, exist_ok=True)

BLUE = "#1766D1"
DEEP_BLUE = "#245A92"
RED = "#A9231F"
WHITE = "#FFFFFF"
GRID = 24
SUPERSAMPLE = 4


def px(value: float, scale: float) -> int:
    return round(value * scale)


def point(value: tuple[float, float], scale: float) -> tuple[int, int]:
    return px(value[0], scale), px(value[1], scale)


def stroke(draw: ImageDraw.ImageDraw, points, color: str, width: float, scale: float, caps: bool = True):
    points_px = [point(item, scale) for item in points]
    width_px = max(1, px(width, scale))
    draw.line(points_px, fill=color, width=width_px, joint="curve")
    if caps:
        radius = width_px / 2
        for x, y in (points_px[0], points_px[-1]):
            draw.ellipse((round(x-radius), round(y-radius), round(x+radius), round(y+radius)), fill=color)


def rounded_rect(draw: ImageDraw.ImageDraw, box, radius: float, color: str, width: float, scale: float):
    draw.rounded_rectangle(tuple(px(v, scale) for v in box), radius=px(radius, scale),
                           outline=color, width=max(1, px(width, scale)))


def solid_circle(draw: ImageDraw.ImageDraw, cx: float, cy: float, radius: float, color: str, scale: float):
    draw.ellipse(tuple(px(v, scale) for v in (cx-radius, cy-radius, cx+radius, cy+radius)), fill=color)


def clear_badge_space(draw: ImageDraw.ImageDraw, cx: float, cy: float, radius: float, scale: float):
    draw.ellipse(tuple(px(v, scale) for v in (cx-radius, cy-radius, cx+radius, cy+radius)), fill=(0, 0, 0, 0))


def x_badge(draw: ImageDraw.ImageDraw, scale: float, color: str):
    cx, cy, radius = 17.2, 17.0, 4.15
    clear_badge_space(draw, cx, cy, radius + .55, scale)
    solid_circle(draw, cx, cy, radius, color, scale)
    stroke(draw, [(15.7, 15.5), (18.7, 18.5)], WHITE, 1.25, scale)
    stroke(draw, [(18.7, 15.5), (15.7, 18.5)], WHITE, 1.25, scale)


def settings_badge(draw: ImageDraw.ImageDraw, scale: float, color: str):
    cx, cy, radius = 17.3, 16.9, 4.25
    clear_badge_space(draw, cx, cy, radius + .55, scale)
    solid_circle(draw, cx, cy, radius, color, scale)
    draw.ellipse(tuple(px(v, scale) for v in (16.15, 15.75, 18.45, 18.05)),
                 outline=WHITE, width=max(1, px(1.0, scale)))
    for points in (
        [(17.3, 14.45), (17.3, 15.05)],
        [(17.3, 18.75), (17.3, 19.35)],
        [(14.85, 16.9), (15.45, 16.9)],
        [(19.15, 16.9), (19.75, 16.9)],
    ):
        stroke(draw, points, WHITE, .95, scale)


def icon_directory(draw: ImageDraw.ImageDraw, scale: float, color: str):
    stroke(draw, [(3.6, 7.2), (3.6, 6.1), (4.7, 5.0), (9.0, 5.0), (11.0, 7.0),
                  (19.2, 7.0), (20.3, 8.1), (20.3, 17.7), (19.2, 18.8), (4.7, 18.8),
                  (3.6, 17.7), (3.6, 7.2)], color, 1.65, scale)
    settings_badge(draw, scale, color)


def icon_declaration(draw: ImageDraw.ImageDraw, scale: float, color: str):
    stroke(draw, [(6.0, 4.6), (7.0, 3.6), (13.1, 3.6), (17.7, 8.2),
                  (17.7, 18.6), (16.7, 19.6), (7.0, 19.6), (6.0, 18.6), (6.0, 4.6)],
           color, 1.65, scale)
    stroke(draw, [(13.1, 3.8), (13.1, 8.2), (17.5, 8.2)], color, 1.45, scale)
    stroke(draw, [(8.8, 11.2), (13.1, 11.2)], color, 1.35, scale)
    x_badge(draw, scale, color)


def icon_screenshot(draw: ImageDraw.ImageDraw, scale: float, color: str):
    rounded_rect(draw, (3.4, 4.2, 20.6, 19.7), 2.0, color, 1.65, scale)
    solid_circle(draw, 8.0, 8.3, 1.25, color, scale)
    stroke(draw, [(5.3, 17.4), (9.4, 13.2), (12.1, 15.7), (14.0, 13.9)], color, 1.45, scale)
    x_badge(draw, scale, color)


def icon_start(draw: ImageDraw.ImageDraw, scale: float, color: str):
    for points in (
        [(8.1, 4.0), (5.6, 4.0), (4.0, 5.6), (4.0, 8.1)],
        [(15.9, 4.0), (18.4, 4.0), (20.0, 5.6), (20.0, 8.1)],
        [(20.0, 15.9), (20.0, 18.4), (18.4, 20.0), (15.9, 20.0)],
        [(8.1, 20.0), (5.6, 20.0), (4.0, 18.4), (4.0, 15.9)],
    ):
        stroke(draw, points, color, 1.75, scale)
    rounded_rect(draw, (8.0, 6.6, 16.0, 17.4), 1.15, color, 1.45, scale)
    stroke(draw, [(10.0, 10.2), (14.0, 10.2)], color, 1.2, scale)
    stroke(draw, [(10.0, 13.2), (14.0, 13.2)], color, 1.2, scale)


def icon_list(draw: ImageDraw.ImageDraw, scale: float, color: str):
    for y, end in ((6.3, 19.5), (11.2, 19.5), (16.1, 13.0)):
        solid_circle(draw, 4.8, y, .85, color, scale)
        stroke(draw, [(8.0, y), (end, y)], color, 1.55, scale)
    x_badge(draw, scale, color)


ICONS = {
    "directory-settings": (icon_directory, BLUE),
    "declaration-clean": (icon_declaration, RED),
    "screenshot-clean": (icon_screenshot, RED),
    "start-recognition-blue": (icon_start, DEEP_BLUE),
    "start-recognition-white": (icon_start, WHITE),
    "list-clean": (icon_list, RED),
}


for name, (painter, color) in ICONS.items():
    for size in (24, 32, 48):
        scale = (size / GRID) * SUPERSAMPLE
        image = Image.new("RGBA", (size * SUPERSAMPLE, size * SUPERSAMPLE), (0, 0, 0, 0))
        painter(ImageDraw.Draw(image), scale, color)
        image = image.resize((size, size), Image.Resampling.LANCZOS)
        pixels = image.load()
        for index in range(size):
            pixels[index, 0] = (0, 0, 0, 0)
            pixels[index, size - 1] = (0, 0, 0, 0)
            pixels[0, index] = (0, 0, 0, 0)
            pixels[size - 1, index] = (0, 0, 0, 0)
        image.save(OUT / f"{name}-{size}.png")

print(f"Generated {len(ICONS) * 3} PNG files in {OUT}")
