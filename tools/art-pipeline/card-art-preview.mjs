#!/usr/bin/env node
// カードイラストを実際の枠（所属の枠 → マスクで切った絵 → 種類の枠）に重ねた確認画像を作る。
//   node tools/art-pipeline/card-art-preview.mjs --out art-work/review/cards.png [--scale 2] [--cols 6] id:type:affiliation:file ...
//   例: striker_basic_attack:attack:striker:art-work/cards/striker_basic_attack_1.png
// file を省くと src/Client/public/assets/cards/art/<id>.png を使う。
import { readPng, writePng, createImage, checker, blit } from './png.mjs'

const FRAMES = 'src/Client/public/assets/cards/frames'
const argv = process.argv.slice(2)
const opt = (k, d) => { const i = argv.indexOf(`--${k}`); return i >= 0 ? argv.splice(i, 2)[1] : d }
const out = opt('out', 'art-work/review/cards.png')
const S = Number(opt('scale', '2')), cols = Number(opt('cols', '6'))
const W = 112, H = 157, gap = 8
// Card.css の .card__art-content（left 3%, top 13.4%, 94% × 50.4%）。
const ART_X = 3, ART_Y = 21

function over(dst, src, dx = 0, dy = 0, mask = null) {
  for (let y = 0; y < src.height; y++) for (let x = 0; x < src.width; x++) {
    const X = x + dx, Y = y + dy
    if (X < 0 || Y < 0 || X >= dst.width || Y >= dst.height) continue
    const s = (y * src.width + x) * 4, d = (Y * dst.width + X) * 4
    let a = src.data[s + 3] / 255
    if (mask) a *= (mask.data[d + 3] / 255) * (mask.data[d] / 255)
    for (let c = 0; c < 3; c++) dst.data[d + c] = Math.round(src.data[s + c] * a + dst.data[d + c] * (1 - a))
    dst.data[d + 3] = Math.max(dst.data[d + 3], Math.round(a * 255))
  }
}

const cards = argv.map(spec => {
  const [id, type, aff, file] = spec.split(':')
  const card = createImage(W, H)
  over(card, readPng(`${FRAMES}/affiliations/${aff}.png`))
  over(card, readPng(file ?? `src/Client/public/assets/cards/art/${id}.png`), ART_X, ART_Y, readPng(`${FRAMES}/masks/${type}.png`))
  over(card, readPng(`${FRAMES}/types/${type}.png`))
  return card
})
const rows = Math.ceil(cards.length / cols)
const sheet = checker(cols * (W * S + gap) + gap, rows * (H * S + gap) + gap, 8)
cards.forEach((c, i) => blit(sheet, c, gap + (i % cols) * (W * S + gap), gap + Math.floor(i / cols) * (H * S + gap), S))
writePng(out, sheet)
console.log('wrote', out)
