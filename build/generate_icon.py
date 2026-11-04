#!/usr/bin/env python3
"""
Generate the Saturn Tweaks .ico file.

Produces a proper multi-size Windows icon containing:
  16x16, 24x24, 32x32, 48x48, 64x64, 128x128, 256x256

Each size is rendered as an SVG then rasterised via ImageMagick.
The final .ico is assembled with all sizes embedded.

Run from the project root:
  python3 build/generate_icon.py

Output: src/SaturnTweaks.UI/Assets/saturn.ico
"""

import os
import struct
import subprocess
import sys
import tempfile

# Saturn Tweaks icon: a stylised Saturn planet ring over a deep violet circle.
# Design: dark charcoal background, violet/indigo gradient circle representing
# Saturn, a tilted elliptical ring in cool blue, subtle star dots.
# Chosen to be readable at 16x16 and sharp at 256x256.

SVG_TEMPLATE = """<svg xmlns="http://www.w3.org/2000/svg"
     width="{size}" height="{size}" viewBox="0 0 100 100">
  <defs>
    <radialGradient id="bg" cx="50%" cy="50%" r="50%">
      <stop offset="0%" stop-color="#1C1C26"/>
      <stop offset="100%" stop-color="#0D0D12"/>
    </radialGradient>
    <radialGradient id="planet" cx="40%" cy="35%" r="60%">
      <stop offset="0%" stop-color="#9B7FFF"/>
      <stop offset="45%" stop-color="#7C5CFC"/>
      <stop offset="100%" stop-color="#3B2A8A"/>
    </radialGradient>
    <radialGradient id="shine" cx="35%" cy="30%" r="40%">
      <stop offset="0%" stop-color="#FFFFFF" stop-opacity="0.18"/>
      <stop offset="100%" stop-color="#FFFFFF" stop-opacity="0"/>
    </radialGradient>
    <linearGradient id="ring" x1="0%" y1="50%" x2="100%" y2="50%">
      <stop offset="0%" stop-color="#5B8DEF" stop-opacity="0.15"/>
      <stop offset="20%" stop-color="#7AB3F5" stop-opacity="0.9"/>
      <stop offset="50%" stop-color="#A8D0FF" stop-opacity="1"/>
      <stop offset="80%" stop-color="#7AB3F5" stop-opacity="0.9"/>
      <stop offset="100%" stop-color="#5B8DEF" stop-opacity="0.15"/>
    </linearGradient>
  </defs>

  <!-- Outer rounded square background -->
  <rect width="100" height="100" rx="22" ry="22" fill="url(#bg)"/>

  <!-- Ring BACK (behind planet) — lower arc of ellipse -->
  <ellipse cx="50" cy="52" rx="44" ry="12"
           fill="none"
           stroke="url(#ring)"
           stroke-width="5"
           clip-path="url(#ring-back)"
           stroke-linecap="round"
           transform="rotate(-18 50 52)"/>

  <!-- Planet body -->
  <circle cx="50" cy="50" r="28" fill="url(#planet)"/>

  <!-- Planet highlight -->
  <circle cx="50" cy="50" r="28" fill="url(#shine)"/>

  <!-- Ring FRONT (in front of planet) — upper arc of ellipse, clipped above centre -->
  <ellipse cx="50" cy="52" rx="44" ry="12"
           fill="none"
           stroke="url(#ring)"
           stroke-width="5"
           stroke-linecap="round"
           transform="rotate(-18 50 52)">
    <!-- Clip to only show the top half of the ring in front -->
    <animate attributeName="opacity" values="1" dur="0s" fill="freeze"/>
  </ellipse>

  <!-- Ring front clip: draw a clean arc over the top half -->
  <path d="M 6,47 A 44,12 0 0,1 94,47"
        fill="none"
        stroke="url(#ring)"
        stroke-width="5.5"
        stroke-linecap="round"
        transform="rotate(-18 50 52)"/>

  <!-- Star accents (small dots in corners) -->
  <circle cx="18" cy="20" r="1.2" fill="#A78BFA" opacity="0.7"/>
  <circle cx="82" cy="15" r="0.9" fill="#7AB3F5" opacity="0.6"/>
  <circle cx="78" cy="83" r="1.1" fill="#A78BFA" opacity="0.5"/>
  <circle cx="14" cy="78" r="0.8" fill="#7AB3F5" opacity="0.5"/>
  <circle cx="88" cy="55" r="0.7" fill="#FFFFFF" opacity="0.4"/>
  <circle cx="12" cy="47" r="0.6" fill="#FFFFFF" opacity="0.3"/>

  <!-- "S" letterform hint at large sizes — a small bright arc -->
  <path d="M 42,44 Q 46,40 50,42 Q 54,44 50,48 Q 46,52 50,54 Q 54,56 58,52"
        fill="none"
        stroke="#FFFFFF"
        stroke-width="2.8"
        stroke-linecap="round"
        opacity="0.85"/>
</svg>"""


