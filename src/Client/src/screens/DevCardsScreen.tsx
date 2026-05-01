// Phase 10.5.I: Dev cards read-only viewer.
// 左カラムに card 一覧、右カラムに選択中 card の詳細 (versions タブ + spec JSON + auto-text プレビュー)。
// 編集 UI は本フェーズでは出さない (10.5.J で追加)。

import { useEffect, useState } from 'react'
import type { DevCardDto } from '../api/dev'
import { fetchDevCards } from '../api/dev'
import { CardDesc } from '../components/CardDesc'
import './DevCardsScreen.css'

type Props = {
  /** ?dev (home) へ戻る用。省略可 (テスト用)。 */
  onBack?: () => void
}

export function DevCardsScreen({ onBack }: Props = {}) {
  const [cards, setCards] = useState<DevCardDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [selectedVer, setSelectedVer] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    fetchDevCards()
      .then((list) => {
        if (cancelled) return
        setCards(list)
        if (list.length > 0) {
          setSelectedId(list[0].id)
          setSelectedVer(list[0].activeVersion)
        }
      })
      .catch((e) => {
        if (!cancelled) setError(String(e))
      })
    return () => {
      cancelled = true
    }
  }, [])

  if (error) return <div className="dev-error">Error: {error}</div>
  if (!cards) return <div className="dev-loading">Loading...</div>

  const selected = cards.find((c) => c.id === selectedId) ?? null

  return (
    <div className="dev-cards">
      <aside className="dev-cards__list">
        <h2>Cards ({cards.length})</h2>
        <ul>
          {cards.map((c) => (
            <li
              key={c.id}
              className={c.id === selectedId ? 'is-active' : ''}
              onClick={() => {
                setSelectedId(c.id)
                setSelectedVer(c.activeVersion)
              }}
            >
              {c.id}
              <span className="dev-cards__active-tag">({c.activeVersion})</span>
            </li>
          ))}
        </ul>
      </aside>
      <main className="dev-cards__detail">
        {onBack ? (
          <div className="dev-cards__close">
            <button type="button" onClick={onBack}>
              ← Dev ホームへ
            </button>
          </div>
        ) : null}
        {selected ? (
          <DevCardDetail
            card={selected}
            versionId={selectedVer ?? selected.activeVersion}
            onSelectVersion={setSelectedVer}
          />
        ) : (
          <p>カードを選択してください。</p>
        )}
      </main>
    </div>
  )
}

type DetailProps = {
  card: DevCardDto
  versionId: string
  onSelectVersion: (v: string) => void
}

function DevCardDetail({ card, versionId, onSelectVersion }: DetailProps) {
  const ver = card.versions.find((v) => v.version === versionId)

  // spec は server から raw JSON 文字列で来るため、表示用に parse して整形。
  // parse 失敗時は空オブジェクト相当として扱い、spec 表示はそのまま raw 文字列を出す。
  let parsedSpec: Record<string, unknown> = {}
  let parseError: string | null = null
  if (ver) {
    try {
      const v = JSON.parse(ver.spec) as unknown
      if (v && typeof v === 'object') parsedSpec = v as Record<string, unknown>
    } catch (e) {
      parseError = String(e)
    }
  }

  // description 手書き値があれば preview に出す。なければプレースホルダ表示。
  const description =
    typeof parsedSpec.description === 'string' ? (parsedSpec.description as string) : null

  return (
    <div className="dev-card-detail">
      <header>
        <h2>{card.name}</h2>
        <code>{card.id}</code>
        {card.displayName ? <small>(displayName: {card.displayName})</small> : null}
      </header>
      <section className="dev-card-detail__versions">
        <h3>Versions</h3>
        <div className="dev-card-detail__version-tabs">
          {card.versions.map((v) => {
            const cls = ['dev-card-detail__ver-btn']
            if (v.version === versionId) cls.push('is-selected')
            if (v.version === card.activeVersion) cls.push('is-active')
            return (
              <button
                key={v.version}
                type="button"
                className={cls.join(' ')}
                onClick={() => onSelectVersion(v.version)}
              >
                {v.version}
                {v.version === card.activeVersion ? ' ✓' : ''}
                {v.label ? ` (${v.label})` : ''}
              </button>
            )
          })}
        </div>
      </section>
      <section className="dev-card-detail__preview">
        <h3>Description Preview ({versionId})</h3>
        <div className="dev-card-detail__card-wrap">
          {description ? (
            <CardDesc text={description} />
          ) : (
            <em>
              (description override は未設定。auto-text は catalog 経由で生成されるため、ここでは spec
              JSON を参照してください。)
            </em>
          )}
        </div>
      </section>
      <section className="dev-card-detail__spec">
        <h3>Spec (JSON)</h3>
        {parseError ? <p style={{ color: '#ff6b6b' }}>parse error: {parseError}</p> : null}
        <pre>
          <code>
            {parseError ? ver?.spec : JSON.stringify(parsedSpec, null, 2)}
          </code>
        </pre>
      </section>
    </div>
  )
}
