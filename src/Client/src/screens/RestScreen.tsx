import { useState } from 'react'
import type { CardInstanceDto } from '../api/types'
import { Button } from '../components/Button'
import { useCardCatalog } from '../hooks/useCardCatalog'

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
      <div className="rest-screen" role="dialog" aria-modal="true">
        <h2>強化するカードを選ぶ</h2>
        <ul>
          {candidates.map(e => {
            const name = names[e.card.id] ?? e.card.id
            return (
              <li key={e.index}>
                <Button
                  onClick={() => onUpgrade(e.index)}
                  disabled={completed}
                  aria-label={`Upgrade ${name} at ${e.index}`}
                >
                  {name} を強化 (#{e.index})
                </Button>
              </li>
            )
          })}
        </ul>
        <Button onClick={() => setMode('choose')} aria-label="Back">戻る</Button>
      </div>
    )
  }

  return (
    <div className="rest-screen" role="dialog" aria-modal="true">
      <h2>休息所{completed ? '(使用済み)' : ''}</h2>
      <Button onClick={() => onHeal()} aria-label="Heal" disabled={completed}>
        回復 (+30% max HP)
      </Button>
      <Button onClick={() => setMode('upgrade')} aria-label="Upgrade card" disabled={completed}>
        カードを強化
      </Button>
      {completed && (
        <Button onClick={() => onClose()} aria-label="Close">
          閉じる
        </Button>
      )}
    </div>
  )
}
