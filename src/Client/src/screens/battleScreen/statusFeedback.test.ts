import { describe, expect, it } from 'vitest'
import { resolveFeedbackStatusId, statusFeedbackText } from './statusFeedback'

describe('statusFeedback', () => {
  it('uses the official Japanese status name and Japanese action wording', () => {
    expect(statusFeedbackText('strength', 2)).toBe('筋力を2付与')
    expect(statusFeedbackText('strength', -2)).toBe('筋力が2低下')
    expect(statusFeedbackText('vulnerable', 0, true)).toBe('脆弱を解除')
  })

  it('normalises legacy trigger-decorated notes without exposing English', () => {
    expect(resolveFeedbackStatusId('strength:start')).toBe('strength')
    expect(resolveFeedbackStatusId('start_strength')).toBe('strength')
    expect(statusFeedbackText('strength:start', -2)).toBe('筋力が2低下')
  })

  it('uses a Japanese-only fallback for an unknown internal id', () => {
    expect(statusFeedbackText('legacy_start_flag', 1)).toBe('状態変化を1付与')
    expect(statusFeedbackText('legacy_start_flag', 0, true)).toBe('状態変化を解除')
  })
})