def render_size(svg_content: str, size: int, tmp_dir: str) -> bytes:
    """Render an SVG at the given pixel size to PNG using ImageMagick."""
    svg_path = os.path.join(tmp_dir, f"saturn_{size}.svg")
    png_path = os.path.join(tmp_dir, f"saturn_{size}.png")

    with open(svg_path, "w") as f:
        f.write(svg_content.format(size=size))

    result = subprocess.run(
        [
            "convert",
            "-background", "none",
            "-resize", f"{size}x{size}",
            "-density", "144",
            svg_path,
            png_path,
        ],
        capture_output=True,
    )

    if result.returncode != 0:
        raise RuntimeError(
            f"ImageMagick failed for {size}px: {result.stderr.decode()}"
        )

    with open(png_path, "rb") as f:
        return f.read()


def build_ico(size_data: list[tuple[int, bytes]]) -> bytes:
    """
    Assemble a Windows .ico file from a list of (size, png_bytes) tuples.

    ICO format:
      6-byte header: reserved(2) + type(2) + count(2)
      count * 16-byte directory entries
      image data concatenated after directory
    """
    count = len(size_data)
    header = struct.pack("<HHH", 0, 1, count)  # reserved=0, type=1(ICO), count

    # Directory entries are 16 bytes each
    # Data starts after: 6 (header) + count * 16 (directory)
    data_offset = 6 + count * 16

    directory = b""
    image_data = b""

    for size, png_bytes in size_data:
        # Width and height: 0 means 256 in ICO format
        w = 0 if size >= 256 else size
        h = 0 if size >= 256 else size

        entry = struct.pack(
            "<BBBBHHII",
            w,            # width
            h,            # height
            0,            # colour count (0 = no palette)
            0,            # reserved
            1,            # colour planes
            32,           # bits per pixel
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

    print("Saturn Tweaks icon generator")
    print(f"Output: {output_path}")
    print()

    with tempfile.TemporaryDirectory() as tmp_dir:
        size_data = []
        for size in sizes:
            print(f"  Rendering {size}x{size}...", end=" ", flush=True)
            try:
                png_bytes = render_size(SVG_TEMPLATE, size, tmp_dir)
                size_data.append((size, png_bytes))
                print(f"OK ({len(png_bytes):,} bytes)")
            except RuntimeError as e:
                print(f"FAILED: {e}")
                sys.exit(1)

        print()
        print("Assembling .ico...", end=" ", flush=True)
        ico_bytes = build_ico(size_data)
        print(f"OK ({len(ico_bytes):,} bytes, {len(size_data)} sizes)")

    with open(output_path, "wb") as f:
        f.write(ico_bytes)

    print(f"\nIcon written: {output_path}")
    print("Sizes embedded:", [s for s, _ in size_data])


if __name__ == "__main__":
    main()
