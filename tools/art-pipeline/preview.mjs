#!/usr/bin/env node
// 複数の PNG を整数倍に拡大し、市松模様の上に横並びにしたプレビューを作る。各画像の統計も表示する。
//   node tools/art-pipeline/preview.mjs --scale 4 --out art-work/preview.png a.png b.png ...
import { readPng, writePng, checker, blit, stats } from './png.mjs'

const argv = process.argv.slice(2)
let scale = 4, out = 'art-work/preview.png', gap = 16
const files = []
for (let i = 0; i < argv.length; i++) {
  if (argv[i] === '--scale') scale = Number(argv[++i])
  else if (argv[i] === '--out') out = argv[++i]
  else if (argv[i] === '--gap') gap = Number(argv[++i])
  else files.push(argv[i])
}
const images = files.map(f => ({ f, img: readPng(f) }))
const width = images.reduce((w, { img }) => w + img.width * scale + gap, gap)
const height = Math.max(...images.map(({ img }) => img.height * scale)) + gap * 2
const canvas = checker(width, height, 8)
let x = gap
for (const { f, img } of images) {
  blit(canvas, img, x, gap, scale)
  x += img.width * scale + gap
  const s = stats(img)
  console.log(`${f}: ${img.width}x${img.height} colors=${s.colors} semiAlpha=${s.semi} bounds=${JSON.stringify(s.bounds)}`)
}
writePng(out, canvas)
console.log(`preview -> ${out} (${width}x${height})`)
