#!/usr/bin/env node
// 基本立ち絵＋目閉じ差分から待機ループ（呼吸の上下 1px・まばたき 1 フレーム・髪の遅れ揺れ）を作る。
//   node tools/art-pipeline/idle.mjs --base base.png --blink blink.png --out-dir dir
//     [--cut 44]                    上半身として上下させる最下行（この行以下は動かさない）
//     [--sway 13,4,34,40]           髪などを横に揺らす矩形 x0,y0,x1,y1（含む）。透明でない画素だけ動かす
//     [--frames 8] [--blink-frame 6]
//     [--scale 3 --frame-w 192 --frame-h 256 --bottom 245]  ゲーム用シートへの整数倍拡大・足元配置
// 出力: frame_00.png..（原寸）, idle.png（横並びシート）, stand.png（1フレーム目）
import fs from 'node:fs'
import path from 'node:path'
import { readPng, writePng, createImage, blit, stats } from './png.mjs'

const a = {}
const argv = process.argv.slice(2)
for (let i = 0; i < argv.length; i += 2) a[argv[i].replace(/^--/, '')] = argv[i + 1]
const base = readPng(a.base), blink = readPng(a.blink)
const W = base.width, H = base.height
const N = Number(a.frames ?? 8), BLINK = Number(a['blink-frame'] ?? 6), CUT = Number(a.cut ?? Math.round(H * 0.55))
const sway = (a.sway ?? '').split(',').filter(Boolean).map(Number)
// 呼吸: 吸う(上がる=0) → 吐く(1px 下がる)。揺れは呼吸より 1 フレーム遅らせる。
const breath = Array.from({ length: N }, (_, f) => (f >= N * 3 / 8 && f < N * 6 / 8 ? 1 : 0))
const swayDx = breath.map((_, f) => -breath[(f - 1 + N) % N])

const at = (img, x, y) => (x < 0 || y < 0 || x >= W || y >= H) ? null : img.data.subarray((y * W + x) * 4, (y * W + x) * 4 + 4)
const inSway = (x, y) => sway.length === 4 && x >= sway[0] && x <= sway[2] && y >= sway[1] && y <= sway[3]

const frames = []
for (let f = 0; f < N; f++) {
  const src = f === BLINK ? blink : base
  const out = createImage(W, H)
  const put = (x, y, px) => { if (px && px[3] && x >= 0 && y >= 0 && x < W && y < H) out.data.set(px, (y * W + x) * 4) }
  // 下半身（動かさない）→ 上半身（dy）→ 揺れ部分（dy + dx）の順に重ねる。
  for (let y = CUT + 1; y < H; y++) for (let x = 0; x < W; x++) if (!inSway(x, y)) put(x, y, at(src, x, y))
  const dy = breath[f]
  for (let y = 0; y <= CUT; y++) for (let x = 0; x < W; x++) if (!inSway(x, y)) put(x, y + dy, at(src, x, y))
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) if (inSway(x, y)) put(x + swayDx[f], y + (y <= CUT ? dy : 0), at(src, x, y))
  frames.push(out)
}

fs.mkdirSync(a['out-dir'], { recursive: true })
frames.forEach((fr, i) => writePng(path.join(a['out-dir'], `frame_${String(i).padStart(2, '0')}.png`), fr))

// ゲーム用シート: 内容の横中心をフレーム中央に、最下行を --bottom に合わせて整数倍拡大する。
const S = Number(a.scale ?? 3), FW = Number(a['frame-w'] ?? 192), FH = Number(a['frame-h'] ?? 256), BOTTOM = Number(a.bottom ?? 245)
const b = stats(base).bounds
const ox = Math.round(FW / 2 - ((b.minX + b.maxX + 1) / 2) * S)
const oy = BOTTOM + 1 - (b.maxY + 1) * S
const sheet = createImage(FW * N, FH)
frames.forEach((fr, i) => {
  const cell = createImage(FW, FH)
  blit(cell, fr, ox, oy, S)
  blit(sheet, cell, i * FW, 0)
  if (i === 0) writePng(path.join(a['out-dir'], 'stand.png'), cell)
})
writePng(path.join(a['out-dir'], 'idle.png'), sheet)
console.log(`idle ${N}f cut=${CUT} breath=${breath.join('')} sway=${swayDx.join(',')} -> ${a['out-dir']} (sheet ${FW * N}x${FH}, offset ${ox},${oy})`)
