#!/usr/bin/env node
// 画像の不透明部分を切り出し、縦横比を保って縮小し、規格キャンバスへ足元基準で配置する。
//   node tools/art-pipeline/fit.mjs --in src.png --out dst.png --w 80 --h 80 [--margin 4] [--max-h 76]
// 縮小は面積平均（アルファ加重）。半透明は 0/255 に二値化する（しきい値 128）。
import { readPng, writePng, createImage, stats } from './png.mjs'

const a = {}
const argv = process.argv.slice(2)
for (let i = 0; i < argv.length; i += 2) a[argv[i].replace(/^--/, '')] = argv[i + 1]
const W = Number(a.w), H = Number(a.h), margin = Number(a.margin ?? 4)
const src = readPng(a.in)
const b = stats(src).bounds
if (!b) throw new Error('empty image')
const cw = b.maxX - b.minX + 1, ch = b.maxY - b.minY + 1
const maxH = Number(a['max-h'] ?? (H - margin * 2))
const maxW = W - 2
const scale = Math.min(maxH / ch, maxW / cw, 1)
const tw = Math.max(1, Math.round(cw * scale)), th = Math.max(1, Math.round(ch * scale))
const dst = createImage(W, H)
const ox = Math.floor((W - tw) / 2), oy = H - margin - th
for (let y = 0; y < th; y++) for (let x = 0; x < tw; x++) {
  const sx0 = b.minX + x / scale, sx1 = b.minX + (x + 1) / scale
  const sy0 = b.minY + y / scale, sy1 = b.minY + (y + 1) / scale
  let r = 0, g = 0, bl = 0, al = 0, n = 0
  // 倍率が割り切れないと最後の行・列で範囲の外を読む（未定義値が黒になる）ので、内容の範囲で止める。
  const syEnd = Math.min(Math.ceil(sy1), b.maxY + 1), sxEnd = Math.min(Math.ceil(sx1), b.maxX + 1)
  for (let sy = Math.floor(sy0); sy < syEnd; sy++) for (let sx = Math.floor(sx0); sx < sxEnd; sx++) {
    const i = (sy * src.width + sx) * 4
    const alpha = src.data[i + 3] / 255
    r += src.data[i] * alpha; g += src.data[i + 1] * alpha; bl += src.data[i + 2] * alpha
    al += alpha; n++
  }
  const o = ((oy + y) * W + ox + x) * 4
  if (n === 0 || al / n < 0.5) continue
  dst.data[o] = Math.round(r / al); dst.data[o + 1] = Math.round(g / al); dst.data[o + 2] = Math.round(bl / al)
  dst.data[o + 3] = 255
}
writePng(a.out, dst)
const s = stats(dst)
console.log(`fit ${a.in} -> ${a.out} ${W}x${H} content=${tw}x${th} scale=${scale.toFixed(3)} colors=${s.colors}`)
