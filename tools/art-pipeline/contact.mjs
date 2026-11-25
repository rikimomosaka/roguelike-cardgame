#!/usr/bin/env node
// フォルダ内の PNG を一覧にした確認用画像を作る（市松模様の上に整数倍で並べる）。
//   node tools/art-pipeline/contact.mjs --dir src/Client/public/assets/icons/relics --scale 2 --cols 11 --out art-work/review/relics.png [--first-frame]
// サブフォルダの stand.png も拾う（戦闘キャラ用）。--first-frame はシートの 1 コマ目だけを並べる（正方形のコマ前提）。
import fs from 'node:fs'
import path from 'node:path'
import { readPng, writePng, checker, blit } from './png.mjs'
import { sub } from './lib/pixel.mjs'

const a = {}
const argv = process.argv.slice(2)
for (let i = 0; i < argv.length; i++) {
  if (argv[i] === '--first-frame') a['first-frame'] = true
  else { a[argv[i].replace(/^--/, '')] = argv[i + 1]; i++ }
}
const scale = Number(a.scale ?? 2), cols = Number(a.cols ?? 8), gap = 6
const files = []
for (const f of fs.readdirSync(a.dir).sort()) {
  const p = path.join(a.dir, f)
  if (fs.statSync(p).isDirectory()) { const s = path.join(p, 'stand.png'); if (fs.existsSync(s)) files.push(s) }
  else if (f.endsWith('.png')) files.push(p)
}
const imgs = files.map(f => {
  let im = readPng(f)
  if (a['first-frame'] && im.width > im.height) im = sub(im, 0, 0, im.height, im.height)
  return im
})
const cw = Math.max(...imgs.map(i => i.width)) * scale, ch = Math.max(...imgs.map(i => i.height)) * scale
const rows = Math.ceil(imgs.length / cols)
const out = checker(cols * (cw + gap) + gap, rows * (ch + gap) + gap, 8)
imgs.forEach((im, i) => blit(out, im, gap + (i % cols) * (cw + gap), gap + Math.floor(i / cols) * (ch + gap), scale))
fs.mkdirSync(path.dirname(a.out), { recursive: true })
writePng(a.out, out)
console.log(`${files.length} images -> ${a.out}`)
files.forEach((f, i) => { if (i % cols === 0) process.stdout.write(`\nrow ${Math.floor(i / cols)}: `); process.stdout.write(path.basename(f === path.join(path.dirname(f), 'stand.png') ? path.dirname(f) : f).replace('.png', '') + ' ') })
console.log()
