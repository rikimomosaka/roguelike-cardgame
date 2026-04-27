import { useState } from 'react'
import type { BattlePlaceholderStateDto } from '../api/types'
import { Button } from '../components/Button'

type Props = {
  battle: BattlePlaceholderStateDto
  onWin: () => Promise<void> | void
  onDebugDamage?: () => void
}

export function BattleOverlay({ battle, onWin, onDebugDamage }: Props) {
  const [busy, setBusy] = useState(false)

  return (
    <div className="battle-overlay" role="dialog" aria-modal="true">
      <h2 className="battle-overlay__title">❖ BATTLE ❖</h2>
      <div className="battle-overlay__enemies">
        {battle.enemies.map((e, i) => (
          <div className="battle-enemy" key={i}>
            <div className="battle-enemy__image" data-image-id={e.imageId}>
              {e.imageId}
            </div>
            <div className="battle-enemy__name">{e.name}</div>
            <div className="battle-enemy__hp">
              HP {e.currentHp}/{e.maxHp}
            </div>
          </div>
        ))}
      </div>
      <div className="battle-overlay__actions">
        <button
          type="button"
          className="battle-overlay__victory"
          disabled={busy}
          onClick={async () => {
            setBusy(true)
            try {
              await onWin()
            } finally {
              setBusy(false)
            }
          }}
        >
          勝利
        </button>
        {import.meta.env.DEV && onDebugDamage && (
          <Button onClick={onDebugDamage} aria-label="DEBUG -10HP">DEBUG -10HP</Button>
        )}
      </div>
    </div>
  )
}
