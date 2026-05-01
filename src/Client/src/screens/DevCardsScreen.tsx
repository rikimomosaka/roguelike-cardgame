// Phase 10.5.I: Dev cards read-only viewer.
// Phase 10.5.J: Editor (label + textarea + Save / Set active / Promote / Delete) を追加。
// Phase 10.5.K: "+ New Card" モーダルから override 層にゼロから新規カードを作成可能。
// Phase 10.5.M: textarea 撤去 → CardSpecForm (構造化フォーム + ライブテキスト + ライブビジュアル
//               プレビュー)。Delete Card ボタンで override only / alsoBase 削除可能。

import { useEffect, useState } from 'react'
import type { DevCardDto, DevMeta } from '../api/dev'
import {
  createNewCard,
  deleteCard as deleteCardApi,
  deleteCardVersion,
  fetchDevCards,
  fetchDevMeta,
  promoteCardVersion,
  saveCardVersion,
  switchActiveVersion,
} from '../api/dev'
import { CardSpecForm } from './dev/CardSpecForm'
import type { CardSpec } from './dev/DevSpecTypes'
import { parseSpec, specToJsonObject } from './dev/DevSpecTypes'
import './DevCardsScreen.css'
import './dev/CardSpecForm.css'

type Props = {
  /** ?dev (home) へ戻る用。省略可 (テスト用)。 */
  onBack?: () => void
}

export function DevCardsScreen({ onBack }: Props = {}) {
  const [cards, setCards] = useState<DevCardDto[] | null>(null)
  const [meta, setMeta] = useState<DevMeta | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [selectedVer, setSelectedVer] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)
  const [newCardOpen, setNewCardOpen] = useState(false)
  const [pendingSelectId, setPendingSelectId] = useState<string | null>(null)

  // 1) cards を fetch
  useEffect(() => {
    let cancelled = false
    fetchDevCards()
      .then((list) => {
        if (cancelled) return
        setCards(list)
        if (list.length > 0) {
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
        } else {
          setSelectedId(null)
          setSelectedVer(null)
        }
      })
      .catch((e) => {
        if (!cancelled) setError(String(e))
      })
    return () => {
      cancelled = true
    }
  }, [reloadKey])

  // 2) meta を 1 回だけ fetch
  useEffect(() => {
    let cancelled = false
    fetchDevMeta()
      .then((m) => {
        if (!cancelled) setMeta(m)
      })
      .catch((e) => {
        if (!cancelled) setError(String(e))
      })
    return () => {
      cancelled = true
    }
  }, [])

  if (error) return <div className="dev-error">Error: {error}</div>
  if (!cards || !meta) return <div className="dev-loading">Loading...</div>

  const selected = cards.find((c) => c.id === selectedId) ?? null
  const allCardIds = cards.map((c) => c.id)
  const cardNames: Record<string, string> = {}
  for (const c of cards) {
    cardNames[c.id] = c.displayName ?? c.name
  }

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
            meta={meta}
            allCardIds={allCardIds}
            cardNames={cardNames}
            onSelectVersion={setSelectedVer}
            onAfterMutation={() => setReloadKey((k) => k + 1)}
            onAfterDelete={() => {
              setSelectedId(null)
              setSelectedVer(null)
              setReloadKey((k) => k + 1)
            }}
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
    <div className="dev-modal-backdrop" onClick={onClose} role="presentation">
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

// ---- Phase 10.5.M: Delete Card modal ----

type DeleteModalProps = {
  cardId: string
  onClose: () => void
  onConfirm: (alsoBase: boolean) => Promise<void>
}

