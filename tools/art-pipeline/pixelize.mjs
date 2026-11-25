#!/usr/bin/env node
// 高解像度の「ドット絵風」画像を、本物のドット絵（低解像度・少色・半透明なし）に変換する。
//   node tools/art-pipeline/pixelize.mjs --in src.png --out dst.png --w 418 [--h 235] [--colors 64] [--preview prev.png]
// - 縮小は面積平均（アルファ加重）。--h 省略時は縦横比を保つ。
// - 減色は k-means（決定的初期化、ディザなし）。
// - アルファは 0/255 に二値化（しきい値 --alpha 128）。
// - --preview を付けると、元のサイズへ最近傍で戻した画像を書き出す（見た目確認用）。
import fs from 'node:fs'
import { readPng, writePng, createImage, blit } from './png.mjs'

const a = {}
const argv = process.argv.slice(2)
for (let i = 0; i < argv.length; i += 2) a[argv[i].replace(/^--/, '')] = argv[i + 1]
const src = readPng(a.in)
const W = Number(a.w), H = Number(a.h ?? Math.round(src.height * W / src.width))
const K = Number(a.colors ?? 64), ALPHA = Number(a.alpha ?? 128)

// --- 面積平均で縮小 ---
const dst = createImage(W, H)
const sx = src.width / W, sy = src.height / H
for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
  const x0 = x * sx, x1 = (x + 1) * sx, y0 = y * sy, y1 = (y + 1) * sy
  let r = 0, g = 0, b = 0, al = 0, area = 0
  for (let yy = Math.floor(y0); yy < Math.ceil(y1); yy++) {
    const wy = Math.min(yy + 1, y1) - Math.max(yy, y0)
    for (let xx = Math.floor(x0); xx < Math.ceil(x1); xx++) {
      const w = (Math.min(xx + 1, x1) - Math.max(xx, x0)) * wy
      const i = (yy * src.width + xx) * 4
      const alpha = src.data[i + 3] / 255 * w
      r += src.data[i] * alpha; g += src.data[i + 1] * alpha; b += src.data[i + 2] * alpha
      al += alpha; area += w
    }
  }
  const o = (y * W + x) * 4
  if (al / area * 255 < ALPHA) continue
  dst.data[o] = r / al; dst.data[o + 1] = g / al; dst.data[o + 2] = b / al; dst.data[o + 3] = 255
}

// --- k-means 減色 ---
const px = []
for (let i = 0; i < W * H; i++) if (dst.data[i * 4 + 3]) px.push(i)
const col = i => [dst.data[i * 4], dst.data[i * 4 + 1], dst.data[i * 4 + 2]]
const d2 = (p, c) => (p[0] - c[0]) ** 2 + (p[1] - c[1]) ** 2 + (p[2] - c[2]) ** 2
const pts = px.map(col)
const centers = []
if (pts.length) {
  centers.push(pts.reduce((s, p) => [s[0] + p[0] / pts.length, s[1] + p[1] / pts.length, s[2] + p[2] / pts.length], [0, 0, 0]))
  const near = pts.map(p => d2(p, centers[0]))
  while (centers.length < Math.min(K, pts.length)) {
    let best = 0
    for (let i = 1; i < pts.length; i++) if (near[i] > near[best]) best = i
    if (near[best] === 0) break
    centers.push(pts[best].slice())
    for (let i = 0; i < pts.length; i++) near[i] = Math.min(near[i], d2(pts[i], pts[best]))
  }
}
const assign = new Int32Array(pts.length)
for (let it = 0; it < 24; it++) {
  let changed = 0
  for (let i = 0; i < pts.length; i++) {
    let bi = 0, bd = Infinity
    for (let k = 0; k < centers.length; k++) { const d = d2(pts[i], centers[k]); if (d < bd) { bd = d; bi = k } }
    if (assign[i] !== bi) { assign[i] = bi; changed++ }
  }
  const sum = centers.map(() => [0, 0, 0, 0])
  pts.forEach((p, i) => { const s = sum[assign[i]]; s[0] += p[0]; s[1] += p[1]; s[2] += p[2]; s[3]++ })
  sum.forEach((s, k) => { if (s[3]) centers[k] = [s[0] / s[3], s[1] / s[3], s[2] / s[3]] })
  if (!changed && it) break
}
px.forEach((pi, i) => {
  const c = centers[assign[i]]
  dst.data.set([Math.round(c[0]), Math.round(c[1]), Math.round(c[2]), 255], pi * 4)
})

writePng(a.out, dst, { palette: true })
if (a.preview) {
  const scale = Math.max(1, Math.round(src.width / W))
  const prev = createImage(W * scale, H * scale)
  blit(prev, dst, 0, 0, scale)
  writePng(a.preview, prev)
}
const kb = (f) => (fs.statSync(f).size / 1024).toFixed(1)
console.log(`pixelize ${a.in} (${src.width}x${src.height}, ${kb(a.in)}KB) -> ${a.out} ${W}x${H} colors=${centers.length} ${kb(a.out)}KB`)
