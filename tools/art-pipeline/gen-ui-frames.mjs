#!/usr/bin/env node
// 共通UIの枠（9 分割用のドット絵）をコードで描いて art/source/ui/common/ に書く。
// 端の模様が 1 ドット単位でそろうので、CSS の border-image で伸ばしても崩れない。
// 配色は src/Client/src/styles/tokens.css の茶色・金に合わせる。
//   node tools/art-pipeline/gen-ui-frames.mjs && node tools/art-pipeline/build-assets.mjs ui/common
import fs from 'node:fs'
import path from 'node:path'
import { createImage, writePng } from './png.mjs'

const OUT = 'art/source/ui/common'
const hex = (h) => [parseInt(h.slice(1, 3), 16), parseInt(h.slice(3, 5), 16), parseInt(h.slice(5, 7), 16), 255]

const C = {
  outline: '#0a0604',
  deep: '#10080a',
  bg1: '#140b07',
  bg2: '#1a0f0a',
  bg3: '#2a1810',
  bg4: '#3a2414',
  borderDark: '#3a2410',
  borderMid: '#5a3a1a',
  borderLight: '#8a6a3a',
  goldDeep: '#c9985a',
  goldSoft: '#e8c87a',
  gold: '#ffd54f',
  dangerDark: '#6a2418',
  danger: '#c0503c',
  dangerSoft: '#e88870',
}

/**
 * 枠を描く。
 * rings: 外側から順に [左上の色, 右下の色]（立体感）
 * fill: 中の色、fillTop: 中の最上段（ハイライト）
 * cut: 角を欠く大きさ（1 = 角 1 ドット、2 = 3 ドット）
 * studs: 角の飾り（[色, 内側からの位置]）
 */
function frame(name, size, { rings, fill, fillTop, cut = 0, studs }) {
  const img = createImage(size, size)
  const set = (x, y, color) => img.data.set(hex(color), (y * size + x) * 4)
  rings.forEach(([tl, br], i) => {
    const a = i, b = size - 1 - i
    for (let k = a; k <= b; k++) {
      set(k, a, tl); set(a, k, tl) // 上・左
      set(k, b, br); set(b, k, br) // 下・右
    }
    set(b, a, br); set(a, b, tl) // 右上・左下の角は境目の色
  })
  const inner = rings.length
  for (let y = inner; y < size - inner; y++) for (let x = inner; x < size - inner; x++) {
    set(x, y, y === inner && fillTop ? fillTop : fill)
  }
  if (studs) {
    const [color, at] = studs
    for (const [x, y] of [[at, at], [size - 1 - at, at], [at, size - 1 - at], [size - 1 - at, size - 1 - at]]) set(x, y, color)
  }
  const clear = (x, y) => img.data.fill(0, (y * size + x) * 4, (y * size + x) * 4 + 4)
  for (const [cx, cy, dx, dy] of [[0, 0, 1, 1], [size - 1, 0, -1, 1], [0, size - 1, 1, -1], [size - 1, size - 1, -1, -1]]) {
    if (cut >= 1) clear(cx, cy)
    if (cut >= 2) { clear(cx + dx, cy); clear(cx, cy + dy) }
  }
  fs.mkdirSync(OUT, { recursive: true })
  writePng(path.join(OUT, `${name}.png`), img, { palette: true })
  return name
}

