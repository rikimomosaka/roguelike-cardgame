// src/Client/src/screens/battleScreen/CardChoiceModal.tsx
//
// Phase 10.5.M2-Choose T7: choose 中の手札選択 UI (pile=hand のみ)。
// pile=draw / discard は T8 で List モードを追加予定 (現状は null を返す)。
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
  /** T8 で利用予定 (pile=draw)。 */
  drawPile?: BattleCardInstanceDto[]
  /** T8 で利用予定 (pile=discard)。 */
  discardPile?: BattleCardInstanceDto[]
  cardNames: Record<string, string>
  /** カード定義から CardType を解決 (catalog 未ロード時の default を含めて呼出側で吸収)。 */
  cardTypeOf: (defId: string) => CardType
  cardRarityOf: (defId: string) => CardRarity
  onConfirm: (selectedIds: string[]) => Promise<void>
}

export function CardChoiceModal({
  pending,
  hand,
  cardNames,
  cardTypeOf,
  cardRarityOf,
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

  // T7: pile=hand のみ実装。pile=draw / discard は T8 で追加予定。
  if (choice.pile !== 'hand') return null

  const title = `${actionTitle(choice.action)} (${selected.length}/${choice.count})`

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
      <div className="ccm-hand">
        {hand.map((c) => {
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
                cost={c.costOverride ?? 0}
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
