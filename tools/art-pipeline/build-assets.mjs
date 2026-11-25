#!/usr/bin/env node
// art/source/ の元画像から、規格どおりの表示用ドット絵を src/Client/public/assets/ に作る。
// 規格: docs/current-art-asset-spec.md / 設定: tools/art-pipeline/asset-rules.mjs
//   node tools/art-pipeline/build-assets.mjs                 # 全部
//   node tools/art-pipeline/build-assets.mjs icons/relics     # key の前方一致で絞る（複数可）
//   node tools/art-pipeline/build-assets.mjs --only goblin_a  # id で絞る
// 戦闘キャラの寸法（影の幅・見た目の中心）と主人公モーションの情報は
// src/Client/src/presentation/generated/actor-assets.json に書き出す（表示側が読む）。
import fs from 'node:fs'
import path from 'node:path'
import { readPng, writePng, createImage, stats } from './png.mjs'
import { resizeArea, cropToContent, sub, paste, quantize, fitContain, fitCover, checkSpec } from './lib/pixel.mjs'
import { RULES, ACTOR, HERO_MOTIONS } from './asset-rules.mjs'

const SRC = 'art/source'
const OUT = 'src/Client/public/assets'
const GENERATED = 'src/Client/src/presentation/generated/actor-assets.json'

const args = process.argv.slice(2)
const onlyIdx = args.indexOf('--only')
const only = onlyIdx >= 0 ? args.splice(onlyIdx, 2)[1] : null
const filters = args
const rules = RULES.filter(r => !filters.length || filters.some(f => r.key.startsWith(f) || f.startsWith(r.key)))

const generated = fs.existsSync(GENERATED)
  ? JSON.parse(fs.readFileSync(GENERATED, 'utf8'))
  : { actors: {}, heroes: {} }
const failures = []
let built = 0

const readMeta = (pngPath) => {
  const j = pngPath.replace(/\.png$/, '.json')
  return fs.existsSync(j) ? JSON.parse(fs.readFileSync(j, 'utf8')) : {}
}
const save = (file, img, spec) => {
  const errors = checkSpec(img, spec)
  if (errors.length) failures.push(`${file}: ${errors.join(', ')}`)
  fs.mkdirSync(path.dirname(file), { recursive: true })
  writePng(file, img, { palette: true })
  built++
}
const pngs = (dir) => fs.existsSync(dir) ? fs.readdirSync(dir).filter(f => f.endsWith('.png')).sort() : []

/** 1 コマを規格のキャンバスへ。 */
function fitFrame(frame, w, h, fit, margin = 0) {
  if (fit === 'cover') return fitCover(frame, w, h)
  if (fit === 'stretch') return resizeArea(frame, w, h)
  const content = cropToContent(frame)
  return content ? fitContain(content, w, h, margin) : createImage(w, h)
}

function buildPixelize(rule, srcDir, outDir) {
  for (const f of pngs(srcDir)) {
    const id = f.replace(/\.png$/, '')
    if (only && id !== only) continue
    const meta = readMeta(path.join(srcDir, f))
    const size = rule.files?.[id] ?? { w: rule.w, h: rule.h }
    if (!size.w) { failures.push(`${srcDir}/${f}: キャンバス未定義（asset-rules の files に追加する）`); continue }
    const colors = meta.colors ?? rule.colors
    const frames = meta.frames ?? 1
    const src = readPng(path.join(srcDir, f))
    let sheet
    if ((meta.mode ?? rule.mode) === 'native') {
      sheet = src
    } else {
      const fw = Math.floor(src.width / frames)
      const cells = Array.from({ length: frames }, (_, i) =>
        fitFrame(sub(src, i * fw, 0, fw, src.height), size.w, size.h, meta.fit ?? rule.fit, meta.margin ?? rule.margin ?? 0))
      quantize(cells, colors)
      sheet = createImage(size.w * frames, size.h)
      cells.forEach((c, i) => paste(sheet, c, i * size.w, 0))
    }
    save(path.join(outDir, `${id}.png`), sheet, { w: size.w * frames, h: size.h, colors })
  }
}

/** 戦闘キャラの寸法を記録する（表示側の影の幅・効果の中心に使う）。 */
function recordActor(key, img) {
  const b = stats(img).bounds
  if (!b) return
  generated.actors[key] = {
    centerY: +(((b.minY + b.maxY + 1) / 2) / ACTOR.size).toFixed(3),
    footprint: b.maxX - b.minX + 1,
    top: b.minY,
    bottom: b.maxY,
  }
}

