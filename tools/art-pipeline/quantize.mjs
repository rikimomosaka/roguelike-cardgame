#!/usr/bin/env node
// 不透明ピクセルの色数を k 色へ減色する（k-means、決定的初期化）。ディザなし。
//   node tools/art-pipeline/quantize.mjs --in src.png --out dst.png [--colors 24] [--iter 30]
import { readPng, writePng, stats } from './png.mjs'

const a = {}
const argv = process.argv.slice(2)
for (let i = 0; i < argv.length; i += 2) a[argv[i].replace(/^--/, '')] = argv[i + 1]
const K = Number(a.colors ?? 24), ITER = Number(a.iter ?? 30)
const img = readPng(a.in)

const px = []
for (let i = 0; i < img.width * img.height; i++) {
  if (img.data[i * 4 + 3] === 0) continue
  px.push([img.data[i * 4], img.data[i * 4 + 1], img.data[i * 4 + 2], i])
}
const d2 = (p, c) => (p[0] - c[0]) ** 2 + (p[1] - c[1]) ** 2 + (p[2] - c[2]) ** 2

// k-means++ を決定的に（毎回、最も遠い点を次の中心にする）。
const centers = [px.reduce((s, p) => [s[0] + p[0] / px.length, s[1] + p[1] / px.length, s[2] + p[2] / px.length], [0, 0, 0])]
const nearest = px.map(p => d2(p, centers[0]))
while (centers.length < Math.min(K, px.length)) {
  let best = 0
  for (let i = 1; i < px.length; i++) if (nearest[i] > nearest[best]) best = i
  const c = px[best].slice(0, 3)
  centers.push(c)
  for (let i = 0; i < px.length; i++) nearest[i] = Math.min(nearest[i], d2(px[i], c))
}

const assign = new Array(px.length).fill(0)
for (let it = 0; it < ITER; it++) {
  let changed = 0
  for (let i = 0; i < px.length; i++) {
    let bi = 0, bd = Infinity
    for (let k = 0; k < centers.length; k++) { const d = d2(px[i], centers[k]); if (d < bd) { bd = d; bi = k } }
    if (assign[i] !== bi) { assign[i] = bi; changed++ }
  }
  const sum = centers.map(() => [0, 0, 0, 0])
  px.forEach((p, i) => { const s = sum[assign[i]]; s[0] += p[0]; s[1] += p[1]; s[2] += p[2]; s[3]++ })
  sum.forEach((s, k) => { if (s[3]) centers[k] = [s[0] / s[3], s[1] / s[3], s[2] / s[3]] })
  if (changed === 0 && it > 0) break
}

px.forEach((p, i) => {
  const c = centers[assign[i]], o = p[3] * 4
  img.data[o] = Math.round(c[0]); img.data[o + 1] = Math.round(c[1]); img.data[o + 2] = Math.round(c[2])
})
writePng(a.out, img)
console.log(`quantize ${a.in} -> ${a.out} colors=${stats(img).colors}`)
