// 依存なしの PNG 読み書き（8bit RGB/RGBA/パレット、非インターレース）と簡単な画像操作。
import fs from 'node:fs'
import zlib from 'node:zlib'

/** @returns {{width:number,height:number,data:Buffer}} data は RGBA */
export function readPng(file) {
  const buf = fs.readFileSync(file)
  let pos = 8
  let width = 0, height = 0, colorType = 0, bitDepth = 0, interlace = 0
  let palette = null, trns = null
  const idat = []
  while (pos < buf.length) {
    const len = buf.readUInt32BE(pos)
    const type = buf.toString('ascii', pos + 4, pos + 8)
    const data = buf.subarray(pos + 8, pos + 8 + len)
    if (type === 'IHDR') {
      width = data.readUInt32BE(0); height = data.readUInt32BE(4)
      bitDepth = data[8]; colorType = data[9]; interlace = data[12]
    } else if (type === 'PLTE') palette = data
    else if (type === 'tRNS') trns = data
    else if (type === 'IDAT') idat.push(data)
    else if (type === 'IEND') break
    pos += 12 + len
  }
  if (interlace) throw new Error(`${file}: interlaced PNG is not supported`)
  const channels = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 }[colorType]
  if (bitDepth !== 8 && !(colorType === 3 && [1, 2, 4].includes(bitDepth))) {
    throw new Error(`${file}: unsupported bit depth ${bitDepth} (type ${colorType})`)
  }
  const bitsPerPixel = channels * bitDepth
  const bpp = Math.max(1, bitsPerPixel >> 3)
  const stride = Math.ceil(width * bitsPerPixel / 8)
  const raw = zlib.inflateSync(Buffer.concat(idat))
  const out = Buffer.alloc(width * height * 4)
  let prev = Buffer.alloc(stride)
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)]
    const line = Buffer.from(raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1)))
    for (let x = 0; x < stride; x++) {
      const a = x >= bpp ? line[x - bpp] : 0
      const b = prev[x]
      const c = x >= bpp ? prev[x - bpp] : 0
      let v = line[x]
      if (filter === 1) v += a
      else if (filter === 2) v += b
      else if (filter === 3) v += (a + b) >> 1
      else if (filter === 4) {
        const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c)
        v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c
      }
      line[x] = v & 255
    }
    for (let x = 0; x < width; x++) {
      const o = (y * width + x) * 4
      if (colorType === 3) {
        const perByte = 8 / bitDepth
        const byte = line[Math.floor(x / perByte)]
        const shift = (perByte - 1 - (x % perByte)) * bitDepth
        const idx = (byte >> shift) & ((1 << bitDepth) - 1)
        out[o] = palette[idx * 3]; out[o + 1] = palette[idx * 3 + 1]; out[o + 2] = palette[idx * 3 + 2]
        out[o + 3] = trns && idx < trns.length ? trns[idx] : 255
      } else if (colorType === 6) {
        line.copy(out, o, x * 4, x * 4 + 4)
      } else if (colorType === 2) {
        out[o] = line[x * 3]; out[o + 1] = line[x * 3 + 1]; out[o + 2] = line[x * 3 + 2]; out[o + 3] = 255
      } else if (colorType === 4) {
        out[o] = out[o + 1] = out[o + 2] = line[x * 2]; out[o + 3] = line[x * 2 + 1]
      } else {
        out[o] = out[o + 1] = out[o + 2] = line[x]; out[o + 3] = 255
      }
    }
    prev = line
  }
  return { width, height, data: out }
}

const CRC_TABLE = (() => {
  const t = new Uint32Array(256)
  for (let n = 0; n < 256; n++) {
    let c = n
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
    t[n] = c >>> 0
  }
  return t
})()

function crc32(buf) {
  let c = 0xffffffff
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8)
  return (c ^ 0xffffffff) >>> 0
}

function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length)
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data])
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td))
  return Buffer.concat([len, td, crc])
}

/** RGBA 画像を PNG（RGBA 8bit）で保存する。 */
export function writePng(file, image) {
  const { width, height, data } = image
  const ihdr = Buffer.alloc(13)
  ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4)
  ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0
  const raw = Buffer.alloc((width * 4 + 1) * height)
  for (let y = 0; y < height; y++) {
    raw[y * (width * 4 + 1)] = 0
    data.copy(raw, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4)
  }
  const png = Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ])
  fs.writeFileSync(file, png)
}

export function createImage(width, height, rgba = [0, 0, 0, 0]) {
  const data = Buffer.alloc(width * height * 4)
  for (let i = 0; i < width * height; i++) data.set(rgba, i * 4)
  return { width, height, data }
}

/** src を dst の (dx,dy) へ scale 倍（最近傍）でアルファ合成する。 */
export function blit(dst, src, dx, dy, scale = 1) {
  for (let y = 0; y < src.height * scale; y++) {
    for (let x = 0; x < src.width * scale; x++) {
      const tx = dx + x, ty = dy + y
      if (tx < 0 || ty < 0 || tx >= dst.width || ty >= dst.height) continue
      const s = (Math.floor(y / scale) * src.width + Math.floor(x / scale)) * 4
      const a = src.data[s + 3] / 255
      if (a === 0) continue
      const d = (ty * dst.width + tx) * 4
      for (let k = 0; k < 3; k++) dst.data[d + k] = Math.round(src.data[s + k] * a + dst.data[d + k] * (1 - a))
      dst.data[d + 3] = Math.max(dst.data[d + 3], src.data[s + 3])
    }
  }
}

/** 市松模様の背景（透明部分の確認用）。 */
export function checker(width, height, cell = 8) {
  const img = createImage(width, height)
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const light = ((Math.floor(x / cell) + Math.floor(y / cell)) % 2) === 0
    img.data.set(light ? [58, 52, 66, 255] : [44, 40, 50, 255], (y * width + x) * 4)
  }
  return img
}

/** 色数・半透明ピクセル数・不透明範囲を返す。 */
export function stats(image) {
  const colors = new Set()
  let semi = 0, opaque = 0
  let minX = image.width, minY = image.height, maxX = -1, maxY = -1
  for (let y = 0; y < image.height; y++) for (let x = 0; x < image.width; x++) {
    const i = (y * image.width + x) * 4
    const a = image.data[i + 3]
    if (a === 0) continue
    if (a < 255) semi++
    else opaque++
    colors.add(image.data.readUInt32BE(i))
    if (x < minX) minX = x; if (y < minY) minY = y
    if (x > maxX) maxX = x; if (y > maxY) maxY = y
  }
  return { colors: colors.size, semi, opaque, bounds: maxX < 0 ? null : { minX, minY, maxX, maxY } }
}
