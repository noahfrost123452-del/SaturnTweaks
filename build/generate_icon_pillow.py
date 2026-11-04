#!/usr/bin/env python3
"""
Generate Saturn Tweaks saturn.ico using Pillow only.
No SVG renderer required — draws directly with PIL primitives.

Design: deep charcoal rounded-square background, violet/indigo planet circle,
cool-blue elliptical ring (back arc + front arc), white 'S' accent, star dots.
Readable at 16×16, sharp at 256×256.

Output: src/SaturnTweaks.UI/Assets/saturn.ico
"""

import math
import os
import struct
import sys

try:
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:
    print("ERROR: Pillow is required. Run: pip install Pillow --break-system-packages")
    sys.exit(1)


# ─── Colour palette ────────────────────────────────────────────────────────

BG_OUTER     = (13,  13,  18,  255)   # #0D0D12 — deep charcoal
BG_INNER     = (28,  28,  38,  255)   # #1C1C26
PLANET_DARK  = (59,  42, 138,  255)   # #3B2A8A
PLANET_MID   = (124, 92, 252,  255)   # #7C5CFC — saturn violet
PLANET_LIGHT = (155, 127,255,  255)   # #9B7FFF
RING_CORE    = (168, 208,255,  255)   # #A8D0FF — ring highlight
RING_MID     = (122, 179,245,  255)   # #7AB3F5
RING_FADE    = (91, 141, 239,   60)   # #5B8DEF transparent
SHINE        = (255, 255,255,   38)   # white at 15% — planet specular
STAR_V       = (167, 139,250,  180)   # #A78BFA — violet star
STAR_B       = (122, 179,245,  154)   # #7AB3F5 — blue star
LETTER       = (255, 255,255,  218)   # white 85% — S accent
TRANSPARENT  = (0,   0,   0,   0)


# ─── Drawing helpers ────────────────────────────────────────────────────────

def lerp_color(c1, c2, t):
    return tuple(int(a + (b - a) * t) for a, b in zip(c1, c2))


def radial_gradient_circle(draw, cx, cy, radius, center_color, edge_color):
    """Draw a radial gradient circle, innermost pixel first."""
    # Sample a smooth gradient by drawing concentric filled circles outward→in
    steps = max(radius, 8)
    for i in range(steps, -1, -1):
        t = i / steps
        color = lerp_color(center_color, edge_color, t)
        r = int(radius * i / steps)
        if r > 0:
            draw.ellipse(
                [cx - r, cy - r, cx + r, cy + r],
                fill=color,
            )


def rounded_rect(draw, x0, y0, x1, y1, radius, fill):
    """Draw a filled rounded rectangle."""
    draw.rectangle([x0 + radius, y0, x1 - radius, y1], fill=fill)
    draw.rectangle([x0, y0 + radius, x1, y1 - radius], fill=fill)
    draw.ellipse([x0, y0, x0 + 2*radius, y0 + 2*radius], fill=fill)
    draw.ellipse([x1 - 2*radius, y0, x1, y0 + 2*radius], fill=fill)
    draw.ellipse([x0, y1 - 2*radius, x0 + 2*radius, y1], fill=fill)
    draw.ellipse([x1 - 2*radius, y1 - 2*radius, x1, y1], fill=fill)


