import { useEffect, useRef } from 'react'

/**
 * マップの道（マス同士をつなぐ線）をドット絵として描くキャンバス。
 * 画面の半分の解像度で 1 ドットずつ描き、CSS で ×2 に拡大する（image-rendering: pixelated）。
 * 規格: docs/current-art-asset-spec.md（ステージの基準倍率 ×2）。
 *
 * 旧 SVG 版のアニメーションを同じ時間・同じ見た目の順序で再現する:
 *  - 通った道（traced）: 現れた時に 620ms・8 段階で点線が流れ込み、薄い状態から濃くなる。
 *  - 次に行ける道（next）: 点線が 1.1 秒で 2 周期ぶん流れ続ける。
 *  - 移動中の点（travel）: 500ms で始点から終点へ動いて止まる。
 * 動きを減らす設定（OS の prefers-reduced-motion、ゲーム設定の motion=off）では動かさない。
 */
export type RouteEdgeState = 'traced' | 'next' | 'far'

export type RouteEdge = {
  id: string
  /** viewBox 0〜100 の座標（旧 SVG と同じ）。 */
  from: { x: number; y: number }
  to: { x: number; y: number }
  state: RouteEdgeState
}

export type RouteTravel = {
  key: string
  from: { x: number; y: number }
  to: { x: number; y: number }
}

const SCALE = 2
const COLORS: Record<RouteEdgeState, string> = {
  traced: '#3ec6ff',
  next: '#f1c95b',
  far: '#ffffff',
}
const OUTLINE = 'rgba(0, 0, 0, 0.85)'
/** 線の太さ（ドット）。色の線 3、黒縁込み 5。旧 SVG の 6px / 9px に相当。 */
const INNER = 3
const OUTER = 5
/** 点線（ドット）: 通った道は 9 描いて 3 空ける、次に行ける道は 4 と 4。 */
const TRACED_DASH = { on: 9, off: 3 }
const NEXT_DASH = { on: 4, off: 4 }
const TRACE_MS = 620
const TRACE_STEPS = 8
/** 次に行ける道: 1.1 秒で 16 ドット（2 周期）流れる。 */
const NEXT_FLOW_MS = 1100
const NEXT_FLOW_PX = 16
const TRAVEL_MS = 500

function motionReduced(): boolean {
  if (typeof window === 'undefined') return true
  const media = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
  return media || document.documentElement.dataset.motion === 'off'
}

/** Bresenham で線上の点を順に返す。 */
function linePoints(x0: number, y0: number, x1: number, y1: number): [number, number][] {
  const points: [number, number][] = []
  let x = Math.round(x0), y = Math.round(y0)
  const tx = Math.round(x1), ty = Math.round(y1)
  const dx = Math.abs(tx - x), dy = -Math.abs(ty - y)
  const sx = x < tx ? 1 : -1, sy = y < ty ? 1 : -1
  let err = dx + dy
  for (;;) {
    points.push([x, y])
    if (x === tx && y === ty) break
    const e2 = 2 * err
    if (e2 >= dy) { err += dy; x += sx }
    if (e2 <= dx) { err += dx; y += sy }
  }
  return points
}

