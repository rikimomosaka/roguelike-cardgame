import './ActorStats.css'

/**
 * キャラクター共通の能力値。操作キャラ・味方・敵のすべてが持つ。
 * 体力=HP、行動力=ターン開始時のエナジー回復量、知力=ターン開始時のドロー枚数。
 * 値が無い（古いサーバー等で未送信）場合は 0 と誤表示せず「—」にする。
 */
export type ActorStatValues = {
  hp: number | null | undefined
  energyPerTurn: number | null | undefined
  drawPerTurn: number | null | undefined
  attack: number | null | undefined
  defense: number | null | undefined
}

const STAT_ROWS: ReadonlyArray<{
  key: keyof ActorStatValues
  label: string
  hint: string
}> = [
  { key: 'hp', label: '体力', hint: 'HP' },
  { key: 'energyPerTurn', label: '行動力', hint: 'エナジー回復量' },
  { key: 'drawPerTurn', label: '知力', hint: 'ドロー枚数' },
  { key: 'attack', label: '攻撃力', hint: '攻撃力参照カードの基準値' },
  { key: 'defense', label: '守備力', hint: '守備力参照カードの基準値' },
]

function displayValue(value: number | null | undefined): string {
  return typeof value === 'number' ? String(value) : '—'
}

// eslint-disable-next-line react-refresh/only-export-components
export function formatActorStats(stats: ActorStatValues): string {
  return STAT_ROWS.map(row => `${row.label} ${displayValue(stats[row.key])}`).join(' / ')
}

/** 能力値を 1 行 1 項目で表示する（キャラ選択・アーカイブス共通）。 */
export function ActorStats({ stats, className }: {
  stats: ActorStatValues
  className?: string
}) {
  return (
    <dl
      className={`actor-stats${className ? ` ${className}` : ''}`}
      aria-label="能力値"
    >
      {STAT_ROWS.map(row => (
        <div key={row.key} className={`actor-stats__item actor-stats__item--${row.key}`} title={row.hint}>
          <dt>{row.label}</dt>
          <dd>{displayValue(stats[row.key])}</dd>
        </div>
      ))}
    </dl>
  )
}
