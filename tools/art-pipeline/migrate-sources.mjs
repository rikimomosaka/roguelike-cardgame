#!/usr/bin/env node
// 一度きりの移行: src/Client/public の旧画像を art/source/ の新しい構成へ git mv する。
// 規格: docs/current-art-asset-spec.md
//   node tools/art-pipeline/migrate-sources.mjs          # 予定を表示するだけ
//   node tools/art-pipeline/migrate-sources.mjs --apply  # 実行
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'

const PUB = 'src/Client/public'
const SRC = 'art/source'
const ARCHIVE = 'art/source/_archive'

const STATUS = new Set([
  'afterglow', 'afterimage', 'barrier', 'bind', 'burn', 'combo_carryover', 'confusion', 'continuity', 'cover',
  'curse', 'defensive_stance', 'dexterity', 'disruption', 'distress', 'exhaustion', 'fortify', 'growth',
  'hand_cost_increase', 'hand_cost_reduction', 'haste', 'insight', 'lifesteal', 'mastery', 'omnistrike',
  'on_hit', 'poison', 'pursuit', 'reflect', 'regeneration', 'resistance', 'resonance', 'revive', 'scorch',
  'seal', 'silence', 'stagnation', 'status_unknown', 'steadfast', 'strength', 'vigor', 'virulent_poison',
  'vulnerable', 'weak',
])
const INTENT = { attack: 'attack', attack_all: 'attack_all', attack_random: 'attack_random', block: 'block',
  intent_buff: 'buff', intent_debuff: 'debuff', intent_heal: 'heal' }
const SCENE = new Set(['rest_fire', 'rest_flame', 'rest_heal', 'rest_logs'])
// 状態アイコンの「ブロック値」は HP バー横の盾。状態フォルダに block として置く。
const RENAME_STATUS = { block_value: 'block' }

const moves = []
const add = (from, to) => moves.push([from, to])
const list = (dir) => fs.existsSync(path.join(PUB, dir)) ? fs.readdirSync(path.join(PUB, dir)) : []
const base = (f) => f.replace(/\.png$/, '')

for (const f of list('icons/ui')) {
  const id = base(f)
  if (INTENT[id]) add(`icons/ui/${f}`, `icons/intent/${INTENT[id]}.png`)
  else if (RENAME_STATUS[id]) add(`icons/ui/${f}`, `icons/status/${RENAME_STATUS[id]}.png`)
  else if (STATUS.has(id)) add(`icons/ui/${f}`, `icons/status/${id}.png`)
  else if (SCENE.has(id)) add(`icons/ui/${f}`, `icons/scene/${id}.png`)
  else add(`icons/ui/${f}`, `icons/system/${id}.png`)
}
for (const f of list('icons/tiles')) add(`icons/tiles/${f}`, `icons/map/${f}`)
for (const f of list('icons/relics')) add(`icons/relics/${f}`, `icons/relics/${f}`)
for (const f of list('icons/potions')) add(`icons/potions/${f}`, `icons/potions/${f}`)
for (const f of list('icons/atlases')) add(`icons/atlases/${f}`, `_archive/icons_atlases/${f}`)
for (const kind of ['battle', 'map', 'events']) {
  for (const f of list(`backgrounds/${kind}`)) add(`backgrounds/${kind}/${f}`, `backgrounds/${kind}/${f}`)
}
for (const part of ['affiliations', 'types', 'costs', 'masks']) {
  for (const f of list(`cards/frames/${part}`)) add(`cards/frames/${part}/${f}`, `cards/frames/${part}/${f}`)
}
for (const f of list('ui')) {
  const m = f.match(/^battle-control-(combo|tactical|ultimate|ready)\.png$/)
  if (m) add(`ui/${f}`, `ui/heroes/default_frame/${m[1]}.png`)
  else add(`ui/${f}`, `_archive/ui/${f}`)
}
for (const f of list('characters/enemies')) add(`characters/enemies/${f}`, `actors/enemies/${f}`)
for (const f of list('characters/units')) add(`characters/units/${f}`, `actors/units/${f}`)
for (const f of list('characters/portraits')) add(`characters/portraits/${f}`, `portraits/${f}`)
for (const f of list('characters')) {
  const m = f.match(/^(guardian_mireille|tactician_yuno|enhancer_lyra)_(stand|idle|attack|hurt)\.png$/)
  if (m) add(`characters/${f}`, `actors/heroes/${m[1]}/${m[2]}.png`)
  else if (/^player_/.test(f)) add(`characters/${f}`, `_archive/characters/${f}`)
}

const apply = process.argv.includes('--apply')
for (const [from, to] of moves) {
  const src = path.join(PUB, from)
  const dst = to.startsWith('_archive/') ? path.join(ARCHIVE, to.slice('_archive/'.length)) : path.join(SRC, to)
  if (!apply) { console.log(`${from} -> ${dst}`); continue }
  fs.mkdirSync(path.dirname(dst), { recursive: true })
  execFileSync('git', ['mv', '-k', src, dst])
}
console.log(`${moves.length} files ${apply ? 'moved' : 'planned'}`)
