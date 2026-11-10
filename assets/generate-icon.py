"""
Generates icon.png, the NuGet package icon: the two Pitons of St Lucia, rising from the sea under a blue sky.

Petit Piton (left) is the taller, narrower peak in front; Gros Piton (right) is broader and lower, behind it.
Each is a triangle with a lit face and a shadowed face, shaded with gradients. Rolling hills run in from the
left, as in the view from the north, with trees along the shore and the peaks reflected in the sea.

Requires ImageMagick 7: the `magick` command on the PATH, or its path in the MAGICK environment variable.

Usage: python assets/generate-icon.py
"""

import os
import shutil
import subprocess
import sys
from pathlib import Path

# Drawn at 4x and downscaled, for smooth edges
SIZE = 512
# NuGet's recommended package icon size
OUTPUT_SIZE = 128
CORNER_RADIUS = 64

WATERLINE = 360
BASE = 372  # The mountains' bases, below the waterline: the sea covers them

SKY = ("#2a78c8", "#d4edf9")  # top, horizon
SEA = ("#2a6f99", "#0f3550")  # waterline, bottom

# Each mountain: peak, base left, base right, and the x where the lit and shadowed faces meet at the base.
# Faces: (colour at the peak, colour at the base)
PETIT_PITON = {
    "peak": (200, 80), "left": (108, BASE), "right": (318, BASE), "split": 216,
    "lit": ("#48963e", "#1d5521"), "shadow": ("#25652a", "#103a15"),
}
GROS_PITON = {
    "peak": (374, 150), "left": (246, BASE), "right": (508, BASE), "split": 390,
    "lit": ("#3f8a38", "#1a4f1f"), "shadow": ("#1f5a24", "#0e3413"),
}

# The rolling hills to the left of Petit Piton
HILLS = "path 'M 0,252 C 30,236 72,236 104,258 C 126,236 158,230 184,262 L 184,372 L 0,372 Z'"
HILLS_COLOURS = ("#2f7330", "#14401a")

# Pale rock on the steep faces, as in the photograph
ROCK = "rgba(196,178,132,0.42)"
ROCKS = [
    [(236, 178), (252, 236), (266, 320), (250, 300), (240, 238)],  # Petit Piton's right face
    [(404, 214), (418, 262), (408, 266)],                           # Gros Piton's right face
]

# Faint ridges on the lit faces
RIDGES = [
    "path 'M 196,100 Q 170,210 140,350'",
    "path 'M 204,110 Q 196,230 184,350'",
    "path 'M 368,170 Q 336,260 290,350'",
]

CLOUDS = [
    # Behind Petit Piton, as in the photograph
    (78, 186, 46), (122, 156, 40), (52, 216, 32), (126, 200, 34), (160, 186, 28),
    # A small one to the right
    (444, 112, 26), (470, 122, 22), (422, 128, 18),
]
CLOUD = "rgba(255,255,255,0.95)"
CLOUD_SHADOW = "rgba(190,208,226,0.95)"

SHORE_TREES = "#123d17"
SHIMMER = [(70, 392, 190), (230, 410, 330), (120, 438, 260), (300, 462, 430), (60, 480, 150)]


def circle(x: int, y: int, r: int) -> str:
    return f"circle {x},{y} {x},{y - r}"


def polygon(*points: tuple[int, int]) -> str:
    return "polygon " + " ".join(f"{x},{y}" for x, y in points)


def gradient_layer(shape: str, colours: tuple[str, str], vector: tuple[int, int, int, int]) -> list[str]:
    """A shape filled with a linear gradient along vector (x1, y1, x2, y2), composited over the image."""
    x1, y1, x2, y2 = vector
    return [
        "(",
        "-size", f"{SIZE}x{SIZE}", "-define", f"gradient:vector={x1},{y1},{x2},{y2}",
        f"gradient:{colours[0]}-{colours[1]}",
        "(", "-size", f"{SIZE}x{SIZE}", "xc:black", "-fill", "white", "-draw", shape, ")",
        "-alpha", "off", "-compose", "CopyOpacity", "-composite",
        ")",
        "-compose", "Over", "-composite",
    ]