function buildActors(rule, srcDir, outDir) {
  const kind = rule.key.split('/')[1]
  for (const f of pngs(srcDir)) {
    const id = f.replace(/\.png$/, '')
    if (only && id !== only) continue
    const meta = readMeta(path.join(srcDir, f))
    const colors = meta.colors ?? rule.colors
    let cell
    if ((meta.mode ?? rule.mode) === 'native') {
      cell = readPng(path.join(srcDir, f))
    } else {
      const content = cropToContent(readPng(path.join(srcDir, f)))
      const height = meta.height ?? 64
      const width = Math.max(1, Math.round(content.width * height / content.height))
      const small = resizeArea(content, width, height)
      cell = createImage(ACTOR.size, ACTOR.size)
      const bottom = ACTOR.footRow - (meta.lift ?? 0)
      paste(cell, small, ACTOR.centerX - Math.round(width / 2), bottom - height + 1)
      quantize(cell, colors)
    }
    save(path.join(outDir, id, 'stand.png'), cell, { w: ACTOR.size, h: ACTOR.size, colors })
    recordActor(`${kind}/${id}`, cell)
  }
}

/** 主人公: character.json に従ってモーションを 128×128 のコマへ並べ直す。 */
function buildHeroes(rule, srcDir, outDir) {
  for (const id of fs.readdirSync(srcDir).filter(d => fs.existsSync(path.join(srcDir, d, 'character.json')))) {
    if (only && id !== only) continue
    const dir = path.join(srcDir, id)
    const def = JSON.parse(fs.readFileSync(path.join(dir, 'character.json'), 'utf8'))
    const S = ACTOR.size
    const sheets = {}
    if (def.mode === 'native') {
      for (const [motion, m] of Object.entries(def.motions)) {
        const src = readPng(path.join(dir, m.file))
        const dx = Math.floor(ACTOR.centerX - m.anchorX), dy = ACTOR.footRow - m.footRow
        sheets[motion] = Array.from({ length: m.frames }, (_, i) => {
          const cell = createImage(S, S)
          paste(cell, sub(src, i * m.frameWidth, 0, m.frameWidth, m.frameHeight), dx, dy)
          return cell
        })
      }
    } else {
      // 旧素材: 192×256 基準のコマを、立ち絵の身長が def.height になる倍率で縮小する。
      const stand = readPng(path.join(dir, def.motions.stand.file))
      const sb = stats(stand).bounds
      const s = def.height / (sb.maxY - sb.minY + 1)
      const all = []
      for (const [motion, m] of Object.entries(def.motions)) {
        const src = readPng(path.join(dir, m.file))
        const fh = src.height
        sheets[motion] = Array.from({ length: m.frames }, (_, i) => {
          const frame = sub(src, i * m.frameWidth, 0, m.frameWidth, fh)
          const w = Math.round(m.frameWidth * s), h = Math.round(fh * s)
          const small = resizeArea(frame, w, h)
          const cell = createImage(S, S)
          paste(cell, small, Math.round(ACTOR.centerX - w / 2), ACTOR.footRow - Math.floor((sb.maxY + 0.5) * s))
          all.push(cell)
          return cell
        })
      }
      quantize(all, rule.colors)
    }
    if (!sheets.stand && sheets.idle) sheets.stand = [sheets.idle[0]]
    const info = {}
    for (const [motion, cells] of Object.entries(sheets)) {
      const sheet = createImage(S * cells.length, S)
      cells.forEach((c, i) => paste(sheet, c, i * S, 0))
      // 被弾の赤フラッシュなど、モーション単位で色数の例外を character.json の colors で認める。
      const colors = def.motions[motion]?.colors ?? rule.colors
      save(path.join(outDir, id, `${motion}.png`), sheet, { w: S * cells.length, h: S, colors })
      const std = HERO_MOTIONS[motion] ?? {}
      info[motion] = {
        frames: cells.length,
        durationMs: def.motions[motion]?.durationMs ?? std.durationMs ?? 0,
        loop: std.loop ?? false,
      }
    }
    generated.heroes[id] = info
    recordActor(`heroes/${id}`, sheets.stand[0])
  }
}

for (const rule of rules) {
  const srcDir = path.join(SRC, rule.key), outDir = path.join(OUT, rule.key)
  if (rule.mode === 'actor') buildActors(rule, srcDir, outDir)
  else if (rule.mode === 'hero') buildHeroes(rule, srcDir, outDir)
  else if (rule.nested) {
    for (const d of fs.existsSync(srcDir) ? fs.readdirSync(srcDir) : []) {
      if (fs.statSync(path.join(srcDir, d)).isDirectory()) buildPixelize(rule, path.join(srcDir, d), path.join(outDir, d))
    }
  } else buildPixelize(rule, srcDir, outDir)
}

fs.mkdirSync(path.dirname(GENERATED), { recursive: true })
const sortKeys = (o) => Object.fromEntries(Object.keys(o).sort().map(k => [k, o[k]]))
// イラストのあるカード ID（無いカードは画像を読みに行かず、仮の表示にする）。
const cardArt = pngs(path.join(OUT, 'cards/art')).map(f => f.replace(/\.png$/, ''))
fs.writeFileSync(GENERATED, JSON.stringify({ actors: sortKeys(generated.actors), heroes: sortKeys(generated.heroes), cardArt }, null, 2) + '\n')

console.log(`built ${built} files`)
if (failures.length) {
  console.error(`規格違反 ${failures.length} 件:\n  ` + failures.join('\n  '))
  process.exit(1)
}
