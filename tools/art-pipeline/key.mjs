#!/usr/bin/env node
// 単色背景を透過にする。
//   node tools/art-pipeline/key.mjs --in concept.png --out keyed.png [--margin 40] [--color green|magenta|flood]
// green（既定）: #00FF00 背景。緑成分が他より margin 以上強い画素を消し、縁の緑かぶりを抑える。
// magenta: #FF00FF 背景を同じ考え方で消す（緑を使うキャラ用）。紫の服も消えるので、紫があるときは flood を使う。
// flood: 四隅から背景色を測り、外周からつながった「背景色に近い画素」だけを消す（キャラ内の近い色は残る）。
//   [--tol 110]    外周からつながる背景とみなす色の距離
//   [--hole 60]    つながっていなくても消す距離（腕と体のすき間などに残った背景）
//   [--fringe 150] 透過に接する画素のうち、この距離より背景に近いもの（縁のにじみ）を消す
import { readPng, writePng, stats } from './png.mjs'

const a = {}
const argv = process.argv.slice(2)
for (let i = 0; i < argv.length; i += 2) a[argv[i].replace(/^--/, '')] = argv[i + 1]
const margin = Number(a.margin ?? 40)
const img = readPng(a.in)
const W = img.width, H = img.height
let removed = 0, despilled = 0

if (a.color === 'flood') {
  const tol = Number(a.tol ?? 110), hole = Number(a.hole ?? 60), fringe = Number(a.fringe ?? 150)
  // 背景色: 四隅 8×8 の平均。
  const bg = [0, 0, 0]
  let n = 0
  for (const [cx, cy] of [[0, 0], [W - 8, 0], [0, H - 8], [W - 8, H - 8]]) {
    for (let y = cy; y < cy + 8; y++) for (let x = cx; x < cx + 8; x++) {
      const o = (y * W + x) * 4
      bg[0] += img.data[o]; bg[1] += img.data[o + 1]; bg[2] += img.data[o + 2]; n++
    }
  }
  bg.forEach((v, k) => { bg[k] = v / n })
  const dist = (i) => {
    const o = i * 4
    return Math.hypot(img.data[o] - bg[0], img.data[o + 1] - bg[1], img.data[o + 2] - bg[2])
  }
  const clear = new Uint8Array(W * H)
  const stack = []
  for (let x = 0; x < W; x++) stack.push(x, (H - 1) * W + x)
  for (let y = 0; y < H; y++) stack.push(y * W, y * W + W - 1)
  while (stack.length) {
    const i = stack.pop()
    if (clear[i] || dist(i) >= tol) continue
    clear[i] = 1
    const x = i % W, y = (i / W) | 0
    if (x > 0) stack.push(i - 1)
    if (x < W - 1) stack.push(i + 1)
    if (y > 0) stack.push(i - W)
    if (y < H - 1) stack.push(i + W)
  }
  for (let i = 0; i < W * H; i++) if (!clear[i] && dist(i) < hole) clear[i] = 1
  // 縁のにじみ: 透過に接していて背景色に近い画素を 1 周だけ消す。
  const edge = []
  for (let i = 0; i < W * H; i++) {
    if (clear[i]) continue
    const x = i % W, y = (i / W) | 0
    const touches = (x > 0 && clear[i - 1]) || (x < W - 1 && clear[i + 1]) || (y > 0 && clear[i - W]) || (y < H - 1 && clear[i + W])
    if (touches && dist(i) < fringe) edge.push(i)
  }
  for (const i of edge) { clear[i] = 1; despilled++ }
  for (let i = 0; i < W * H; i++) if (clear[i]) { img.data[i * 4 + 3] = 0; removed++ }
  console.log(`background ${bg.map(Math.round).join(',')}`)
} else {
  for (let i = 0; i < W * H; i++) {
    const o = i * 4
    const r = img.data[o], g = img.data[o + 1], b = img.data[o + 2]
    if (a.color === 'magenta') {
      const low = Math.min(r, b)
      if (low - g > margin && low > 120) { img.data[o + 3] = 0; removed++ }
      else if (low > g) { img.data[o] = Math.max(g, r - (low - g)); img.data[o + 2] = Math.max(g, b - (low - g)); despilled++ }
      continue
    }
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
}
writePng(a.out, img)
const s = stats(img)
console.log(`key ${a.in} -> ${a.out} removed=${removed} despilled=${despilled} bounds=${JSON.stringify(s.bounds)}`)
