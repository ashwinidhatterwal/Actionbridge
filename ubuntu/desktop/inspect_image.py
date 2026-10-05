import sys,warnings
from PIL import Image
warnings.simplefilter('error',Image.DecompressionBombWarning)
Image.MAX_IMAGE_PIXELS=80_000_000
try:
 with Image.open(sys.argv[1]) as image:
  if image.width<1 or image.height<1 or image.width*image.height>80_000_000:raise ValueError('size')
  image.verify()
except Exception:
 sys.exit(1)