const made = [
  // 大きい窓（ポップアップ・メニュー・一覧の外枠）: 24×24、端 6 ドット。
  frame('panel', 24, {
    rings: [[C.outline, C.outline], [C.goldDeep, C.borderMid], [C.borderLight, C.borderDark], [C.outline, C.outline]],
    fill: C.bg2, fillTop: C.bg3, cut: 2, studs: [C.goldSoft, 2],
  }),
  // 小さい箱（上部バーの項目・ラベル）: 12×12、端 3 ドット。
  frame('box', 12, {
    rings: [[C.outline, C.outline], [C.borderMid, C.borderDark]],
    fill: C.bg1, fillTop: C.bg2, cut: 1,
  }),
  // へこんだ枠（選択肢の行・スロット）: 12×12、端 3 ドット。
  frame('inset', 12, {
    rings: [[C.outline, C.borderMid], [C.deep, C.bg3]],
    fill: '#120a07',
  }),
  // へこんだ枠（選択中・ホバー）: 金の縁。
  frame('inset_active', 12, {
    rings: [[C.outline, C.outline], [C.goldSoft, C.goldDeep]],
    fill: '#1f130b', fillTop: C.bg3,
  }),
  // ボタン: 16×16、端 4 ドット。通常・ホバー・控えめ・危険。
  frame('button', 16, {
    rings: [[C.outline, C.outline], [C.goldDeep, C.borderMid], [C.bg4, C.bg2]],
    fill: C.bg3, cut: 1,
  }),
  frame('button_hover', 16, {
    rings: [[C.outline, C.outline], [C.goldSoft, C.goldDeep], ['#4a2e18', C.bg3]],
    fill: C.bg4, cut: 1,
  }),
  frame('button_secondary', 16, {
    rings: [[C.outline, C.outline], [C.borderLight, C.borderDark], [C.bg3, C.bg1]],
    fill: '#160c08', cut: 1,
  }),
  frame('button_danger', 16, {
    rings: [[C.outline, C.outline], [C.danger, C.dangerDark], ['#3a1810', '#1a0a08']],
    fill: '#2a1210', cut: 1,
  }),
  frame('button_danger_hover', 16, {
    rings: [[C.outline, C.outline], [C.dangerSoft, C.danger], ['#4a2018', '#2a1210']],
    fill: '#3a1810', cut: 1,
  }),
  // ツールチップ: 12×12、端 3 ドット。
  frame('tooltip', 12, {
    rings: [[C.outline, C.outline], [C.goldDeep, C.borderMid]],
    fill: '#0e0806', cut: 1,
  }),
  // ゲージの溝（HP バーなど）: 6×6、端 1 ドット。目立ちすぎないよう黒縁は付けず、茶色の細い縁だけ。
  frame('bar', 6, {
    rings: [[C.borderMid, C.borderDark]],
    fill: '#050302',
  }),
]

/**
 * ドットの円の枠（マップのマス・ポーション枠など、丸い部品）。中も塗る。
 * 外周 1 ドットが縁の色、その内側 1 ドットが縁の暗い色、中は fill。
 */
function ring(name, size, edge, fill) {
  const img = createImage(size, size)
  const c = size / 2
  const [er, eg, eb] = hex(edge)
  const shade = `#${[er, eg, eb].map(v => Math.round(v * 0.55).toString(16).padStart(2, '0')).join('')}`
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const d = Math.hypot(x + 0.5 - c, y + 0.5 - c)
    if (d > c) continue
    const color = d > c - 1 ? edge : d > c - 2 ? shade : fill
    img.data.set(hex(color), (y * size + x) * 4)
  }
  writePng(path.join(OUT, `${name}.png`), img, { palette: true })
  return name
}

// マップのマス（通常 28 = 56px、現在地 34 = 68px、ボス 40 = 80px）。色は MapScreen.css の状態ごとの縁の色。
const NODE = {
  default: [C.borderMid, '#0a0604'],
  past: [C.borderDark, '#120b06'],
  far: [C.borderDark, '#0a0604'],
  next: [C.goldDeep, '#241610'],
  next_hover: [C.gold, '#241610'],
  elite_next: ['#ff9a8a', '#241610'],
  rest_next: ['#8fd08f', '#241610'],
  merchant_next: ['#ffd27a', '#241610'],
  boss_far: ['#7a3a2a', '#0a0604'],
}
for (const [state, [edge, fill]] of Object.entries(NODE)) {
  made.push(ring(`node_${state}`, 28, edge, fill))
  made.push(ring(`node_${state}_boss`, 40, edge, fill))
}
made.push(ring('node_current', 34, '#3ec6ff', '#071a24'))
// ポーション枠（36px = 18 ドット ×2）。
made.push(ring('slot_round', 18, C.borderLight, C.outline))

// 規格（ビルドの検査）: そのまま使う native。サイズはファイル名ごとに asset-rules.mjs の files に書く。
for (const name of made) {
  fs.writeFileSync(path.join(OUT, `${name}.json`), JSON.stringify({ mode: 'native', note: 'tools/art-pipeline/gen-ui-frames.mjs が生成' }, null, 2) + '\n')
}
console.log(`frames: ${made.join(', ')}`)
