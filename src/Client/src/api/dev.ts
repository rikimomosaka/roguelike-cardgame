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
