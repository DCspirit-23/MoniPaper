# MoniPaper icon

The icon was originally created for PaperCare and is retained after the rename to MoniPaper. The original generation prompt below is preserved unchanged.

The icon uses an ivory folded paper sheet, a muted sage backing sheet, and three rounded reading lines on a forest-green tile. It contains no eyes, faces, or human features.

The source image was created with the built-in ImageGen tool. `papercare.png` is the source asset; `papercare.ico` contains the Windows icon sizes derived from it. The original image is retained with its transparency and provenance metadata.

## Rebuild the Windows icon

The application build uses the checked-in ICO and does not require Python. To regenerate the ICO after changing the source PNG, use Python with Pillow installed:

```text
python tools/convert-icon.py Assets/papercare.png Assets/papercare.ico --contact-sheet artifacts/icon-contact-sheet.png
```

The converter includes 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixel frames. It only changes format and resolution; it does not redraw or retouch the source artwork.

## Generation prompt

Use case: logo-brand. Asset type: finished Windows desktop application icon for PaperCare, a calm paper-texture reading comfort utility. Design ONE beautiful, minimalist, reassuring PAPER-ONLY app icon. Square composition with true transparent alpha exterior. A centered deep forest-green (#214F40) rounded-square/squircle tile occupying about 90% of the canvas. On it, one elegant ivory sheet of thick matte paper with a softly curled top-right corner, and one offset muted-sage sheet behind it. The front ivory sheet has only THREE simple short parallel horizontal sage reading lines with softly rounded ends. Ordinary horizontal reading lines only, no enclosing outline and no dots. Keep the composition restrained, friendly, sophisticated and instantly legible at 32px. Front-facing, balanced, substantial shapes, soft edges, subtle tactile paper shading, warm ivory and forest green. No eyes whatsoever: no eye symbols, no almond shapes, no pupils, no human features, no faces, no figures. No text, no letters, no magnifying glass, no shields, no medical symbols, no watermark, no device mockup, no decorative clutter, no shiny plastic. The area outside the tile must be truly transparent, not white or a painted checkerboard. Finished singular app icon, not a sheet of alternatives.
