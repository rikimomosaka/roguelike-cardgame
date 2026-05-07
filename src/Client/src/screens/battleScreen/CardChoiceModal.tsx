// src/Client/src/screens/battleScreen/CardChoiceModal.tsx
//
// Phase 10.5.M2-Choose T7: choose 中の手札選択 UI (pile=hand)。
// Phase 10.5.M2-Choose T8: pile=draw / discard の List モードを追加。
//   List モードはカード名のみの縦リスト (full-card 描画は重いため)。
//
// Server 側 RejectIfPending と組み合わせて二重防御するため、modal は
// closeOnEsc=false / closeOnBackdrop=false で「確定」のみ抜け道。
// 候補カード以外はクリック不可、選択枚数 != count では確定 disabled。

import { useState } from 'react'
import { Popup } from '../../components/Popup'
import { Button } from '../../components/Button'
import { Card } from '../../components/Card'
import type { CardType, CardRarity } from '../../components/Card'
import type { BattleCardInstanceDto, PendingCardPlayDto } from '../../api/types'
import './CardChoiceModal.css'

type Props = {
  pending: PendingCardPlayDto
  hand: BattleCardInstanceDto[]
  /** pile=draw 用 (List モード)。 */
  drawPile?: BattleCardInstanceDto[]
  /** pile=discard 用 (List モード)。 */
  discardPile?: BattleCardInstanceDto[]
  cardNames: Record<string, string>
  /** カード定義から CardType を解決 (catalog 未ロード時の default を含めて呼出側で吸収)。 */
  cardTypeOf: (defId: string) => CardType
  cardRarityOf: (defId: string) => CardRarity
  /** カード定義から base cost を解決 (costOverride が無い場合の fallback)。 */
  cardCostOf: (defId: string) => number
  /** 解決失敗時に modal 内 banner として表示するエラーメッセージ。 */
  errorMessage?: string | null
  onConfirm: (selectedIds: string[]) => Promise<void>
}

export function CardChoiceModal({
  pending,
  hand,
  drawPile,
  discardPile,
  cardNames,
  cardTypeOf,
  cardRarityOf,
  cardCostOf,
  errorMessage,
  onConfirm,
}: Props) {
  const [selected, setSelected] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const choice = pending.choice

  const isCandidate = (id: string) => choice.candidateInstanceIds.includes(id)
  const isSelected = (id: string) => selected.includes(id)

  const toggle = (id: string) => {
    if (busy || !isCandidate(id)) return
    setSelected((prev) => {
      if (prev.includes(id)) return prev.filter((x) => x !== id)
      // 上限到達時は単一枚 (count=1) なら入れ替え、複数枚なら無視。
      if (prev.length >= choice.count) {
        if (choice.count === 1) return [id]
        return prev
      }
      return [...prev, id]
    })
  }

  const handleConfirm = async () => {
    if (selected.length !== choice.count || busy) return
    setBusy(true)
    try {
      await onConfirm(selected)
    } finally {
      setBusy(false)
    }
  }

  // pile に応じて source pile を選択。draw / discard は props 未指定なら空配列。
  const sourcePile: BattleCardInstanceDto[] =
    choice.pile === 'hand'
      ? hand
      : choice.pile === 'draw'
        ? (drawPile ?? [])
        : choice.pile === 'discard'
          ? (discardPile ?? [])
          : []

  // Why (Minor #6, T7): hand モードでは「プレイ中のカード自身」を表示から除外し、
  //  視覚的混乱 (選べないカードがグレーで残る) を防ぐ。
  //  draw / discard では played card は別パイル (hand) に居るため filter 不要。
  const visibleSource =
    choice.pile === 'hand'
      ? sourcePile.filter((c) => c.instanceId !== pending.cardInstanceId)
      : sourcePile

  const title = `${actionTitle(choice.action)} (${selected.length}/${choice.count})`

  // Hand モード: full-card 描画 (Card コンポーネント、flex-wrap グリッド)。
  if (choice.pile === 'hand') {
    return (
      <Popup
        open
        variant="modal"
        title={title}
        width={1200}
        closeOnEsc={false}
        closeOnBackdrop={false}
        footerAlign="center"
        footer={
          <Button
            onClick={() => void handleConfirm()}
            disabled={selected.length !== choice.count || busy}
          >
            確定
          </Button>
        }
      >
        {errorMessage ? <div className="ccm-error">{errorMessage}</div> : null}
        <div className="ccm-hand">
          {visibleSource.map((c) => {
            const cand = isCandidate(c.instanceId)
            const sel = isSelected(c.instanceId)
            const cls = [
              'ccm-hand__card',
              cand ? 'is-candidate' : 'is-disabled',
              sel ? 'is-selected' : '',
            ]
              .filter(Boolean)
              .join(' ')
            return (
              <div
                key={c.instanceId}
                className={cls}
                onClick={cand && !busy ? () => toggle(c.instanceId) : undefined}
              >
                <Card
                  name={cardNames[c.cardDefinitionId] ?? c.cardDefinitionId}
                  cost={c.costOverride ?? cardCostOf(c.cardDefinitionId)}
                  type={cardTypeOf(c.cardDefinitionId)}
                  rarity={cardRarityOf(c.cardDefinitionId)}
                  upgraded={c.isUpgraded}
                />
              </div>
            )
          })}
        </div>
      </Popup>
    )
  }

  // List モード (draw / discard): カード名のみの縦リスト。
  return (
    <Popup
      open
      variant="modal"
      title={title}
      width={720}
      closeOnEsc={false}
      closeOnBackdrop={false}
      footerAlign="center"
      footer={
        <Button
          onClick={() => void handleConfirm()}
          disabled={selected.length !== choice.count || busy}
        >
          確定
        </Button>
      }
    >
      {errorMessage ? <div className="ccm-error">{errorMessage}</div> : null}
      <div className="ccm-list">
        {visibleSource.map((c) => {
          const cand = isCandidate(c.instanceId)
          const sel = isSelected(c.instanceId)
          const cls = [
            'ccm-list__row',
            cand ? 'is-candidate' : 'is-disabled',
            sel ? 'is-selected' : '',
          ]
            .filter(Boolean)
            .join(' ')
          return (
            <div
              key={c.instanceId}
              className={cls}
              onClick={cand && !busy ? () => toggle(c.instanceId) : undefined}
            >
              <span className="ccm-list__name">
                {cardNames[c.cardDefinitionId] ?? c.cardDefinitionId}
                {c.isUpgraded ? '+' : ''}
              </span>
            </div>
          )
        })}
      </div>
    </Popup>
  )
}

function actionTitle(action: string): string {
  switch (action) {
    case 'discard':
      return 'カードを捨てる'
    case 'exhaustCard':
      return 'カードを除外する'
    case 'upgrade':
      return 'カードを強化する'
    case 'recoverFromDiscard':
      return 'カードを手札に戻す'
    default:
      return 'カードを選ぶ'
  }
}
