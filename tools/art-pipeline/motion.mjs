#!/usr/bin/env node
// PixelLab のアニメーション出力（frame_NN.png）を整えて、原寸の横並びシートとゲーム用シートを作る。
//   node tools/art-pipeline/motion.mjs --dir gen/attack_a --palette base.png --out-dir final/attack_a
//     [--skip-first 1]        先頭フレーム（入力画像のコピー）を捨てる（既定 1）
//     [--extra 8]             基本パレットから遠い色（エフェクト等）に追加で割り当てる最大色数
//     [--main-only 1]         本体とつながっていない塊（浮いたエフェクト）を消す
//     [--clear-above 17]      この行より上を消す（頭上に残ったエフェクト用）
//     [--offset-x -2,-3,-1,0]フレームごとの横ずらし（原寸 px、ノックバック等）
//     [--tint 1,0,0,0]        フレームごとの赤フラッシュ（1 = 適用）
//     [--anchor-x 63.5]       原寸での体の横中心（ゲーム用シートでセル中央に合わせる）
//     [--bottom-row 93]       原寸での足元の行（ゲーム用シートで --bottom に合わせる）
//     [--scale 3 --cell-w 384 --cell-h 256 --bottom 245]
// 出力: frame_NN.png（整形後の原寸）, strip.png（原寸横並び）, game.png（ゲーム用）
import fs from 'node:fs'
import path from 'node:path'
import { readPng, writePng, createImage, blit, stats } from './png.mjs'

const a = {}
const argv = process.argv.slice(2)
for (let i = 0; i < argv.length; i += 2) a[argv[i].replace(/^--/, '')] = argv[i + 1]
const list = (v) => (v ?? '').split(',').filter(s => s !== '').map(Number)

const files = fs.readdirSync(a.dir).filter(f => /^frame_\d+\.png$/.test(f)).sort()
  .slice(Number(a['skip-first'] ?? 1))
const frames = files.map(f => readPng(path.join(a.dir, f)))
const W = frames[0].width, H = frames[0].height

// --- パレット: 基本画像の色 + 遠い色の k-means（最大 --extra 色） ---
const base = readPng(a.palette)
const palette = []
const seen = new Set()
for (let i = 0; i < base.data.length; i += 4) {
  if (!base.data[i + 3]) continue
  const key = base.data.readUInt32BE(i)
  if (!seen.has(key)) { seen.add(key); palette.push([base.data[i], base.data[i + 1], base.data[i + 2]]) }
}
const d2 = (p, c) => (p[0] - c[0]) ** 2 + (p[1] - c[1]) ** 2 + (p[2] - c[2]) ** 2
const nearest = (p, pal) => { let bi = 0, bd = Infinity; pal.forEach((c, k) => { const d = d2(p, c); if (d < bd) { bd = d; bi = k } }); return [bi, bd] }
const FAR = 45 ** 2
const outliers = []
for (const fr of frames) for (let i = 0; i < fr.data.length; i += 4) {
  if (!fr.data[i + 3]) continue
  const p = [fr.data[i], fr.data[i + 1], fr.data[i + 2]]
  if (nearest(p, palette)[1] > FAR) outliers.push(p)
}
const EXTRA = Math.min(Number(a.extra ?? 8), outliers.length)
if (EXTRA > 0) {
  const centers = [outliers[0]]
  const dist = outliers.map(p => d2(p, centers[0]))
  while (centers.length < EXTRA) {
    let best = 0
    for (let i = 1; i < outliers.length; i++) if (dist[i] > dist[best]) best = i
    if (dist[best] === 0) break
    centers.push(outliers[best])
    outliers.forEach((p, i) => { dist[i] = Math.min(dist[i], d2(p, outliers[best])) })
  }
  for (let it = 0; it < 20; it++) {
    const sum = centers.map(() => [0, 0, 0, 0])
    for (const p of outliers) { const [k] = nearest(p, centers); sum[k][0] += p[0]; sum[k][1] += p[1]; sum[k][2] += p[2]; sum[k][3]++ }
    sum.forEach((s, k) => { if (s[3]) centers[k] = [s[0] / s[3], s[1] / s[3], s[2] / s[3]].map(Math.round) })
  }
  palette.push(...centers)
}

