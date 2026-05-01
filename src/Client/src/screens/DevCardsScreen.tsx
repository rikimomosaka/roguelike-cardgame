// Phase 10.5.I: Dev cards read-only viewer.
// Phase 10.5.J: Editor (label + textarea + Save / Set active / Promote / Delete) を追加。
// Phase 10.5.K: "+ New Card" モーダルから override 層にゼロから新規カードを作成可能。

import { useEffect, useState } from 'react'
import type { DevCardDto } from '../api/dev'
import {
  createNewCard,
  deleteCardVersion,
  fetchDevCards,
  promoteCardVersion,
  saveCardVersion,
  switchActiveVersion,
} from '../api/dev'
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
  const [reloadKey, setReloadKey] = useState(0)
  // Phase 10.5.K: new card modal state
  const [newCardOpen, setNewCardOpen] = useState(false)
  // 作成直後に list 再 fetch → 自動選択するための pending id
  const [pendingSelectId, setPendingSelectId] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    fetchDevCards()
      .then((list) => {
        if (cancelled) return
        setCards(list)
        if (list.length > 0) {
          // pendingSelectId があり list に含まれていればそれを優先 (新規作成直後)
          setSelectedId((prevId) => {
            let id: string
            if (pendingSelectId && list.some((c) => c.id === pendingSelectId)) {
              id = pendingSelectId
            } else {
              id = prevId && list.some((c) => c.id === prevId) ? prevId : list[0].id
            }
            const card = list.find((c) => c.id === id) ?? list[0]
            setSelectedVer(card.activeVersion ?? null)
            return id
          })
          if (pendingSelectId) setPendingSelectId(null)
        }
      })
      .catch((e) => {
        if (!cancelled) setError(String(e))
      })
    return () => {
      cancelled = true
    }
  }, [reloadKey])

  if (error) return <div className="dev-error">Error: {error}</div>
  if (!cards) return <div className="dev-loading">Loading...</div>

  const selected = cards.find((c) => c.id === selectedId) ?? null

  return (
    <div className="dev-cards">
      <aside className="dev-cards__list">
        <h2>Cards ({cards.length})</h2>
        <button
          type="button"
          className="dev-new-card-btn"
          onClick={() => setNewCardOpen(true)}
        >
          + New Card
        </button>
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
            key={`${selected.id}`}
            card={selected}
            versionId={selectedVer ?? selected.activeVersion}
            onSelectVersion={setSelectedVer}
            onAfterMutation={() => setReloadKey((k) => k + 1)}
          />
        ) : (
          <p>カードを選択してください。</p>
        )}
      </main>
      {newCardOpen && (
        <NewCardModal
          existingIds={cards.map((c) => c.id)}
          onClose={() => setNewCardOpen(false)}
          onCreated={(id) => {
            setNewCardOpen(false)
            setPendingSelectId(id)
            setReloadKey((k) => k + 1)
          }}
        />
      )}
    </div>
  )
}

// ---- Phase 10.5.K: New Card modal ----

type NewCardModalProps = {
  existingIds: string[]
  onClose: () => void
  onCreated: (id: string) => void
}

