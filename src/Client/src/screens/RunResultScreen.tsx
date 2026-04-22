import type { RunResultDto } from '../api/types'
import { Button } from '../components/Button'

type Props = {
  result: RunResultDto
  onReturnToMenu: () => void
}

function formatSeconds(total: number): string {
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

export function RunResultScreen({ result, onReturnToMenu }: Props) {
  return (
    <div className="run-result-screen" role="dialog" aria-modal="true">
      <h1>{result.outcome}</h1>
      <dl>
        <dt>到達層</dt><dd>Act {result.actReached}</dd>
        <dt>訪問ノード数</dt><dd>{result.nodesVisited}</dd>
        <dt>プレイ時間</dt><dd>{formatSeconds(result.playSeconds)}</dd>
        <dt>HP</dt><dd>{result.finalHp} / {result.finalMaxHp}</dd>
        <dt>Gold</dt><dd>{result.finalGold}</dd>
      </dl>
      <section>
        <h2>レリック</h2>
        <ul>{result.finalRelics.map(r => <li key={r}>{r}</li>)}</ul>
      </section>
      <section>
        <h2>デッキ ({result.finalDeck.length})</h2>
        <ul>{result.finalDeck.map((c, i) => (
          <li key={i}>{c.id}{c.upgraded ? '+' : ''}</li>
        ))}</ul>
      </section>
      <Button onClick={onReturnToMenu}>メニューへ戻る</Button>
    </div>
  )
}
