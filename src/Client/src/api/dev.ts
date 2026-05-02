// Dev menu 用 API helper (Phase 10.5.I)。
// /api/dev/cards は Server 側で IsDevelopment() ガードされており、本番では 404。
// 通常 /api 経由 (proxy or 同 origin)。catalog の apiRequest は /api/v1/catalog... なので
// dev endpoint 用に直接 fetch を使う。

export type DevCardVersionDto = {
  version: string
  createdAt: string | null
  label: string | null
  /** spec は server から raw JSON 文字列で来る (UI 側で JSON.parse して表示)。 */
  spec: string
}

export type DevCardDto = {
  id: string
  name: string
  displayName: string | null
  activeVersion: string
  versions: DevCardVersionDto[]
}

export async function fetchDevCards(): Promise<DevCardDto[]> {
  const resp = await fetch('/api/dev/cards')
  if (!resp.ok) {
    throw new Error(`fetchDevCards failed: ${resp.status}`)
  }
  return (await resp.json()) as DevCardDto[]
}

// ---- Phase 10.5.J: card editor mutation API ----

/**
 * 新 version を override に追加。version id はサーバ側が base + override から自動採番 (`v{N+1}`)。
 * 初 save (override 未存在) なら activeVersion も新 version に設定される。
 */
export async function saveCardVersion(
  id: string,
  label: string | null,
  spec: unknown,
): Promise<{ newVersion: string }> {
  const resp = await fetch(`/api/dev/cards/${encodeURIComponent(id)}/versions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ label, spec }),
  })
  if (!resp.ok) {
    const txt = await resp.text().catch(() => '')
    throw new Error(`saveCardVersion failed: ${resp.status} ${txt}`)
  }
  return (await resp.json()) as { newVersion: string }
}

/** override の activeVersion を上書き保存。指定 version は base + override に存在する必要あり。 */
export async function switchActiveVersion(id: string, version: string): Promise<void> {
  const resp = await fetch(`/api/dev/cards/${encodeURIComponent(id)}/active`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ version }),
  })
  if (!resp.ok) {
    const txt = await resp.text().catch(() => '')
    throw new Error(`switchActiveVersion failed: ${resp.status} ${txt}`)
  }
}

/** override から指定 version を削除。active な version は削除不可。 */
export async function deleteCardVersion(id: string, version: string): Promise<void> {
  const resp = await fetch(
    `/api/dev/cards/${encodeURIComponent(id)}/versions/${encodeURIComponent(version)}`,
    { method: 'DELETE' },
  )
  if (!resp.ok) {
    const txt = await resp.text().catch(() => '')
    throw new Error(`deleteCardVersion failed: ${resp.status} ${txt}`)
  }
}

/**
 * override の version を base JSON に転記、override から削除。base は backup を取ってから上書き。
 * makeActiveOnBase=true なら base.activeVersion も更新。
 */
export async function promoteCardVersion(
  id: string,
  version: string,
  makeActiveOnBase = false,
): Promise<void> {
  const resp = await fetch(`/api/dev/cards/${encodeURIComponent(id)}/promote`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ version, makeActiveOnBase }),
  })
  if (!resp.ok) {
    const txt = await resp.text().catch(() => '')
    throw new Error(`promoteCardVersion failed: ${resp.status} ${txt}`)
  }
}

// ---- Phase 10.5.M: form editor / preview / delete / meta API ----

/**
 * /api/dev/meta が返す enum 値リスト。 Form の dropdown 選択肢供給用。
 *
 * Phase 10.5.L1.5: relicTriggers は廃止、triggers に relic + power 統合した
 * 18 値を含むように変更。
 */
export type DevMeta = {
  cardTypes: string[]
  rarities: { value: number; label: string }[]
  effectActions: string[]
  effectScopes: string[]
  effectSides: string[]
  piles: string[]
  selectModes: string[]
  /** unified trigger list (relic + power statuses 統合)。Phase 10.5.L1.5 以降 18 値。 */
  triggers: string[]
  amountSources: string[]
  keywords: { id: string; name: string; description: string }[]
  statuses: { id: string; jp: string }[]
}

/** GET /api/dev/meta — Form 用 enum リスト取得。 */
export async function fetchDevMeta(): Promise<DevMeta> {
  const r = await fetch('/api/dev/meta')
  if (!r.ok) throw new Error(`fetchDevMeta failed: ${r.status}`)
  return (await r.json()) as DevMeta
}

/**
 * POST /api/dev/cards/preview — spec を CardTextFormatter で auto-text 化。
 * marker 入りの description を返し、Client 側 CardDesc で render される。
 */
export async function previewDescription(
  spec: unknown,
  upgraded: boolean,
): Promise<string> {
  const r = await fetch('/api/dev/cards/preview', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ spec, upgraded }),
  })
  if (!r.ok) {
    const txt = await r.text().catch(() => '')
    throw new Error(`previewDescription failed: ${r.status} ${txt}`)
  }
  const j = (await r.json()) as { description: string }
  return j.description
}

/**
 * DELETE /api/dev/cards/{id} — override file を削除。
 * alsoBase=true なら base file も backup を取って削除 (撤回不可)。
 */
export async function deleteCard(id: string, alsoBase: boolean): Promise<void> {
  const r = await fetch(
    `/api/dev/cards/${encodeURIComponent(id)}?alsoBase=${alsoBase}`,
    { method: 'DELETE' },
  )
  if (!r.ok) {
    const txt = await r.text().catch(() => '')
    throw new Error(`deleteCard failed: ${r.status} ${txt}`)
  }
}

// ---- Phase 10.5.K: new card creation API ----

/**
 * 新規カードを override 層に作成。
 * id は `^[a-z][a-z0-9_]*$` を満たすこと、base + override で uniqueness が必要。
 * templateCardId 指定時は当該カードの merged active spec を v1 にコピー、未指定時は default spec
 * (Skill / cost 1 / effects=[])。
 */
export async function createNewCard(
  id: string,
  name: string,
  displayName: string | null,
  templateCardId: string | null,
): Promise<{ id: string }> {
  const resp = await fetch('/api/dev/cards', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ id, name, displayName, templateCardId }),
  })
  if (!resp.ok) {
    const txt = await resp.text().catch(() => '')
    throw new Error(`createNewCard failed: ${resp.status} ${txt}`)
  }
  return (await resp.json()) as { id: string }
}

// ============================================================
// Phase 10.5.L1: Relic dev API (mirror of card)
// ============================================================

export type DevRelicVersionDto = {
  version: string
  createdAt: string | null
  label: string | null
  /** spec は server から raw JSON 文字列で来る (UI 側で JSON.parse して表示)。 */
  spec: string
}

export type DevRelicDto = {
  id: string
  name: string
  displayName: string | null
  activeVersion: string
  versions: DevRelicVersionDto[]
}

export async function fetchDevRelics(): Promise<DevRelicDto[]> {
  const resp = await fetch('/api/dev/relics')
  if (!resp.ok) {
    throw new Error(`fetchDevRelics failed: ${resp.status}`)
  }
  return (await resp.json()) as DevRelicDto[]
}

export async function saveRelicVersion(
  id: string,
  label: string | null,
  spec: unknown,
): Promise<{ newVersion: string }> {
  const resp = await fetch(`/api/dev/relics/${encodeURIComponent(id)}/versions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ label, spec }),
  })
  if (!resp.ok) {
    const txt = await resp.text().catch(() => '')
    throw new Error(`saveRelicVersion failed: ${resp.status} ${txt}`)
  }
  return (await resp.json()) as { newVersion: string }
}

