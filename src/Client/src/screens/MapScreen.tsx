import { useCallback, useEffect, useRef, useState } from 'react'
import type { PointerEvent as ReactPointerEvent, ReactNode } from 'react'
import { getCurrentRun, heartbeat, moveToNode } from '../api/runs'
import {
  claimGold,
  claimPotion,
  claimRelic,
  discardPotion,
  pickCard,
  proceedReward,
  rerollCardChoices,
  skipCard,
  consumePotionOutsideBattle,
} from '../api/rewards'
import { buyFromMerchant, discardAtMerchant, leaveMerchant } from '../api/merchant'
import { chooseEvent } from '../api/event'
import { restHeal, restUpgrade } from '../api/rest'
import { chooseActStartRelic, enterActStart } from '../api/actStart'
import { applyDebugDamage } from '../api/debug'
import type { MapNodeDto, RunSnapshotDto, RunResultDto, TileKind } from '../api/types'
import { useAudio } from '../audio'
import { BGM_TRACKS, selectSe } from '../audio/audioManifest'
import { resolveMapBgmScene } from '../audio/audioScene'
import { useAccount } from '../context/AccountContext'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import { Button } from '../components/Button'
import { TopBar } from '../components/TopBar'
import { BattleScreen } from './BattleScreen'
import { RewardPopup } from './RewardPopup'
import { InGameMenuScreen } from './InGameMenuScreen'
import { MerchantScreen } from './MerchantScreen'
import { EventScreen } from './EventScreen'
import { RestScreen } from './RestScreen'
import { ActStartRelicScreen } from './ActStartRelicScreen'
import { mapIcon } from '../presentation/assets'
import './MapScreen.css'

type Props = {
  snapshot: RunSnapshotDto
  onExitToMenu: () => void
  onAbandon: (result: RunResultDto | null, snapshot: RunSnapshotDto) => void
  onDebugDamage?: () => void
  onRunFinished?: (result: RunResultDto, snapshot: RunSnapshotDto) => void
}

const SHOP_MESSAGE_MS = 2500
const MAX_ROW = 16

const TILE_IMG_SRC: Record<TileKind, string> = {
  Enemy: mapIcon('enemy'),
  Elite: mapIcon('elite'),
  Merchant: mapIcon('merchant'),
  Rest: mapIcon('rest'),
  Treasure: mapIcon('treasure'),
  Event: mapIcon('event'),
  Unknown: mapIcon('unknown'),
  Boss: mapIcon('boss'),
  Start: mapIcon('start'),
}

function iconFor(kind: TileKind, resolvedKind: TileKind | null): ReactNode {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  return <img src={TILE_IMG_SRC[k]} alt="" className="map-screen__node-img" draggable={false} />
}

function kindClassFor(kind: TileKind, resolvedKind: TileKind | null): string {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  return `k--${k.toLowerCase()}`
}

