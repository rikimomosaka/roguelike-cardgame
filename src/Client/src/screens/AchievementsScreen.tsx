import { useEffect, useState } from 'react'
import { getBestiary } from '../api/bestiary'
import { getHistory } from '../api/history'
import type { BestiaryDto, RunResultDto } from '../api/types'
import { Button } from '../components/Button'

type Tab = 'cards' | 'relics' | 'potions' | 'enemies' | 'history'

type Props = {
  accountId: string
  onBack: () => void
}

export function AchievementsScreen({ accountId, onBack }: Props) {
  const [tab, setTab] = useState<Tab>('cards')
  const [bestiary, setBestiary] = useState<BestiaryDto | null>(null)
  const [history, setHistory] = useState<RunResultDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    Promise.all([getBestiary(accountId), getHistory(accountId)])
      .then(([b, h]) => {
        if (cancelled) return
        setBestiary(b)
        setHistory(h)
      })
      .catch(() => { if (!cancelled) setError('読み込みに失敗しました') })
    return () => { cancelled = true }
  }, [accountId])

  if (error) return (
    <main className="achievements">
      <p>{error}</p>
      <Button variant="secondary" onClick={onBack}>戻る</Button>
    </main>
  )
  if (bestiary === null || history === null) return (
    <main className="achievements"><p>読み込み中...</p></main>
  )

  return (
    <main className="achievements">
      <header className="achievements__tabs" role="tablist">
        <TabButton label="カード" active={tab === 'cards'} onClick={() => setTab('cards')} />
        <TabButton label="レリック" active={tab === 'relics'} onClick={() => setTab('relics')} />
        <TabButton label="ポーション" active={tab === 'potions'} onClick={() => setTab('potions')} />
        <TabButton label="モンスター" active={tab === 'enemies'} onClick={() => setTab('enemies')} />
        <TabButton label="履歴" active={tab === 'history'} onClick={() => setTab('history')} />
      </header>
      <section className="achievements__content">
        {tab === 'cards' && <BestiaryList
          allIds={bestiary.allKnownCardBaseIds}
          discovered={new Set(bestiary.discoveredCardBaseIds)}
          unknownLabel="???" />}
        {tab === 'relics' && <BestiaryList
          allIds={bestiary.allKnownRelicIds}
          discovered={new Set(bestiary.discoveredRelicIds)}
          unknownLabel="???" />}
        {tab === 'potions' && <BestiaryList
          allIds={bestiary.allKnownPotionIds}
          discovered={new Set(bestiary.discoveredPotionIds)}
          unknownLabel="???" />}
        {tab === 'enemies' && <BestiaryList
          allIds={bestiary.allKnownEnemyIds}
          discovered={new Set(bestiary.encounteredEnemyIds)}
          unknownLabel="???" />}
        {tab === 'history' && <HistoryList history={history} />}
      </section>
      <footer className="achievements__footer">
        <Button variant="secondary" onClick={onBack}>戻る</Button>
      </footer>
    </main>
  )
}

function TabButton({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button role="tab" aria-selected={active} onClick={onClick}
      className={'achievements__tab' + (active ? ' achievements__tab--active' : '')}>
      {label}
    </button>
  )
}

function BestiaryList({ allIds, discovered, unknownLabel }: {
  allIds: string[]; discovered: Set<string>; unknownLabel: string
}) {
  return (
    <div>
      <p className="achievements__count">{discovered.size} / {allIds.length} 発見</p>
      <ul className="achievements__list">
        {allIds.map(id => (
          <li key={id} className="achievements__item">
            {discovered.has(id) ? <span>✓ {id} ({id})</span> : <span>{unknownLabel} ({id})</span>}
          </li>
        ))}
      </ul>
    </div>
  )
}

function HistoryList({ history }: { history: RunResultDto[] }) {
  const [expanded, setExpanded] = useState<string | null>(null)
  if (history.length === 0) return <p>履歴なし</p>
  return (
    <ul className="achievements__history">
      {history.map(run => (
        <li key={run.runId}>
          <button className="achievements__history-summary"
            onClick={() => setExpanded(expanded === run.runId ? null : run.runId)}>
            [{run.outcome}] Act{run.actReached} / {formatPlayTime(run.playSeconds)} / {run.endedAtUtc}
          </button>
          {expanded === run.runId && <HistoryDetail run={run} />}
        </li>
      ))}
    </ul>
  )
}

function HistoryDetail({ run }: { run: RunResultDto }) {
  return (
    <div className="achievements__history-detail">
      <p>最終 HP: {run.finalHp}/{run.finalMaxHp}</p>
      <p>最終 Gold: {run.finalGold}</p>
      <p>最終デッキ: {run.finalDeck.length === 0
        ? '（なし）'
        : run.finalDeck.map(c => c.id + (c.upgraded ? '+' : '')).join(', ')}</p>
      <p>最終レリック数: {run.finalRelics.length}</p>
      <p>見たカード: {run.seenCardBaseIds.length === 0 ? '（なし）' : run.seenCardBaseIds.join(', ')}</p>
      <p>入手レリック: {run.acquiredRelicIds.length === 0 ? '（なし）' : run.acquiredRelicIds.join(', ')}</p>
      <p>入手ポーション: {run.acquiredPotionIds.length === 0 ? '（なし）' : run.acquiredPotionIds.join(', ')}</p>
      <p>遭遇敵: {run.encounteredEnemyIds.length === 0 ? '（なし）' : run.encounteredEnemyIds.join(', ')}</p>
    </div>
  )
}

function formatPlayTime(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = Math.floor(seconds % 60)
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
}
