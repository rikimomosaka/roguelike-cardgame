// ドット絵変換の共通処理（依存なし）。画像は { width, height, data: RGBA Buffer }。
import { createImage, blit, stats } from '../png.mjs'

/** 面積平均（アルファ加重）で縮小する。アルファは alphaCut 未満を透明、それ以外を不透明にする。 */
export function resizeArea(src, W, H, alphaCut = 128) {
  const dst = createImage(W, H)
  const sx = src.width / W, sy = src.height / H
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const x0 = x * sx, x1 = (x + 1) * sx, y0 = y * sy, y1 = (y + 1) * sy
    let r = 0, g = 0, b = 0, al = 0, area = 0
    for (let yy = Math.floor(y0); yy < Math.ceil(y1); yy++) {
      if (yy < 0 || yy >= src.height) continue
      const wy = Math.min(yy + 1, y1) - Math.max(yy, y0)
      for (let xx = Math.floor(x0); xx < Math.ceil(x1); xx++) {
        if (xx < 0 || xx >= src.width) continue
        const w = (Math.min(xx + 1, x1) - Math.max(xx, x0)) * wy
        const i = (yy * src.width + xx) * 4
        const alpha = src.data[i + 3] / 255 * w
        r += src.data[i] * alpha; g += src.data[i + 1] * alpha; b += src.data[i + 2] * alpha
        al += alpha; area += w
      }
    }
    if (!area || al / area * 255 < alphaCut) continue
    dst.data.set([Math.round(r / al), Math.round(g / al), Math.round(b / al), 255], (y * W + x) * 4)
  }
  return dst
}

/** 不透明部分だけを切り出す（無ければ null）。 */
export function cropToContent(img) {
  const b = stats(img).bounds
  if (!b) return null
  const w = b.maxX - b.minX + 1, h = b.maxY - b.minY + 1
  const out = createImage(w, h)
  for (let y = 0; y < h; y++) img.data.copy(out.data, y * w * 4, ((b.minY + y) * img.width + b.minX) * 4, ((b.minY + y) * img.width + b.maxX + 1) * 4)
  return out
}

/** 部分画像 (x, y, w, h) を切り出す。 */
export function sub(img, x, y, w, h) {
  const out = createImage(w, h)
  for (let yy = 0; yy < h; yy++) img.data.copy(out.data, yy * w * 4, ((y + yy) * img.width + x) * 4, ((y + yy) * img.width + x + w) * 4)
  return out
}

/** 透明でない画素へ blit する（拡大なし、はみ出しは切る）。 */
export function paste(dst, src, dx, dy) { blit(dst, src, dx, dy, 1) }

/**
 * k-means で K 色へ減色する（決定的初期化・ディザなし）。
 * 複数画像を渡すと共通のパレットで減色する（アニメーションのコマ間で色がちらつかない）。
 * fixed を渡すと、その色をパレットに必ず含める（キャラの基本パレットなど）。
 */
export function quantize(images, K, fixed = []) {
  const list = Array.isArray(images) ? images : [images]
  const pts = []
  for (const img of list) for (let i = 0; i < img.data.length; i += 4) if (img.data[i + 3]) pts.push([img.data[i], img.data[i + 1], img.data[i + 2]])
  if (!pts.length) return []
  // 大きい画像は代表点を間引いて学習する（結果の割り当ては全画素に対して行う）。
  const step = Math.max(1, Math.floor(pts.length / 60000))
  const sample = step === 1 ? pts : pts.filter((_, i) => i % step === 0)
  const d2 = (p, c) => (p[0] - c[0]) ** 2 + (p[1] - c[1]) ** 2 + (p[2] - c[2]) ** 2
  const centers = fixed.map(c => c.slice())
  const isFixed = centers.map(() => true)
  if (!centers.length) {
    centers.push(sample.reduce((s, p) => [s[0] + p[0] / sample.length, s[1] + p[1] / sample.length, s[2] + p[2] / sample.length], [0, 0, 0]))
    isFixed.push(false)
  }
  const near = sample.map(p => Math.min(...centers.map(c => d2(p, c))))
  while (centers.length < K) {
    let best = 0
    for (let i = 1; i < sample.length; i++) if (near[i] > near[best]) best = i
    if (near[best] === 0) break
    centers.push(sample[best].slice()); isFixed.push(false)
    for (let i = 0; i < sample.length; i++) near[i] = Math.min(near[i], d2(sample[i], sample[best]))
  }
  const nearest = (p) => { let bi = 0, bd = Infinity; for (let k = 0; k < centers.length; k++) { const d = d2(p, centers[k]); if (d < bd) { bd = d; bi = k } } return bi }
  for (let it = 0; it < 20; it++) {
    const sum = centers.map(() => [0, 0, 0, 0])
    for (const p of sample) { const s = sum[nearest(p)]; s[0] += p[0]; s[1] += p[1]; s[2] += p[2]; s[3]++ }
    let moved = 0
    sum.forEach((s, k) => {
      if (!s[3] || isFixed[k]) return
      const c = [s[0] / s[3], s[1] / s[3], s[2] / s[3]]
      moved += d2(c, centers[k]); centers[k] = c
    })
    if (moved < 1) break
  }
  const pal = centers.map(c => c.map(Math.round))
  for (const img of list) for (let i = 0; i < img.data.length; i += 4) {
    if (!img.data[i + 3]) continue
    const c = pal[nearest([img.data[i], img.data[i + 1], img.data[i + 2]])]
    img.data[i] = c[0]; img.data[i + 1] = c[1]; img.data[i + 2] = c[2]
  }
  return pal
}

/** 縦横比を保って W×H の中に収める（中央寄せ、margin ドットの余白）。 */
export function fitContain(src, W, H, margin = 0, alphaCut = 128) {
  const s = Math.min((W - margin * 2) / src.width, (H - margin * 2) / src.height)
  const w = Math.max(1, Math.round(src.width * s)), h = Math.max(1, Math.round(src.height * s))
  const small = resizeArea(src, w, h, alphaCut)
  const out = createImage(W, H)
  paste(out, small, Math.floor((W - w) / 2), Math.floor((H - h) / 2))
  return out
}

/** 縦横比を保って W×H を覆うように縮小し、はみ出しを中央で切る（背景用）。 */
export function fitCover(src, W, H) {
  const s = Math.max(W / src.width, H / src.height)
  const cw = Math.round(W / s), ch = Math.round(H / s)
  const crop = sub(src, Math.floor((src.width - cw) / 2), Math.floor((src.height - ch) / 2), cw, ch)
  return resizeArea(crop, W, H)
}

/** 色数・半透明・サイズの検査。違反の説明を配列で返す。 */
export function checkSpec(img, { w, h, colors }) {
  const s = stats(img)
  const errors = []
  if (img.width !== w || img.height !== h) errors.push(`size ${img.width}x${img.height} != ${w}x${h}`)
  if (s.semi) errors.push(`${s.semi} semi-transparent pixels`)
  if (s.colors > colors) errors.push(`${s.colors} colors > ${colors}`)
  return errors
}
