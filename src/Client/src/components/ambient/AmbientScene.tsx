import type { CSSProperties } from 'react'
import { useMemo } from 'react'
import { AMBIENT_SCENES, type AmbientParticles, type AmbientScene as SceneDef } from './ambientScenes'
import './AmbientScene.css'

/**
 * 背景の控えめな演出の層。背景画像（center / cover）とまったく同じ範囲・同じ拡大率で重なり、
 * 元の絵のドット単位で揺れ・光・粒・霧を置く。操作は受けない（pointer-events: none）。
 * モーション設定 off で停止（粒は消す）、reduced で周期 2 倍（AmbientScene.css）。
 */
export function AmbientScene({ scene, className }: { scene: string | null | undefined; className?: string }) {
  const def = scene ? AMBIENT_SCENES[scene] : undefined
  const particles = useMemo(() => (def ? layoutParticles(scene!, def) : []), [scene, def])
  if (!def) return null
  const stageStyle = {
    '--w': def.width,
    '--h': def.height,
    '--ar': `${def.width} / ${def.height}`,
    '--src': `url('${def.src}')`,
  } as CSSProperties
  return (
    <div className={`ambient${className ? ` ${className}` : ''}`} data-ambient={scene} aria-hidden="true">
      <div className="ambient__stage" style={stageStyle}>
        {def.mist?.map((mist, i) => (
          <div
            key={`mist-${i}`}
            className="ambient__mist"
            style={px({ top: mist.y, height: mist.h }, {
              '--mist-color': mist.color,
              opacity: mist.opacity,
              '--dur': `${mist.period}s`,
            })}
          >
            <i />
          </div>
        ))}
        {def.sway?.map((sway, i) => (
          <div
            key={`sway-${i}`}
            className={`ambient__sway ambient__sway--${sway.axis ?? 'x'}`}
            style={px({ left: sway.x, top: sway.y, width: sway.w, height: sway.h }, {
              '--bx': -sway.x,
              '--by': -sway.y,
              '--dur': `${sway.period}s`,
              '--delay': `${sway.delay ?? 0}s`,
            })}
          />
        ))}
        {def.rays?.map((ray, i) => (
          <div
            key={`ray-${i}`}
            className="ambient__ray"
            style={px({ left: ray.x - ray.w / 2, top: ray.y, width: ray.w, height: ray.h }, {
              '--ray-color': ray.color,
              '--angle': `${ray.angle}deg`,
              '--min': ray.min,
              '--max': ray.max,
              '--dur': `${ray.period}s`,
              '--delay': `${ray.delay ?? 0}s`,
            })}
          />
        ))}
        {def.glows?.map((glow, i) => (
          <div
            key={`glow-${i}`}
            className={`ambient__glow ambient__glow--${glow.style ?? 'flicker'}`}
            style={px({ left: glow.x - glow.r, top: glow.y - glow.r, width: glow.r * 2, height: glow.r * 2 }, {
              '--glow-color': glow.color,
              '--min': glow.min,
              '--max': glow.max,
              '--dur': `${glow.period}s`,
              '--delay': `${glow.delay ?? 0}s`,
            })}
          />
        ))}
        {particles.map(p => (
          <span key={p.key} className={`ambient__particle ambient__particle--${p.kind}`} style={p.style} />
        ))}
      </div>
    </div>
  )
}

/** 元の絵のドット単位の位置・大きさを、表示上の長さ（--px = 1 ドット）にする。 */
function px(rect: Partial<Record<'left' | 'top' | 'width' | 'height', number>>, extra: Record<string, string | number>): CSSProperties {
  const style: Record<string, string | number> = { ...extra }
  for (const [key, value] of Object.entries(rect)) {
    if (value !== undefined) style[key] = `calc(${value} * var(--px))`
  }
  return style as CSSProperties
}

type LaidParticle = { key: string; kind: AmbientParticles['kind']; style: CSSProperties }

/** 粒の位置・動き・タイミングを、場面ごとに決まった乱数で決める（毎回同じ並びになる）。 */
function layoutParticles(scene: string, def: SceneDef): LaidParticle[] {
  const out: LaidParticle[] = []
  def.particles?.forEach((group, g) => {
    const rand = seeded(`${scene}:${g}`)
    for (let i = 0; i < group.count; i++) {
      const x = group.area.x + rand() * group.area.w
      const y = group.area.y + rand() * group.area.h
      const dur = group.duration * (0.8 + rand() * 0.4)
      const motion = particleMotion(group.kind, rand)
      out.push({
        key: `p-${g}-${i}`,
        kind: group.kind,
        style: px({ left: Math.round(x), top: Math.round(y) }, {
          '--color': group.colors[Math.floor(rand() * group.colors.length)],
          '--op': group.opacity,
          '--dx': motion.dx,
          '--dy': motion.dy,
          '--dur': `${dur.toFixed(2)}s`,
          '--delay': `${(-rand() * dur).toFixed(2)}s`,
        }),
      })
    }
  })
  return out
}

/** 粒の種類ごとの動く量（ドット）。 */
function particleMotion(kind: AmbientParticles['kind'], rand: () => number): { dx: number; dy: number } {
  const signed = (n: number) => Math.round((rand() * 2 - 1) * n)
  switch (kind) {
    case 'mote': return { dx: signed(14), dy: signed(10) }
    case 'firefly': return { dx: signed(22), dy: signed(14) }
    case 'ember': return { dx: signed(18), dy: -Math.round(90 + rand() * 60) }
    case 'leaf': return { dx: -Math.round(40 + rand() * 50), dy: Math.round(130 + rand() * 50) }
    case 'drip': return { dx: 0, dy: Math.round(60 + rand() * 40) }
    case 'sparkle': return { dx: 0, dy: 0 }
  }
}

/** 文字列から決まる乱数（mulberry32）。 */
function seeded(seed: string): () => number {
  let h = 1779033703 ^ seed.length
  for (let i = 0; i < seed.length; i++) {
    h = Math.imul(h ^ seed.charCodeAt(i), 3432918353)
    h = (h << 13) | (h >>> 19)
  }
  let a = h >>> 0
  return () => {
    a = (a + 0x6d2b79f5) | 0
    let t = Math.imul(a ^ (a >>> 15), 1 | a)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}
