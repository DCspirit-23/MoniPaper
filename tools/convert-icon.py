#!/usr/bin/env python3
"""Convert the approved PaperCare PNG into a transparent multi-size ICO.

This tool only changes the container and resolution. It does not draw, retouch,
or otherwise alter the source artwork.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)
CONTACT_SIZES = (16, 32, 48, 256)


def square_canvas(image: Image.Image) -> Image.Image:
    """Place a non-square source on a transparent square without cropping it."""

    image = image.convert("RGBA")
    side = max(image.width, image.height)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    left = (side - image.width) // 2
    top = (side - image.height) // 2
    canvas.alpha_composite(image, (left, top))
    return canvas


def resized(source: Image.Image, size: int) -> Image.Image:
    return source.resize((size, size), Image.Resampling.LANCZOS)


def write_ico(source_path: Path, output_path: Path) -> None:
    source = square_canvas(Image.open(source_path))
    output_path.parent.mkdir(parents=True, exist_ok=True)
    # Pillow's ICO writer emits all requested PNG-compressed frames and keeps
    # the source alpha channel intact.
    largest = resized(source, 256)
    largest.save(output_path, format="ICO", sizes=[(size, size) for size in SIZES])


def write_contact_sheet(source_path: Path, output_path: Path) -> None:
    source = square_canvas(Image.open(source_path))
    cell_width = 300
    cell_height = 270
    sheet = Image.new("RGBA", (cell_width * len(CONTACT_SIZES), cell_height * 2), (0, 0, 0, 0))
    draw = ImageDraw.Draw(sheet)
    font = None
    for font_path in (
        Path(r"C:\Windows\Fonts\msyh.ttc"),
        Path(r"C:\Windows\Fonts\simhei.ttf"),
        Path("segoeui.ttf"),
    ):
        try:
            font = ImageFont.truetype(str(font_path), 18)
            break
        except OSError:
            continue
    if font is None:
        font = ImageFont.load_default()

    for row, background in enumerate(((247, 241, 229, 255), (31, 39, 43, 255))):
        for column, size in enumerate(CONTACT_SIZES):
            left = column * cell_width
            top = row * cell_height
            sheet.paste(background, (left, top, left + cell_width, top + cell_height))
            icon = resized(source, size)
            display_size = 180 if size < 256 else 220
            preview = icon.resize((display_size, display_size), Image.Resampling.NEAREST)
            x = left + (cell_width - display_size) // 2
            y = top + 35
            sheet.alpha_composite(preview, (x, y))
            label_color = (36, 54, 47, 255) if row == 0 else (245, 242, 232, 255)
            draw.text((left + 12, top + 10), f"{'浅色' if row == 0 else '深色'} · {size}px", fill=label_color, font=font)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(output_path, format="PNG")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="approved source PNG")
    parser.add_argument("output", type=Path, help="destination ICO")
    parser.add_argument("--contact-sheet", type=Path, help="optional visual QA PNG path")
    args = parser.parse_args()

    if not args.source.is_file():
        parser.error(f"source PNG not found: {args.source}")

    write_ico(args.source, args.output)
    if args.contact_sheet:
        write_contact_sheet(args.source, args.contact_sheet)

    with Image.open(args.output) as icon:
        actual_sizes = sorted((width, height) for width, height in icon.ico.sizes())
    expected_sizes = sorted((size, size) for size in SIZES)
    if actual_sizes != expected_sizes:
        raise RuntimeError(f"ICO sizes mismatch: expected {expected_sizes}, got {actual_sizes}")
    print(f"ICO: {args.output.resolve()}")
    print(f"sizes: {actual_sizes}")
    if args.contact_sheet:
        print(f"contact sheet: {args.contact_sheet.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
