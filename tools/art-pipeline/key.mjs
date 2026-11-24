#!/usr/bin/env node
// 単色グリーン背景（#00FF00 想定）を透過にする。縁の緑かぶりは緑成分を抑えて補正する。
//   node tools/art-pipeline/key.mjs --in concept.png --out keyed.png [--margin 40]
import { readPng, writePng, stats } from './png.mjs'

const a = {}
const argv = process.argv.slice(2)
for (let i = 0; i < argv.length; i += 2) a[argv[i].replace(/^--/, '')] = argv[i + 1]
const margin = Number(a.margin ?? 40)
const img = readPng(a.in)
let removed = 0, despilled = 0
for (let i = 0; i < img.width * img.height; i++) {
  const o = i * 4
  const r = img.data[o], g = img.data[o + 1], b = img.data[o + 2]
  const other = Math.max(r, b)
  if (g - other > margin && g > 120) {
    img.data[o + 3] = 0
    removed++
  } else if (g > other) {
    // 緑かぶり（アンチエイリアスの縁）: 緑を周囲の色に寄せる。
    img.data[o + 1] = other
    despilled++
  }
}
writePng(a.out, img)
const s = stats(img)
console.log(`key ${a.in} -> ${a.out} removed=${removed} despilled=${despilled} bounds=${JSON.stringify(s.bounds)}`)
