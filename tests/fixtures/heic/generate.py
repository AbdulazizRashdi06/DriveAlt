"""Author the synthetic HEVC/HEIC decoder fixture (requires Pillow and pillow-heif).

Run only to regenerate the checked-in fixture; image builds use the saved bytes.
There is no source photo or student material in this fixture.
"""
from pathlib import Path

from PIL import Image, ImageDraw
import pillow_heif

pillow_heif.register_heif_opener()
image = Image.new("RGB", (96, 64), "white")
draw = ImageDraw.Draw(image)
draw.rectangle((0, 0, 47, 31), fill="red")
draw.rectangle((48, 0, 95, 31), fill="green")
draw.rectangle((0, 32, 47, 63), fill="blue")
image.save(Path(__file__).with_name("color-blocks.heic"), format="HEIF", quality=60)
