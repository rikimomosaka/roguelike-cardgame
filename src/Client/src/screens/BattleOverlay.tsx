import { useState } from 'react'
import type { BattleStateDto } from '../api/types'
import { Button } from '../components/Button'

type Props = {
  battle: BattleStateDto
  onWin: () => Promise<void> | void
}

export function BattleOverlay({ battle, onWin }: Props) {
  const [peeking, setPeeking] = useState(false)
  const [busy, setBusy] = useState(false)

  if (peeking) {
    return (
      <div className="battle-peek" onClick={() => setPeeking(false)}>
        <span>クリックで戦闘画面に戻る</span>
      </div>
    )
  }

  return (
    <div className="battle-overlay" role="dialog" aria-modal="true">
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
        <Button
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
        </Button>
        <Button onClick={() => setPeeking(true)}>マップを見る</Button>
      </div>
    </div>
  )
}