function nodeLabelFor(kind: TileKind, resolvedKind: TileKind | null): string | null {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  switch (k) {
    case 'Enemy': return '敵'
    case 'Elite': return '強敵'
    case 'Rest': return '休憩'
    case 'Merchant': return '商店'
    case 'Treasure': return '宝箱'
    case 'Event': return 'イベント'
    case 'Unknown': return '不明'
    case 'Boss': return 'ボス'
    case 'Start': return '開始'
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
  const { playBgm, playSe, setBgmDuck, stopBgm } = useAudio()
  const [snap, setSnap] = useState<RunSnapshotDto>(snapshot)
  const { names: relicNames, catalog: relicCatalog } = useRelicCatalog()
  const relicDescriptions = relicCatalog
    ? Object.fromEntries(Object.entries(relicCatalog).map(([k, v]) => [k, v.description]))
    : undefined
  // Phase 10.6.B フォローアップ: ActStartRelicScreen tooltip で effectText / flavor を分離表示するため
  const relicEffectTexts = relicCatalog
    ? Object.fromEntries(Object.entries(relicCatalog).map(([k, v]) => [k, v.effectText]))
    : undefined
  const relicFlavors = relicCatalog
    ? Object.fromEntries(Object.entries(relicCatalog).map(([k, v]) => [k, v.flavor]))
    : undefined
  const [menuOpen, setMenuOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [shopMessage, setShopMessage] = useState<string | null>(null)
  const [rewardDismissed, setRewardDismissed] = useState(false)
  const [merchantDismissed, setMerchantDismissed] = useState(false)
  const [eventDismissed, setEventDismissed] = useState(false)
  const [restDismissed, setRestDismissed] = useState(false)
  const [peekMap, setPeekMap] = useState(false)
  // Why: バトル中は live battle state (HP / potions) を TopBar に表示する。
  // peek でマップへ切り替えても snap.run の戦闘前の値ではなくここに保持した
  // 直近 battle state を使い続ける (ユーザ要望)。
  const [livefBattleState, setLivefBattleState] = useState<import('../api/types').BattleStateDto | null>(null)
  // Why: 戦闘中のタイマー継続用 tick。MapScreen / BattleScreen を切り替えても
  // 親が継続して秒数をカウントすることで、TopBar の playSeconds 表示が peek
  // 切替時にリセットされない (ユーザ要望)。
  const [battleClock, setBattleClock] = useState({ key: '', ticks: 0 })
  const [pan, setPan] = useState({ x: 0, y: 0 })
  const [dragging, setDragging] = useState(false)
  const [zoom, setZoom] = useState(1)
  const [actBanner, setActBanner] = useState<number | null>(null)
  const [actStartRelicDismissed, setActStartRelicDismissed] = useState(false)
  const [travel, setTravel] = useState<{ fromId: number; toId: number } | null>(null)
  const [busyLabel, setBusyLabel] = useState<string | null>(null)
  const [pendingFinish, setPendingFinish] = useState<
    | { kind: 'gameover'; result: RunResultDto; cause: 'hp' }
    | { kind: 'abandon'; result: RunResultDto | null }
    | null
  >(null)
  const dragRef = useRef<{ startX: number; startY: number; startPan: { x: number; y: number }; moved: boolean } | null>(null)
  const stageRef = useRef<HTMLDivElement | null>(null)
  const mountedAt = useRef<number>(0)
  const snapRef = useRef<RunSnapshotDto>(snap)
  useEffect(() => {
    mountedAt.current = performance.now()
  }, [])
  useEffect(() => { snapRef.current = snap }, [snap])

  const toggleMenu = useCallback(() => setMenuOpen(v => !v), [])

  const clampPan = useCallback((value: { x: number; y: number }, atZoom = zoom) => {
    const stage = stageRef.current
    if (!stage || stage.clientWidth <= 0 || stage.clientHeight <= 0) return value
    // The route is three viewports tall and intentionally allows the first and
    // last floor to be centred. These bounds keep the map recoverable without
    // preventing that edge-to-centre travel.
    const xLimit = 48 + stage.clientWidth * Math.max(0, atZoom - 1) * 0.5
    const yLimit = stage.clientHeight * 1.55 * atZoom
    return {
      x: Math.max(-xLimit, Math.min(xLimit, value.x)),
      y: Math.max(-yLimit, Math.min(yLimit, value.y)),
    }
  }, [zoom])

  const setZoomClamped = useCallback((next: number) => {
    const value = Math.max(0.6, Math.min(2, next))
    setZoom(value)
    setPan(current => clampPan(current, value))
  }, [clampPan])

  function onStagePointerDown(e: ReactPointerEvent<HTMLDivElement>) {
    if (e.button !== 0) return
    const target = e.target as HTMLElement
    if (target.closest('.map-screen__node, .map-screen__controls')) return
    dragRef.current = {
      startX: e.clientX,
      startY: e.clientY,
      startPan: pan,
      moved: false,
    }
    setDragging(true)
    e.currentTarget.setPointerCapture(e.pointerId)
  }

  function onStagePointerMove(e: ReactPointerEvent<HTMLDivElement>) {
    const d = dragRef.current
    if (!d) return
    const dx = e.clientX - d.startX
    const dy = e.clientY - d.startY
    if (!d.moved && Math.hypot(dx, dy) > 3) d.moved = true
    setPan(clampPan({ x: d.startPan.x + dx, y: d.startPan.y + dy }))
  }

  function onStagePointerUp(e: ReactPointerEvent<HTMLDivElement>) {
    dragRef.current = null
    setDragging(false)
    if (e.currentTarget.hasPointerCapture(e.pointerId)) {
      e.currentTarget.releasePointerCapture(e.pointerId)
    }
  }

  const elapsedSeconds = useCallback(() => {
    const e = Math.max(0, Math.floor((performance.now() - mountedAt.current) / 1000))
    // 報告した整数秒ぶんだけ起点を進め、サブ秒の余りを次回に持ち越す。
    // performance.now() で起点をリセットすると毎回端数が捨てられ、
    // サーバ PlaySeconds が TopBar のリアル時刻表示よりじわじわ遅れていく。
    mountedAt.current += e * 1000
    return e
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
      if ('outcome' in resp) {
        const result = resp as RunResultDto
        if (result.outcome === 'GameOver') {
          // Zero HP locally first so the topbar bar animates down, then
          // show GAME OVER once the animation has had time to play out.
          setSnap(s => ({ ...s, run: { ...s.run, currentHp: 0 } }))
          window.setTimeout(() => {
            setPendingFinish({ kind: 'gameover', result, cause: 'hp' })
          }, 600)
        } else {
          onRunFinished?.(result, snapRef.current)
        }
      } else {
        setSnap(resp as RunSnapshotDto)
      }
    } catch (err) {
      console.error('[DEBUG -10HP] applyDebugDamage failed:', err)
      await refresh()
    }
  }, [accountId, onRunFinished, refresh])

  useEffect(() => {
    return () => {
      if (!accountId) return
      const elapsed = Math.max(0, Math.floor((performance.now() - mountedAt.current) / 1000))
      if (elapsed > 0) {
        mountedAt.current += elapsed * 1000
        void heartbeat(accountId, elapsed).catch(() => {})
      }
    }
  }, [accountId])

  // Why: ESC で in-game menu を開閉。menu 開いている間は Popup 側
  // (mode==='main' のときの closeOnEsc=true) に任せ、ここでは何もしない。
  // 確認サブ Popup (closeOnEsc=false) 中は ESC 無効。
  // INPUT/TEXTAREA/SELECT focus 中や modifier キー併用時は捕まえない。
  useEffect(() => {
    if (menuOpen) return
    const handler = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return
      const tag = (e.target as HTMLElement | null)?.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return
      if (e.key === 'Escape') {
        e.preventDefault()
        toggleMenu()
      }
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [menuOpen, toggleMenu])

  useEffect(() => {
    if (!shopMessage) return
    const h = window.setTimeout(() => setShopMessage(null), SHOP_MESSAGE_MS)
    return () => window.clearTimeout(h)
  }, [shopMessage])

  // Act transition: reset pan so START is centered. Show ACT X START banner for
  // 2s only when the player is currently AT the start node (i.e. the first
  // moment of an act, or when restoring a saved run that was paused at the
  // start). Mid-act snapshots — including saved runs resumed from anywhere
  // else on the map — must not show the banner.
  // Start node (row 0) sits at y% = 4 + 94 = 98 inside a 300%-tall inner container
  // that is offset top: -100%. So in body coords, start = -100% + 98% * 300% = 194%.
  // To center it at 50% of body, pan.y = 50% - 194% = -144% of body height.
  const currentActValue = snap.run.currentAct
  const startNodeId = snap.map.startNodeId
  const atStartNode = snap.run.currentNodeId === startNodeId
  useEffect(() => {
    const stage = stageRef.current
    if (stage) {
      const h = stage.clientHeight
      setPan({ x: 0, y: -1.44 * h })
    }
    setZoom(1)
    if (!atStartNode) {
      setActBanner(null)
      return
    }
    setActBanner(currentActValue)
    const t = window.setTimeout(() => setActBanner(null), 2000)
    return () => window.clearTimeout(t)
  }, [currentActValue, atStartNode])

  useEffect(() => {
    const onResize = () => setPan(current => clampPan(current))
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [clampPan])

  // GAME OVER intermission: show the end-of-run banner for 2s before the
  // parent transitions to the result screen. Triggered for HP=0 (gameover)
  // and abandon. Cleared (boss kill) bypasses this and goes straight to the
  // result screen — the user's spec only asks for GAME OVER.
  useEffect(() => {
    if (!pendingFinish) return
    const t = window.setTimeout(() => {
      if (pendingFinish.kind === 'abandon') {
        onAbandon(pendingFinish.result, snapRef.current)
      } else {
        onRunFinished?.(pendingFinish.result, snapRef.current)
      }
    }, 2000)
    return () => window.clearTimeout(t)
  }, [pendingFinish, onAbandon, onRunFinished])

  const currentNode = snap.map.nodes.find((n) => n.id === snap.run.currentNodeId)!
  const visited = new Set(snap.run.visitedNodeIds)
  const activeBattle = snap.run.activeBattle
  const activeBattleKey = activeBattle
    ? `${snap.run.currentNodeId}:${activeBattle.encounterId}`
    : null

  // The stage is unmounted while the battle view is open and mounted again
  // after returning. Rebind the wheel handler to that new DOM node; a []
  // effect would keep the old (detached) node and zoom would stop working.
  useEffect(() => {
    const stage = stageRef.current
    if (!stage) return
    const onWheel = (e: WheelEvent) => {
      e.preventDefault()
      setZoom(z => {
        const next = Math.max(0.6, Math.min(2.0, z + (e.deltaY < 0 ? 0.1 : -0.1)))
        setPan(current => clampPan(current, next))
        return next
      })
    }
    stage.addEventListener('wheel', onWheel, { passive: false })
    return () => stage.removeEventListener('wheel', onWheel)
  }, [activeBattleKey, peekMap, clampPan])
  const battleTicks = activeBattleKey !== null && battleClock.key === activeBattleKey
    ? battleClock.ticks
    : 0

  // Why: 戦闘中の秒カウンタ。切替で親が継続するため peek でも timer が止まらない。
  useEffect(() => {
    if (activeBattleKey === null) return
    const id = window.setInterval(() => {
      setBattleClock((clock) => clock.key === activeBattleKey
        ? { ...clock, ticks: clock.ticks + 1 }
        : { key: activeBattleKey, ticks: 1 })
    }, 1000)
    return () => window.clearInterval(id)
  }, [activeBattleKey])
  const activeReward = snap.run.activeReward
  const activeMerchant = snap.run.activeMerchant
  const activeEvent = snap.run.activeEvent
  const activeRestPending = snap.run.activeRestPending
  const activeActStartRelicChoice = snap.run.activeActStartRelicChoice
  const rewardVisible = activeReward !== null && !rewardDismissed
  const merchantVisible = activeMerchant !== null && !merchantDismissed
  const eventVisible = activeEvent !== null && !eventDismissed
  const restVisible = activeRestPending && !restDismissed
  const actStartRelicVisible = activeActStartRelicChoice !== null && !actStartRelicDismissed
  const resolvedCurrentNodeKind = currentNode.kind === 'Unknown'
    ? (snap.run.unknownResolutions[currentNode.id] ?? null)
    : null
  const bgmScene = resolveMapBgmScene({
    activeBattle: activeBattle !== null,
    currentNodeKind: currentNode.kind,
    resolvedNodeKind: resolvedCurrentNodeKind,
    merchantVisible,
  })

  useEffect(() => {
    if (pendingFinish?.kind === 'gameover') {
      stopBgm({ fadeMs: 500 })
      return
    }
    playBgm(BGM_TRACKS[bgmScene], {
      fadeMs: bgmScene.startsWith('battle') ? 600 : 800,
    })
  }, [bgmScene, pendingFinish?.kind, playBgm, stopBgm])

  useEffect(() => {
    setBgmDuck(menuOpen ? 0.6 : 1, 140)
    return () => setBgmDuck(1, 80)
  }, [menuOpen, setBgmDuck])

  // 選択未完了の間は (popup を一旦閉じていても) 移動を禁止する。
  const choicePending = activeActStartRelicChoice !== null
  // Closing a required-choice popup only hides it; it does not resolve the
  // current node. Keep outgoing nodes locked until the server snapshot confirms
  // that the choice was completed, matching Core RunActions.
  const requiredChoicePending = (activeEvent !== null && activeEvent.chosenIndex === null)
    || (activeRestPending && !snap.run.activeRestCompleted)
    || choicePending
  const blockedByModal = activeBattle !== null
    || rewardVisible
    || merchantVisible
    || eventVisible
    || restVisible
    || choicePending
    || pendingFinish !== null

  // Start タイルが未通過のときは、まず Start マスを踏んでレリック選択イベントを
  // 発動させる必要がある。それまでは次マスを選べないようにする。
  const isActStartEntry = (n: MapNodeDto): boolean =>
    n.id === snap.run.currentNodeId
    && n.kind === 'Start'
    && !visited.has(n.id)
    && activeActStartRelicChoice === null

  function isSelectable(n: MapNodeDto): boolean {
    if (blockedByModal || requiredChoicePending) return false
    if (snap.run.progress !== 'InProgress') return false
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
    if (activeActStartRelicChoice !== null && actStartRelicDismissed) return true
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
      } catch {
        setShopMessage('開始地点へ入れませんでした。')
      } finally { setBusy(false) }
      return
    }
    if (isCurrentReopenable(n)) {
      setRewardDismissed(false)
      setMerchantDismissed(false)
      setEventDismissed(false)
      setRestDismissed(false)
      setActStartRelicDismissed(false)
      return
    }
    if (!isSelectable(n)) return
    setBusy(true)
    setBusyLabel(`${nodeTooltipFor(n.kind, null)}へ移動中…`)
    setTravel({ fromId: currentNode.id, toId: n.id })
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
      setActStartRelicDismissed(false)
    } catch {
      setShopMessage('移動できませんでした。もう一度お試しください。')
    } finally {
      setBusy(false)
      setBusyLabel(null)
      setTravel(null)
    }
  }

  async function handleClaimGold() {
    if (!accountId) return
    await claimGold(accountId)
    void playSe(selectSe('reward', `gold:${snap.run.currentNodeId}`))
    await refresh()
  }

  async function handleClaimPotion() {
    if (!accountId) return
    await claimPotion(accountId)
    void playSe(selectSe('reward', `potion:${snap.run.currentNodeId}`))
    await refresh()
  }

  async function handlePickCard(cardId: string) {
    if (!accountId) return
    await pickCard(accountId, cardId)
    void playSe(selectSe('reward', cardId))
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

  async function handleUsePotionOutsideBattle(slotIndex: number) {
    if (!accountId || activeBattle) return
    await consumePotionOutsideBattle(accountId, slotIndex)
    void playSe(selectSe('potion', slotIndex))
    await refresh()
  }

  async function handleClaimRelic() {
    if (!accountId) return
    await claimRelic(accountId)
    void playSe(selectSe('reward', `relic:${snap.run.currentNodeId}`))
    await refresh()
  }

  async function handleRerollCard() {
    if (!accountId) return
    const next = await rerollCardChoices(accountId)
    void playSe(selectSe('cardShuffle', snap.run.currentNodeId))
    setSnap(next)
  }

  async function handleBuy(kind: 'card' | 'relic' | 'potion', id: string) {
    if (!accountId) return
    await buyFromMerchant(accountId, { kind, id })
    void playSe(selectSe('purchase', `${kind}:${id}`))
    await refresh()
  }

  async function handleDiscardAtMerchant(deckIndex: number) {
    if (!accountId) return
    await discardAtMerchant(accountId, deckIndex)
    void playSe(selectSe('cardDiscard', deckIndex))
    await refresh()
  }

  async function handleLeaveMerchant() {
    if (!accountId) return
    await leaveMerchant(accountId)
    setMerchantDismissed(true)
  }

  async function handleChooseEvent(choiceIndex: number) {
    if (!accountId) return
    const result = await chooseEvent(accountId, choiceIndex)
    if ('outcome' in result) {
      onRunFinished?.(result, snapRef.current)
    } else {
      setSnap(result)
    }
  }

  async function handleRestHeal() {
    if (!accountId) return
    await restHeal(accountId)
    void playSe(selectSe('heal', snap.run.currentNodeId))
    await refresh()
  }

  async function handleRestUpgrade(deckIndex: number) {
    if (!accountId) return
    await restUpgrade(accountId, deckIndex)
    void playSe(selectSe('cardUpgrade', deckIndex))
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

  // Why: 戦闘中 (activeBattle && !peekMap) は確認ダイアログを挟み、戦闘外 (peek
  // 中 / 戦闘なし) は即タイトルに戻す。両分岐で同じ menuNode を render することで
  // onAbandon / onClose の closures が一箇所に集約され、二重メンテナンスを防ぐ。
  const requireExitConfirm = !!activeBattle && !peekMap
  const menuNode = menuOpen ? (
    <InGameMenuScreen
      onClose={() => setMenuOpen(false)}
      onExitToMenu={onExitToMenu}
      onAbandon={(result) => {
        setMenuOpen(false)
        setPendingFinish({ kind: 'abandon', result })
      }}
      elapsedSecondsRef={mountedAt}
      requireExitConfirm={requireExitConfirm}
    />
  ) : null

  // 戦闘中はマップ UI ではなく BattleScreen をフルスクリーン表示する。
  // BattleScreen が /finalize を呼び終えたら RunSnapshotDto (続行) または
  // RunResultDto (ラン終了) を渡してくる。
  // peekMap=true のときは戦闘中でもマップ表示にスイッチ (BattleScreen の MAP
  // ボタンから peek 切替で再帰的に来る)。Server 側の戦闘 session は維持される
  // ので peek 解除で BattleScreen に戻ると getBattle で復元される。
  if (activeBattle && accountId && !peekMap) {
    // playSeconds に battleTicks を加算した snapshot を渡し、TopBar timer が
    // 切替で reset しないようにする (ユーザ要望)。
    const snapWithTickedTime = {
      ...snap,
      run: { ...snap.run, playSeconds: snap.run.playSeconds + battleTicks },
    }
    return (
      <>
        <BattleScreen
          accountId={accountId}
          snapshot={snapWithTickedTime}
          onTogglePeek={() => setPeekMap(true)}
          onBattleStateChange={setLivefBattleState}
          onBattleResolved={(result) => {
            setLivefBattleState(null)
            if ('outcome' in result) {
              onRunFinished?.(result, snapRef.current)
            } else {
              setSnap(result)
              setRewardDismissed(false)
              setPeekMap(false)
            }
          }}
          menuOpen={menuOpen}
          onOpenMenu={toggleMenu}
        />
        {menuNode}
      </>
    )
  }

  // Why: peek 中も TopBar に live battle state を表示する (ユーザ要望)。
  // 直近受け取った livefBattleState の hero HP / potions を優先使用。
  const heroLive = livefBattleState?.allies.find(a => a.definitionId === 'hero')
  const liveHp = heroLive?.currentHp ?? snap.run.currentHp
  const liveMaxHp = heroLive?.maxHp ?? snap.run.maxHp
  const livePotions = livefBattleState?.potions ?? snap.run.potions
  const liveRelics = livefBattleState?.ownedRelicIds ?? snap.run.relics
  return (
      <main className="map-screen">
        <TopBar
          currentHp={liveHp}
          maxHp={liveMaxHp}
          gold={snap.run.gold}
          potions={livePotions}
          deck={snap.run.deck}
          relics={liveRelics}
          onDiscardPotion={handleDiscardPotion}
          onUsePotion={activeBattle ? undefined : handleUsePotionOutsideBattle}
          outsideBattlePotionUseOnly
          onOpenMenu={toggleMenu}
          menuActive={menuOpen}
          onTogglePeek={activeBattle ? () => setPeekMap(v => !v) : () => {}}
          peekActive={peekMap}
          peekDisabled={!activeBattle}
          playSeconds={snap.run.playSeconds + battleTicks}
        />
        <div className="map-screen__body" data-act={snap.run.currentAct}>
          <div className="map-screen__pattern" aria-hidden="true" />
          <div className="map-screen__bg" aria-hidden="true" />

        <div
          className="map-screen__stage"
          ref={stageRef}
          onPointerDown={onStagePointerDown}
          onPointerMove={onStagePointerMove}
          onPointerUp={onStagePointerUp}
          onPointerCancel={onStagePointerUp}
          style={{ cursor: dragging ? 'grabbing' : 'grab' }}
        >
          <div className="map-screen__controls" aria-label="マップ表示操作">
            <button type="button" onClick={() => setZoomClamped(zoom + 0.2)} aria-label="拡大">＋</button>
            <output aria-label="ズーム率">{Math.round(zoom * 100)}%</output>
            <button type="button" onClick={() => setZoomClamped(zoom - 0.2)} aria-label="縮小">－</button>
            <button
              type="button"
              onClick={() => {
                const stage = stageRef.current
                if (!stage) return
                const p = posViewBox(currentNode)
                setPan(clampPan({
                  x: (50 - p.x) / 100 * stage.clientWidth * zoom,
                  y: (50 - (-100 + p.y * 3)) / 100 * stage.clientHeight * zoom,
                }))
              }}
              aria-label="現在地を中央に戻す"
            >
              ◎
            </button>
          </div>
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
                  // Hide "next" highlighting on outgoing edges from a Start
                  // tile that hasn't been entered yet — the player must step
                  // on Start first; only then do the next-tile previews appear.
                  const startEntryActive = currentNode.kind === 'Start' && !visited.has(currentNode.id)
                  const nextEdge = !startEntryActive
                    && n.id === snap.run.currentNodeId
                    && isSelectable(to)
                  // Why: 過去の道は青、次に行ける道は黄色、それ以外は白。
                  // 全ての道を黒で縁取り (ユーザ要望)。
                  const stroke = visitedEdge
                    ? '#3ec6ff'
                    : nextEdge
                      ? '#f1c95b'
                      : '#ffffff'
                  // Why: 旧太さ (viewBox 単位 0.45) は斜道で十分太かったが、
                  // vector-effect="non-scaling-stroke" 化で px 換算したら
                  // 細すぎたため、斜道相当の見た目に合わせて太く。
                  // outer (黒縁) は inner (色) より太く描画して縁取り効果を出す。
                  const edgeState = visitedEdge ? 'is-traced' : nextEdge ? 'is-next' : 'is-far'
                  return (
                    <g key={`${n.id}-${toId}`}>
                      <line
                        x1={a.x} y1={a.y} x2={b.x} y2={b.y}
                        stroke="#000000"
                        strokeOpacity={0.85}
                        strokeWidth={9}
                        strokeLinecap="round"
                        vectorEffect="non-scaling-stroke"
                      />
                      <line
                        className={`map-screen__edge ${edgeState}`}
                        x1={a.x} y1={a.y} x2={b.x} y2={b.y}
                        stroke={stroke}
                        strokeOpacity={1}
                        strokeWidth={6}
                        strokeLinecap="round"
                        vectorEffect="non-scaling-stroke"
                      />
                    </g>
                  )
                }),
              )}
              {travel && (() => {
                const from = snap.map.nodes.find(node => node.id === travel.fromId)
                const to = snap.map.nodes.find(node => node.id === travel.toId)
                if (!from || !to) return null
                const a = posViewBox(from)
                const b = posViewBox(to)
                return (
                  <circle className="map-screen__travel-dot" r="1.15" cx={a.x} cy={a.y}>
                    <animate
                      attributeName="cx"
                      from={a.x}
                      to={b.x}
                      dur="500ms"
                      fill="freeze"
                    />
                    <animate
                      attributeName="cy"
                      from={a.y}
                      to={b.y}
                      dur="500ms"
                      fill="freeze"
                    />
                  </circle>
                )
              })()}
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

              // Why: 通り過ぎたマス / 現在階層と同列または下にあって選択不可な
              // マスは暗色化して誤クリックを防ぐ (ユーザ要望)。現在マス + 次に
              // 行けるマス + 上 (= 未到達 row > current.row) の未選択マスは
              // 暗くしない。
              const isDim =
                !isCurrent && !selectable && !reopen && !startEntry &&
                (isVisited || n.row <= currentNode.row)

              const classes = [
                'map-screen__node',
                stateClass,
                kindClassFor(n.kind, resolvedKind),
                reopen ? 'is-reopen' : '',
                startEntry ? 'is-pulse' : '',
                isDim ? 'is-dim' : '',
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
                  aria-current={isCurrent ? 'location' : undefined}
                  onClick={() => handleClick(n)}
                  disabled={!clickable}
                  className={classes}
                  style={{ left, top, cursor: clickable ? 'pointer' : 'default' }}
                  aria-label={`マス ${n.id} (${nodeTooltipFor(n.kind, resolvedKind)})`}
                  title={nodeTooltipFor(n.kind, resolvedKind)}
                >
                  {isCurrent && (
                    <span
                      className="map-screen__current-marker"
                      data-testid="map-current-marker"
                      aria-hidden="true"
                    >
                      現在地
                    </span>
                  )}
                  <span aria-hidden="true">{iconFor(n.kind, resolvedKind)}</span>
                  {label && <span className="map-screen__node-label">{label}</span>}
                </button>
              )
            })}
          </div>

          <div className="map-screen__key" aria-hidden="true">
            <div className="map-screen__key-title">マップ凡例</div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym"><img src={mapIcon('enemy')} alt="" /></span><span>敵</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym k--elite"><img src={mapIcon('elite')} alt="" /></span><span>強敵</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym k--rest"><img src={mapIcon('rest')} alt="" /></span><span>休憩</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym k--merchant"><img src={mapIcon('merchant')} alt="" /></span><span>商店</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym k--treasure"><img src={mapIcon('treasure')} alt="" /></span><span>宝箱</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym"><img src={mapIcon('event')} alt="" /></span><span>イベント</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym"><img src={mapIcon('unknown')} alt="" /></span><span>不明</span></div>
            <div className="map-screen__key-row"><span className="map-screen__key-sym k--boss"><img src={mapIcon('boss')} alt="" /></span><span>ボス</span></div>
          </div>
          {busyLabel && (
            <div className="map-screen__travel-status" role="status" aria-live="polite">
              <span aria-hidden="true">◆</span> {busyLabel}
            </div>
          )}
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

        {pendingFinish !== null && (
          <div className="map-screen__gameover" role="status" aria-live="polite">
            <span className="map-screen__gameover-title">GAME OVER</span>
            <span className="map-screen__gameover-cause">
              {pendingFinish.kind === 'abandon'
                ? 'ゲームを諦めた'
                : '体力が0になった'}
            </span>
          </div>
        )}

        {import.meta.env.DEV && (
          <div className="map-screen__debug">
            <Button onClick={onDebugDamage ?? internalDebugDamage} aria-label="DEBUG -10HP">DEBUG -10HP</Button>
          </div>
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
            onRerollCard={handleRerollCard}
          />
        )}

        {menuNode}

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
            healPreview={snap.run.restHealPreview}
            onHeal={handleRestHeal}
            onUpgrade={handleRestUpgrade}
            onClose={handleCloseRest}
          />
        )}

        {actStartRelicVisible && activeActStartRelicChoice && (
          <ActStartRelicScreen
            choices={activeActStartRelicChoice.relicIds}
            relicNames={relicNames}
            relicDescriptions={relicDescriptions}
            relicEffectTexts={relicEffectTexts}
            relicFlavors={relicFlavors}
            onChoose={async (relicId) => {
              if (!accountId) return
              await chooseActStartRelic(accountId, relicId)
            }}
            onClose={async () => {
              setActStartRelicDismissed(true)
              await refresh()
            }}
          />
        )}
        </div>
      </main>
  )
}
