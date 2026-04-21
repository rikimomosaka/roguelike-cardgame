import { useEffect, useRef, useState } from 'react'
import { heartbeat, moveToNode } from '../api/runs'
import type { MapNodeDto, RunSnapshotDto, TileKind } from '../api/types'
import { useAccount } from '../context/AccountContext'
import { InGameMenuScreen } from './InGameMenuScreen'

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

function iconFor(kind: TileKind, resolvedKind: TileKind | null): string {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  switch (k) {
    case 'Start': return '●'
    case 'Enemy': return '⚔'
    case 'Elite': return '⚔⚔'
    case 'Merchant': return '商'
    case 'Rest': return '火'
    case 'Treasure': return '宝'
    case 'Unknown': return '?'
    case 'Boss': return '王'
  }
}

export function MapScreen({ snapshot, onExitToMenu, onAbandon }: Props) {
  const { accountId } = useAccount()
  const [snap, setSnap] = useState<RunSnapshotDto>(snapshot)
  const [menuOpen, setMenuOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const mountedAt = useRef<number>(performance.now())

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

  const currentNode = snap.map.nodes.find((n) => n.id === snap.run.currentNodeId)!
  const visited = new Set(snap.run.visitedNodeIds)

  function isSelectable(n: MapNodeDto): boolean {
    return currentNode.outgoingNodeIds.includes(n.id)
  }

  function posOf(n: MapNodeDto): { cx: number; cy: number } {
    const maxRow = 16
    return {
      cx: LEFT_PAD + n.column * COL_W,
      cy: TOP_PAD + (maxRow - n.row) * ROW_H,
    }
  }

  async function handleClick(n: MapNodeDto) {
    if (!accountId || busy || !isSelectable(n)) return
    setBusy(true)
    const elapsed = Math.floor((performance.now() - mountedAt.current) / 1000)
    try {
      await moveToNode(accountId, n.id, Math.max(0, elapsed))
      mountedAt.current = performance.now()
      setSnap((prev) => ({
        ...prev,
        run: {
          ...prev.run,
          currentNodeId: n.id,
          visitedNodeIds: [...prev.run.visitedNodeIds, n.id],
          playSeconds: prev.run.playSeconds + Math.max(0, elapsed),
        },
      }))
    } finally {
      setBusy(false)
    }
  }

  const resolved = snap.run.unknownResolutions
  const maxCol = Math.max(...snap.map.nodes.map((n) => n.column))
  const width = LEFT_PAD * 2 + maxCol * COL_W
  const height = TOP_PAD * 2 + 16 * ROW_H
  const atBoss = currentNode.kind === 'Boss'

  return (
    <main className="map-screen">
      <header className="map-screen__top">
        <span>HP {snap.run.currentHp}/{snap.run.maxHp}</span>
        <span>Gold {snap.run.gold}</span>
        <button aria-label="メニュー" onClick={() => setMenuOpen(true)}>⚙</button>
      </header>

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
          const resolvedKind: TileKind | null = isVisited ? (resolved[n.id] ?? null) : null
          return (
            <g
              key={n.id}
              data-testid={`map-node-${n.id}`}
              data-current={isCurrent ? 'true' : 'false'}
              data-selectable={selectable ? 'true' : 'false'}
              data-visited={isVisited ? 'true' : 'false'}
              onClick={() => handleClick(n)}
              style={{ cursor: selectable ? 'pointer' : 'default' }}
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

      {atBoss && (
        <p className="map-screen__dev-note">
          ボスに到達しました。ここから先は Phase 5 以降で実装されます。
        </p>
      )}

      {menuOpen && (
        <InGameMenuScreen
          onClose={() => setMenuOpen(false)}
          onExitToMenu={onExitToMenu}
          onAbandon={onAbandon}
          elapsedSecondsRef={mountedAt}
        />
      )}
    </main>
  )
}
