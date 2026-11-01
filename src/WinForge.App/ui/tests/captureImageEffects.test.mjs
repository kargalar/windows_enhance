import assert from 'node:assert/strict';
import test from 'node:test';
import { applyRegionEffect, blurPixels, pixelatePixels } from '../src/features/captureImageEffects.ts';

function fixture(width, height) {
  const data = Uint8ClampedArray.from({ length: width * height * 4 }, (_, index) => (index * 37) % 256);
  return { width, height, data };
}

test('blur matches a reference box filter and preserves alpha, including narrow regions', () => {
  for (const [width, height] of [[1, 1], [1, 25], [29, 1], [37, 23]]) {
    const image = fixture(width, height);
    const original = image.data.slice();
    const horizontal = original.slice();
    const expected = original.slice();
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) for (let c = 0; c < 3; c++) {
      let sum = 0, count = 0;
      for (let sx = Math.max(0, x - 8); sx <= Math.min(width - 1, x + 8); sx++) { sum += original[(y * width + sx) * 4 + c]; count++; }
      horizontal[(y * width + x) * 4 + c] = sum / count;
    }
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) for (let c = 0; c < 3; c++) {
      let sum = 0, count = 0;
      for (let sy = Math.max(0, y - 8); sy <= Math.min(height - 1, y + 8); sy++) { sum += horizontal[(sy * width + x) * 4 + c]; count++; }
      expected[(y * width + x) * 4 + c] = sum / count;
    }
    blurPixels(image);
    assert.deepEqual(image.data, expected);
  }
});

test('pixelation averages blocks and keeps each alpha value', () => {
  const image = fixture(19, 19);
  const original = image.data.slice();
  pixelatePixels(image);
  for (let index = 3; index < image.data.length; index += 4) assert.equal(image.data[index], original[index]);
  assert.deepEqual(image.data.slice(0, 3), image.data.slice((17 * 19 + 17) * 4, (17 * 19 + 17) * 4 + 3));
  assert.deepEqual(image.data.slice(-4), original.slice(-4));
  const blurred = fixture(19, 19);
  blurPixels(blurred);
  assert.notDeepEqual(blurred.data, image.data);
});

test('effects clip to image bounds and write only the selected region', () => {
  for (const blur of [false, true]) {
    let written;
    const context = {
      canvas: { width: 100, height: 80 },
      getImageData(x, y, width, height) {
        assert.deepEqual([x, y, width, height], [94, 73, 6, 7]);
        return fixture(width, height);
      },
      putImageData(image, x, y) { written = [x, y, image.width, image.height]; },
    };
    applyRegionEffect(context, { x: 94, y: 73, width: 20, height: 20 }, blur);
    assert.deepEqual(written, [94, 73, 6, 7]);
    context.getImageData = () => assert.fail('empty or offscreen selection must be ignored');
    applyRegionEffect(context, { x: 110, y: 90, width: 20, height: 20 }, blur);
    applyRegionEffect(context, { x: 10, y: 10, width: 0, height: 10 }, blur);
  }
});
