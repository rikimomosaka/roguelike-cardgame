/**
 * 対象選択のクリック判定。キャラの領域（体の幅 × 頭上から足元まで）は隣のキャラと重なってよい。
 * 重なった位置をクリックしたときは、今対象にしていない方をクリックしたものとして扱う
 * （B を選んでいる時に A と B の重なりを押したら A）。候補が複数残れば中心が近い方。
 */
export type TargetRegion = {
  actorId: string
  left: number
  right: number
  top: number
  bottom: number
}

export function pickTargetAtPoint(
  regions: ReadonlyArray<TargetRegion>,
  x: number,
  y: number,
  currentTargetId: string | null,
): string | null {
  const hits = regions.filter(region =>
    x >= region.left && x <= region.right && y >= region.top && y <= region.bottom)
  if (hits.length === 0) return null
  if (hits.length === 1) return hits[0].actorId
  const others = hits.filter(region => region.actorId !== currentTargetId)
  const pool = others.length > 0 ? others : hits
  let best = pool[0]
  let bestDistance = Infinity
  for (const region of pool) {
    const distance = Math.abs((region.left + region.right) / 2 - x)
    if (distance < bestDistance) {
      best = region
      bestDistance = distance
    }
  }
  return best.actorId
}

/**
 * 側（味方・敵）の枠の中の、対象にできるキャラの領域を DOM から読む。
 * 横は「スロットの幅」と「体の幅（data-body-width）」の広い方、縦はスロットとスプライトを合わせた範囲。
 */
export function readTargetRegions(container: HTMLElement): TargetRegion[] {
  const slots = Array.from(container.querySelectorAll<HTMLElement>(
    '.battle__slot[data-targetable="1"][data-actor-id]'))
  return slots.map(slot => {
    const rect = slot.getBoundingClientRect()
    const sprite = slot.querySelector<HTMLElement>('.sprite')?.getBoundingClientRect()
    const bodyWidth = Number(slot.dataset.bodyWidth) || 0
    const half = Math.max(rect.width, bodyWidth) / 2
    const centerX = rect.left + rect.width / 2
    return {
      actorId: slot.dataset.actorId ?? '',
      left: centerX - half,
      right: centerX + half,
      top: Math.min(rect.top, sprite?.top ?? rect.top),
      bottom: Math.max(rect.bottom, sprite?.bottom ?? rect.bottom),
    }
  })
}