function NewCardModal({ existingIds, onClose, onCreated }: NewCardModalProps) {
  const [id, setId] = useState('')
  const [name, setName] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [templateId, setTemplateId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const submit = async () => {
    setError(null)
    if (!/^[a-z][a-z0-9_]*$/.test(id)) {
      setError('id must match ^[a-z][a-z0-9_]*$')
      return
    }
    if (!name) {
      setError('name is required')
      return
    }
    if (existingIds.includes(id)) {
      setError(`id '${id}' already exists`)
      return
    }
    setSubmitting(true)
    try {
      await createNewCard(id, name, displayName || null, templateId || null)
      onCreated(id)
    } catch (e) {
      setError(String(e))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div
      className="dev-modal-backdrop"
      onClick={onClose}
      role="presentation"
    >
      <div
        className="dev-modal"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label="New Card"
      >
        <h3>New Card</h3>
        <label>
          ID
          <input
            value={id}
            onChange={(e) => setId(e.target.value)}
            placeholder="lowercase_id"
            aria-label="new card id"
          />
        </label>
        <label>
          Name
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="表示名"
            aria-label="new card name"
          />
        </label>
        <label>
          Display Name
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="(optional)"
            aria-label="new card display name"
          />
        </label>
        <label>
          Template Card ID
          <input
            value={templateId}
            onChange={(e) => setTemplateId(e.target.value)}
            placeholder="(optional, e.g., strike)"
            aria-label="new card template id"
          />
        </label>
        {error && <div className="dev-error">{error}</div>}
        <div className="dev-modal__actions">
          <button type="button" onClick={onClose} disabled={false}>
            Cancel
          </button>
          <button type="button" onClick={submit} disabled={submitting}>
            Create
          </button>
        </div>
      </div>
    </div>
  )
}

type DetailProps = {
  card: DevCardDto
  versionId: string
  onSelectVersion: (v: string) => void
  onAfterMutation: () => void
}

function DevCardDetail({ card, versionId, onSelectVersion, onAfterMutation }: DetailProps) {
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

  // ---- editor state (Phase 10.5.J) ----
  const initialDraft = ver ? formatDraft(ver.spec) : ''
  const [draft, setDraft] = useState<string>(initialDraft)
  const [label, setLabel] = useState<string>('')
  const [editError, setEditError] = useState<string | null>(null)
  const [saving, setSaving] = useState<boolean>(false)
  const nextVerN = computeNextVerN(card)

  useEffect(() => {
    // version 切替で draft をその version の spec に同期
    if (ver) setDraft(formatDraft(ver.spec))
    setLabel('')
    setEditError(null)
  }, [card.id, versionId, ver?.spec])

  const saveAsNew = async () => {
    setEditError(null)
    setSaving(true)
    try {
      let parsed: unknown
      try {
        parsed = JSON.parse(draft)
      } catch (e) {
        setEditError(`Invalid JSON: ${String(e)}`)
        setSaving(false)
        return
      }
      await saveCardVersion(card.id, label || null, parsed)
      onAfterMutation()
    } catch (e) {
      setEditError(String(e))
    } finally {
      setSaving(false)
    }
  }

  const setActive = async () => {
    setEditError(null)
    setSaving(true)
    try {
      await switchActiveVersion(card.id, versionId)
      onAfterMutation()
    } catch (e) {
      setEditError(String(e))
    } finally {
      setSaving(false)
    }
  }

  const promote = async () => {
    if (!confirm(`Promote ${versionId} to source (base JSON)?`)) {
      return
    }
    setEditError(null)
    setSaving(true)
    try {
      await promoteCardVersion(card.id, versionId)
      onAfterMutation()
    } catch (e) {
      setEditError(String(e))
    } finally {
      setSaving(false)
    }
  }

  const remove = async () => {
    if (!confirm(`Delete version ${versionId}?`)) {
      return
    }
    setEditError(null)
    setSaving(true)
    try {
      await deleteCardVersion(card.id, versionId)
      onAfterMutation()
    } catch (e) {
      setEditError(String(e))
    } finally {
      setSaving(false)
    }
  }

  const isActive = versionId === card.activeVersion

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
          <code>{parseError ? ver?.spec : JSON.stringify(parsedSpec, null, 2)}</code>
        </pre>
      </section>
      <section className="dev-card-detail__editor">
        <h3>Editor (selected: {versionId})</h3>
        <input
          type="text"
          className="dev-card-detail__label-input"
          placeholder="Label (optional)"
          value={label}
          onChange={(e) => setLabel(e.target.value)}
          disabled={saving}
        />
        <textarea
          className="dev-card-detail__textarea"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          disabled={saving}
          aria-label="card spec editor"
        />
        <div className="dev-card-detail__actions">
          <button type="button" onClick={saveAsNew} disabled={saving}>
            Save as v{nextVerN}
          </button>
          <button type="button" onClick={setActive} disabled={saving || isActive}>
            Set as active
          </button>
          <button type="button" onClick={promote} disabled={saving}>
            Promote to source
          </button>
          <button type="button" onClick={remove} disabled={saving || isActive}>
            Delete version
          </button>
        </div>
        {editError ? <div className="dev-error">{editError}</div> : null}
      </section>
    </div>
  )
}

/** spec 文字列 (raw JSON) を整形して textarea 用 draft にする。parse 失敗時はそのまま返す。 */
function formatDraft(specJson: string): string {
  try {
    const parsed = JSON.parse(specJson)
    return JSON.stringify(parsed, null, 2)
  } catch {
    return specJson
  }
}

/** base + override の versions[] から v\d+ パターンの最大番号を見て次を返す。 */
function computeNextVerN(card: DevCardDto): number {
  let max = 0
  for (const v of card.versions) {
    const m = /^v(\d+)$/.exec(v.version)
    if (m) {
      const n = parseInt(m[1], 10)
      if (n > max) max = n
    }
  }
  return max + 1
}