def mountain(m: dict) -> list[str]:
    peak, left, right = m["peak"], m["left"], m["right"]
    split = (m["split"], BASE)
    # Lighter at the peak, darker towards the base
    vector = (peak[0], peak[1], split[0], split[1])
    return (
        gradient_layer(polygon(peak, left, split), m["lit"], vector)
        + gradient_layer(polygon(peak, split, right), m["shadow"], vector)
    )


def reflection(m: dict) -> str:
    """The mountain mirrored in the sea, shortened."""
    (px, py), (lx, _), (rx, _) = m["peak"], m["left"], m["right"]
    depth = int((WATERLINE - py) * 0.3)
    return polygon((lx, WATERLINE), (px, WATERLINE + depth), (rx, WATERLINE))


def main() -> None:
    magick = os.environ.get("MAGICK") or shutil.which("magick")
    if not magick:
        sys.exit("ImageMagick 7 not found: put `magick` on the PATH, or set MAGICK to its path")

    output = Path(__file__).with_name("icon.png")

    # Sky
    args = [magick, "-size", f"{SIZE}x{SIZE}", f"gradient:{SKY[0]}-{SKY[1]}"]

    # Clouds, with a soft shadow underneath
    args += ["-fill", CLOUD_SHADOW]
    args += [arg for x, y, r in CLOUDS for arg in ("-draw", circle(x, y + 6, r))]
    args += ["-fill", CLOUD]
    args += [arg for x, y, r in CLOUDS for arg in ("-draw", circle(x, y, r))]

    # Back to front: Gros Piton, the hills, then Petit Piton
    args += mountain(GROS_PITON)
    args += gradient_layer(HILLS, HILLS_COLOURS, (0, 230, 0, BASE))
    args += mountain(PETIT_PITON)

    # Rock and ridges
    args += ["-fill", ROCK]
    args += [arg for rock in ROCKS for arg in ("-draw", polygon(*rock))]
    args += ["-fill", "none", "-stroke", "rgba(0,0,0,0.14)", "-strokewidth", "3"]
    args += [arg for ridge in RIDGES for arg in ("-draw", ridge)]
    args += ["-stroke", "none"]

    # Trees along the shore: a bumpy band the sea cuts in half
    args += ["-fill", SHORE_TREES]
    args += [arg for x in range(0, SIZE + 14, 14) for arg in ("-draw", circle(x, WATERLINE + 1, 9 + (x // 14) % 3))]

    # The sea, covering the mountains' bases for a straight waterline
    args += gradient_layer(f"rectangle 0,{WATERLINE} {SIZE - 1},{SIZE - 1}", SEA, (0, WATERLINE, 0, SIZE))

    # Reflections and shimmer
    args += ["-fill", "rgba(8,40,20,0.28)"]
    args += ["-draw", reflection(GROS_PITON), "-draw", reflection(PETIT_PITON)]
    args += ["-fill", "rgba(255,255,255,0.22)"]
    args += [arg for x1, y, x2 in SHIMMER for arg in ("-draw", f"roundrectangle {x1},{y} {x2},{y + 4} 2,2")]

    # Rounded corners
    args += [
        "(", "+clone", "-alpha", "extract", "-fill", "black", "-colorize", "100",
        "-fill", "white", "-draw", f"roundrectangle 0,0 {SIZE - 1},{SIZE - 1} {CORNER_RADIUS},{CORNER_RADIUS}", ")",
        "-alpha", "off", "-compose", "CopyOpacity", "-composite",
        "-resize", f"{OUTPUT_SIZE}x{OUTPUT_SIZE}",
        "-strip", f"PNG32:{output}",
    ]

    subprocess.run(args, check=True)
    print(f"Wrote {output}")


if __name__ == "__main__":
    main()
