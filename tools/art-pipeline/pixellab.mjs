#!/usr/bin/env node
// PixelLab API の最小クライアント（素材制作パイプライン用）。
// トークンはリポジトリ直下の .env（git 管理外）の PIXELLAB_API_TOKEN から読む。表示・ログには出さない。
//
// 使い方（リポジトリ直下で）:
//   node tools/art-pipeline/pixellab.mjs balance
//   node tools/art-pipeline/pixellab.mjs image --desc "..." --w 128 --h 160 --out art-work/x.png [--seed 1] [--engine pixflux|pixen|bitforge] [--no-bg] [--view side] [--direction east]
//   node tools/art-pipeline/pixellab.mjs animate --first art-work/base.png --action "..." --frames 6 --out art-work/attack_a [--last art-work/base.png] [--seed 1]
//   node tools/art-pipeline/pixellab.mjs gen2 --desc "..." --w 80 --h 80 --ref "a.png::用途;;b.png::用途" --style "s.png::用途" [--colors 24] --out art-work/x.png
//   node tools/art-pipeline/pixellab.mjs edit --image art-work/base.png --desc "eyes closed" --out art-work/blink.png
import fs from 'node:fs'
import path from 'node:path'

const API = 'https://api.pixellab.ai/v2'
const LOG = 'art-work/pixellab-log.jsonl'

function readToken() {
  const env = fs.existsSync('.env') ? fs.readFileSync('.env', 'utf8') : ''
  const match = env.match(/^\s*PIXELLAB_API_TOKEN\s*=\s*(.+?)\s*$/m)
  const token = process.env.PIXELLAB_API_TOKEN ?? match?.[1]?.replace(/^["']|["']$/g, '')
  if (!token) throw new Error('PIXELLAB_API_TOKEN が .env に見つかりません。')
  return token
}

function args(argv) {
  const out = { _: [] }
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i]
    if (a.startsWith('--')) {
      const key = a.slice(2)
      const next = argv[i + 1]
      if (next === undefined || next.startsWith('--')) out[key] = true
      else { out[key] = next; i++ }
    } else out._.push(a)
  }
  return out
}

function b64(file) {
  return { type: 'base64', base64: fs.readFileSync(file).toString('base64'), format: 'png' }
}

function pngSize(file) {
  const buf = fs.readFileSync(file)
  return { width: buf.readUInt32BE(16), height: buf.readUInt32BE(20) }
}

function savePng(base64, file) {
  fs.mkdirSync(path.dirname(file), { recursive: true })
  fs.writeFileSync(file, Buffer.from(String(base64).replace(/^data:image\/\w+;base64,/, ''), 'base64'))
  return file
}

function log(entry) {
  fs.mkdirSync(path.dirname(LOG), { recursive: true })
  fs.appendFileSync(LOG, JSON.stringify({ at: new Date().toISOString(), ...entry }) + '\n')
}

