// Phase 10.5.L1: Dev relics editor screen.
// Card editor (DevCardsScreen) を relic に mirror。
//   - 一覧 + 詳細の 2 ペイン
//   - versions タブ + RelicSpecForm + 各種 mutation ボタン
//   - 新規レリック / 削除レリック モーダル

import { useEffect, useMemo, useState } from 'react'
import type { DevMeta, DevRelicDto } from '../api/dev'
import {
  createNewRelic,
  deleteRelic as deleteRelicApi,
  deleteRelicVersion,
  fetchDevMeta,
  fetchDevRelics,
  promoteRelicVersion,
  saveRelicVersion,
  switchActiveRelicVersion,
} from '../api/dev'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import { useTooltipTarget } from '../components/Tooltip'
import type { TooltipContent } from '../components/Tooltip'
import { RelicSpecForm } from './dev/RelicSpecForm'
import type { RelicSpec } from './dev/DevSpecTypes'
import { parseRelicSpec, relicSpecToJsonObject } from './dev/DevSpecTypes'
import './DevCardsScreen.css'
import './dev/CardSpecForm.css'
import './dev/RelicSpecForm.css'

type Props = {
  /** ?dev (home) へ戻る用。省略可 (テスト用)。 */
  onBack?: () => void
}

export function DevRelicsScreen({ onBack }: Props = {}) {
  const [relics, setRelics] = useState<DevRelicDto[] | null>(null)
  const [meta, setMeta] = useState<DevMeta | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [selectedVer, setSelectedVer] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)
  const [newRelicOpen, setNewRelicOpen] = useState(false)
  const [pendingSelectId, setPendingSelectId] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    fetchDevRelics()
      .then((list) => {
        if (cancelled) return
        setRelics(list)
        if (list.length > 0) {
          setSelectedId((prevId) => {
            let id: string
            if (pendingSelectId && list.some((c) => c.id === pendingSelectId)) {
              id = pendingSelectId
            } else {
              id = prevId && list.some((c) => c.id === prevId) ? prevId : list[0].id
            }
            const r = list.find((c) => c.id === id) ?? list[0]
            setSelectedVer(r.activeVersion ?? null)
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

  if (error) return <div className="dev-error">エラー: {error}</div>
  if (!relics || !meta) return <div className="dev-loading">読込中...</div>

  const selected = relics.find((c) => c.id === selectedId) ?? null
  const allRelicIds = relics.map((c) => c.id)

  return (
    <div className="dev-cards">
      <aside className="dev-cards__list">
        <h2>レリック一覧 ({relics.length})</h2>
        <button
          type="button"
          className="dev-new-card-btn"
          onClick={() => setNewRelicOpen(true)}
        >
          + 新規レリック
        </button>
        <ul>
          {relics.map((c) => (
            <RelicListItem
              key={c.id}
              relic={c}
              isActive={c.id === selectedId}
              onClick={() => {
                setSelectedId(c.id)
                setSelectedVer(c.activeVersion)
              }}
            />
          ))}
        </ul>
      </aside>
      <main className="dev-cards__detail">
        {onBack ? (
          <div className="dev-cards__close">
            <button type="button" onClick={onBack}>
              ← 開発者メニューへ
            </button>
          </div>
        ) : null}
        {selected ? (
          <DevRelicDetail
            key={`${selected.id}`}
            relic={selected}
            versionId={selectedVer ?? selected.activeVersion}
            meta={meta}
            allRelicIds={allRelicIds}
            onSelectVersion={setSelectedVer}
            onAfterMutation={() => setReloadKey((k) => k + 1)}
            onAfterDelete={() => {
              setSelectedId(null)
              setSelectedVer(null)
              setReloadKey((k) => k + 1)
            }}
          />
        ) : (
          <p>レリックを選択してください。</p>
        )}
      </main>
      {newRelicOpen && (
        <NewRelicModal
          existingIds={relics.map((c) => c.id)}
          onClose={() => setNewRelicOpen(false)}
          onCreated={(id) => {
            setNewRelicOpen(false)
            setPendingSelectId(id)
            setReloadKey((k) => k + 1)
          }}
        />
      )}
    </div>
  )
}

// ---- Phase 10.6.B follow-up: list 行 hover で in-game tooltip を出す ----

type RelicListItemProps = {
  relic: DevRelicDto
  isActive: boolean
  onClick: () => void
}

function RelicListItem({ relic, isActive, onClick }: RelicListItemProps) {
  const { catalog } = useRelicCatalog()
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    const entry = catalog?.[relic.id]
    if (!entry) return null
    // relic は effectText / flavor を分離して tooltip 表示 (Phase 10.5.M6.3 の慣習に合わせる)
    return {
      name: entry.name,
      desc: entry.effectText,
      flavor: entry.flavor || undefined,
    }
  }, [catalog, relic.id])
  const tip = useTooltipTarget(tooltipContent)
  const displayName = relic.displayName ?? relic.name
  return (
    <li
      className={isActive ? 'is-active' : ''}
      onClick={onClick}
      onMouseEnter={tip.onMouseEnter}
      onMouseMove={tip.onMouseMove}
      onMouseLeave={tip.onMouseLeave}
    >
      <span className="dev-cards__list-name">{displayName}</span>
      <span className="dev-cards__list-id">({relic.id})</span>
      <span className="dev-cards__active-tag">({relic.activeVersion})</span>
    </li>
  )
}

// ---- New Relic modal ----

type NewRelicModalProps = {
  existingIds: string[]
  onClose: () => void
  onCreated: (id: string) => void
}

function NewRelicModal({ existingIds, onClose, onCreated }: NewRelicModalProps) {
  const [id, setId] = useState('')
  const [name, setName] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [templateId, setTemplateId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const submit = async () => {
    setError(null)
    if (!/^[a-z][a-z0-9_]*$/.test(id)) {
      setError('ID は半角英小文字 + 数字 + アンダースコアのみ (例: my_relic)')
      return
    }
    if (!name) {
      setError('名前は必須です')
      return
    }
    if (existingIds.includes(id)) {
      setError(`ID '${id}' は既に存在します`)
      return
    }
    setSubmitting(true)
    try {
      await createNewRelic(id, name, displayName || null, templateId || null)
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
        aria-label="New Relic"
      >
        <h3>新規レリック</h3>
        <label>
          ID
          <input
            value={id}
            onChange={(e) => setId(e.target.value)}
            placeholder="lowercase_id"
            aria-label="new relic id"
          />
        </label>
        <label>
          名前
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="表示名"
            aria-label="new relic name"
          />
        </label>
        <label>
          表示名 (任意)
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="(任意、name と異なる時のみ)"
            aria-label="new relic display name"
          />
        </label>
        <label>
          テンプレートレリック ID (任意)
          <input
            value={templateId}
            onChange={(e) => setTemplateId(e.target.value)}
            placeholder="(任意、例: act1_start_01 をクローン)"
            aria-label="new relic template id"
          />
        </label>
        {error && <div className="dev-error">{error}</div>}
        <div className="dev-modal__actions">
          <button type="button" onClick={onClose} disabled={false}>
            キャンセル
          </button>
          <button type="button" onClick={submit} disabled={submitting}>
            作成
          </button>
        </div>
      </div>
    </div>
  )
}

// ---- Delete Relic modal ----

type DeleteModalProps = {
  relicId: string
  onClose: () => void
  onConfirm: (alsoBase: boolean) => Promise<void>
}

function DeleteRelicModal({ relicId, onClose, onConfirm }: DeleteModalProps) {
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
        aria-label="Delete Relic"
      >
        <h3>レリックを削除</h3>
        <p>
          <code>{relicId}</code> を削除しますか?
        </p>
        <label>
          <input
            type="checkbox"
            checked={alsoBase}
            onChange={(e) => setAlsoBase(e.target.checked)}
            aria-label="also delete base file"
          />
          ソース (src/Core/Data/Relics/) からも削除する。バックアップは
          data-local/backups/ に保存されます。
        </label>
        {alsoBase && (
          <div className="dev-delete-modal__warning">
            ⚠ ソースファイル (src/Core/Data/Relics/{relicId}.json) が削除されます。
          </div>
        )}
        {err && <div className="dev-error">{err}</div>}
        <div className="dev-modal__actions">
          <button type="button" onClick={onClose} disabled={submitting}>
            キャンセル
          </button>
          <button type="button" onClick={handle} disabled={submitting}>
            {submitting ? '削除中...' : '削除を確定'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ---- Relic detail panel ----

type DetailProps = {
  relic: DevRelicDto
  versionId: string
  meta: DevMeta
  allRelicIds: string[]
  onSelectVersion: (v: string) => void
  onAfterMutation: () => void
  onAfterDelete: () => void
}

function DevRelicDetail({
  relic,
  versionId,
  meta,
  allRelicIds: _allRelicIds,
  onSelectVersion,
  onAfterMutation,
  onAfterDelete,
}: DetailProps) {
  const ver = relic.versions.find((v) => v.version === versionId)

  const [draft, setDraft] = useState<RelicSpec>(() =>
    ver ? parseRelicSpec(ver.spec) : parseRelicSpec('{}'),
  )
  const [label, setLabel] = useState<string>('')
  const [editError, setEditError] = useState<string | null>(null)
  const [saving, setSaving] = useState<boolean>(false)
  const [deleteOpen, setDeleteOpen] = useState<boolean>(false)
  const nextVerN = computeNextVerN(relic)

  useEffect(() => {
    if (ver) setDraft(parseRelicSpec(ver.spec))
    setLabel('')
    setEditError(null)
  }, [relic.id, versionId, ver?.spec])

  const saveAsNew = async () => {
    setEditError(null)
    setSaving(true)
    try {
      const obj = relicSpecToJsonObject(draft)
      await saveRelicVersion(relic.id, label || null, obj)
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
      await switchActiveRelicVersion(relic.id, versionId)
      onAfterMutation()
    } catch (e) {
      setEditError(String(e))
    } finally {
      setSaving(false)
    }
  }

  const promote = async () => {
    if (!confirm(`${versionId} をソース (base JSON) に昇格しますか?`)) {
      return
    }
    setEditError(null)
    setSaving(true)
    try {
      await promoteRelicVersion(relic.id, versionId)
      onAfterMutation()
    } catch (e) {
      setEditError(String(e))
    } finally {
      setSaving(false)
    }
  }

  const removeVersion = async () => {
    if (!confirm(`バージョン ${versionId} を削除しますか?`)) {
      return
    }
    setEditError(null)
    setSaving(true)
    try {
      await deleteRelicVersion(relic.id, versionId)
      onAfterMutation()
    } catch (e) {
      setEditError(String(e))
    } finally {
      setSaving(false)
    }
  }

  const handleDeleteRelic = async (alsoBase: boolean) => {
    await deleteRelicApi(relic.id, alsoBase)
    setDeleteOpen(false)
    onAfterDelete()
  }

  const isActive = versionId === relic.activeVersion

  return (
    <div className="dev-card-detail">
      <header>
        <h2>{relic.name}</h2>
        <code>{relic.id}</code>
        {relic.displayName ? <small>(表示名: {relic.displayName})</small> : null}
        <button
          type="button"
          className="dev-card-detail__delete"
          onClick={() => setDeleteOpen(true)}
          disabled={saving}
          aria-label="delete relic"
        >
          🗑 レリック削除
        </button>
      </header>
      <section className="dev-card-detail__versions">
        <h3>バージョン</h3>
        <div className="dev-card-detail__version-tabs">
          {relic.versions.map((v) => {
            const cls = ['dev-card-detail__ver-btn']
            if (v.version === versionId) cls.push('is-selected')
            if (v.version === relic.activeVersion) cls.push('is-active')
            return (
              <button
                key={v.version}
                type="button"
                className={cls.join(' ')}
                onClick={() => onSelectVersion(v.version)}
              >
                {v.version}
                {v.version === relic.activeVersion ? ' ✓' : ''}
                {v.label ? ` (${v.label})` : ''}
              </button>
            )
          })}
        </div>
      </section>
      <section className="dev-card-detail__form">
        <h3>スペック編集 (選択中: {versionId})</h3>
        <RelicSpecForm
          relicId={relic.id}
          relicName={relic.displayName ?? relic.name}
          spec={draft}
          meta={meta}
          allCardIds={[]}
          onChange={setDraft}
        />
      </section>
      <section className="dev-card-detail__editor-actions">
        <input
          type="text"
          className="dev-card-detail__label-input"
          placeholder="ラベル (任意)"
          value={label}
          onChange={(e) => setLabel(e.target.value)}
          disabled={saving}
          aria-label="version label"
        />
        <div className="dev-card-detail__actions">
          <button type="button" onClick={saveAsNew} disabled={saving}>
            v{nextVerN} として保存
          </button>
          <button type="button" onClick={setActive} disabled={saving || isActive}>
            このバージョンを有効化
          </button>
          <button type="button" onClick={promote} disabled={saving}>
            ソースに昇格
          </button>
          <button type="button" onClick={removeVersion} disabled={saving || isActive}>
            このバージョンを削除
          </button>
        </div>
        {editError ? <div className="dev-error">{editError}</div> : null}
      </section>
      {deleteOpen && (
        <DeleteRelicModal
          relicId={relic.id}
          onClose={() => setDeleteOpen(false)}
          onConfirm={handleDeleteRelic}
        />
      )}
    </div>
  )
}

function computeNextVerN(relic: DevRelicDto): number {
  let max = 0
  for (const v of relic.versions) {
    const m = /^v(\d+)$/.exec(v.version)
    if (m) {
      const n = parseInt(m[1], 10)
      if (n > max) max = n
    }
  }
  return max + 1
}
