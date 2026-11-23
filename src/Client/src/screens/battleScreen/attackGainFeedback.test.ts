import { describe, expect, it } from 'vitest'
import { attackGainFeedbackText } from './attackGainFeedback'

describe('attackGainFeedbackText', () => {
  it.each([
    ['single', '単体アタック +6'],
    ['random', 'ランダムアタック +6'],
    ['all', '全体アタック +6'],
  ])('localizes the %s attack scope', (scope, expected) => {
    expect(attackGainFeedbackText(scope, 6)).toBe(expected)
  })

  it('keeps a useful generic fallback for a legacy event', () => {
    expect(attackGainFeedbackText(null, 4)).toBe('アタック +4')
  })

  it('ignores an engine provenance suffix after the scope', () => {
    expect(attackGainFeedbackText('random;power:crossfire', 5))
      .toBe('ランダムアタック +5')
  })
})
