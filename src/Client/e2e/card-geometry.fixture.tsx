/* eslint-disable react-refresh/only-export-components -- Browser-only E2E fixture exports its imperative mount entry point. */
import { createRoot } from 'react-dom/client'
import type { CSSProperties, ReactNode } from 'react'
import { Card } from '../src/components/Card'

import '../src/screens/RewardPopup.css'
import '../src/screens/MerchantScreen.css'
import '../src/screens/RestScreen.css'
import '../src/screens/AchievementsScreen.css'
import '../src/components/TopBar.css'
import '../src/screens/battleScreen/CardChoiceModal.css'
import '../src/screens/BattleScreen.css'

function TestCard() {
  return (
    <Card
      name="防御撃"
      cost={1}
      type="attack"
      rarity="c"
      description="5ダメージ。4ブロック。"
    />
  )
}

function TwoDigitCard() {
  return (
    <Card
      name="高コスト"
      cost={12}
      type="power"
      rarity="e"
      description="二桁コスト表示の検証用。"
    />
  )
}

function ReducedCostCard() {
  return (
    <Card
      name="強打"
      cost={1}
      costOrig={2}
      type="attack"
      rarity="r"
      description="軽減コスト表示の検証用。"
    />
  )
}

function Case({ id, children }: { id: string; children: ReactNode }) {
  return (
    <section
      data-card-context={id}
      style={{ width: 140, height: 190, position: 'relative' }}
    >
      {children}
    </section>
  )
}

/** Mounts every full-card presentation context without adding a production route. */
export async function mountCardGeometryFixture() {
  document.getElementById('card-geometry-fixture')?.remove()

  const host = document.createElement('div')
  host.id = 'card-geometry-fixture'
  Object.assign(host.style, {
    position: 'fixed',
    inset: '0',
    zIndex: '2147483000',
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'flex-start',
    gap: '16px',
    padding: '20px',
    overflow: 'auto',
    background: '#111',
  })
  document.body.append(host)

  createRoot(host).render(
    <>
      <Case id="plain"><TestCard /></Case>
      <Case id="two-digit"><TwoDigitCard /></Case>
      <Case id="reduced-cost"><ReducedCostCard /></Case>
      <Case id="reward">
        <button type="button" className="rw-picker__card"><TestCard /></button>
      </Case>
      <Case id="merchant-sale">
        <li className="mc-card-slot">
          <button type="button" className="mc-card-slot__btn"><TestCard /></button>
        </li>
      </Case>
      <Case id="merchant-picker">
        <li className="mc-picker-item">
          <button type="button" className="mc-picker-card"><TestCard /></button>
        </li>
      </Case>
      <Case id="rest-picker">
        <li className="rs-picker-item">
          <button type="button" className="rs-picker-card"><TestCard /></button>
        </li>
      </Case>
      <Case id="topbar">
        <li className="topbar__deck-item"><TestCard /></li>
      </Case>
      <Case id="achievement">
        <div className="achievements__card-cell"><TestCard /></div>
      </Case>
      <Case id="choice">
        <div className="ccm-hand__card"><TestCard /></div>
      </Case>
      <Case id="hand">
        <div
          className="hand"
          style={{
            left: 56,
            bottom: 'auto',
            width: 112,
            height: 156.8,
          }}
        >
          <TestCard />
        </div>
      </Case>
      <div
        className="playing-card"
        data-card-context="playing"
        data-play-stage="holding"
        style={{
          '--center-x': '1080px',
          '--center-y': '500px',
        } as CSSProperties}
      >
        <TestCard />
      </div>
    </>,
  )

  await new Promise<void>(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  })

  const hand = host.querySelector<HTMLElement>('[data-card-context="hand"] .card')
  hand?.style.setProperty('--fan-x', '0px')
  hand?.style.setProperty('--fan-y', '0px')
  hand?.style.setProperty('--fan-r', '0deg')

  await new Promise<void>(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  })
  document.documentElement.dataset.cardGeometryReady = 'true'
}
