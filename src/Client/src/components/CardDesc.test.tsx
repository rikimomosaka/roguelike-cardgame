import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { CardDesc } from './CardDesc'

describe('CardDesc', () => {
  it('renders plain text without markers as-is', () => {
    const { container } = render(<CardDesc text="敵に攻撃する。" />)
    expect(container.textContent).toContain('敵に攻撃する。')
  })

  it('wraps [N:5] in a yellow num span', () => {
    const { container } = render(<CardDesc text="敵 1 体に [N:5] ダメージ。" />)
    const num = container.querySelector('.card-desc-num')
    expect(num).toBeInTheDocument()
    expect(num?.textContent).toBe('5')
  })

  it('wraps [K:wild] in keyword span with display name', () => {
    const { container } = render(<CardDesc text={'[K:wild]\n敵 1 体に [N:5] ダメージ。'} />)
    const kw = container.querySelector('.card-desc-keyword')
    expect(kw?.textContent).toBe('ワイルド')
    expect(kw?.getAttribute('data-keyword')).toBe('wild')
  })

  it('renders [T:OnTurnStart] as JP label', () => {
    const { container } = render(<CardDesc text="[T:OnTurnStart]の度にカードを [N:1] 枚引く。" />)
    expect(container.textContent).toContain('ターン開始時')
    const trig = container.querySelector('.card-desc-trigger')
    expect(trig?.textContent).toBe('ターン開始時')
  })

  it('renders [V:X|手札の数] as X(Xは手札の数)', () => {
    const { container } = render(<CardDesc text="敵 1 体に [V:X|手札の数] ダメージ。" />)
    expect(container.textContent).toContain('X(Xは手札の数)')
    const v = container.querySelector('.card-desc-var')
    expect(v).toBeInTheDocument()
  })

  it('renders [C:strike] as card name reference', () => {
    const { container } = render(
      <CardDesc text="[C:strike] を手札に [N:1] 枚加える。" cardNames={{ strike: 'ストライク' }} />,
    )
    expect(container.textContent).toContain('ストライク')
    const c = container.querySelector('.card-desc-cardref')
    expect(c?.textContent).toBe('ストライク')
  })

  it('falls back to card id when cardNames missing', () => {
    const { container } = render(<CardDesc text="[C:burn] を山札に [N:2] 枚加える。" />)
    const c = container.querySelector('.card-desc-cardref')
    expect(c?.textContent).toBe('burn')
  })

  it('handles multiline newlines (\\n string literal)', () => {
    const { container } = render(<CardDesc text={'行 1。\n行 2。'} />)
    const lines = container.querySelectorAll('.card-desc-line')
    expect(lines.length).toBe(2)
  })

  it('handles literal \\n escape sequence', () => {
    // 文字列としての "\\n" (2 文字) も改行として扱う (テスト経由のフィクスチャ向け)
    const text = '行 1。\\n行 2。'
    const { container } = render(<CardDesc text={text} />)
    const lines = container.querySelectorAll('.card-desc-line')
    expect(lines.length).toBe(2)
  })

  it('renders multiple markers in one line', () => {
    const { container } = render(<CardDesc text="敵 1 体に [N:6] ダメージ × [N:3] 回。" />)
    const nums = container.querySelectorAll('.card-desc-num')
    expect(nums.length).toBe(2)
    expect(nums[0].textContent).toBe('6')
    expect(nums[1].textContent).toBe('3')
  })

  it('renders unknown keyword id as raw id', () => {
    const { container } = render(<CardDesc text="[K:unknownKw]" />)
    const kw = container.querySelector('.card-desc-keyword')
    expect(kw?.textContent).toBe('unknownKw')
  })

  // ===== Phase 10.5.C: up/down marker color =====

  it('renders [N:7|up] with up class', () => {
    const { container } = render(<CardDesc text="敵に [N:7|up] ダメージ。" />)
    const num = container.querySelector('.card-desc-num')
    expect(num).toBeInTheDocument()
    expect(num?.classList.contains('card-desc-num--up')).toBe(true)
    expect(num?.classList.contains('card-desc-num--down')).toBe(false)
    expect(num?.textContent).toBe('7')
  })

  it('renders [N:3|down] with down class', () => {
    const { container } = render(<CardDesc text="敵に [N:3|down] ダメージ。" />)
    const num = container.querySelector('.card-desc-num')
    expect(num).toBeInTheDocument()
    expect(num?.classList.contains('card-desc-num--down')).toBe(true)
    expect(num?.classList.contains('card-desc-num--up')).toBe(false)
    expect(num?.textContent).toBe('3')
  })

  it('renders [N:5] (no modifier) with default class only', () => {
    const { container } = render(<CardDesc text="敵に [N:5] ダメージ。" />)
    const num = container.querySelector('.card-desc-num')
    expect(num?.classList.contains('card-desc-num--up')).toBe(false)
    expect(num?.classList.contains('card-desc-num--down')).toBe(false)
  })
})
