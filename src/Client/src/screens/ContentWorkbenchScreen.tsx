import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import {
  deleteContentOverride,
  fetchContentDocument,
  fetchContentList,
  fetchContentMeta,
  saveContentDocument,
  saveContentAsStandardV1,
  previewContentDocument,
  validateContentDocument,
  type DevContentDocument,
  type DevContentKind,
  type DevContentMeta,
  type DevContentSummary,
} from '../api/devContent'
import { Card, type CardRarity, type CardType } from '../components/Card'
import type { CardPoolClass } from '../api/catalog'
import { CardDesc } from '../components/CardDesc'
import type { CardDescTerm } from '../components/CardDesc'
import { useTooltipTarget } from '../components/Tooltip'
import { StructuredJsonEditor, type ReferenceCatalog } from './dev/StructuredJsonEditor'
import './ContentWorkbenchScreen.css'

type Props = {
  initialKind?: string
  onClose: () => void
}

type EditorMode = 'form' | 'json'

export function ContentWorkbenchScreen({ initialKind, onClose }: Props) {
  const [meta, setMeta] = useState<DevContentMeta | null>(null)
  const [kind, setKind] = useState(initialKind ?? 'cards')
  const [items, setItems] = useState<DevContentSummary[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [loaded, setLoaded] = useState<DevContentDocument | null>(null)
  const [draft, setDraft] = useState<Record<string, unknown> | null>(null)
  const [raw, setRaw] = useState('')
  const [mode, setMode] = useState<EditorMode>('form')
  const [search, setSearch] = useState('')
  const [classFilter, setClassFilter] = useState('all')
  const [references, setReferences] = useState<ReferenceCatalog>({})
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [past, setPast] = useState<Record<string, unknown>[]>([])
  const [future, setFuture] = useState<Record<string, unknown>[]>([])

  const activeKind = meta?.kinds.find(item => item.id === kind) ?? null
  const virtualKind = kind === 'statusDefinitions' || kind === 'effectDefinitions'
  const readOnly = virtualKind || (meta?.readOnly ?? true)
  const dirty = useMemo(() => {
    if (!loaded || !draft) return false
    if (mode === 'json' && raw !== pretty(draft)) return true
    return JSON.stringify(loaded.document) !== JSON.stringify(draft)
  }, [draft, loaded, mode, raw])

  useEffect(() => {
    let cancelled = false
    fetchContentMeta()
      .then(async nextMeta => {
        if (cancelled) return
        setMeta(nextMeta)
        if (!nextMeta.kinds.some(item => item.id === kind)) setKind('cards')
        const backedKinds = nextMeta.kinds.filter(item => item.sourceDirectory)
        const entries = await Promise.all(backedKinds.map(async item => {
          try { return [item.id, await fetchContentList(item.id)] as const }
          catch { return [item.id, []] as const }
        }))
        if (!cancelled) setReferences(Object.fromEntries(entries))
      })
      .catch(reason => { if (!cancelled) setError(errorText(reason)) })
    return () => { cancelled = true }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const loadList = useCallback(async (nextKind: string, preferredId?: string) => {
    if (!meta) return
    setBusy(true)
    setError(null)
    setMessage(null)
    try {
      if (nextKind === 'statusDefinitions') {
        const virtualItems = meta.enums.statuses.map(status => summary(status.id, status.name))
        setItems(virtualItems)
        setSelectedId(preferredId ?? virtualItems[0]?.id ?? null)
      } else if (nextKind === 'effectDefinitions') {
        const virtualItems = meta.enums.actions.map(action => summary(action, action))
        setItems(virtualItems)
        setSelectedId(preferredId ?? virtualItems[0]?.id ?? null)
      } else {
        const list = await fetchContentList(nextKind)
        setItems(list)
        setReferences(previous => ({ ...previous, [nextKind]: list }))
        const nextId = preferredId && list.some(item => item.id === preferredId)
          ? preferredId
          : list[0]?.id ?? null
        setSelectedId(nextId)
      }
    } catch (reason) {
      setError(errorText(reason))
    } finally {
      setBusy(false)
    }
  }, [meta])

  useEffect(() => { void loadList(kind) }, [kind, loadList])

  useEffect(() => {
    if (!meta || !selectedId) {
      setLoaded(null)
      setDraft(null)
      setRaw('')
      return
    }
    setMessage(null)
    setError(null)
    if (kind === 'statusDefinitions') {
      const status = meta.enums.statuses.find(item => item.id === selectedId)
      if (status) setVirtualDocument(kind, selectedId, status)
      return
    }
    if (kind === 'effectDefinitions') {
      setVirtualDocument(kind, selectedId, {
        action: selectedId,
        note: 'このactionの入力欄・検証・日本語表示はCardEffectとCardTextFormatterが共通管理します。新規action追加はCore実装が必要です。',
      })
      return
    }
    let cancelled = false
    setBusy(true)
    fetchContentDocument(kind, selectedId)
      .then(document => {
        if (cancelled) return
        setLoaded(document)
        setDraft(document.document)
        setRaw(pretty(document.document))
        setPast([])
        setFuture([])
      })
      .catch(reason => { if (!cancelled) setError(errorText(reason)) })
      .finally(() => { if (!cancelled) setBusy(false) })
    return () => { cancelled = true }

    function setVirtualDocument(documentKind: string, id: string, document: Record<string, unknown>) {
      const virtual: DevContentDocument = {
        kind: documentKind, id, name: id, document, hasBase: true, hasOverride: false,
        revision: 'readonly', referencedBy: [],
      }
      setLoaded(virtual)
      setDraft(document)
      setRaw(pretty(document))
      setPast([])
      setFuture([])
    }
  }, [kind, meta, selectedId])

  useEffect(() => {
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      if (!dirty) return
      event.preventDefault()
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [dirty])

  const updateDraft = (next: Record<string, unknown>) => {
    if (draft) setPast(history => [...history.slice(-39), structuredClone(draft)])
    setFuture([])
    setDraft(next)
    setRaw(pretty(next))
    setMessage(null)
  }

  const selectKind = (nextKind: string) => {
    if (nextKind === kind) return
    if (dirty && !window.confirm('未保存の変更を破棄して種類を切り替えますか？')) return
    setKind(nextKind)
    setSelectedId(null)
    setSearch('')
    setClassFilter('all')
    setMessage(null)
    setError(null)
  }

  const selectDocument = (id: string) => {
    if (id === selectedId) return
    if (dirty && !window.confirm('未保存の変更を破棄して別のデータを開きますか？')) return
    setMessage(null)
    setError(null)
    setSelectedId(id)
  }

  const applyRaw = (): Record<string, unknown> | null => {
    try {
      const parsed: unknown = JSON.parse(raw)
      if (!isRecord(parsed)) throw new Error('JSONのルートはオブジェクトにしてください。')
      if (draft) setPast(history => [...history.slice(-39), structuredClone(draft)])
      setDraft(parsed)
      setFuture([])
      setError(null)
      return parsed
    } catch (reason) {
      setError(errorText(reason))
      return null
    }
  }

  const validate = async () => {
    if (!loaded || !draft || virtualKind) return
    const candidate = mode === 'json' ? applyRaw() : draft
    if (!candidate) return
    setBusy(true)
    setError(null)
    try {
      await validateContentDocument(kind, loaded.id, candidate)
      setMessage('全データとの整合性を含めて検証しました。')
    } catch (reason) { setError(errorText(reason)) }
    finally { setBusy(false) }
  }

  const save = async () => {
    if (!loaded || !draft || virtualKind) return
    const candidate = mode === 'json' ? applyRaw() : draft
    if (!candidate) return
    setBusy(true)
    setError(null)
    try {
      const saved = await saveContentDocument(kind, loaded.id, candidate, loaded.revision)
      setLoaded(saved)
      setDraft(saved.document)
      setRaw(pretty(saved.document))
      setPast([])
      setFuture([])
      await loadList(kind, saved.id)
      setMessage('編集用上書きへ保存し、実ゲームへ反映しました。')
    } catch (reason) { setError(errorText(reason)) }
    finally { setBusy(false) }
  }

  const saveAsStandardV1 = async () => {
    if (!loaded || !draft || virtualKind) return
    const candidate = mode === 'json' ? applyRaw() : draft
    if (!candidate) return
    if (!window.confirm(
      '現在の使用中バージョンだけを標準データの v1 として保存します。旧バージョンと編集用上書きは整理されます。続けますか？',
    )) return
    setBusy(true)
    setError(null)
    try {
      const saved = await saveContentAsStandardV1(kind, loaded.id, candidate, loaded.revision)
      setLoaded(saved)
      setDraft(saved.document)
      setRaw(pretty(saved.document))
      setPast([])
      setFuture([])
      await loadList(kind, saved.id)
      setMessage('使用中バージョンを標準データの v1 として整理し、実ゲームへ反映しました。')
    } catch (reason) { setError(errorText(reason)) }
    finally { setBusy(false) }
  }

  const clone = async () => {
    if (!activeKind?.canCreate || !draft) return
    const id = window.prompt('新しいID（英小文字・数字・_・-）')?.trim()
    if (!id) return
    const cloned = structuredClone(draft)
    if (kind === 'actStartRelicPools') {
      const match = /^act(\d+)$/.exec(id)
      if (!match) { setError('開始レリックプールのIDは act4 の形式にしてください。'); return }
      cloned.act = Number(match[1])
    } else {
      cloned.id = id
      if (typeof cloned.name === 'string') cloned.name = `${cloned.name} コピー`
      if (typeof cloned.displayName === 'string') cloned.displayName = `${cloned.displayName} コピー`
    }
    applyNewContentDefaults(kind, cloned)
    setBusy(true)
    setError(null)
    try {
      await saveContentDocument(kind, id, cloned)
      await loadList(kind, id)
      setMessage(`${id} を管理画面の新規データとして作成しました。`)
    } catch (reason) { setError(errorText(reason)) }
    finally { setBusy(false) }
  }

  const discardOverride = async () => {
    if (!loaded?.hasOverride || !activeKind?.canDelete) return
    if (!window.confirm(`${loaded.name} の編集内容を破棄し、標準データへ戻しますか？`)) return
    setBusy(true)
    setError(null)
    try {
      await deleteContentOverride(kind, loaded.id, loaded.revision)
      await loadList(kind, loaded.hasBase ? loaded.id : undefined)
      setMessage('編集内容を破棄し、標準データへ戻しました。')
    } catch (reason) { setError(errorText(reason)) }
    finally { setBusy(false) }
  }

  const filteredItems = items.filter(item => {
    const matchesSearch = `${item.name} ${item.id}`.toLowerCase().includes(search.toLowerCase())
    const matchesClass = classFilter === 'all'
      || (kind === 'cards'
        ? (classFilter === 'unpooled'
          ? item.poolClass == null
          : item.poolClass === classFilter)
        : kind === 'characters'
          ? item.characterClass === classFilter
          : true)
    return matchesSearch && matchesClass
  })
  const groupedKinds = groupKinds(meta?.kinds ?? [])
  const classFilterOptions = kind === 'cards'
    ? (meta?.enums.cardPoolClasses ?? []).map(value => ({
      value,
      label: meta?.enums.optionLabels[`cardPoolClasses:${value}`] ?? value,
    })).concat({ value: 'unpooled', label: 'プール外' })
    : kind === 'characters'
      ? (meta?.enums.characterClasses ?? []).map(value => ({
        value,
        label: meta?.enums.optionLabels[`characterClasses:${value}`] ?? value,
      }))
      : []

  return (
    <main className="content-workbench">
      <header className="content-workbench__header">
        <div>
          <p className="content-workbench__eyebrow">DEVELOPMENT / CONTENT WORKBENCH</p>
          <h1>ゲームデータ管理</h1>
        </div>
        <div className="content-workbench__header-actions">
          {dirty && <span className="content-workbench__dirty">未保存</span>}
          <button type="button" onClick={onClose}>ゲームへ戻る</button>
        </div>
      </header>

      <nav className="content-workbench__kinds" aria-label="データ種別">
        {groupedKinds.map(group => (
          <div className="content-kind-group" key={group.name}>
            <span>{group.name}</span>
            <div>
              {group.items.map(item => (
                <button type="button" className={item.id === kind ? 'is-active' : ''} onClick={() => selectKind(item.id)} key={item.id}>
                  {item.label}
                </button>
              ))}
            </div>
          </div>
        ))}
      </nav>

      <section className="content-workbench__body">
        <aside className="content-browser">
          <div className="content-browser__heading">
            <div>
              <strong>{activeKind?.label ?? kind}</strong>
              <small>{filteredItems.length === items.length ? `${items.length}件` : `${filteredItems.length} / ${items.length}件`}</small>
            </div>
            {activeKind?.canCreate && !readOnly && <button type="button" onClick={() => void clone()} disabled={!draft || busy}>複製して新規</button>}
          </div>
          <input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="名前 / IDを検索" />
          {classFilterOptions.length > 0 && (
            <label className="content-browser__class-filter">
              <span>{kind === 'cards' ? 'カード所属' : 'キャラクタークラス'}</span>
              <select
                aria-label="所属クラスで絞り込み"
                value={classFilter}
                onChange={event => setClassFilter(event.target.value)}
              >
                <option value="all">すべて</option>
                {classFilterOptions.map(option => (
                  <option value={option.value} key={option.value}>{option.label}</option>
                ))}
              </select>
            </label>
          )}
          <div className="content-browser__list">
            {filteredItems.map(item => (
              <button type="button" data-content-id={item.id} className={item.id === selectedId ? 'is-active' : ''} onClick={() => selectDocument(item.id)} key={item.id}>
                <span>{item.name}</span><code>{item.id}</code>
                {item.hasOverride && <i>変更あり</i>}
              </button>
            ))}
            {filteredItems.length === 0 && <p className="content-browser__empty">該当するデータはありません。</p>}
          </div>
        </aside>

        <section className="content-editor">
          <div className="content-editor__toolbar">
            <div className="content-editor__identity">
              <strong>{loaded?.name ?? 'データを選択'}</strong>
              {loaded && <code>{loaded.id}</code>}
            </div>
            <div className="content-editor__modes">
              <button type="button" className={mode === 'form' ? 'is-active' : ''} onClick={() => { if (mode === 'json') applyRaw(); setMode('form') }}>フォーム</button>
              <button type="button" className={mode === 'json' ? 'is-active' : ''} onClick={() => { if (draft) setRaw(pretty(draft)); setMode('json') }}>JSON</button>
            </div>
            <div className="content-editor__actions">
              <button type="button" disabled={past.length === 0 || readOnly} onClick={() => {
                if (!draft || past.length === 0) return
                const previous = past[past.length - 1]
                setPast(past.slice(0, -1)); setFuture([structuredClone(draft), ...future]); setDraft(previous); setRaw(pretty(previous))
              }}>元に戻す</button>
              <button type="button" disabled={future.length === 0 || readOnly} onClick={() => {
                if (!draft || future.length === 0) return
                const next = future[0]
                setFuture(future.slice(1)); setPast([...past, structuredClone(draft)]); setDraft(next); setRaw(pretty(next))
              }}>やり直す</button>
              {!readOnly && <button type="button" onClick={() => void validate()} disabled={!draft || busy}>検証</button>}
              {!readOnly && <button type="button" className="is-primary" onClick={() => void save()} disabled={!draft || busy || !dirty}>保存して反映</button>}
              {!readOnly && draft && canSaveAsStandardV1(kind, draft) && (
                <button type="button" className="is-standard" onClick={() => void saveAsStandardV1()} disabled={busy}>
                  決定稿を標準v1へ
                </button>
              )}
              {!readOnly && loaded?.hasOverride && activeKind?.canDelete && <button type="button" className="is-danger" onClick={() => void discardOverride()} disabled={busy}>変更を破棄</button>}
            </div>
          </div>

          {activeKind?.limitation && <div className="content-notice"><strong>現在の制限</strong>{activeKind.limitation}</div>}
          {meta?.readOnly && <div className="content-notice"><strong>読み取り専用</strong>E2E環境では実データの保存を無効にしています。</div>}
          {error && <div className="content-message is-error">{error}</div>}
          {message && <div className="content-message is-success">{message}</div>}
          <div className="content-editor__scroll">
            {busy && !draft ? <p className="content-loading">読み込み中…</p> : draft && meta ? (
              mode === 'form' ? (
                <fieldset className="content-editor__fieldset" disabled={readOnly}>
                  <StructuredJsonEditor value={draft} onChange={value => { if (isRecord(value)) updateDraft(value) }} meta={meta.enums} references={references} contentKind={kind} />
                </fieldset>
              ) : (
                <div className="content-raw-editor">
                  <textarea value={raw} onChange={event => setRaw(event.target.value)} readOnly={readOnly} spellCheck={false} aria-label="JSON編集" />
                  <button type="button" onClick={applyRaw} disabled={readOnly}>JSONをフォームへ適用</button>
                </div>
              )
            ) : <p className="content-loading">左からデータを選択してください。</p>}
          </div>
        </section>

        <aside className="content-inspector">
          <h2>ゲーム表示プレビュー</h2>
          {draft && meta ? <ContentPreview kind={kind} id={loaded?.id ?? ''} document={draft} meta={meta} references={references} /> : <p>データを選択してください。</p>}
          <div className="content-inspector__diagnostics">
            <h2>診断</h2>
            <dl>
              <dt>保存状態</dt><dd>{contentSourceLabel(loaded)}</dd>
              <dt>revision</dt><dd><code>{loaded?.revision ?? '—'}</code></dd>
            </dl>
            <details className="content-inspector__help">
              <summary>「変更あり」とは？</summary>
              <p>標準データ自体は壊さず、開発用の上書きファイルへ変更を保存している状態です。ゲームでは標準データと変更内容を合成して使用します。「変更を破棄」で標準状態へ戻せます。</p>
            </details>
            <h3>参照元</h3>
            {loaded?.referencedBy.length ? <ul>{loaded.referencedBy.map(reference => <li key={reference}>{reference}</li>)}</ul> : <p>参照元なし</p>}
          </div>
        </aside>
      </section>
    </main>
  )
}

function ContentPreview({ kind, id, document, meta, references }: {
  kind: string
  id: string
  document: Record<string, unknown>
  meta: DevContentMeta
  references: ReferenceCatalog
}) {
  const [preview, setPreview] = useState<{
    description: string
    upgradedDescription?: string | null
    flavor?: string
    classification?: string
    initialDeck?: string[]
    energyPerTurn?: number
    drawPerTurn?: number
    traitName?: string | null
    traitDescription?: string | null
    traitTrigger?: string | null
    traitEffects?: string | null
  }>({ description: '' })
  const activeSpec = activeVersionSpec(document)
  const documentKey = JSON.stringify(document)
  useEffect(() => {
    let cancelled = false
    const timer = window.setTimeout(() => {
      previewContentDocument(kind, document)
        .then(result => { if (!cancelled) setPreview(result) })
        .catch(() => {
          const effects = collectEffects(activeSpec ?? document)
          if (!cancelled) setPreview({ description: effects.length === 0
            ? '設定された共通効果はありません。'
            : '入力途中のため、まだ自動翻訳できません。' })
        })
    }, 250)
    return () => { cancelled = true; window.clearTimeout(timer) }
  }, [documentKey, kind]) // eslint-disable-line react-hooks/exhaustive-deps

  const source = activeSpec ?? document
  const cardNames = referenceNames(references.cards)
  const unitNames = referenceNames(references.units)
  const classification = authoringClassification(kind, document, meta, references)
    ?? preview.classification
  const initialDeck = preview.initialDeck
    ?? (kind === 'characters' && Array.isArray(document.deck)
      ? document.deck.filter((value): value is string => typeof value === 'string')
      : undefined)
  if (kind === 'cards') {
    const rarity = rarityCode(number(source.rarity))
    const cardType = cardTypeCode(String(source.cardType ?? 'Skill'))
    const cost = source.cost === null ? 'X' : number(source.cost)
    return (
      <div className="content-preview-card">
        {classification && (
          <p className="content-preview-generic__stat">{classification}</p>
        )}
        <div className="content-preview-card__variants">
          <Card
            name={String(document.displayName ?? document.name ?? id)} cost={cost} type={cardType} rarity={rarity}
            poolClass={cardPoolClassCode(document.poolClass)}
            description={preview.description} upgradedDescription={preview.upgradedDescription} upgraded={false} art={<span>ILLUST</span>}
          />
          {preview.upgradedDescription && (
            <Card
              name={String(document.displayName ?? document.name ?? id)}
              cost={source.upgradedCost === null || source.upgradedCost === undefined ? cost : number(source.upgradedCost)}
              type={cardType} rarity={rarity} poolClass={cardPoolClassCode(document.poolClass)} description={preview.description}
              upgradedDescription={preview.upgradedDescription} upgraded art={<span>ILLUST</span>}
            />
          )}
        </div>
        <div className="content-preview-rich-text">
          <strong>通常</strong>
          <CardDesc text={preview.description} cardNames={cardNames} unitNames={unitNames} compress={false} renderTerm={renderPreviewTerm} />
          {preview.upgradedDescription && (
            <>
              <strong>強化後</strong>
              <CardDesc text={preview.upgradedDescription} cardNames={cardNames} unitNames={unitNames} compress={false} renderTerm={renderPreviewTerm} />
            </>
          )}
        </div>
      </div>
    )
  }

  const icon = kind === 'relics' ? `/icons/relics/${id}.png` : kind === 'potions' ? `/icons/potions/${id}.png` : null
  return (
    <div className="content-preview-generic">
      {icon && <img src={icon} alt="" onError={event => { event.currentTarget.style.display = 'none' }} />}
      <h3>{String(document.displayName ?? document.name ?? id)}</h3>
      {classification && <p className="content-preview-generic__stat">{classification}</p>}
      {'hp' in document && <p className="content-preview-generic__stat">HP {String(document.hp)}</p>}
      {kind === 'characters' && (
        <p className="content-preview-generic__stat">
          ターン開始時: エナジー {preview.energyPerTurn ?? number(document.energyPerTurn ?? 3)}
          {' / '}ドロー {preview.drawPerTurn ?? number(document.drawPerTurn ?? 5)}枚
        </p>
      )}
      {kind === 'characters' && preview.traitName && (
        <div className="content-preview-generic__effect">
          <strong>特性: {preview.traitName}</strong>
          {preview.traitTrigger && <p>発動: {preview.traitTrigger}</p>}
          {preview.traitDescription && <p>{preview.traitDescription}</p>}
          {preview.traitEffects && (
            <CardDesc text={preview.traitEffects} cardNames={cardNames} unitNames={unitNames} compress={false} renderTerm={renderPreviewTerm} />
          )}
        </div>
      )}
      <div className="content-preview-generic__effect">
        <CardDesc text={preview.description} cardNames={cardNames} unitNames={unitNames} compress={false} renderTerm={renderPreviewTerm} />
      </div>
      {preview.flavor && <p className="content-preview-generic__flavor">{preview.flavor}</p>}
      {initialDeck && (
        <div className="content-preview-generic__deck">
          <strong>初期デッキ（{initialDeck.length}枚）</strong>
          <ul>{initialDeck.map((cardId, index) => (
            <li key={`${cardId}:${index}`}>{cardId}({cardNames[cardId] ?? '表示名未登録'})</li>
          ))}</ul>
        </div>
      )}
      {kind === 'statusDefinitions' && <p>種別: {String(document.kind)} / 永続: {String(document.isPermanent)}</p>}
      {kind === 'effectDefinitions' && <p>カード、ポーション、レリック、固有スキル、敵行動、召喚行動で共通利用します。</p>}
      {meta.kinds.find(item => item.id === kind)?.supportsGamePreview === false && <small>専用ビジュアルプレビューは未実装です。保存・検証・ゲーム反映には対応しています。</small>}
    </div>
  )
}

function PreviewTermTooltip({ term, children }: { term: CardDescTerm; children: ReactNode }) {
  const tip = useTooltipTarget({
    name: term.name,
    desc: term.desc,
    selfReference: { kind: term.kind, id: term.id },
  })
  return (
    <span
      className="content-preview-term"
      tabIndex={0}
      data-term={term.id}
      {...tip}
    >
      {children}
    </span>
  )
}

function renderPreviewTerm(term: CardDescTerm, rendered: ReactNode, key: number): ReactNode {
  return <PreviewTermTooltip term={term} key={`${term.kind}:${term.id}:${key}`}>{rendered}</PreviewTermTooltip>
}

function canSaveAsStandardV1(kind: string, document: Record<string, unknown>): boolean {
  return (kind === 'cards' || kind === 'relics') && activeVersionSpec(document) !== null
}

function activeVersionSpec(document: Record<string, unknown>): Record<string, unknown> | null {
  if (!Array.isArray(document.versions) || typeof document.activeVersion !== 'string') return null
  const entry = document.versions.find(version => isRecord(version) && version.version === document.activeVersion)
  return isRecord(entry) && isRecord(entry.spec) ? entry.spec : null
}

function collectEffects(value: unknown): unknown[] {
  if (Array.isArray(value)) return value.flatMap(collectEffects)
  if (!isRecord(value)) return []
  const direct = Array.isArray(value.effects)
    ? value.effects.filter(effect => isRecord(effect) && typeof effect.action === 'string')
    : []
  return [...direct, ...Object.entries(value).filter(([key]) => key !== 'effects').flatMap(([, child]) => collectEffects(child))]
}

function summary(id: string, name: string): DevContentSummary {
  return { id, name, hasBase: true, hasOverride: false, revision: 'readonly' }
}

function referenceNames(items: DevContentSummary[] | undefined): Record<string, string> {
  return Object.fromEntries((items ?? []).map(item => [item.id, item.name]))
}

function contentSourceLabel(document: DevContentDocument | null): string {
  if (!document) return '—'
  if (document.hasOverride && document.hasBase) return '標準データ＋編集用上書き'
  if (document.hasOverride) return '管理画面で追加したデータ'
  return document.hasBase ? '標準データ（未変更）' : '—'
}

function groupKinds(kinds: DevContentKind[]): Array<{ name: string; items: DevContentKind[] }> {
  const groups = new Map<string, DevContentKind[]>()
  for (const kind of kinds) groups.set(kind.group, [...(groups.get(kind.group) ?? []), kind])
  return [...groups].map(([name, items]) => ({ name, items }))
}

function pretty(value: unknown): string { return JSON.stringify(value, null, 2) }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null && !Array.isArray(value) }
function errorText(error: unknown): string { return error instanceof Error ? error.message : String(error) }
function number(value: unknown): number { return typeof value === 'number' ? value : Number(value ?? 0) }
function authoringClassification(
  kind: string,
  document: Record<string, unknown>,
  meta: DevContentMeta,
  references: ReferenceCatalog,
): string | null {
  const label = (category: string, value: unknown) => {
    const id = String(value)
    return meta.enums.optionLabels[`${category}:${id}`] ?? id
  }
  if (kind === 'characters') {
    return `クラス: ${label('characterClasses', document.characterClass ?? 'Striker')}`
  }
  if (kind !== 'cards') return null
  const poolKinds = Array.isArray(document.poolKinds) ? document.poolKinds : []
  const uses = poolKinds.length > 0
    ? poolKinds.map(value => label('cardPoolKinds', value)).join('・')
    : '通常プール外'
  const pool = typeof document.poolClass === 'string'
    ? label('cardPoolClasses', document.poolClass)
    : '未設定'
  const eligible = Array.isArray(document.eligibleCharacterIds)
    ? document.eligibleCharacterIds.filter((value): value is string => typeof value === 'string')
    : []
  const eligibleText = eligible.length > 0
    ? eligible.map(id => `${id}(${references.characters?.find(item => item.id === id)?.name ?? '表示名未登録'})`).join('、')
    : '同クラスの全キャラクター'
  return `用途: ${uses} / 所属: ${pool} / 取得可能: ${eligibleText}`
}
function rarityCode(value: number): CardRarity { return value === 2 ? 'r' : value === 3 ? 'e' : value === 4 ? 'l' : value === 5 ? 't' : 'c' }
function cardTypeCode(value: string): CardType {
  const normalized = value.toLowerCase()
  return normalized === 'attack' || normalized === 'power' || normalized === 'curse' || normalized === 'status' || normalized === 'unit'
    ? normalized : 'skill'
}

function cardPoolClassCode(value: unknown): CardPoolClass | null {
  return value === 'Universal' || value === 'Striker' || value === 'Guardian'
    || value === 'Tactician' || value === 'Enhancer'
    ? value
    : null
}

function applyNewContentDefaults(kind: string, document: Record<string, unknown>): void {
  if (kind === 'cards') {
    if (!Array.isArray(document.poolKinds)) document.poolKinds = ['Reward']
    if (typeof document.poolClass !== 'string') document.poolClass = 'Striker'
    if (!Array.isArray(document.eligibleCharacterIds)) document.eligibleCharacterIds = []
  }
  if (kind === 'characters' && typeof document.characterClass !== 'string') {
    document.characterClass = 'Striker'
  }
  if (kind === 'characters') {
    if (typeof document.energyPerTurn !== 'number') document.energyPerTurn = 3
    if (typeof document.drawPerTurn !== 'number') document.drawPerTurn = 5
  }
}
