type Region = { x: number; y: number; width: number; height: number };
const PIXEL_BLOCK_SIZE = 18;
const BLUR_RADIUS = 8;
const CHANNELS = 4;

/** Effects read and write only the selected pixels, preserving alpha. */
export function applyRegionEffect(context: CanvasRenderingContext2D, region: Region, blur: boolean) {
  const x = Math.max(0, Math.floor(region.x));
  const y = Math.max(0, Math.floor(region.y));
  const width = Math.min(context.canvas.width - x, Math.ceil(region.width));
  const height = Math.min(context.canvas.height - y, Math.ceil(region.height));
  if (width <= 0 || height <= 0) return;
  const image = context.getImageData(x, y, width, height);
  if (blur) blurPixels(image);
  else pixelatePixels(image);
  context.putImageData(image, x, y);
}

export function pixelatePixels(image: ImageData) {
  const { data, width, height } = image;
  for (let top = 0; top < height; top += PIXEL_BLOCK_SIZE) {
    for (let left = 0; left < width; left += PIXEL_BLOCK_SIZE) {
      const right = Math.min(width, left + PIXEL_BLOCK_SIZE);
      const bottom = Math.min(height, top + PIXEL_BLOCK_SIZE);
      const sums = [0, 0, 0];
      for (let y = top; y < bottom; y++) for (let x = left; x < right; x++) {
        const offset = (y * width + x) * CHANNELS;
        for (let channel = 0; channel < 3; channel++) sums[channel] += data[offset + channel];
      }
      const count = (right - left) * (bottom - top);
      for (let y = top; y < bottom; y++) for (let x = left; x < right; x++) {
        const offset = (y * width + x) * CHANNELS;
        for (let channel = 0; channel < 3; channel++) data[offset + channel] = sums[channel] / count;
      }
    }
  }
}

/** Two sliding-window passes keep blur linear in the number of selected pixels. */
export function blurPixels(image: ImageData) {
  const { width, height, data } = image;
  const horizontal = new Uint8ClampedArray(data.length);
  const pass = (source: Uint8ClampedArray, target: Uint8ClampedArray, vertical: boolean) => {
    const length = vertical ? height : width;
    const lines = vertical ? width : height;
    const offset = (line: number, position: number, channel: number) =>
      ((vertical ? position * width + line : line * width + position) * CHANNELS) + channel;
    for (let line = 0; line < lines; line++) for (let channel = 0; channel < 3; channel++) {
      let sum = 0;
      for (let position = 0; position <= Math.min(BLUR_RADIUS, length - 1); position++) sum += source[offset(line, position, channel)];
      for (let position = 0; position < length; position++) {
        const start = Math.max(0, position - BLUR_RADIUS);
        const end = Math.min(length - 1, position + BLUR_RADIUS);
        target[offset(line, position, channel)] = sum / (end - start + 1);
        if (position >= BLUR_RADIUS) sum -= source[offset(line, position - BLUR_RADIUS, channel)];
        if (position + BLUR_RADIUS + 1 < length) sum += source[offset(line, position + BLUR_RADIUS + 1, channel)];
      }
    }
  };
  pass(data, horizontal, false);
  pass(horizontal, data, true);
}