export function PixelRouteCanvas({ edges, travel }: { edges: readonly RouteEdge[]; travel: RouteTravel | null }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const edgesRef = useRef(edges)
  const travelRef = useRef(travel)
  /** 通った道が初めて現れた時刻（流れ込み演出の開始）。 */
  const tracedSinceRef = useRef(new Map<string, number>())
  const travelStartRef = useRef<{ key: string; at: number } | null>(null)

  useEffect(() => {
    edgesRef.current = edges
    const now = performance.now()
    const seen = tracedSinceRef.current
    for (const edge of edges) {
      if (edge.state === 'traced' && !seen.has(edge.id)) seen.set(edge.id, now)
      if (edge.state !== 'traced') seen.delete(edge.id)
    }
  }, [edges])

  useEffect(() => {
    travelRef.current = travel
    if (travel && travelStartRef.current?.key !== travel.key) {
      travelStartRef.current = { key: travel.key, at: performance.now() }
    }
  }, [travel])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const context = canvas.getContext('2d')
    if (!context) return
    let frame = 0
    let lastKey = ''

    const resize = () => {
      const parent = canvas.parentElement
      if (!parent) return
      const width = Math.max(1, Math.round(parent.clientWidth / SCALE))
      const height = Math.max(1, Math.round(parent.clientHeight / SCALE))
      if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width
        canvas.height = height
        lastKey = ''
      }
    }

    const draw = (now: number) => {
      const reduced = motionReduced()
      const W = canvas.width, H = canvas.height
      const flow = reduced ? 0 : Math.floor((now % NEXT_FLOW_MS) / NEXT_FLOW_MS * NEXT_FLOW_PX)
      // 流れ込み演出の段階（0〜TRACE_STEPS）を道ごとに求める。
      const traceStep = (id: string) => {
        if (reduced) return TRACE_STEPS
        const since = tracedSinceRef.current.get(id)
        if (since === undefined) return TRACE_STEPS
        return Math.min(TRACE_STEPS, Math.floor((now - since) / (TRACE_MS / TRACE_STEPS)))
      }
      const currentEdges = edgesRef.current
      const steps = currentEdges.map(edge => edge.state === 'traced' ? traceStep(edge.id) : TRACE_STEPS)
      const trip = travelRef.current
      const tripStart = travelStartRef.current
      const tripProgress = trip && tripStart && !reduced
        ? Math.min(1, (now - tripStart.at) / TRAVEL_MS)
        : null
      const tripPos = trip && tripProgress !== null
        ? {
            x: Math.round((trip.from.x + (trip.to.x - trip.from.x) * tripProgress) / 100 * W),
            y: Math.round((trip.from.y + (trip.to.y - trip.from.y) * tripProgress) / 100 * H),
          }
        : null

      // 見た目が変わらない時は描き直さない（流れる点線は 1 ドット単位でしか変わらない）。
      const key = `${W}x${H}|${flow}|${steps.join(',')}|${currentEdges.map(e => e.state).join('')}|${tripPos ? `${tripPos.x},${tripPos.y}` : ''}`
      if (key !== lastKey) {
        lastKey = key
        context.clearRect(0, 0, W, H)
        const paths = currentEdges.map(edge => linePoints(
          edge.from.x / 100 * W, edge.from.y / 100 * H,
          edge.to.x / 100 * W, edge.to.y / 100 * H,
        ))
        // 黒縁は全部の道を先に描く（交差しても色の線が黒縁に隠れない）。
        context.fillStyle = OUTLINE
        const outerHalf = Math.floor(OUTER / 2)
        for (const points of paths) {
          for (const [x, y] of points) context.fillRect(x - outerHalf, y - outerHalf, OUTER, OUTER)
        }
        const innerHalf = Math.floor(INNER / 2)
        currentEdges.forEach((edge, index) => {
          const points = paths[index]
          const step = steps[index]
          context.globalAlpha = edge.state === 'traced' ? 0.35 + 0.65 * (step / TRACE_STEPS) : 1
          context.fillStyle = COLORS[edge.state]
          const dash = edge.state === 'traced' ? TRACED_DASH : edge.state === 'next' ? NEXT_DASH : null
          // 通った道: 点線の位置が 36 ドット手前から 0 へ流れ込む（旧 stroke-dashoffset 72→0 の半分）。
          const offset = edge.state === 'traced'
            ? Math.round(36 * (1 - step / TRACE_STEPS))
            : edge.state === 'next' ? -flow : 0
          points.forEach(([x, y], i) => {
            if (dash) {
              const cycle = dash.on + dash.off
              const at = (((i + offset) % cycle) + cycle) % cycle
              if (at >= dash.on) return
            }
            context.fillRect(x - innerHalf, y - innerHalf, INNER, INNER)
          })
        })
        context.globalAlpha = 1
        if (tripPos) {
          // 移動中の点: 水色の縁取りの 3×3 ドット。
          context.fillStyle = '#1687aa'
          context.fillRect(tripPos.x - 2, tripPos.y - 2, 5, 5)
          context.fillStyle = '#e6faff'
          context.fillRect(tripPos.x - 1, tripPos.y - 1, 3, 3)
        }
      }
      frame = requestAnimationFrame(draw)
    }

    resize()
    const observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(resize) : null
    if (observer && canvas.parentElement) observer.observe(canvas.parentElement)
    frame = requestAnimationFrame(draw)
    return () => {
      cancelAnimationFrame(frame)
      observer?.disconnect()
    }
  }, [])

  return <canvas ref={canvasRef} className="map-screen__routes pixel-art" aria-hidden="true" />
}
