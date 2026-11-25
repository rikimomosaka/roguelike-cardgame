#!/usr/bin/env node
// 一度きり: 旧方式（192×256 の絵を heightTier で拡大縮小）での「画面上の見た目の大きさ」を保ったまま、
// 128×128 共通キャンバスでの身長（ドット）と浮き具合を art/source/actors/<区分>/<id>.json に書く。
// 以後の調整はこの json を直接編集する。
import fs from 'node:fs'
import path from 'node:path'
import { readPng, stats } from './png.mjs'

const heightForTier = (tier) => 38 + Math.max(1, Math.min(10, Math.round(tier))) * 22
// 旧 actorVisualManifest.ts の AIRBORNE_STAGE_BY_IMAGE_ID（表示高さに対する持ち上げ率）。
const AIRBORNE = { cave_bat_a: 0.42, cave_bat_b: 0.42, wisp: 0.38, six_ghost: 0.18 }
const SCALE = 2 // ステージ表示倍率

function tiers(dir) {
  const out = {}
  for (const f of fs.readdirSync(dir).filter(f => f.endsWith('.json'))) {
    const j = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'))
    const id = j.imageId ?? j.id
    if (id && j.heightTier !== undefined && out[id] === undefined) out[id] = j.heightTier
  }
  return out
}

for (const [kind, dataDir] of [['enemies', 'src/Core/Data/Enemies'], ['units', 'src/Core/Data/Units']]) {
  const tierById = tiers(dataDir)
  const srcDir = `art/source/actors/${kind}`
  for (const f of fs.readdirSync(srcDir).filter(f => f.endsWith('.png'))) {
    const id = f.replace(/\.png$/, '')
    const img = readPng(path.join(srcDir, f))
    const b = stats(img).bounds
    const tier = tierById[id] ?? 5
    const display = heightForTier(tier) // 旧方式で 256 行が占めていた画面の高さ
    const contentScreen = display * (b.maxY - b.minY + 1) / img.height
    // 16 ドット未満は形が読めないので 16 に切り上げる。
    const height = Math.max(16, Math.round(contentScreen / SCALE))
    const lift = AIRBORNE[id] ? Math.round(display * AIRBORNE[id] / SCALE) : 0
    const meta = { height, ...(lift ? { lift } : {}), note: `旧 heightTier=${tier} の見た目から算出` }
    fs.writeFileSync(path.join(srcDir, `${id}.json`), JSON.stringify(meta, null, 2) + '\n')
    console.log(kind, id, JSON.stringify(meta))
  }
}
