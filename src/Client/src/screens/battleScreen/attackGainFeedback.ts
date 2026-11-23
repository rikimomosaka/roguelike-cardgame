const ATTACK_SCOPE_LABELS: Readonly<Record<string, string>> = {
  single: '単体アタック',
  random: 'ランダムアタック',
  all: '全体アタック',
}

/**
 * Formats the short actor-overhead message for a newly acquired Attack.
 * The Core event owns the resolved scope; unknown legacy values retain a
 * useful generic label instead of hiding the feedback.
 */
export function attackGainFeedbackText(
  scope: string | null | undefined,
  amount: number,
): string {
  const normalizedScope = scope?.split(';', 1)[0]?.trim().toLowerCase() ?? ''
  const label = ATTACK_SCOPE_LABELS[normalizedScope] ?? 'アタック'
  return `${label} +${amount}`
}
