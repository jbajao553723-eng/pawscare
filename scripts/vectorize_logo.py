"""Create transparent PawCare brand assets from the supplied flat-color logo."""

from pathlib import Path
from xml.sax.saxutils import escape

import cv2
import numpy as np
from PIL import Image


SOURCE = Path(r"C:\Users\melissa\Downloads\97750e42-afef-47b9-a33c-8a3695ea3644.jpg")
OUTPUT = Path(__file__).resolve().parents[1] / "src" / "PawCare.Desktop" / "Assets"

BRAND_GOLD = (212, 184, 122, 255)  # #D4B87A
BRAND_SAGE = (127, 139, 108, 255)  # #7F8B6C
BRAND_INK = (41, 48, 45, 255)      # #29302D

SOURCE_COLORS = (
    ((194, 148, 66), BRAND_GOLD),
    ((103, 121, 89), BRAND_SAGE),
    ((38, 39, 41), BRAND_INK),
    ((255, 255, 255), (255, 255, 255, 0)),
)


def color_distance(a: tuple[int, int, int], b: tuple[int, int, int]) -> int:
    return sum((left - right) ** 2 for left, right in zip(a, b))


def normalize_logo(image: Image.Image) -> Image.Image:
    """Remove the white JPEG field and normalize the three brand colors."""
    source = image.convert("RGB")
    transparent = Image.new("RGBA", source.size, (255, 255, 255, 0))
    output_pixels = transparent.load()

    for y in range(source.height):
        for x in range(source.width):
            pixel = source.getpixel((x, y))
            _, target = min(SOURCE_COLORS, key=lambda item: color_distance(pixel, item[0]))
            output_pixels[x, y] = target

    bounds = transparent.getbbox()
    if bounds is None:
        raise RuntimeError("The source logo did not contain any visible artwork.")
    return transparent.crop(bounds)


def contour_path(mask: np.ndarray) -> str:
    contours, _ = cv2.findContours(mask, cv2.RETR_LIST, cv2.CHAIN_APPROX_TC89_KCOS)
    paths: list[str] = []
    for contour in contours:
        if abs(cv2.contourArea(contour)) < 10:
            continue
        simplified = cv2.approxPolyDP(contour, epsilon=0.85, closed=True)
        points = simplified.reshape(-1, 2)
        if len(points) < 3:
            continue
        commands = [f"M{points[0][0]} {points[0][1]}"]
        commands.extend(f"L{x} {y}" for x, y in points[1:])
        commands.append("Z")
        paths.append(" ".join(commands))
    return " ".join(paths)


def save_vector(image: Image.Image, svg_path: Path, title: str) -> None:
    pixels = np.asarray(image.convert("RGBA"))
    layers = []
    for color, hex_value in (
        (BRAND_GOLD[:3], "#D4B87A"),
        (BRAND_SAGE[:3], "#7F8B6C"),
        (BRAND_INK[:3], "#29302D"),
    ):
        matches = np.all(pixels[:, :, :3] == np.asarray(color), axis=2) & (pixels[:, :, 3] > 0)
        mask = (matches.astype(np.uint8) * 255)
        data = contour_path(mask)
        if data:
            layers.append(f'  <path fill="{hex_value}" fill-rule="evenodd" d="{data}"/>')

    svg = "\n".join(
        [
            f'<svg xmlns="http://www.w3.org/2000/svg" width="{image.width}" height="{image.height}" viewBox="0 0 {image.width} {image.height}" role="img" aria-labelledby="title desc">',
            f'  <title id="title">{escape(title)}</title>',
            '  <desc id="desc">Transparent PawCare veterinary clinic brand artwork in gold, sage, and charcoal.</desc>',
            *layers,
            '</svg>',
            '',
        ]
    )
    svg_path.write_text(svg, encoding="utf-8")


def main() -> None:
    if not SOURCE.exists():
        raise FileNotFoundError(SOURCE)

    OUTPUT.mkdir(parents=True, exist_ok=True)
    original = Image.open(SOURCE)

    full_logo = normalize_logo(original)
    full_png = OUTPUT / "PawCareBrand.png"
    full_svg = OUTPUT / "PawCareBrand.svg"
    full_logo.save(full_png, optimize=True)
    save_vector(full_logo, full_svg, "PawCare clinic management system")

    # The upper portion is the standalone dog, cat, cross, paw, and stethoscope mark.
    icon_source = original.crop((300, 70, 1160, 790))
    icon = normalize_logo(icon_source)
    icon_png = OUTPUT / "PawCareIcon.png"
    icon_svg = OUTPUT / "PawCareIcon.svg"
    icon.save(icon_png, optimize=True)
    save_vector(icon, icon_svg, "PawCare veterinary clinic icon")

    # Windows uses ICO for the executable while the UI uses the transparent PNG/SVG pair.
    icon.save(OUTPUT / "PawCareIcon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

    print(f"Created {full_svg}")
    print(f"Created {icon_svg}")


if __name__ == "__main__":
    main()
