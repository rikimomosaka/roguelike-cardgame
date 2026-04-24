import { useCallback, useEffect, useRef, useState } from 'react'
import type { PointerEvent as ReactPointerEvent } from 'react'
import { getCurrentRun, heartbeat, moveToNode } from '../api/runs'
import { winBattle } from '../api/battle'
import {
  claimGold,
  claimPotion,
  claimRelic,
  discardPotion,
  pickCard,
  proceedReward,
  skipCard,
} from '../api/rewards'
import { buyFromMerchant, discardAtMerchant, leaveMerchant } from '../api/merchant'
import { chooseEvent } from '../api/event'
import { restHeal, restUpgrade } from '../api/rest'
import { chooseActStartRelic, enterActStart } from '../api/actStart'
import { applyDebugDamage } from '../api/debug'
import type { MapNodeDto, RunSnapshotDto, RunResultDto, TileKind } from '../api/types'
import { useAccount } from '../context/AccountContext'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import { Button } from '../components/Button'
import { TopBar } from '../components/TopBar'
import { BattleOverlay } from './BattleOverlay'
import { RewardPopup } from './RewardPopup'
import { InGameMenuScreen } from './InGameMenuScreen'
import { MerchantScreen } from './MerchantScreen'
import { EventScreen } from './EventScreen'
import { RestScreen } from './RestScreen'
import { ActStartRelicScreen } from './ActStartRelicScreen'
import './MapScreen.css'

type Props = {
  snapshot: RunSnapshotDto
  onExitToMenu: () => void
  onAbandon: (result: RunResultDto | null) => void
  onDebugDamage?: () => void
  onRunFinished?: (result: RunResultDto) => void
}

const SHOP_MESSAGE_MS = 2500
const MAX_ROW = 16

function iconFor(kind: TileKind, resolvedKind: TileKind | null): string {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  switch (k) {
    case 'Start': return '●'
    case 'Enemy': return '⚔'
    case 'Elite': return '♛'
    case 'Merchant': return '◆'
    case 'Rest': return '△'
    case 'Treasure': return '◈'
    case 'Event': return '?'
    case 'Unknown': return '?'
    case 'Boss': return '♛'
  }
}

function kindClassFor(kind: TileKind, resolvedKind: TileKind | null): string {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  return `k--${k.toLowerCase()}`
}

function nodeLabelFor(kind: TileKind, resolvedKind: TileKind | null): string | null {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  switch (k) {
    case 'Enemy': return 'ENEMY'
    case 'Elite': return 'ELITE'
    case 'Rest': return 'REST'
    case 'Merchant': return 'SHOP'
    case 'Treasure': return 'TREASURE'
    case 'Event': return 'EVENT'
    case 'Unknown': return 'UNKNOWN'
    case 'Boss': return 'BOSS'
    case 'Start': return 'START'
  }
}

function nodeTooltipFor(kind: TileKind, resolvedKind: TileKind | null): string {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  switch (k) {
    case 'Enemy': return '敵との戦闘'
    case 'Elite': return 'エリート戦 (強敵)'
    case 'Rest': return '休憩 (回復 or 強化)'
    case 'Merchant': return '商人 (購入)'
    case 'Treasure': return '宝箱 (レリック入手)'
    case 'Event': return 'イベント'
    case 'Unknown': return '未知のマス'
    case 'Boss': return 'ボス戦'
    case 'Start': return '開始地点'
  }
}

