import type { CardPoolClass, CharacterClass } from './catalog'

export type DevContentKind = {
  id: string
  label: string
  group: string
  sourceDirectory: string
  overrideDirectory: string
  canCreate: boolean
  canDelete: boolean
  supportsGamePreview: boolean
  limitation: string | null
}

export type DevContentEnums = {
  cardTypes: string[]
  characterClasses: string[]
  cardPoolClasses: string[]
  cardPoolKinds: string[]
  cardAbilities: string[]
  contingencyTriggerTimings: string[]
  characterTraitTriggers: string[]
  rarities: number[]
  scopes: string[]
  sides: string[]
  selects: string[]
  piles: string[]
  tiers: string[]
  triggers: string[]
  actions: string[]
  amountSources: string[]
  eventEffectTypes: string[]
  eventConditionTypes: string[]
  eventRarities: string[]
  tileKinds: string[]
  optionLabels: Record<string, string>
  statuses: Array<{
    id: string
    name: string
    description: string
    kind: string
    isPermanent: boolean
    tickDirection: string
  }>
  keywords: Array<{ id: string; name: string; description: string }>
}

export type DevContentMeta = {
  readOnly: boolean
  kinds: DevContentKind[]
  enums: DevContentEnums
}

export type DevContentSummary = {
  id: string
  name: string
  hasBase: boolean
  hasOverride: boolean
  revision: string
  poolClass?: CardPoolClass | null
  characterClass?: CharacterClass | null
}

export type DevContentDocument = {
  kind: string
  id: string
  name: string
  document: Record<string, unknown>
  hasBase: boolean
  hasOverride: boolean
  revision: string
  referencedBy: string[]
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(`/api/dev/content${path}`, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) {
    const text = await response.text()
    try {
      const parsed = JSON.parse(text) as { error?: unknown; title?: unknown }
      const message = typeof parsed.error === 'string'
        ? parsed.error
        : typeof parsed.title === 'string' ? parsed.title : text
      throw new Error(message || `HTTP ${response.status}`)
    } catch (error) {
      if (error instanceof SyntaxError) throw new Error(text || `HTTP ${response.status}`)
      throw error
    }
  }
  if (response.status === 204) return undefined as T
  return await response.json() as T
}

const segment = (value: string) => encodeURIComponent(value)

export const fetchContentMeta = () => request<DevContentMeta>('GET', '/kinds')
export const fetchContentList = (kind: string) =>
  request<DevContentSummary[]>('GET', `/${segment(kind)}`)
export const fetchContentDocument = (kind: string, id: string) =>
  request<DevContentDocument>('GET', `/${segment(kind)}/${segment(id)}`)
export const validateContentDocument = (kind: string, id: string, document: Record<string, unknown>) =>
  request<{ valid: boolean }>('POST', `/${segment(kind)}/${segment(id)}/validate`, { document })
export const saveContentDocument = (
  kind: string,
  id: string,
  document: Record<string, unknown>,
  revision?: string,
) => request<DevContentDocument>('PUT', `/${segment(kind)}/${segment(id)}`, { document, revision })
export const saveContentAsStandardV1 = (
  kind: string,
  id: string,
  document: Record<string, unknown>,
  revision?: string,
) => request<DevContentDocument>(
  'POST', `/${segment(kind)}/${segment(id)}/standard-v1`, { document, revision },
)
export const deleteContentOverride = (kind: string, id: string, revision: string) =>
  request<void>('DELETE', `/${segment(kind)}/${segment(id)}?revision=${segment(revision)}`)
export const translateEffects = (effects: unknown[]) =>
  request<{ description: string }>('POST', '/translate', { effects })
export const previewContentDocument = (kind: string, document: Record<string, unknown>) =>
  request<{
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
  }>(
    'POST', '/preview', { kind, document },
  )
