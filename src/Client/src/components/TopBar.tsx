import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import type { CardInstanceDto } from '../api/types'
import { Card } from './Card'
import { cardDisplay } from './cardDisplay'
import { PotionSlot } from './PotionSlot'
import { RelicIcon } from './RelicIcon'
import { useCardCatalog } from '../hooks/useCardCatalog'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import './TopBar.css'

// Tween a number from its previous value to the new value over `durationMs`.
// Used for HP/Gold so changes count up/down visibly instead of snapping.
function useAnimatedNumber(target: number, durationMs = 450): number {
  const [display, setDisplay] = useState<number>(target)
  const fromRef = useRef<number>(target)
  const toRef = useRef<number>(target)
  const startRef = useRef<number>(0)
  const rafRef = useRef<number | null>(null)

  useEffect(() => {
    if (target === toRef.current) return
    fromRef.current = display
    toRef.current = target
    startRef.current = performance.now()
    if (rafRef.current !== null) cancelAnimationFrame(rafRef.current)
    const step = (now: number) => {
      const t = Math.min(1, (now - startRef.current) / durationMs)
      // ease-out cubic
      const eased = 1 - Math.pow(1 - t, 3)
      const v = fromRef.current + (toRef.current - fromRef.current) * eased
      setDisplay(t === 1 ? toRef.current : v)
      if (t < 1) rafRef.current = requestAnimationFrame(step)
      else rafRef.current = null
    }
    rafRef.current = requestAnimationFrame(step)
    return () => {
      if (rafRef.current !== null) cancelAnimationFrame(rafRef.current)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [target, durationMs])

  return display
}

type Props = {
  currentHp: number
  maxHp: number
  gold: number
  potions: string[]
  deck: CardInstanceDto[]
  relics: string[]
  onDiscardPotion: (slotIndex: number) => void
  onOpenMenu: () => void
  menuActive?: boolean
  onTogglePeek?: () => void
  peekActive?: boolean
  peekDisabled?: boolean
  /** Server-authoritative elapsed seconds for the current run. */
  playSeconds?: number
}

function formatElapsed(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds))
  // Always render HH:MM:SS with 2-digit groups so the frame width never shifts.
  // Hours saturate at 99 to keep the layout stable across very long sessions.
  const hh = Math.min(99, Math.floor(s / 3600))
  const mm = Math.floor((s % 3600) / 60)
  const ss = s % 60
  return `${String(hh).padStart(2, '0')}:${String(mm).padStart(2, '0')}:${String(ss).padStart(2, '0')}`
}

