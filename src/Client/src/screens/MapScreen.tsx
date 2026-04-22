import { useCallback, useEffect, useRef, useState } from 'react'
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
import type { MapNodeDto, RunSnapshotDto, TileKind } from '../api/types'
import { useAccount } from '../context/AccountContext'
import { TopBar } from '../components/TopBar'
import { BattleOverlay } from './BattleOverlay'
import { RewardPopup } from './RewardPopup'
import { InGameMenuScreen } from './InGameMenuScreen'
import { MerchantScreen } from './MerchantScreen'
import { EventScreen } from './EventScreen'
import { RestScreen } from './RestScreen'

type Props = {
  snapshot: RunSnapshotDto
  onExitToMenu: () => void
  onAbandon: () => void
}

const NODE_R = 20
const COL_W = 100
const ROW_H = 50
const LEFT_PAD = 50
const TOP_PAD = 30
const SHOP_MESSAGE_MS = 2500

function iconFor(kind: TileKind, resolvedKind: TileKind | null): string {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  switch (k) {
    case 'Start': return '●'
    case 'Enemy': return '⚔'
    case 'Elite': return '⚔⚔'
    case 'Merchant': return '商'
    case 'Rest': return '火'
    case 'Treasure': return '宝'
    case 'Event': return 'E'
    case 'Unknown': return '?'
    case 'Boss': return '王'
  }
}

export function MapScreen({ snapshot, onExitToMenu, onAbandon }: Props) {
  const { accountId } = useAccount()
  const [snap, setSnap] = useState<RunSnapshotDto>(snapshot)
  const [menuOpen, setMenuOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [shopMessage, setShopMessage] = useState<string | null>(null)
  const [rewardDismissed, setRewardDismissed] = useState(false)
  const [merchantDismissed, setMerchantDismissed] = useState(false)
  const [eventDismissed, setEventDismissed] = useState(false)
  const [restDismissed, setRestDismissed] = useState(false)
  const [peekMap, setPeekMap] = useState(false)
  const mountedAt = useRef<number>(performance.now())

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

  const currentNode = snap.map.nodes.find((n) => n.id === snap.run.currentNodeId)!
  const visited = new Set(snap.run.visitedNodeIds)
  const activeBattle = snap.run.activeBattle
  const activeReward = snap.run.activeReward
  const activeMerchant = snap.run.activeMerchant
  const activeEvent = snap.run.activeEvent
  const activeRestPending = snap.run.activeRestPending
  const rewardVisible = activeReward !== null && !rewardDismissed
  const merchantVisible = activeMerchant !== null && !merchantDismissed
  const eventVisible = activeEvent !== null && !eventDismissed
  const restVisible = activeRestPending && !restDismissed
  const blockedByModal = activeBattle !== null
    || rewardVisible
    || merchantVisible
    || eventVisible
    || restVisible

  function isSelectable(n: MapNodeDto): boolean {
    if (blockedByModal) return false
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

  function posOf(n: MapNodeDto): { cx: number; cy: number } {
    const maxRow = 16
    return {
      cx: LEFT_PAD + n.column * COL_W,
      cy: TOP_PAD + (maxRow - n.row) * ROW_H,
    }
  }

  async function handleClick(n: MapNodeDto) {
    if (!accountId || busy) return
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
      setRewardDismissed(false)
      setMerchantDismissed(false)
      setEventDismissed(false)
      setRestDismissed(false)
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  async function handleWin() {
    if (!accountId) return
    await winBattle(accountId, elapsedSeconds())
    setRewardDismissed(false)
    setPeekMap(false)
    await refresh()
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

  function handleProceed() {
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
  const maxCol = Math.max(...snap.map.nodes.map((n) => n.column))
  const width = LEFT_PAD * 2 + maxCol * COL_W
  const height = TOP_PAD * 2 + 16 * ROW_H
  const atBoss = currentNode.kind === 'Boss'

  return (
    <>
      <TopBar
        currentHp={snap.run.currentHp}
        maxHp={snap.run.maxHp}
        gold={snap.run.gold}
        potions={snap.run.potions}
        deck={snap.run.deck}
        relics={snap.run.relics}
        onDiscardPotion={handleDiscardPotion}
        onOpenMenu={() => setMenuOpen(true)}
        onTogglePeek={activeBattle ? () => setPeekMap(v => !v) : undefined}
        peekActive={peekMap}
      />
      <main className="map-screen">
        <svg viewBox={`0 0 ${width} ${height}`} className="map-screen__svg">
          {snap.map.nodes.map((n) =>
            n.outgoingNodeIds.map((toId) => {
              const to = snap.map.nodes.find((x) => x.id === toId)!
              const a = posOf(n)
              const b = posOf(to)
              const visitedEdge = visited.has(n.id) && visited.has(toId)
              return (
                <line
                  key={`${n.id}-${toId}`}
                  x1={a.cx} y1={a.cy} x2={b.cx} y2={b.cy}
                  stroke={visitedEdge ? '#888' : '#444'}
                  strokeWidth={visitedEdge ? 3 : 2}
                />
              )
            }),
          )}
          {snap.map.nodes.map((n) => {
            const { cx, cy } = posOf(n)
            const isCurrent = n.id === snap.run.currentNodeId
            const isVisited = visited.has(n.id)
            const selectable = isSelectable(n)
            const reopen = isCurrentReopenable(n)
            const clickable = selectable || reopen
            const resolvedKind: TileKind | null = isVisited ? (resolved[n.id] ?? null) : null
            return (
              <g
                key={n.id}
                data-testid={`map-node-${n.id}`}
                data-current={isCurrent ? 'true' : 'false'}
                data-selectable={selectable ? 'true' : 'false'}
                data-visited={isVisited ? 'true' : 'false'}
                data-reopen-reward={reopen ? 'true' : 'false'}
                onClick={() => handleClick(n)}
                style={{ cursor: clickable ? 'pointer' : 'default' }}
              >
                <circle
                  cx={cx} cy={cy} r={NODE_R}
                  fill={isVisited ? '#444' : '#222'}
                  stroke={isCurrent ? 'gold' : selectable ? '#4ae' : '#666'}
                  strokeWidth={isCurrent ? 4 : selectable ? 3 : 1}
                />
                <text
                  x={cx} y={cy + 5}
                  textAnchor="middle"
                  fill={isVisited ? '#aaa' : '#eee'}
                  fontSize="14"
                >
                  {iconFor(n.kind, resolvedKind)}
                </text>
              </g>
            )
          })}
        </svg>

        {atBoss && !activeBattle && !activeReward && (
          <p className="map-screen__dev-note">
            ボスに到達しました。ここから先は Phase 5 以降で実装されます。
          </p>
        )}

        {shopMessage && (
          <div className="map-screen__shop-toast" role="status">{shopMessage}</div>
        )}
      </main>

      {activeBattle && !peekMap && (
        <BattleOverlay battle={activeBattle} onWin={handleWin} />
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
    </>
  )
}