def draw_ring_arc(img, cx, cy, rx, ry, angle_deg, start_a, end_a,
                  color_center, color_edge, thickness, steps=200):
    """
    Draw one arc of the tilted elliptical ring using composited pixel painting.
    Uses RGBA compositing for proper transparency blending.
    """
    draw = ImageDraw.Draw(img, 'RGBA')
    angle_rad = math.radians(angle_deg)
    cos_a = math.cos(angle_rad)
    sin_a = math.sin(angle_rad)

    half_t = thickness / 2.0

    for i in range(steps + 1):
        t = start_a + (end_a - start_a) * i / steps
        # Parametric ellipse point
        px = rx * math.cos(t)
        py = ry * math.sin(t)
        # Rotate by angle
        rpx = px * cos_a - py * sin_a + cx
        rpy = px * sin_a + py * cos_a + cy

        # Fade at the ends of the arc
        fade = 1.0
        margin = 0.12
        arc_range = abs(end_a - start_a)
        pos_in_arc = abs(t - start_a) / arc_range if arc_range > 0 else 0
        if pos_in_arc < margin:
            fade = pos_in_arc / margin
        elif pos_in_arc > (1.0 - margin):
            fade = (1.0 - pos_in_arc) / margin
        fade = max(0.0, min(1.0, fade))

        # Taper thickness at ends
        local_t = half_t * (0.4 + 0.6 * fade)

        alpha_c = int(color_center[3] * fade)
        alpha_e = int(color_edge[3] * fade)

        center = (alpha_c,) if len(color_center) == 4 else ()
        edge_c = (alpha_e,) if len(color_edge) == 4 else ()

        col_c = color_center[:3] + (alpha_c,)
        col_e = color_edge[:3] + (alpha_e,)

        dot_size = max(1, int(local_t * 2))
        x0 = int(rpx - dot_size // 2)
        y0 = int(rpy - dot_size // 2)
        x1 = x0 + dot_size
        y1 = y0 + dot_size
        mid_col = lerp_color(col_c, col_e, 0.5)
        draw.ellipse([x0, y0, x1, y1], fill=mid_col)


def draw_star(draw, x, y, radius, color):
    r = max(1, int(radius))
    draw.ellipse([x - r, y - r, x + r, y + r], fill=color)


def draw_s_accent(draw, cx, cy, size, color, stroke_w):
    """
    Draw a stylised 'S' curve as three quadratic bezier arcs sampled as polylines.
    Scaled relative to the planet radius.
    """
    r = size * 0.26    # planet radius reference
    pts_top = []
    pts_bot = []

    # Top arc of S: left to centre
    for i in range(20):
        t = i / 19.0
        # Quadratic bezier: P0=(cx-r*0.35, cy-r*0.15), P1=(cx, cy-r*0.55), P2=(cx+r*0.35, cy-r*0.15)
        bx = (1-t)**2*(cx - r*0.35) + 2*(1-t)*t*cx + t**2*(cx + r*0.35)
        by = (1-t)**2*(cy - r*0.15) + 2*(1-t)*t*(cy - r*0.55) + t**2*(cy - r*0.15)
        pts_top.append((bx, by))

    # Bottom arc of S: left to right
    for i in range(20):
        t = i / 19.0
        bx = (1-t)**2*(cx - r*0.35) + 2*(1-t)*t*cx + t**2*(cx + r*0.35)
        by = (1-t)**2*(cy + r*0.15) + 2*(1-t)*t*(cy + r*0.55) + t**2*(cy + r*0.15)
        pts_bot.append((bx, by))

    sw = max(1, int(stroke_w))
    if len(pts_top) > 1:
        for i in range(len(pts_top) - 1):
            draw.line([pts_top[i], pts_top[i+1]], fill=color, width=sw)
    if len(pts_bot) > 1:
        for i in range(len(pts_bot) - 1):
            draw.line([pts_bot[i], pts_bot[i+1]], fill=color, width=sw)

    # Connecting spine
    draw.line([pts_top[-1], pts_bot[0]], fill=color, width=sw)


# ─── Main render ────────────────────────────────────────────────────────────

def render_saturn(size: int) -> Image.Image:
    """Render the Saturn Tweaks icon at the given pixel size."""
    scale = size / 100.0

    img = Image.new("RGBA", (size, size), TRANSPARENT)
    draw = ImageDraw.Draw(img, "RGBA")

    # Background rounded square
    corner = int(22 * scale)
    rounded_rect(draw, 0, 0, size - 1, size - 1, corner, BG_OUTER)

    # Slightly lighter inner area
    inner_margin = int(3 * scale)
    rounded_rect(draw, inner_margin, inner_margin,
                 size - 1 - inner_margin, size - 1 - inner_margin,
                 max(1, corner - 2), BG_INNER)

    cx = size / 2.0
    cy = size / 2.0
    planet_r = size * 0.28

    # Star dots (before planet so planet covers them)
    star_data = [
        (0.18, 0.20, 1.2, STAR_V),
        (0.82, 0.15, 0.9, STAR_B),
        (0.78, 0.83, 1.1, STAR_V),
        (0.14, 0.78, 0.8, STAR_B),
        (0.88, 0.55, 0.7, (255, 255, 255, 100)),
        (0.12, 0.47, 0.6, (255, 255, 255, 80)),
    ]
    for sx, sy, sr, sc in star_data:
        draw_star(draw, int(sx * size), int(sy * size),
                  max(1.0, sr * scale), sc)

    # Ring — back arc (behind planet, drawn first)
    ring_cx = cx
    ring_cy = cy + 2 * scale
    ring_rx = planet_r * 1.62
    ring_ry = planet_r * 0.44
    ring_tilt = -18
    ring_thickness = max(2, planet_r * 0.19)

    # Back arc: lower half of the ellipse (π to 2π)
    draw_ring_arc(img, ring_cx, ring_cy, ring_rx, ring_ry,
                  ring_tilt, math.pi * 0.05, math.pi * 0.95,
                  RING_CORE, RING_FADE, ring_thickness)

    # Planet body — radial gradient
    radial_gradient_circle(draw, int(cx), int(cy), int(planet_r),
                           PLANET_LIGHT, PLANET_DARK)

    # Planet specular highlight (top-left bright spot)
    shine_r = int(planet_r * 0.55)
    shine_cx = int(cx - planet_r * 0.15)
    shine_cy = int(cy - planet_r * 0.15)
    for i in range(shine_r, 0, -1):
        t = 1.0 - (i / shine_r)
        alpha = int(SHINE[3] * (1 - t * t))
        draw.ellipse(
            [shine_cx - i, shine_cy - i, shine_cx + i, shine_cy + i],
            fill=(255, 255, 255, alpha),
        )

    # Ring — front arc (above planet centre line)
    draw_ring_arc(img, ring_cx, ring_cy, ring_rx, ring_ry,
                  ring_tilt, math.pi * 1.05, math.pi * 1.95,
                  RING_CORE, RING_FADE, ring_thickness)

    # S accent (only visible at >= 32px)
    if size >= 32:
        stroke_w = max(1.5, planet_r * 0.095)
        draw_s_accent(draw, cx, cy, size, LETTER, stroke_w)

    # Subtle outer glow on planet edge
    if size >= 48:
        glow_r = int(planet_r + 1.5 * scale)
        draw.ellipse(
            [int(cx - glow_r), int(cy - glow_r),
             int(cx + glow_r), int(cy + glow_r)],
            outline=(124, 92, 252, 55),
            width=max(1, int(scale * 1.5)),
        )

    return img


# ─── ICO assembly ────────────────────────────────────────────────────────────

def img_to_png_bytes(img: Image.Image) -> bytes:
    import io
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def build_ico(frames: list[tuple[int, bytes]]) -> bytes:
    count = len(frames)
    header = struct.pack("<HHH", 0, 1, count)
    data_offset = 6 + count * 16
    directory = b""
    image_data = b""

    for size, png_bytes in frames:
        w = 0 if size >= 256 else size
        h = 0 if size >= 256 else size
        entry = struct.pack(
            "<BBBBHHII",
            w, h, 0, 0,
            1, 32,
            len(png_bytes),
            data_offset,
        )
        directory += entry
        image_data += png_bytes
        data_offset += len(png_bytes)

    return header + directory + image_data


def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_root = os.path.dirname(script_dir)
    output_path = os.path.join(
        project_root, "src", "SaturnTweaks.UI", "Assets", "saturn.ico"
    )
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    sizes = [16, 24, 32, 48, 64, 128, 256]
    print("Saturn Tweaks icon generator (Pillow)")
    print(f"Output: {output_path}")
    print()

    frames = []
    for size in sizes:
        print(f"  Rendering {size:>3}×{size:<3}...", end=" ", flush=True)
        img = render_saturn(size)
        png = img_to_png_bytes(img)
        frames.append((size, png))
        print(f"{len(png):>7,} bytes")

    print()
    print("Assembling .ico...", end=" ", flush=True)
    ico = build_ico(frames)
    print(f"{len(ico):,} bytes across {len(frames)} sizes")

    with open(output_path, "wb") as f:
        f.write(ico)

    print(f"\n✓ Written: {output_path}")


if __name__ == "__main__":
    main()