function DeleteCardModal({ cardId, onClose, onConfirm }: DeleteModalProps) {
  const [alsoBase, setAlsoBase] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  const handle = async () => {
    setSubmitting(true)
    setErr(null)
    try {
      await onConfirm(alsoBase)
    } catch (e) {
      setErr(String(e))
      setSubmitting(false)
    }
  }

  return (
    <div className="dev-modal-backdrop" onClick={onClose} role="presentation">
      <div
        className="dev-modal dev-delete-modal"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label="Delete Card"
      >
        <h3>Delete Card</h3>
        <p>
          Delete <code>{cardId}</code>?
        </p>
        <label>
          <input
            type="checkbox"
            checked={alsoBase}
            onChange={(e) => setAlsoBase(e.target.checked)}
            aria-label="also delete base file"
          />
          Also delete base file (committed source). A backup is saved under
          data-local/backups/.
        </label>
        {alsoBase && (
          <div className="dev-delete-modal__warning">
            ⚠ base file (src/Core/Data/Cards/{cardId}.json) will be removed.
          </div>
        )}
        {err && <div className="dev-error">{err}</div>}
        <div className="dev-modal__actions">
          <button type="button" onClick={onClose} disabled={submitting}>
            Cancel
          </button>
          <button type="button" onClick={handle} disabled={submitting}>
            {submitting ? 'Deleting...' : 'Confirm Delete'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ---- Card detail panel ----

type DetailProps = {
  card: DevCardDto
  versionId: string
  meta: DevMeta
  allCardIds: string[]
  cardNames: Record<string, string>
  onSelectVersion: (v: string) => void
  onAfterMutation: () => void
  onAfterDelete: () => void
}

function DevCardDetail({
  card,
  versionId,
  meta,
  allCardIds,
  cardNames,
  onSelectVersion,
  onAfterMutation,
  onAfterDelete,
}: DetailProps) {
  const ver = card.versions.find((v) => v.version === versionId)

  // ---- editor state (Phase 10.5.J → M) ----
  const [draft, setDraft] = useState<CardSpec>(() =>
    ver ? parseSpec(ver.spec) : parseSpec('{}'),
  )
  const [label, setLabel] = useState<string>('')
  const [editError, setEditError] = useState<string | null>(null)
  const [saving, setSaving] = useState<boolean>(false)
  const [deleteOpen, setDeleteOpen] = useState<boolean>(false)
  const nextVerN = computeNextVerN(card)

  useEffect(() => {
    if (ver) setDraft(parseSpec(ver.spec))
    setLabel('')
    setEditError(null)
    // Why: card.id / version 切替で draft をその version の spec に同期する。
    //   ver?.spec が依存に入っているのは外部 mutation 後 reload した場合の同期用。
  }, [card.id, versionId, ver?.spec])

  const saveAsNew = async () => {
    setEditError(null)
    setSaving(true)
    try {
      const obj = specToJsonObject(draft)
      await saveCardVersion(card.id, label || null, obj)
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

  const removeVersion = async () => {
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

  const handleDeleteCard = async (alsoBase: boolean) => {
    await deleteCardApi(card.id, alsoBase)
    setDeleteOpen(false)
    onAfterDelete()
  }

  const isActive = versionId === card.activeVersion

  return (
    <div className="dev-card-detail">
      <header>
        <h2>{card.name}</h2>
        <code>{card.id}</code>
        {card.displayName ? <small>(displayName: {card.displayName})</small> : null}
        <button
          type="button"
          className="dev-card-detail__delete"
          onClick={() => setDeleteOpen(true)}
          disabled={saving}
          aria-label="delete card"
        >
          🗑 Delete Card
        </button>
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
      <section className="dev-card-detail__form">
        <h3>Spec Editor (selected: {versionId})</h3>
        <CardSpecForm
          spec={draft}
          meta={meta}
          allCardIds={allCardIds}
          cardNames={cardNames}
          cardName={card.name}
          displayName={card.displayName}
          onChange={setDraft}
        />
      </section>
      <section className="dev-card-detail__editor-actions">
        <input
          type="text"
          className="dev-card-detail__label-input"
          placeholder="Label (optional)"
          value={label}
          onChange={(e) => setLabel(e.target.value)}
          disabled={saving}
          aria-label="version label"
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
          <button type="button" onClick={removeVersion} disabled={saving || isActive}>
            Delete version
          </button>
        </div>
        {editError ? <div className="dev-error">{editError}</div> : null}
      </section>
      {deleteOpen && (
        <DeleteCardModal
          cardId={card.id}
          onClose={() => setDeleteOpen(false)}
          onConfirm={handleDeleteCard}
        />
      )}
    </div>
  )
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
