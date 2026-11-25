import { useState } from 'react'
import type { CardInstanceDto, RestHealPreviewDto } from '../api/types'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { cardDisplay } from '../components/cardDisplay'
import { Popup } from '../components/Popup'
import { useCardCatalog } from '../hooks/useCardCatalog'
import { sceneArt, systemIcon } from '../presentation/assets'
import './RestScreen.css'

type Props = {
  deck: CardInstanceDto[]
  completed: boolean
  healPreview?: RestHealPreviewDto | null
  onHeal: () => void | Promise<void>
  onUpgrade: (deckIndex: number) => void | Promise<void>
  onClose: () => void
}

export function RestScreen({ deck, completed, healPreview, onHeal, onUpgrade, onClose }: Props) {
  const [mode, setMode] = useState<'choose' | 'upgrade'>('choose')
  const [confirming, setConfirming] = useState<{ index: number; name: string } | null>(null)
  const [pendingAction, setPendingAction] = useState<'heal' | 'upgrade' | null>(null)
  const [completedAction, setCompletedAction] = useState<'heal' | 'upgrade' | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const { names, catalog } = useCardCatalog()
  const healDescription = healPreview
    ? `最大HPの${healPreview.basePercent}％を回復する(HP${healPreview.currentHpBefore}→${healPreview.currentHpAfter})。`
    : '最大HPの30％を回復する。'

  if (mode === 'upgrade') {
    // Keep the entire deck visible while making only valid upgrade targets
    // actionable, so the player can compare upgraded and unavailable cards.
    const cards = deck.map((card, index) => ({
      card,
      index,
      canUpgrade: !completed && !card.upgraded && !!catalog?.[card.id]?.upgradable,
    }))

    return (
      <>
        <Popup
          open
          variant="picker"
          panelClassName="rest-upgrade-popup"
          title="カードを強化"
          subtitle="カードをクリックして強化"
          width={640}
          footer={
            <Button
              variant="secondary"
              onClick={() => setMode('choose')}
              aria-label="Back"
            >
              戻る
            </Button>
          }
        >
          {cards.length === 0 ? (
            <p className="rs-picker-empty">強化できるカードがありません</p>
          ) : (
            <ul className="rs-picker-body">
              {cards.map(e => {
                const name = names[e.card.id] ?? e.card.id
                const disp = cardDisplay(e.card.id, catalog, name)
                return (
                  <li
                    key={e.index}
                    className={`rs-picker-item${e.canUpgrade ? '' : ' rs-picker-item--unavailable'}`}
                    data-upgrade-available={e.canUpgrade ? 'true' : 'false'}
                  >
                    <button
                      type="button"
                      className={`rs-picker-card${e.canUpgrade ? '' : ' rs-picker-card--unavailable'}`}
                      onClick={() => e.canUpgrade && setConfirming({ index: e.index, name })}
                      disabled={!e.canUpgrade}
                      aria-label={`Upgrade ${name} at ${e.index}`}
                    >
                      <Card
                        name={disp.name}
                        cost={disp.cost}
                        type={disp.type}
                        rarity={disp.rarity}
                        poolClass={disp.poolClass}
                        description={disp.description}
                        upgradedDescription={disp.upgradedDescription}
                        upgraded={e.card.upgraded}
                      />
                      <span className="rs-picker-card__overlay" aria-hidden="true">
                        <span className="rs-picker-card__overlay-label">強化</span>
                      </span>
                    </button>
                  </li>
                )
              })}
            </ul>
          )}
        </Popup>
        {confirming && (() => {
          const target = deck[confirming.index]
          const disp = target ? cardDisplay(target.id, catalog, confirming.name) : null
          return (
            <Popup
              open
              variant="confirm"
              title="強化しますか?"
              width={520}
              footer={
                <>
                  <Button
                    variant="secondary"
                    onClick={() => setConfirming(null)}
                    aria-label="Cancel upgrade"
                  >
                    いいえ
                  </Button>
                  <Button
                    variant="primary"
                    onClick={async () => {
                      if (pendingAction !== null) return
                      const idx = confirming.index
                      setPendingAction('upgrade')
                      setActionError(null)
                      try {
                        await onUpgrade(idx)
                        setCompletedAction('upgrade')
                        setConfirming(null)
                        setMode('choose')
                      } catch {
                        setActionError('強化に失敗しました。もう一度お試しください。')
                      } finally {
                        setPendingAction(null)
                      }
                    }}
                    aria-label="Confirm upgrade"
                    disabled={pendingAction !== null}
                  >
                    {pendingAction === 'upgrade' ? '強化中…' : 'はい'}
                  </Button>
                </>
              }
            >
              {disp ? (
                <div className="rs-confirm-preview">
                  <Card
                    name={disp.name}
                    cost={disp.cost}
                    type={disp.type}
                    rarity={disp.rarity}
                    poolClass={disp.poolClass}
                    description={disp.description}
                    upgradedDescription={disp.upgradedDescription}
                    upgraded={false}
                  />
                  <span className="rs-confirm-arrow" aria-hidden="true">→</span>
                  <Card
                    name={disp.name}
                    cost={disp.cost}
                    type={disp.type}
                    rarity={disp.rarity}
                    poolClass={disp.poolClass}
                    description={disp.description}
                    upgradedDescription={disp.upgradedDescription}
                    upgraded={true}
                  />
                </div>
              ) : null}
              <p className="rs-confirm-text">
                <strong>{confirming.name}</strong> を強化します。
              </p>
            </Popup>
          )
        })()}
      </>
    )
  }

  return (
    <Popup
      open
      variant="modal"
      title="焚き火"
      panelClassName="rest-choice-popup"
      width={640}
    >
      <div
        className={`rs-stage${completedAction ? ` is-complete is-complete--${completedAction}` : ''}`}
        data-action-state={pendingAction ?? (completedAction ? 'complete' : 'idle')}
        aria-busy={pendingAction !== null}
      >
      <div className="rs-fire" aria-hidden="true">
        <img
          className="rs-fire__logs"
          src={sceneArt('rest_logs')}
          alt=""
          width={192}
          height={192}
          draggable={false}
        />
        <span
          className="rs-fire__flame-viewport"
          data-frame-count="6"
          data-frame-width="192"
        >
          <img
            className="rs-fire__flame"
            src={sceneArt('rest_flame')}
            alt=""
            width={1152}
            height={192}
            draggable={false}
          />
        </span>
      </div>
      {pendingAction && (
        <p className="rs-action-status" role="status" aria-live="polite">
          {pendingAction === 'heal' ? '炎のぬくもりを受けています…' : '鍛造しています…'}
        </p>
      )}
      {completedAction && completed && (
        <p className="rs-action-status rs-action-status--complete" role="status" aria-live="polite">
          {completedAction === 'heal' ? '体力を回復しました' : 'カードを強化しました'}
        </p>
      )}
      {actionError && <p className="rs-action-error" role="alert">{actionError}</p>}
      <div className="rs-actions">
        <ul className="rs-choices">
          <li>
            <button
              type="button"
              className="rs-choice rs-choice--heal"
              onClick={async () => {
                if (completed || pendingAction !== null) return
                setPendingAction('heal')
                setActionError(null)
                try {
                  await onHeal()
                  setCompletedAction('heal')
                } catch {
                  setActionError('休息に失敗しました。もう一度お試しください。')
                } finally {
                  setPendingAction(null)
                }
              }}
              disabled={completed || pendingAction !== null}
              aria-label="Heal"
            >
              <span className="rs-choice-icon" aria-hidden="true">
                <img src={sceneArt('rest_heal')} alt="" draggable={false} />
              </span>
              <span className="rs-choice-body">
                <span className="rs-choice-name">休息</span>
                <span className="rs-choice-desc">
                  {healDescription}{completed ? '（使用済み）' : ''}
                </span>
                <span className="rs-choice-tags">
                  <span className="rs-tag rs-tag--heal">HP+{healPreview?.basePercent ?? 30}%</span>
                  {healPreview && healPreview.passiveBonusAmount !== 0 ? (
                    <span className="rs-tag rs-tag--heal">
                      HP{healPreview.passiveBonusAmount > 0 ? '+' : ''}{healPreview.passiveBonusAmount}
                    </span>
                  ) : null}
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
              disabled={completed || pendingAction !== null}
              aria-label="Upgrade card"
            >
              <span className="rs-choice-icon" aria-hidden="true">
                <img src={systemIcon('upgrade')} alt="" draggable={false} />
              </span>
              <span className="rs-choice-body">
                <span className="rs-choice-name">鍛造</span>
                <span className="rs-choice-desc">
                  デッキの未強化カードから1枚を強化する。{completed ? '（使用済み）' : ''}
                </span>
                <span className="rs-choice-tags">
                  <span className="rs-tag rs-tag--upgrade">+カード強化×1</span>
                  {completed ? <span className="rs-tag rs-tag--used">使用済み</span> : null}
                </span>
              </span>
            </button>
          </li>
        </ul>
        <div className="rs-close-wrap">
          <Button onClick={() => onClose()} aria-label="Close" disabled={pendingAction !== null}>
            閉じる
          </Button>
        </div>
      </div>
      </div>
    </Popup>
  )
}
