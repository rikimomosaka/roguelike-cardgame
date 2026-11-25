/* eslint-disable react-refresh/only-export-components -- Browser-only E2E fixture exports its imperative mount entry point. */
import { createRoot } from 'react-dom/client'
import type { CSSProperties, ReactNode } from 'react'
import { Card } from '../src/components/Card'
import type { CardType } from '../src/components/Card'
import { TooltipHost } from '../src/components/Tooltip'
import type { CardPoolClass } from '../src/api/catalog'

import '../src/screens/RewardPopup.css'
import '../src/screens/MerchantScreen.css'
import '../src/screens/RestScreen.css'
import '../src/screens/AchievementsScreen.css'
import '../src/components/TopBar.css'
import '../src/screens/battleScreen/CardChoiceModal.css'
import '../src/screens/battleScreen/FinisherRecordModal.css'
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
      comboContinues
      type="attack"
      rarity="r"
      description="軽減コスト表示の検証用。"
    />
  )
}

function IncreasedCostCard() {
  return (
    <Card
      name="増加検証"
      cost={3}
      costOriginal={2}
      type="skill"
      rarity="r"
      poolClass="Guardian"
      description="増加コスト表示の検証用。"
    />
  )
}

function CostBreakdownCard() {
  return (
    <Card
      name="変動検証"
      cost={2}
      costOriginal={2}
      costChanges={[
        { kind: 'fixed', value: 3, sourceId: 'confusion', sourceName: '混乱' },
        { kind: 'modifier', value: -1, sourceId: 'source', sourceName: '手札整備+' },
      ]}
      type="skill"
      rarity="r"
      description="内訳表示の検証用。"
    />
  )
}

function AuthoritativeOriginalCostCard() {
  return (
    <Card
      name="原価記録"
      cost={1}
      costOriginal={3}
      type="skill"
      rarity="r"
      poolClass="Guardian"
      description="自身に5ブロック。"
    />
  )
}

function ShortLineCard() {
  return (
    <Card
      name="攻撃の基本"
      cost={1}
      type="attack"
      rarity="c"
      description="敵単体に6アタック。"
    />
  )
}

function LongSingleLineCard() {
  return (
    <Card
      name="長文検証"
      cost={2}
      type="skill"
      rarity="r"
      description="自身に11ブロックし次の攻撃を完全に防ぐ。"
    />
  )
}

function TwoLineCard() {
  return (
    <Card
      name="二行検証"
      cost={1}
      type="skill"
      rarity="c"
      description={'ワイルド。\n敵単体に6アタック。'}
    />
  )
}

function ComboAuraCard() {
  return (
    <Card
      name="連鎖攻撃"
      cost={1}
      costOriginal={2}
      comboRingHighlighted
      type="attack"
      rarity="r"
      poolClass="Striker"
      description="敵単体に7アタック。"
    />
  )
}

function LongTextCard() {
  return (
    <Card
      name="決着斬り"
      cost={3}
      type="attack"
      rarity="r"
      poolClass="Striker"
      description={'終撃。\n敵単体に10アタック。\n乗勢4：敵単体に10アタック。\n1ターン1回。'}
      art={<img src="/assets/backgrounds/events/forgotten_forge.png" alt="" />}
    />
  )
}

function FiveLineCard() {
  return (
    <Card
      name="五行検証"
      cost={2}
      type="skill"
      rarity="r"
      poolClass="Tactician"
      description={'ターン中1度だけ使用可能。\nワイルド。\n伏策：相手のアタック時。\n自身に8ブロック。\n乗勢4：1枚引く。'}
    />
  )
}

function KeywordBoundaryCard() {
  return (
    <Card
      name="境界文検証"
      cost={2}
      type="skill"
      rarity="r"
      poolClass="Tactician"
      description={'[K:contingency]：相手のアタック時。\n[K:contingency]：[N:3]コンボ達成時。\n[K:reprise]：コスト[N:2]以下のアタック・スキル。'}
    />
  )
}

