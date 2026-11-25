#!/usr/bin/env node
// カードイラストの候補を PixelLab で生成する（1 枚 1 回分）。
//   node tools/art-pipeline/gen-card-art.mjs [--ids a,b] [--seeds 1,2]
// 候補は art-work/cards/<id>_<seed>.png。気に入ったものを art/source/cards/art/<id>.png に置く
//（backdrop は compose-card-art.mjs が合成して置く）。
import fs from 'node:fs'
import { execFileSync } from 'node:child_process'
import { CARD_ART, CARD_ART_SIZE } from './card-art-prompts.mjs'

const argv = process.argv.slice(2)
const opt = (k) => { const i = argv.indexOf(`--${k}`); return i >= 0 ? argv[i + 1] : undefined }
const ids = opt('ids')?.split(',') ?? Object.keys(CARD_ART)
const seeds = (opt('seeds') ?? '1,2').split(',')

for (const id of ids) {
  const spec = CARD_ART[id]
  if (!spec) throw new Error(`未定義のカード: ${id}`)
  for (const seed of seeds) {
    const out = `art-work/cards/${id}_${seed}.png`
    if (fs.existsSync(out)) continue
    execFileSync('node', ['tools/art-pipeline/pixellab.mjs', 'image', '--engine', 'pixflux',
      '--w', String(CARD_ART_SIZE.w), '--h', String(CARD_ART_SIZE.h), '--seed', seed, '--desc', spec.desc, '--out', out],
    { stdio: 'inherit' })
  }
}