async function request(method, route, body) {
  const res = await fetch(API + route, {
    method,
    headers: {
      Authorization: `Bearer ${readToken()}`,
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  const text = await res.text()
  let json
  try { json = JSON.parse(text) } catch { json = { raw: text.slice(0, 500) } }
  if (!res.ok) {
    const err = new Error(`${method} ${route} -> ${res.status}: ${JSON.stringify(json).slice(0, 600)}`)
    err.status = res.status
    throw err
  }
  return json
}

async function waitJob(jobId, intervalMs = 4000, timeoutMs = 10 * 60 * 1000) {
  const started = Date.now()
  for (;;) {
    const job = await request('GET', `/background-jobs/${jobId}`)
    if (job.status === 'completed') return job
    if (job.status === 'failed') throw new Error(`job ${jobId} failed: ${JSON.stringify(job.last_response).slice(0, 400)}`)
    if (Date.now() - started > timeoutMs) throw new Error(`job ${jobId} timed out`)
    await new Promise(r => setTimeout(r, intervalMs))
  }
}

const commands = {
  async balance() {
    const b = await request('GET', '/balance')
    console.log(JSON.stringify(b, null, 2))
  },

  async image(a) {
    const engine = a.engine ?? 'pixflux'
    const body = {
      description: a.desc,
      image_size: { width: Number(a.w), height: Number(a.h) },
      ...(a['no-bg'] ? { no_background: true } : {}),
      ...(a.seed ? { seed: Number(a.seed) } : {}),
      ...(a.view ? { view: a.view } : {}),
      ...(a.direction ? { direction: a.direction } : {}),
      ...(a.outline ? { outline: a.outline } : {}),
      ...(a.init ? { init_image: b64(a.init), init_image_strength: Number(a['init-strength'] ?? 300) } : {}),
      ...(a.style ? { style_image: b64(a.style), style_strength: Number(a['style-strength'] ?? 50) } : {}),
    }
    const res = await request('POST', `/create-image-${engine}`, body)
    const file = savePng(res.image.base64, a.out)
    log({ cmd: 'image', engine, desc: a.desc, size: body.image_size, seed: a.seed, out: file, usage: res.usage })
    console.log(`saved ${file} usage=${JSON.stringify(res.usage)}`)
  },

  async animate(a) {
    const size = pngSize(a.first)
    const body = {
      first_frame: b64(a.first),
      action: a.action,
      frame_count: Number(a.frames ?? 6),
      no_background: true,
      ...(a.last ? { last_frame: b64(a.last) } : {}),
      ...(a.seed ? { seed: Number(a.seed) } : {}),
    }
    const res = await request('POST', '/animate-with-text-v3', body)
    const jobId = res.background_job_id
    console.log(`job ${jobId} queued (${size.width}x${size.height}, ${body.frame_count} frames)`)
    const job = await waitJob(jobId, 3000)
    const images = job.last_response?.images ?? []
    fs.mkdirSync(a.out, { recursive: true })
    images.forEach((img, i) => savePng(img.base64, path.join(a.out, `frame_${String(i).padStart(2, '0')}.png`)))
    log({ cmd: 'animate', action: a.action, frames: body.frame_count, first: a.first, out: a.out, count: images.length, usage: job.usage })
    console.log(`saved ${images.length} frames to ${a.out} usage=${JSON.stringify(job.usage)}`)
  },

  // 見本画像つき生成（generate-image-v2）。--ref は「画像パス::使い方」を ;; 区切りで最大 4 つ。
  async gen2(a) {
    const ref = (spec) => {
      const [file, usage] = spec.split('::')
      return { image: b64(file), size: pngSize(file), ...(usage ? { usage_description: usage } : {}) }
    }
    const body = {
      description: a.desc,
      image_size: { width: Number(a.w), height: Number(a.h) },
      no_background: true,
      ...(a.ref ? { reference_images: String(a.ref).split(';;').map(ref) } : {}),
      ...(a.style ? { style_image: ref(a.style) } : {}),
      ...(a.colors ? { target_palette: { max_colors: Number(a.colors) } } : {}),
      ...(a.seed ? { seed: Number(a.seed) } : {}),
    }
    const res = await request('POST', '/generate-image-v2', body)
    const job = res.background_job_id ? await waitJob(res.background_job_id, 3000) : { last_response: res }
    const lr = job.last_response ?? {}
    const imgs = lr.images ?? (lr.image ? [lr.image] : [])
    if (!imgs.length) throw new Error('gen2: image not found: ' + JSON.stringify(lr).slice(0, 300))
    const files = imgs.map((img, i) => savePng(img.base64, imgs.length === 1 ? a.out : a.out.replace(/\.png$/, `_${i}.png`)))
    log({ cmd: 'gen2', desc: a.desc, size: body.image_size, ref: a.ref, style: a.style, seed: a.seed, out: files, usage: job.usage ?? res.usage })
    console.log(`saved ${files.join(', ')} usage=${JSON.stringify(job.usage ?? res.usage)}`)
  },

  async edit(a) {
    const size = pngSize(a.image)
    const body = {
      image: b64(a.image),
      image_size: size,
      description: a.desc,
      width: size.width,
      height: size.height,
      no_background: true,
      ...(a.seed ? { seed: Number(a.seed) } : {}),
    }
    const res = await request('POST', '/edit-image', body)
    const job = res.background_job_id ? await waitJob(res.background_job_id) : { last_response: res }
    const lr = job.last_response ?? {}
    const img = lr.image ?? lr.images?.[0]
    if (!img) throw new Error('edit: image not found in response: ' + JSON.stringify(lr).slice(0, 300))
    const file = savePng(img.base64, a.out)
    log({ cmd: 'edit', desc: a.desc, image: a.image, out: file, usage: job.usage ?? res.usage })
    console.log(`saved ${file}`)
  },
}

const a = args(process.argv.slice(2))
const cmd = a._[0]
if (!commands[cmd]) {
  console.error('commands: balance | image | gen2 | animate | edit')
  process.exit(1)
}
commands[cmd](a).catch(err => {
  console.error(err.message)
  process.exit(1)
})