function UnavailableCostCard() {
  return (
    <Card
      name="使用不可"
      cost={2}
      costOriginal={3}
      costUnavailable
      type="skill"
      rarity="r"
      poolClass="Guardian"
      description="現在は使用できない。"
    />
  )
}

function LongNameCard({ name }: { name: string }) {
  return (
    <Card
      name={name}
      cost={1}
      type="power"
      rarity="e"
      poolClass="Enhancer"
      description="味方全体にバフを付与。"
      art={<img src="/assets/backgrounds/events/moonlit_garden.png" alt="" />}
    />
  )
}

function StyledCard({
  name,
  type,
  poolClass,
  selected = false,
  playable = false,
  contingencyArmed = false,
}: {
  name: string
  type: CardType
  poolClass?: CardPoolClass
  selected?: boolean
  playable?: boolean
  contingencyArmed?: boolean
}) {
  return (
    <Card
      name={name}
      cost={1}
      type={type}
      rarity="r"
      poolClass={poolClass}
      selected={selected}
      className={playable ? 'is-playable' : undefined}
      contingencyArmed={contingencyArmed}
      contingencyTriggerText={contingencyArmed ? '相手のアタック時' : undefined}
      description="クラス枠とタイプ別イラスト窓の検証用。"
      art={<img src="/assets/backgrounds/events/moonlit_garden.png" alt="" />}
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
    <TooltipHost>
      <Case id="plain"><TestCard /></Case>
      <Case id="two-digit"><TwoDigitCard /></Case>
      <Case id="reduced-cost"><ReducedCostCard /></Case>
      <Case id="cost-breakdown"><CostBreakdownCard /></Case>
      <Case id="increased-cost"><IncreasedCostCard /></Case>
      <Case id="cost-original-only"><AuthoritativeOriginalCostCard /></Case>
      <Case id="text-short"><ShortLineCard /></Case>
      <Case id="text-long"><LongSingleLineCard /></Case>
      <Case id="two-line-text"><TwoLineCard /></Case>
      <Case id="combo-aura"><ComboAuraCard /></Case>
      <Case id="long-text"><LongTextCard /></Case>
      <Case id="five-line-text"><FiveLineCard /></Case>
      <Case id="keyword-boundary-text"><KeywordBoundaryCard /></Case>
      <Case id="transformed-keyword-boundary-text">
        <div style={{ transform: 'rotate(-14deg) scale(0.8)', transformOrigin: 'top left' }}>
          <KeywordBoundaryCard />
        </div>
      </Case>
      <Case id="unavailable-cost"><UnavailableCostCard /></Case>
      <Case id="transformed-cost">
        <div style={{ transform: 'rotate(-14deg) scale(0.8)', transformOrigin: 'top left' }}>
          <ReducedCostCard />
        </div>
      </Case>
      <Case id="name-five"><LongNameCard name="余韻の賛歌" /></Case>
      <Case id="name-seven"><LongNameCard name="タクティシャン" /></Case>
      <Case id="name-long"><LongNameCard name="グランド・コーラス" /></Case>
      <Case id="pool-striker"><StyledCard name="ストライカー" type="attack" poolClass="Striker" /></Case>
      <Case id="pool-guardian"><StyledCard name="ガーディアン" type="attack" poolClass="Guardian" /></Case>
      <Case id="pool-tactician"><StyledCard name="タクティシャン" type="attack" poolClass="Tactician" /></Case>
      <Case id="pool-enhancer"><StyledCard name="エンハンサー" type="attack" poolClass="Enhancer" /></Case>
      <Case id="pool-universal"><StyledCard name="ユニバーサル" type="attack" poolClass="Universal" /></Case>
      <Case id="type-attack"><StyledCard name="アタック" type="attack" /></Case>
      <Case id="type-skill"><StyledCard name="スキル" type="skill" /></Case>
      <Case id="type-power"><StyledCard name="パワー" type="power" /></Case>
      <Case id="type-curse"><StyledCard name="呪い" type="curse" poolClass="Striker" /></Case>
      <Case id="type-status"><StyledCard name="状態異常" type="status" /></Case>
      <Case id="type-unit"><StyledCard name="ユニット" type="unit" /></Case>
      <Case id="combined-frame-states">
        <StyledCard
          name="伏策状態"
          type="skill"
          poolClass="Tactician"
          selected
          playable
          contingencyArmed
        />
      </Case>
      <Case id="reward">
        <button type="button" className="rw-picker__card"><TestCard /></button>
      </Case>
      <Case id="merchant-sale">
        <li className="mc-card-slot" style={{ listStyle: 'none' }}>
          <button type="button" className="mc-card-slot__btn"><TestCard /></button>
        </li>
      </Case>
      <Case id="merchant-picker">
        <li className="mc-picker-item" style={{ listStyle: 'none' }}>
          <button type="button" className="mc-picker-card"><TestCard /></button>
        </li>
      </Case>
      <Case id="rest-picker">
        <li className="rs-picker-item" style={{ listStyle: 'none' }}>
          <button type="button" className="rs-picker-card"><TestCard /></button>
        </li>
      </Case>
      <Case id="topbar">
        <li className="topbar__deck-item" style={{ listStyle: 'none' }}><TestCard /></li>
      </Case>
      <Case id="achievement">
        <div className="achievements__card-cell"><TestCard /></div>
      </Case>
      <Case id="edge-topbar">
        <ul className="topbar__deck-list" style={{ width: 140, height: 185 }}>
          <li className="topbar__deck-item"><ReducedCostCard /></li>
        </ul>
      </Case>
      <Case id="edge-pile">
        <ul className="pile-modal__list" style={{ width: 140, height: 185 }}>
          <li className="pile-modal__item"><UnavailableCostCard /></li>
        </ul>
      </Case>
      <Case id="edge-choice-selected">
        <div className="ccm-list ccm-card-grid" style={{ width: 140, height: 185 }}>
          <button type="button" className="ccm-list__row is-selected">
            <UnavailableCostCard />
          </button>
        </div>
      </Case>
      <Case id="edge-rest">
        <div className="rest-upgrade-popup">
          <ul className="rs-picker-body" style={{ width: 140, height: 185 }}>
            <li className="rs-picker-item">
              <button type="button" className="rs-picker-card"><ReducedCostCard /></button>
            </li>
          </ul>
        </div>
      </Case>
      <Case id="edge-summon-rail">
        <div className="achievements__summon-cards" style={{ width: 140, height: 185 }}>
          <ReducedCostCard />
        </div>
      </Case>
      <Case id="edge-achievement-grid">
        <div className="achievements__scroll" style={{ width: 140, height: 185 }}>
          <div className="achievements__card-grid">
            <div className="achievements__card-cell"><UnavailableCostCard /></div>
          </div>
        </div>
      </Case>
      <Case id="finisher-order">
        <ol className="finisher-record__cards">
          <li>
            <span className="finisher-record__order" aria-hidden="true">1</span>
            <ReducedCostCard />
          </li>
        </ol>
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
      <div
        className="effect-exhaust-card"
        data-card-context="effect-exhaust"
        style={{
          '--exhaust-start-x': '280px',
          '--exhaust-start-y': '620px',
          '--exhaust-start-rot': '-14deg',
          '--exhaust-center-x': '640px',
          '--exhaust-center-y': '360px',
        } as CSSProperties}
      >
        <TestCard />
      </div>
    </TooltipHost>,
  )

  await new Promise<void>(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  })

  const exhaustAnimation = host
    .querySelector<HTMLElement>('[data-card-context="effect-exhaust"]')
    ?.getAnimations()[0]
  if (!exhaustAnimation) throw new Error('effect exhaust animation was not created')
  await exhaustAnimation.ready
  exhaustAnimation.pause()
  exhaustAnimation.currentTime = 315

  const hand = host.querySelector<HTMLElement>('[data-card-context="hand"] .card')
  hand?.style.setProperty('--fan-x', '0px')
  hand?.style.setProperty('--fan-y', '0px')
  hand?.style.setProperty('--fan-r', '0deg')

  await new Promise<void>(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  })
  document.documentElement.dataset.cardGeometryReady = 'true'
}
