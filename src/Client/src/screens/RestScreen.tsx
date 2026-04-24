import { useState } from 'react'
import type { CardInstanceDto } from '../api/types'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { cardDisplay } from '../components/cardDisplay'
import { Popup } from '../components/Popup'
import { useCardCatalog } from '../hooks/useCardCatalog'
import './RestScreen.css'

type Props = {
  deck: CardInstanceDto[]
  completed: boolean
  onHeal: () => void | Promise<void>
  onUpgrade: (deckIndex: number) => void | Promise<void>
  onClose: () => void
}

export function RestScreen({ deck, completed, onHeal, onUpgrade, onClose }: Props) {
  const [mode, setMode] = useState<'choose' | 'upgrade'>('choose')
  const { names, catalog } = useCardCatalog()

  if (mode === 'upgrade') {
    const candidates = deck
      .map((card, index) => ({ card, index, def: catalog?.[card.id] }))
      .filter(e => !e.card.upgraded && !!e.def?.upgradable)

    return (
      <Popup
        open
        variant="modal"
        title="鍛える"
        subtitle="デッキから 1 枚を強化する"
        width={720}
        footer={
          <div className="rs-footer">
            <Button
              variant="secondary"
              onClick={() => setMode('choose')}
              aria-label="Back"
            >
              戻る
            </Button>
          </div>
        }
      >
        {candidates.length === 0 ? (
          <p className="rs-picker-empty">強化できるカードがありません</p>
        ) : (
          <>
            <p className="rs-picker-hint">カードを選んで強化</p>
            <ul className="rs-picker-body">
              {candidates.map(e => {
                const name = names[e.card.id] ?? e.card.id
                const disp = cardDisplay(e.card.id, catalog, name)
                return (
                  <li key={e.index} className="rs-picker-item">
                    <Card
                      name={disp.name}
                      cost={disp.cost}
                      type={disp.type}
                      rarity={disp.rarity}
                      description={disp.description}
                      upgradedDescription={disp.upgradedDescription}
                      upgraded={e.card.upgraded}
                      width={128}
                    />
                    <Button
                      variant="primary"
                      onClick={() => onUpgrade(e.index)}
                      disabled={completed}
                      aria-label={`Upgrade ${name} at ${e.index}`}
                    >
                      強化
                    </Button>
                  </li>
                )
              })}
            </ul>
          </>
        )}
      </Popup>
    )
  }

  return (
    <Popup
      open
      variant="modal"
      title="焚き火"
      subtitle={`休息所${completed ? '(使用済み)' : ''}`}
      width={640}
      footer={
        <div className="rs-footer">
          <Button onClick={() => onClose()} aria-label="Close">
            閉じる
          </Button>
        </div>
      }
    >
      <div className="rs-fire" aria-hidden="true">✦</div>
      <ul className="rs-choices">
        <li>
          <button
            type="button"
            className="rs-choice rs-choice--heal"
            onClick={() => onHeal()}
            disabled={completed}
            aria-label="Heal"
          >
            <span className="rs-choice-icon" aria-hidden="true">☾</span>
            <span className="rs-choice-body">
              <span className="rs-choice-name">休息</span>
              <span className="rs-choice-desc">
                最大 HP の 30% を回復する{completed ? '(使用済み)' : ''}
              </span>
              <span className="rs-choice-tags">
                <span className="rs-tag rs-tag--heal">HP +30%</span>
                {completed ? <span className="rs-tag rs-tag--used">使用済み</span> : null}
              </span>
            </span>
          </button>
        </li>
        <li>
          <button
            type="button"
            className="rs-choice rs-choice--upgrade"
            onClick={() => setMode('upgrade')}
            disabled={completed}
            aria-label="Upgrade card"
          >
            <span className="rs-choice-icon" aria-hidden="true">✦</span>
            <span className="rs-choice-body">
              <span className="rs-choice-name">鍛える</span>
              <span className="rs-choice-desc">
                デッキから 1 枚を強化する{completed ? '(使用済み)' : ''}
              </span>
              <span className="rs-choice-tags">
                <span className="rs-tag rs-tag--upgrade">+ カード強化 x1</span>
                {completed ? <span className="rs-tag rs-tag--used">使用済み</span> : null}
              </span>
            </span>
          </button>
        </li>
      </ul>
    </Popup>
  )
}
