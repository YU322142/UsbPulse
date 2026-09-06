"""Reproduce the supplied two-segment tray mark at multiple native icon sizes."""
from pathlib import Path
from PIL import Image, ImageDraw

assets = Path(__file__).resolve().parent.parent / "Assets"
for name, color in [("UsbPulse", "black"), ("UsbPulse-light", "white")]:
    image = Image.new("RGBA", (1024, 1024))
    draw = ImageDraw.Draw(image)
    draw.arc((100, 100, 924, 924), 232, 307, fill=color, width=152)
    draw.arc((100, 100, 924, 924), 320, 580, fill=color, width=152)
    image.resize((256, 256), Image.Resampling.LANCZOS).save(assets / f"{name}.ico",
        sizes=[(s, s) for s in [16, 20, 24, 32, 48, 64, 128, 256]])
    image.resize((128, 128), Image.Resampling.LANCZOS).save(assets / f"{name}.png")