// --- 本体以外の浮いた塊を消す（4 近傍の連結成分で、最大の塊とその近くだけ残す） ---
function keepMain(fr) {
  const label = new Int32Array(W * H).fill(-1)
  const sizes = []
  for (let s = 0; s < W * H; s++) {
    if (label[s] >= 0 || !fr.data[s * 4 + 3]) continue
    const id = sizes.length
    let n = 0
    const stack = [s]
    label[s] = id
    while (stack.length) {
      const q = stack.pop(); n++
      const x = q % W, y = (q / W) | 0
      for (const [nx, ny] of [[x + 1, y], [x - 1, y], [x, y + 1], [x, y - 1]]) {
        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue
        const r = ny * W + nx
        if (label[r] < 0 && fr.data[r * 4 + 3]) { label[r] = id; stack.push(r) }
      }
    }
    sizes.push(n)
  }
  const main = sizes.indexOf(Math.max(...sizes))
  for (let s = 0; s < W * H; s++) if (label[s] >= 0 && label[s] !== main) fr.data[s * 4 + 3] = 0
}

const offsets = list(a['offset-x']), tints = list(a.tint)
const out = frames.map((fr, f) => {
  if (a['main-only']) keepMain(fr)
  // 頭より上に出たエフェクト（髪に接していて連結判定で消えないもの）を消す。
  if (a['clear-above']) fr.data.fill(0, 0, Number(a['clear-above']) * W * 4)
  const img = createImage(W, H)
  const dx = offsets[f] ?? 0
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const i = (y * W + x) * 4
    if (!fr.data[i + 3] || fr.data[i + 3] < 128) continue
    const tx = x + dx
    if (tx < 0 || tx >= W) continue
    let [r, g, b] = palette[nearest([fr.data[i], fr.data[i + 1], fr.data[i + 2]], palette)[0]]
    if (tints[f]) { r = Math.min(255, Math.round(r * 0.6 + 102)); g = Math.round(g * 0.6); b = Math.round(b * 0.6) }
    img.data.set([r, g, b, 255], (y * W + tx) * 4)
  }
  return img
})

fs.mkdirSync(a['out-dir'], { recursive: true })
const strip = createImage(W * out.length, H)
out.forEach((img, i) => {
  writePng(path.join(a['out-dir'], `frame_${String(i).padStart(2, '0')}.png`), img)
  blit(strip, img, i * W, 0)
})
writePng(path.join(a['out-dir'], 'strip.png'), strip)

const S = Number(a.scale ?? 3), CW = Number(a['cell-w'] ?? 384), CH = Number(a['cell-h'] ?? 256), BOTTOM = Number(a.bottom ?? 245)
const anchorX = Number(a['anchor-x'] ?? W / 2), bottomRow = Number(a['bottom-row'] ?? H - 1)
const ox = Math.round(CW / 2 - anchorX * S), oy = BOTTOM + 1 - (bottomRow + 1) * S
const game = createImage(CW * out.length, CH)
let clippedTop = 0
out.forEach((img, i) => {
  const cell = createImage(CW, CH)
  blit(cell, img, ox, oy, S)
  blit(game, cell, i * CW, 0)
  const b = stats(img).bounds
  if (b && oy + b.minY * S < 0) clippedTop = Math.max(clippedTop, -(oy + b.minY * S))
})
writePng(path.join(a['out-dir'], 'game.png'), game)
const colors = new Set(out.flatMap(img => { const c = []; for (let i = 0; i < img.data.length; i += 4) if (img.data[i + 3]) c.push(img.data.readUInt32BE(i)); return c })).size
console.log(`motion ${a.dir} -> ${a['out-dir']} frames=${out.length} colors=${colors} (palette ${palette.length}) game=${CW * out.length}x${CH}${clippedTop ? ` clippedTop=${clippedTop}px` : ''}`)
