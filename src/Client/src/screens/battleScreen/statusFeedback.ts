import { STATUS_DEFS } from '../../components/CardDesc'

const STATUS_IDS_BY_LENGTH = Object.keys(STATUS_DEFS)
  .sort((left, right) => right.length - left.length)

const STATUS_ID_BY_LOWER_CASE = new Map(
  STATUS_IDS_BY_LENGTH.map(statusId => [statusId.toLowerCase(), statusId]),
)

/**
 * Old journal entries occasionally decorated a status note with a trigger
 * phase (for example `strength:start`). Presentation must never expose that
 * internal English note. Resolve the canonical status id when possible and
 * let callers use a neutral Japanese fallback otherwise.
 */
export function resolveFeedbackStatusId(rawStatusId: string | null | undefined): string | null {
  const raw = rawStatusId?.trim()
  if (!raw) return null

  const exact = STATUS_ID_BY_LOWER_CASE.get(raw.toLowerCase())
  if (exact) return exact

  const tokens = raw.match(/[A-Za-z][A-Za-z0-9]*/g) ?? []
  for (const token of tokens) {
    const statusId = STATUS_ID_BY_LOWER_CASE.get(token.toLowerCase())
    if (statusId) return statusId
  }

  // Compatibility for separators that were embedded in legacy source notes
  // without producing a clean token (for example `start_strength`).
  const lower = raw.toLowerCase()
  for (const statusId of STATUS_IDS_BY_LENGTH) {
    const id = statusId.toLowerCase()
    const index = lower.indexOf(id)
    if (index < 0) continue
    const before = index === 0 ? '' : lower[index - 1]
    const afterIndex = index + id.length
    const after = afterIndex >= lower.length ? '' : lower[afterIndex]
    if ((!before || !/[a-z0-9]/.test(before)) && (!after || !/[a-z0-9]/.test(after))) {
      return statusId
    }
  }

  return null
}

/** Japanese-only combat feedback for every status mutation. */
export function statusFeedbackText(
  rawStatusId: string | null | undefined,
  amount: number,
  removed = false,
): string {
  const statusId = resolveFeedbackStatusId(rawStatusId)
  const name = statusId ? STATUS_DEFS[statusId].name : '状態変化'
  if (removed) return `${name}を解除`
  if (amount > 0) return `${name}を${amount}付与`
  if (amount < 0) return `${name}が${Math.abs(amount)}低下`
  return `${name}を付与`
}