export function MapScreen({ snapshot, onExitToMenu, onAbandon, onDebugDamage, onRunFinished }: Props) {
  const { accountId } = useAccount()
  const [snap, setSnap] = useState<RunSnapshotDto>(snapshot)
  const { names: relicNames, catalog: relicCatalog } = useRelicCatalog()
  const relicDescriptions = relicCatalog
    ? Object.fromEntries(Object.entries(relicCatalog).map(([k, v]) => [k, v.description]))
    : undefined
  const [menuOpen, setMenuOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [shopMessage, setShopMessage] = useState<string | null>(null)
  const [rewardDismissed, setRewardDismissed] = useState(false)
  const [merchantDismissed, setMerchantDismissed] = useState(false)
  const [eventDismissed, setEventDismissed] = useState(false)
  const [restDismissed, setRestDismissed] = useState(false)
  const [peekMap, setPeekMap] = useState(false)
  const [pan, setPan] = useState({ x: 0, y: 0 })
  const [zoom, setZoom] = useState(1)
  const [actBanner, setActBanner] = useState<number | null>(snapshot.run.currentAct)
  const dragRef = useRef<{ startX: number; startY: number; startPan: { x: number; y: number }; moved: boolean } | null>(null)
  const stageRef = useRef<HTMLDivElement | null>(null)
  const mountedAt = useRef<number>(performance.now())

  useEffect(() => {
    const stage = stageRef.current
    if (!stage) return
    const onWheel = (e: WheelEvent) => {
      e.preventDefault()
      setZoom(z => Math.max(0.6, Math.min(2.0, z + (e.deltaY < 0 ? 0.1 : -0.1))))
    }
    stage.addEventListener('wheel', onWheel, { passive: false })
    return () => stage.removeEventListener('wheel', onWheel)
  }, [])

  function onStagePointerDown(e: ReactPointerEvent<HTMLDivElement>) {
    if (e.button !== 0) return
    const target = e.target as HTMLElement
    if (target.closest('.map-screen__node')) return
    dragRef.current = {
      startX: e.clientX,
      startY: e.clientY,
      startPan: pan,
      moved: false,
    }
    e.currentTarget.setPointerCapture(e.pointerId)
  }

  function onStagePointerMove(e: ReactPointerEvent<HTMLDivElement>) {
    const d = dragRef.current
    if (!d) return
    const dx = e.clientX - d.startX
    const dy = e.clientY - d.startY
    if (!d.moved && Math.hypot(dx, dy) > 3) d.moved = true
    setPan({ x: d.startPan.x + dx, y: d.startPan.y + dy })
  }

  function onStagePointerUp(e: ReactPointerEvent<HTMLDivElement>) {
    dragRef.current = null
    if (e.currentTarget.hasPointerCapture(e.pointerId)) {
      e.currentTarget.releasePointerCapture(e.pointerId)
    }
  }

  const elapsedSeconds = useCallback(() => {
    const e = Math.floor((performance.now() - mountedAt.current) / 1000)
    mountedAt.current = performance.now()
    return Math.max(0, e)
  }, [])

  const refresh = useCallback(async () => {
    if (!accountId) return
    const next = await getCurrentRun(accountId)
    if (next) setSnap(next)
  }, [accountId])

  const internalDebugDamage = useCallback(async () => {
    if (!accountId) return
    try {
      const resp = await applyDebugDamage(accountId, 10)
      if ('outcome' in resp) onRunFinished?.(resp as RunResultDto)
      else setSnap(resp as RunSnapshotDto)
    } catch (err) {
      console.error('[DEBUG -10HP] applyDebugDamage failed:', err)
      await refresh()
    }
  }, [accountId, onRunFinished, refresh])

  useEffect(() => {
    return () => {
      if (!accountId) return
      const elapsed = Math.floor((performance.now() - mountedAt.current) / 1000)
      if (elapsed > 0) void heartbeat(accountId, elapsed).catch(() => {})
    }
  }, [accountId])

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setMenuOpen((v) => !v)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  useEffect(() => {
    if (!shopMessage) return
    const h = window.setTimeout(() => setShopMessage(null), SHOP_MESSAGE_MS)
    return () => window.clearTimeout(h)
  }, [shopMessage])

  // Act transition: reset pan so START is centered, show ACT START banner for 1s.
  // Start node (row 0) sits at y% = 4 + 94 = 98 inside a 300%-tall inner container
  // that is offset top: -100%. So in body coords, start = -100% + 98% * 300% = 194%.
  // To center it at 50% of body, pan.y = 50% - 194% = -144% of body height.
  const currentActValue = snap.run.currentAct
  useEffect(() => {
    const stage = stageRef.current
    if (stage) {
      const h = stage.clientHeight
      setPan({ x: 0, y: -1.44 * h })
    }
    setZoom(1)
    setActBanner(currentActValue)
    const t = window.setTimeout(() => setActBanner(null), 1000)
    return () => window.clearTimeout(t)
  }, [currentActValue])

  const currentNode = snap.map.nodes.find((n) => n.id === snap.run.currentNodeId)!
  const visited = new Set(snap.run.visitedNodeIds)
  const activeBattle = snap.run.activeBattle
  const activeReward = snap.run.activeReward
  const activeMerchant = snap.run.activeMerchant
  const activeEvent = snap.run.activeEvent
  const activeRestPending = snap.run.activeRestPending
  const activeActStartRelicChoice = snap.run.activeActStartRelicChoice
  const rewardVisible = activeReward !== null && !rewardDismissed
  const merchantVisible = activeMerchant !== null && !merchantDismissed
  const eventVisible = activeEvent !== null && !eventDismissed
  const restVisible = activeRestPending && !restDismissed
  const blockedByModal = activeBattle !== null
    || rewardVisible
    || merchantVisible
    || eventVisible
    || restVisible
    || activeActStartRelicChoice !== null

  // Start タイルが未通過のときは、まず Start マスを踏んでレリック選択イベントを
  // 発動させる必要がある。それまでは次マスを選べないようにする。
  const isActStartEntry = (n: MapNodeDto): boolean =>
    n.id === snap.run.currentNodeId
    && n.kind === 'Start'
    && !visited.has(n.id)
    && activeActStartRelicChoice === null

  function isSelectable(n: MapNodeDto): boolean {
    if (blockedByModal) return false
    // 現在マスが未通過（= スタートマス入場イベント未消化）の間は次マスを選べない。
    if (!visited.has(currentNode.id)) return false
    return currentNode.outgoingNodeIds.includes(n.id)
  }

  function isCurrentReopenable(n: MapNodeDto): boolean {
    if (n.id !== snap.run.currentNodeId) return false
    if (activeReward !== null && rewardDismissed) return true
    if (activeMerchant !== null && merchantDismissed) return true
    if (activeEvent !== null && eventDismissed) return true
    if (activeRestPending && restDismissed) return true
    return false
  }

  // Map grid -> % coords for HTML positioning. Rows go bottom-up so
  // row 0 = START (bottom) and row 16 = BOSS (top).
  function posPct(n: MapNodeDto, maxCol: number): { left: string; top: string } {
    // Horizontal: distribute columns across 14%–86% so nodes don't clip the stage edges.
    const colSpan = Math.max(1, maxCol)
    const xPct = 14 + (n.column / colSpan) * 72
    const yPct = 4 + ((MAX_ROW - n.row) / MAX_ROW) * 94
    return { left: `${xPct}%`, top: `${yPct}%` }
  }


  async function handleClick(n: MapNodeDto) {
    if (!accountId || busy) return
    if (isActStartEntry(n)) {
      setBusy(true)
      try {
        const next = await enterActStart(accountId)
        setSnap(next)
      } finally { setBusy(false) }
      return
    }
    if (isCurrentReopenable(n)) {
      setRewardDismissed(false)
      setMerchantDismissed(false)
      setEventDismissed(false)
      setRestDismissed(false)
      return
    }
    if (!isSelectable(n)) return
    setBusy(true)
    try {
      if (activeReward) {
        await proceedReward(accountId, elapsedSeconds())
      }
      await moveToNode(accountId, n.id, elapsedSeconds())
      // Fetch the new snapshot BEFORE flipping dismissed flags so all state updates
      // batch into a single render. Otherwise the previous tile's popup flashes
      // briefly (old snap still has activeReward, but dismissed is already false).
      const next = await getCurrentRun(accountId)
      if (next) setSnap(next)
      setRewardDismissed(false)
      setMerchantDismissed(false)
      setEventDismissed(false)
      setRestDismissed(false)
    } finally {
      setBusy(false)
    }
  }

  async function handleWin() {
    if (!accountId) return
    const resp = await winBattle(accountId, elapsedSeconds())
    if ('outcome' in resp) {
      onRunFinished?.(resp)
      return
    }
    setRewardDismissed(false)
    setPeekMap(false)
    setSnap(resp)
  }

  async function handleClaimGold() {
    if (!accountId) return
    await claimGold(accountId)
    await refresh()
  }

  async function handleClaimPotion() {
    if (!accountId) return
    await claimPotion(accountId)
    await refresh()
  }

  async function handlePickCard(cardId: string) {
    if (!accountId) return
    await pickCard(accountId, cardId)
    await refresh()
  }

  async function handleSkipCard() {
    if (!accountId) return
    await skipCard(accountId)
    await refresh()
  }

  async function handleProceed() {
    if (!accountId || busy) return
    // Boss reward triggers server-side act transition. Non-boss rewards dismiss locally
    // so the player can re-open the reward popup by clicking the current tile.
    if (activeReward?.isBossReward) {
      setBusy(true)
      try {
        const next = await proceedReward(accountId, elapsedSeconds())
        setRewardDismissed(false)
        setSnap(next)
      } finally {
        setBusy(false)
      }
      return
    }
    setRewardDismissed(true)
  }

  async function handleDiscardPotion(slotIndex: number) {
    if (!accountId) return
    await discardPotion(accountId, slotIndex)
    await refresh()
  }

  async function handleClaimRelic() {
    if (!accountId) return
    await claimRelic(accountId)
    await refresh()
  }

  async function handleBuy(kind: 'card' | 'relic' | 'potion', id: string) {
    if (!accountId) return
    await buyFromMerchant(accountId, { kind, id })
    await refresh()
  }

  async function handleDiscardAtMerchant(deckIndex: number) {
    if (!accountId) return
    await discardAtMerchant(accountId, deckIndex)
    await refresh()
  }

  async function handleLeaveMerchant() {
    if (!accountId) return
    await leaveMerchant(accountId)
    setMerchantDismissed(true)
  }

  async function handleChooseEvent(choiceIndex: number) {
    if (!accountId) return
    await chooseEvent(accountId, choiceIndex)
    await refresh()
  }

  async function handleRestHeal() {
    if (!accountId) return
    await restHeal(accountId)
    await refresh()
  }

  async function handleRestUpgrade(deckIndex: number) {
    if (!accountId) return
    await restUpgrade(accountId, deckIndex)
    await refresh()
  }

  function handleCloseEvent() { setEventDismissed(true) }
  function handleCloseRest() { setRestDismissed(true) }

  const resolved = snap.run.unknownResolutions
  const maxCol = Math.max(1, ...snap.map.nodes.map((n) => n.column))
  const atBoss = currentNode.kind === 'Boss'

  // Floor label rows: every 3rd row, plus START (0) and BOSS (16) and current.
  // Node coordinates in viewBox units (0-100) for SVG edge rendering.
  function posViewBox(n: MapNodeDto): { x: number; y: number } {
    const colSpan = Math.max(1, maxCol)
    return {
      x: 14 + (n.column / colSpan) * 72,
      y: 4 + ((MAX_ROW - n.row) / MAX_ROW) * 94,
    }
  }

  return (
      <main className="map-screen">
        <TopBar
          currentHp={snap.run.currentHp}
          maxHp={snap.run.maxHp}
          gold={snap.run.gold}
          potions={snap.run.potions}
          deck={snap.run.deck}
          relics={snap.run.relics}
          onDiscardPotion={handleDiscardPotion}
          onOpenMenu={() => setMenuOpen(v => !v)}
          menuActive={menuOpen}
          onTogglePeek={activeBattle ? () => setPeekMap(v => !v) : () => {}}
          peekActive={peekMap}
          peekDisabled={!activeBattle}
        />
        <div className="map-screen__body">
          <div className="map-screen__pattern" aria-hidden="true" />

        <div
          className="map-screen__stage"
          ref={stageRef}
          onPointerDown={onStagePointerDown}
          onPointerMove={onStagePointerMove}
          onPointerUp={onStagePointerUp}
          onPointerCancel={onStagePointerUp}
          style={{ cursor: dragRef.current ? 'grabbing' : 'grab' }}
        >
          <div className="map-screen__act-badge" aria-hidden="true">
            <span className="k">ACT</span>
            <span className="v">{snap.run.currentAct}</span>
            <span className="sep">·</span>
            <span className="k">FLOOR</span>
            <span className="v">{currentNode.row} / {MAX_ROW}</span>
          </div>

          <div
            className="map-screen__inner"
            style={{ transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})` }}
          >
            <svg
              className="map-screen__svg"
              viewBox="0 0 100 100"
              preserveAspectRatio="none"
              aria-hidden="true"
            >
              {snap.map.nodes.map((n) =>
                n.outgoingNodeIds.map((toId) => {
                  const to = snap.map.nodes.find((x) => x.id === toId)
                  if (!to) return null
                  const a = posViewBox(n)
                  const b = posViewBox(to)
                  const visitedEdge = visited.has(n.id) && visited.has(toId)
                  const nextEdge = n.id === snap.run.currentNodeId
                    && currentNode.outgoingNodeIds.includes(toId)
                  const stroke = visitedEdge
                    ? '#c9985a'
                    : nextEdge
                      ? '#d9b77a'
                      : '#b08a5a'
                  const opacity = visitedEdge ? 0.9 : nextEdge ? 0.95 : 0.75
                  const strokeWidth = visitedEdge ? 0.55 : nextEdge ? 0.5 : 0.45
                  const dash = visitedEdge
                    ? undefined
                    : nextEdge
                      ? '1.2 1'
                      : undefined
                  return (
                    <line
                      key={`${n.id}-${toId}`}
                      x1={a.x} y1={a.y} x2={b.x} y2={b.y}
                      stroke={stroke}
                      strokeOpacity={opacity}
                      strokeWidth={strokeWidth}
                      strokeLinecap="round"
                      strokeDasharray={dash}
                    />
                  )
                }),
              )}
            </svg>

            {snap.map.nodes.map((n) => {
              const { left, top } = posPct(n, maxCol)
              const isCurrent = n.id === snap.run.currentNodeId
              const isVisited = visited.has(n.id)
              const selectable = isSelectable(n)
              const reopen = isCurrentReopenable(n)
              const startEntry = isActStartEntry(n)
              const clickable = selectable || reopen || startEntry
              const resolvedKind: TileKind | null = isVisited ? (resolved[n.id] ?? null) : null

              const stateClass = isCurrent
                ? 'is-current'
                : isVisited
                  ? 'is-past'
                  : selectable
                    ? 'is-next'
                    : 'is-far'

              const classes = [
                'map-screen__node',
                stateClass,
                kindClassFor(n.kind, resolvedKind),
                reopen ? 'is-reopen' : '',
              ].filter(Boolean).join(' ')

              const label = (isCurrent || selectable) ? nodeLabelFor(n.kind, resolvedKind) : null

              return (
                <button
                  key={n.id}
                  type="button"
                  data-testid={`map-node-${n.id}`}
                  data-current={isCurrent ? 'true' : 'false'}
                  data-selectable={selectable ? 'true' : 'false'}
                  data-visited={isVisited ? 'true' : 'false'}
                  data-reopen-reward={reopen ? 'true' : 'false'}
                  data-start-entry={startEntry ? 'true' : 'false'}
                  onClick={() => handleClick(n)}
                  disabled={!clickable}
                  className={classes}
                  style={{ left, top, cursor: clickable ? 'pointer' : 'default' }}
                  aria-label={`マス ${n.id} (${nodeTooltipFor(n.kind, resolvedKind)})`}
                  title={nodeTooltipFor(n.kind, resolvedKind)}
                >
                  <span aria-hidden="true">{iconFor(n.kind, resolvedKind)}</span>
                  {label && <span className="map-screen__node-label">{label}</span>}
                </button>
              )
            })}
          </div>

          <div className="map-screen__key" aria-hidden="true">
            <div className="map-screen__key-title">MAP LEGEND</div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym">⚔</span><span>ENEMY</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym k--elite">♛</span><span>ELITE</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym k--rest">△</span><span>REST</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym k--merchant">◆</span><span>SHOP</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym k--treasure">◈</span><span>TREASURE</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym">?</span><span>UNKNOWN</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym k--boss">♛</span><span>BOSS</span></div>
          </div>
        </div>

        {atBoss && !activeBattle && !activeReward && (
          <p className="map-screen__dev-note">
            ボスに到達しました。ここから先は Phase 5 以降で実装されます。
          </p>
        )}

        {shopMessage && (
          <div className="map-screen__shop-toast" role="status">{shopMessage}</div>
        )}

        {actBanner !== null && (
          <div className="map-screen__act-banner" role="status" aria-live="polite">
            <span className="map-screen__act-banner-text">ACT {actBanner} START</span>
          </div>
        )}

        {import.meta.env.DEV && (
          <div className="map-screen__debug">
            <Button onClick={onDebugDamage ?? internalDebugDamage} aria-label="DEBUG -10HP">DEBUG -10HP</Button>
          </div>
        )}

        {activeBattle && !peekMap && (
          <BattleOverlay
            battle={activeBattle}
            onWin={handleWin}
            onDebugDamage={onDebugDamage ?? internalDebugDamage}
          />
        )}

        {rewardVisible && activeReward && (
          <RewardPopup
            reward={activeReward}
            potions={snap.run.potions}
            potionSlotCount={snap.run.potionSlotCount}
            onClaimGold={handleClaimGold}
            onClaimPotion={handleClaimPotion}
            onPickCard={handlePickCard}
            onSkipCard={handleSkipCard}
            onProceed={handleProceed}
            onDiscardPotion={handleDiscardPotion}
            onClaimRelic={handleClaimRelic}
          />
        )}

        {menuOpen && (
          <InGameMenuScreen
            onClose={() => setMenuOpen(false)}
            onExitToMenu={onExitToMenu}
            onAbandon={onAbandon}
            elapsedSecondsRef={mountedAt}
          />
        )}

        {merchantVisible && activeMerchant && (
          <MerchantScreen
            gold={snap.run.gold}
            deck={snap.run.deck}
            inventory={activeMerchant}
            onBuy={handleBuy}
            onDiscard={handleDiscardAtMerchant}
            onLeave={handleLeaveMerchant}
          />
        )}

        {eventVisible && activeEvent && (
          <EventScreen
            event={activeEvent}
            onChoose={handleChooseEvent}
            onClose={handleCloseEvent}
          />
        )}

        {restVisible && (
          <RestScreen
            deck={snap.run.deck}
            completed={snap.run.activeRestCompleted}
            onHeal={handleRestHeal}
            onUpgrade={handleRestUpgrade}
            onClose={handleCloseRest}
          />
        )}

        {snap.run.activeActStartRelicChoice && (
          <ActStartRelicScreen
            choices={snap.run.activeActStartRelicChoice.relicIds}
            relicNames={relicNames}
            relicDescriptions={relicDescriptions}
            onChoose={async (relicId) => {
              if (!accountId) return
              const next = await chooseActStartRelic(accountId, relicId)
              setSnap(next)
            }}
          />
        )}
        </div>
      </main>
  )
}