export async function switchActiveRelicVersion(id: string, version: string): Promise<void> {
  const resp = await fetch(`/api/dev/relics/${encodeURIComponent(id)}/active`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ version }),
  })
  if (!resp.ok) {
    const txt = await resp.text().catch(() => '')
    throw new Error(`switchActiveRelicVersion failed: ${resp.status} ${txt}`)
  }
}

export async function deleteRelicVersion(id: string, version: string): Promise<void> {
  const resp = await fetch(
    `/api/dev/relics/${encodeURIComponent(id)}/versions/${encodeURIComponent(version)}`,
    { method: 'DELETE' },
  )
  if (!resp.ok) {
    const txt = await resp.text().catch(() => '')
    throw new Error(`deleteRelicVersion failed: ${resp.status} ${txt}`)
  }
}

export async function promoteRelicVersion(
  id: string,
  version: string,
  makeActiveOnBase = false,
): Promise<void> {
  const resp = await fetch(`/api/dev/relics/${encodeURIComponent(id)}/promote`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ version, makeActiveOnBase }),
  })
  if (!resp.ok) {
    const txt = await resp.text().catch(() => '')
    throw new Error(`promoteRelicVersion failed: ${resp.status} ${txt}`)
  }
}

export async function createNewRelic(
  id: string,
  name: string,
  displayName: string | null,
  templateRelicId: string | null,
): Promise<{ id: string }> {
  const resp = await fetch('/api/dev/relics', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ id, name, displayName, templateRelicId }),
  })
  if (!resp.ok) {
    const txt = await resp.text().catch(() => '')
    throw new Error(`createNewRelic failed: ${resp.status} ${txt}`)
  }
  return (await resp.json()) as { id: string }
}

export type RelicPreviewResult = {
  /** "{effectText}\n{flavor}" で結合した後方互換用テキスト */
  description: string
  /** 手動入力の flavor text (フレーバー部分のみ、空の可能性あり) */
  flavor: string
  /** effects から CardTextFormatter で自動生成した機械的説明 (空の可能性あり) */
  effectText: string
}

/**
 * POST /api/dev/relics/preview — relic spec から description を返す。
 * Phase 10.5.L1.5+: flavor / effectText を分離して返すため、
 *  Client 側は層別レイアウト (効果上、点線、フレーバー下) で描画できる。
 */
export async function previewRelicDescription(spec: unknown): Promise<RelicPreviewResult> {
  const r = await fetch('/api/dev/relics/preview', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ spec }),
  })
  if (!r.ok) {
    const txt = await r.text().catch(() => '')
    throw new Error(`previewRelicDescription failed: ${r.status} ${txt}`)
  }
  const j = (await r.json()) as Partial<RelicPreviewResult>
  return {
    description: j.description ?? '',
    flavor: j.flavor ?? '',
    effectText: j.effectText ?? '',
  }
}

export async function deleteRelic(id: string, alsoBase: boolean): Promise<void> {
  const r = await fetch(
    `/api/dev/relics/${encodeURIComponent(id)}?alsoBase=${alsoBase}`,
    { method: 'DELETE' },
  )
  if (!r.ok) {
    const txt = await r.text().catch(() => '')
    throw new Error(`deleteRelic failed: ${r.status} ${txt}`)
  }
}