export function TopBar({
  currentHp,
  maxHp,
  gold,
  potions,
  deck,
  relics,
  onDiscardPotion,
  onOpenMenu,
  menuActive,
  onTogglePeek,
  peekActive,
  peekDisabled,
  playSeconds,
}: Props) {
  const [deckOpen, setDeckOpen] = useState(false)
  // Re-sync the displayed timer whenever the server-side baseline changes.
  const baselineRef = useRef<{ seconds: number; at: number }>({
    seconds: playSeconds ?? 0,
    at: Date.now(),
  })
  useEffect(() => {
    baselineRef.current = { seconds: playSeconds ?? 0, at: Date.now() }
  }, [playSeconds])
  const [now, setNow] = useState<number>(() => Date.now())
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [])
  const elapsedSec = baselineRef.current.seconds + (now - baselineRef.current.at) / 1000
  const elapsedLabel = formatElapsed(elapsedSec)
  // Drag-scroll for the relics row. Long-press (mousedown) then drag horizontally.
  const relicsRef = useRef<HTMLUListElement | null>(null)
  const dragRef = useRef<{ startX: number; startScroll: number; pointerId: number } | null>(null)
  const onRelicsPointerDown = (e: ReactPointerEvent<HTMLUListElement>) => {
    if (e.button !== 0) return
    const el = relicsRef.current
    if (!el) return
    if (el.scrollWidth <= el.clientWidth) return
    dragRef.current = { startX: e.clientX, startScroll: el.scrollLeft, pointerId: e.pointerId }
    el.setPointerCapture(e.pointerId)
    el.classList.add('topbar__relics--dragging')
  }
  const onRelicsPointerMove = (e: ReactPointerEvent<HTMLUListElement>) => {
    const d = dragRef.current
    const el = relicsRef.current
    if (!d || !el || d.pointerId !== e.pointerId) return
    el.scrollLeft = d.startScroll - (e.clientX - d.startX)
  }
  const onRelicsPointerEnd = (e: ReactPointerEvent<HTMLUListElement>) => {
    const d = dragRef.current
    const el = relicsRef.current
    if (!d || !el || d.pointerId !== e.pointerId) return
    el.releasePointerCapture(e.pointerId)
    el.classList.remove('topbar__relics--dragging')
    dragRef.current = null
  }
  const { names, catalog } = useCardCatalog()
  const { names: relicNames, catalog: relicCatalog } = useRelicCatalog()
  const deckLabel = (id: string) => names[id] ?? id
  const sortedDeck = [...deck].sort((a, b) =>
    deckLabel(a.id).localeCompare(deckLabel(b.id), 'ja'),
  )
  const deckOpenAria: 'true' | 'false' = deckOpen ? 'true' : 'false'
  const peekPressedAria: 'true' | 'false' = peekActive ? 'true' : 'false'
  const menuPressedAria: 'true' | 'false' = menuActive ? 'true' : 'false'
  const animatedHp = useAnimatedNumber(currentHp)
  const animatedGold = useAnimatedNumber(gold)
  const displayedHp = Math.round(animatedHp)
  const displayedGold = Math.round(animatedGold)
  const hpPct = Math.max(0, Math.min(100, maxHp > 0 ? (animatedHp / maxHp) * 100 : 0))
  const hpState: 'high' | 'mid' | 'low' | 'crit' =
    hpPct > 60 ? 'high' : hpPct > 30 ? 'mid' : hpPct > 15 ? 'low' : 'crit'

  return (
    <div className="topbar" role="status">
      <span className="topbar__group topbar__hp" data-hp={hpState}>
        <span className="topbar__hp-track">
          <span className="topbar__hp-fill" style={{ width: `${hpPct}%` }} aria-hidden="true" />
          <span className="topbar__hp-label">HP {displayedHp}/{maxHp}</span>
        </span>
      </span>
      <span className="topbar__group topbar__gold">
        <span className="topbar__gold-text">
          <span className="topbar__num">{displayedGold}</span> GOLD
        </span>
      </span>
      <span
        className="topbar__group topbar__timer"
        aria-label={`経過時間 ${elapsedLabel}`}
      >
        <img
          className="topbar__timer-icon"
          src="/icons/ui/time.png"
          alt=""
          draggable={false}
        />
        <span className="topbar__num topbar__timer-text">{elapsedLabel}</span>
      </span>
      <ul
        ref={relicsRef}
        className="topbar__relics"
        aria-label={`レリック (${relics.length}個)`}
        onPointerDown={onRelicsPointerDown}
        onPointerMove={onRelicsPointerMove}
        onPointerUp={onRelicsPointerEnd}
        onPointerCancel={onRelicsPointerEnd}
      >
        {relics.map((id, i) => (
          <li key={`${id}-${i}`} className="topbar__relic">
            <RelicIcon id={id} catalog={relicCatalog} names={relicNames} />
          </li>
        ))}
      </ul>
      <div className="topbar__potions">
        {potions.map((id, i) => (
          <PotionSlot
            key={i}
            slotIndex={i}
            potionId={id}
            onDiscard={() => onDiscardPotion(i)}
          />
        ))}
      </div>
      <div className="topbar__actions">
        <div className="topbar__deck-wrap">
          <button
            type="button"
            className="topbar__btn topbar__btn--deck"
            aria-label={`デッキ (${deck.length}枚)`}
            aria-expanded={deckOpenAria}
            aria-pressed={deckOpenAria}
            onClick={() => setDeckOpen((v) => !v)}
          >
            <img className="topbar__btn-icon" src="/icons/ui/deck.png" alt="" draggable={false} />
            <span className="topbar__btn-num">{deck.length}</span>
          </button>
          {deckOpen && (
            <div className="topbar__deck-menu" role="dialog" aria-label="現在のデッキ">
              <header className="topbar__deck-menu-header">
                <span>デッキ ({deck.length}枚)</span>
                <button
                  type="button"
                  className="topbar__btn"
                  aria-label="デッキを閉じる"
                  onClick={() => setDeckOpen(false)}
                >
                  ×
                </button>
              </header>
              {sortedDeck.length === 0 ? (
                <p className="topbar__deck-empty">デッキは空です</p>
              ) : (
                <ul className="topbar__deck-list">
                  {sortedDeck.map((card, i) => {
                    const disp = cardDisplay(card.id, catalog, deckLabel(card.id))
                    return (
                      <li key={`${card.id}-${i}`} className="topbar__deck-item">
                        <Card
                          name={disp.name}
                          cost={disp.cost}
                          type={disp.type}
                          rarity={disp.rarity}
                          description={disp.description}
                          upgradedDescription={disp.upgradedDescription}
                          upgraded={card.upgraded}
                          width={112}
                        />
                      </li>
                    )
                  })}
                </ul>
              )}
            </div>
          )}
        </div>
        <button
          type="button"
          className="topbar__btn topbar__btn--icon"
          aria-label={peekActive ? '戦闘に戻る' : 'マップを見る'}
          aria-pressed={peekPressedAria}
          onClick={onTogglePeek}
          disabled={peekDisabled || !onTogglePeek}
        >
          <img className="topbar__btn-icon" src="/icons/ui/map.png" alt="" draggable={false} />
          <span className="topbar__btn-label">MAP</span>
        </button>
        <button
          type="button"
          className="topbar__btn topbar__btn--icon"
          aria-label="メニュー"
          aria-pressed={menuPressedAria}
          onClick={onOpenMenu}
        >
          <img className="topbar__btn-icon" src="/icons/ui/settings.png" alt="" draggable={false} />
          <span className="topbar__btn-label">MENU</span>
        </button>
      </div>
    </div>
  )
}
