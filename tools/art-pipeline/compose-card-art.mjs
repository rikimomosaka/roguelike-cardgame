#!/usr/bin/env node
// 選んだ候補（art-work/cards/<id>_<seed>.png）から、カードイラストの元画像 art/source/cards/art/<id>.png を作る。
//   node tools/art-pipeline/compose-card-art.mjs striker_basic_attack=2 striker_basic_quickdraw=1 ...
// backdrop のカードは、背景を少し暗くして主人公のモーションの 1 コマを等倍で重ねる（膝上の構図）。
// 作ったあとは node tools/art-pipeline/build-assets.mjs cards/art で表示用に変換する。
import fs from 'node:fs'
import path from 'node:path'
import { readPng, writePng, createImage } from './png.mjs'
import { sub } from './lib/pixel.mjs'
import { CARD_ART, CARD_ART_ACTORS, CARD_ART_SIZE } from './card-art-prompts.mjs'
import actorAssets from '../../src/Client/src/presentation/generated/actor-assets.json' with { type: 'json' }

const OUT = 'art/source/cards/art'
const HEROES = 'src/Client/public/assets/actors/heroes'
const FRAME = 128
// 絵の窓（種類ごとのマスクで見える範囲）の中心と、頭の上端を置く位置。
const WINDOW_CENTER_X = 53
const HEAD_TOP_Y = 12

function compose(backdrop, actor) {
  const { w, h } = CARD_ART_SIZE
  const img = createImage(w, h)
  // 背景: キャラが浮くように少し暗くする。
  for (let i = 0; i < w * h * 4; i += 4) {
    img.data[i] = backdrop.data[i] * 0.78
    img.data[i + 1] = backdrop.data[i + 1] * 0.78
    img.data[i + 2] = backdrop.data[i + 2] * 0.82
    img.data[i + 3] = 255
  }
  const sheet = readPng(path.join(HEROES, actor.hero, `${actor.sheet}.png`))
  const frame = sub(sheet, actor.frame * FRAME, 0, FRAME, FRAME)
  const top = actorAssets.actors[`heroes/${actor.hero}`]?.top ?? 48
  const dx = WINDOW_CENTER_X - FRAME / 2 + (actor.dx ?? 0)
  const dy = HEAD_TOP_Y - top + (actor.dy ?? 0)
  // キャラを等倍で重ねる（窓の下端より下＝膝から下はマスクで隠れる）。
  for (let y = 0; y < FRAME; y++) for (let x = 0; x < FRAME; x++) {
    const s = (y * FRAME + x) * 4
    if (frame.data[s + 3] < 128) continue
    const X = x + dx, Y = y + dy
    if (X < 0 || Y < 0 || X >= w || Y >= h) continue
    img.data.set(frame.data.subarray(s, s + 3), (Y * w + X) * 4)
  }
  return img
}

fs.mkdirSync(OUT, { recursive: true })
for (const arg of process.argv.slice(2)) {
  const [id, seed] = arg.split('=')
  const spec = CARD_ART[id]
  if (!spec) throw new Error(`未定義のカード: ${id}`)
  const candidate = `art-work/cards/${id}_${seed}.png`
  const src = readPng(candidate)
  const img = spec.kind === 'backdrop' ? compose(src, CARD_ART_ACTORS[id]) : src
  writePng(path.join(OUT, `${id}.png`), img)
  const actor = spec.kind === 'backdrop' ? CARD_ART_ACTORS[id] : undefined
  fs.writeFileSync(path.join(OUT, `${id}.json`), JSON.stringify({
    note: 'tools/art-pipeline/compose-card-art.mjs が生成（指示文は card-art-prompts.mjs）',
    kind: spec.kind, seed: Number(seed), ...(actor ? { actor } : {}),
  }, null, 2) + '\n')
  console.log('composed', id, 'seed', seed)
}
