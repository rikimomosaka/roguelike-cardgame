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
