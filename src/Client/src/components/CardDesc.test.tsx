import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { CardDesc, KEYWORD_DEFS, STATUS_DEFS, extractCardDescRefs } from './CardDesc'

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

  it('renders a counted keyword as name plus a yellow count', () => {
    const { container } = render(<CardDesc text="[K:bipolar|2]" />)
    const kw = container.querySelector('.card-desc-keyword')
    expect(kw?.textContent).toBe('双極2')
    expect(kw?.getAttribute('data-keyword')).toBe('bipolar')
    expect(kw?.querySelector('.card-desc-num')?.textContent).toBe('2')
    expect(kw).not.toHaveClass('card-desc-keyword--enhanced')
  })

  it('marks keywords added by enhance with a highlighter background', () => {
    const { container } = render(<CardDesc text={'[K:bipolar|+2]\n[K:wild]'} />)
    const [added, own] = Array.from(container.querySelectorAll('.card-desc-keyword'))
    expect(added.textContent).toBe('双極2')
    expect(added).toHaveClass('card-desc-keyword--enhanced')
    expect(own).not.toHaveClass('card-desc-keyword--enhanced')
    const single = render(<CardDesc text="[K:bipolar|+]" />).container.querySelector('.card-desc-keyword')
    expect(single?.textContent).toBe('双極')
    expect(single).toHaveClass('card-desc-keyword--enhanced')
  })

  it('fills the bipolar count into its tooltip description and adds phase terms', () => {
    const refs = extractCardDescRefs('[K:bipolar|+2]\n位相を[P:calm]にする。[K:enhance]【[K:bipolar]】[K:invert]')
    const bipolar = refs.find(r => r.kind === 'keyword' && r.id === 'bipolar')
    expect(bipolar && 'desc' in bipolar ? bipolar.desc : '').toBe(
      '位相が[P:calm]の時、手札にある限り、次のターンに持ち越される。\n位相が[P:motion]の時、発動時にエナジーを+[N:2]する。\nこのカードの発動後、双極が消滅する。')
    // 双極の説明に出てくる静・動も続けて補足する。
    expect(refs.map(r => r.id)).toEqual(['bipolar', 'phase:calm', 'enhance', 'invert', 'phase:motion'])
    const calm = refs.find(r => r.id === 'phase:calm')
    expect(calm?.name).toBe('静')
    expect(refs.find(r => r.id === 'enhance')).toMatchObject({ name: 'エンハンス', desc: '【 】内の効果を与える。' })
    expect(refs.find(r => r.id === 'invert')).toMatchObject({ name: '反転', desc: '位相を持つ場合、対となる位相に変更する。' })
  })

  it('also explains keywords that appear inside another explanation', () => {
    const refs = extractCardDescRefs('位相を[P:calm]にする。')
    expect(refs.map(r => r.id)).toEqual(['phase:calm', 'enhance', 'bipolar', 'phase:motion'])
  })

  it('uses 1 for the bipolar count when the keyword has no count', () => {
    const refs = extractCardDescRefs('[K:bipolar]')
    const bipolar = refs[0]
    expect('desc' in bipolar ? bipolar.desc : '').toContain('+[N:1]する')
  })

  it('colours phase words: 静 blue and 動 red', () => {
    const { container } = render(<CardDesc text="位相を[P:calm]にする。[P:motion]" />)
    const calm = container.querySelector('.card-desc-phase--calm')
    const motion = container.querySelector('.card-desc-phase--motion')
    expect(calm?.textContent).toBe('静')
    expect(motion?.textContent).toBe('動')
    expect(container.textContent).toBe('位相を静にする。動')
  })

  it('renders a single keyword without a count', () => {
    const { container } = render(<CardDesc text="[K:bipolar]" />)
    expect(container.querySelector('.card-desc-keyword')?.textContent).toBe('双極')
    expect(KEYWORD_DEFS.bipolar.desc).toBe('位相が[P:calm]の時、手札にある限り、次のターンに持ち越される。\n位相が[P:motion]の時、発動時にエナジーを+[N:X]する。\nこのカードの発動後、双極が消滅する。')
  })

  it('marks a disabled Wild keyword with a strike-through label and red suffix', () => {
    const { container } = render(<CardDesc text="[K:wild]" disabledKeywords={['wild']} />)
    const keyword = container.querySelector('.card-desc-keyword--disabled')
    expect(keyword).toBeInTheDocument()
    expect(keyword?.querySelector('.card-desc-keyword__label')).toBeInTheDocument()
    expect(keyword?.querySelector('.card-desc-keyword__disabled')?.textContent).toBe(' (無効)')
  })

  it('recomputes horizontal fitting when a keyword becomes disabled', () => {
    const clientWidth = vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get')
      .mockImplementation(function (this: HTMLElement) {
        return this.classList.contains('card-desc-line') ? 80 : 0
      })
    const scrollWidth = vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get')
      .mockImplementation(function (this: HTMLElement) {
        if (!this.classList.contains('card-desc-line-inner')) return 0
        return this.textContent?.includes('(無効)') ? 120 : 70
      })

    try {
      const text = '[K:wild]/[K:finisher]'
      const { container, rerender } = render(
        <CardDesc text={text} disabledKeywords={[]} />,
      )
      const inner = container.querySelector<HTMLElement>('.card-desc-line-inner')!
      expect(inner.dataset.fit).toBeUndefined()
      expect(inner.style.transform).toBe('')

      rerender(<CardDesc text={text} disabledKeywords={['wild']} />)

      expect(inner.dataset.fit).toBe('scaled')
      expect(inner.style.transform).toContain('scaleX(')
    } finally {
      clientWidth.mockRestore()
      scrollWidth.mockRestore()
    }
  })

  it('uses sub-pixel width and reserves black-outline space for a boundary line', () => {
    const clientWidth = vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get')
      .mockImplementation(function (this: HTMLElement) {
        return this.classList.contains('card-desc-line') ? 100 : 0
      })
    const scrollWidth = vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get')
      .mockImplementation(function (this: HTMLElement) {
        return this.classList.contains('card-desc-line-inner') ? 96 : 0
      })
    const nativeGetComputedStyle = window.getComputedStyle.bind(window)
    const computedStyle = vi.spyOn(window, 'getComputedStyle')
      .mockImplementation((element, pseudoElement) => {
        if (!element.classList.contains('card-desc-line-inner')) {
          return nativeGetComputedStyle(element, pseudoElement)
        }
        const letterSpacing = (element as HTMLElement).style.letterSpacing
        const width = letterSpacing === '-0.2px' ? '95.9px' : '96.25px'
        return { width } as CSSStyleDeclaration
      })

    try {
      const { container } = render(<CardDesc text="伏策：相手のアタック時" />)
      const inner = container.querySelector<HTMLElement>('.card-desc-line-inner')!

      expect(inner.dataset.fit).toBe('tracking')
      expect(inner.style.letterSpacing).toBe('-0.2px')
      expect(inner.style.transform).toBe('')
    } finally {
      computedStyle.mockRestore()
      clientWidth.mockRestore()
      scrollWidth.mockRestore()
    }
  })

  it('uses the final fractional tracked width when calculating horizontal scale', () => {
    const clientWidth = vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get')
      .mockImplementation(function (this: HTMLElement) {
        return this.classList.contains('card-desc-line') ? 100 : 0
      })
    const scrollWidth = vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get')
      .mockImplementation(function (this: HTMLElement) {
        return this.classList.contains('card-desc-line-inner') ? 111 : 0
      })
    const nativeGetComputedStyle = window.getComputedStyle.bind(window)
    const computedStyle = vi.spyOn(window, 'getComputedStyle')
      .mockImplementation((element, pseudoElement) => {
        if (!element.classList.contains('card-desc-line-inner')) {
          return nativeGetComputedStyle(element, pseudoElement)
        }
        const letterSpacing = (element as HTMLElement).style.letterSpacing
        return { width: letterSpacing === '-0.6px' ? '110.5px' : '120.75px' } as CSSStyleDeclaration
      })

    try {
      const { container } = render(<CardDesc text="再演：コスト2以下のアタック・スキル" />)
      const inner = container.querySelector<HTMLElement>('.card-desc-line-inner')!
      const scale = inner.style.transform.match(/^scaleX\((.+)\)$/)

      expect(inner.dataset.fit).toBe('scaled')
      expect(inner.style.left).toBe('2px')
      expect(Number(scale?.[1])).toBeCloseTo(96 / 110.5, 10)
    } finally {
      computedStyle.mockRestore()
      clientWidth.mockRestore()
      scrollWidth.mockRestore()
    }
  })

  it('keeps Wild and SuperWild tooltip descriptions aligned with the game rules', () => {
    expect(KEYWORD_DEFS.wild.desc).toBe(
      'このカード及び次に使うカードではコンボが途切れない。このコンボ中、以降のワイルドを無効にする。',
    )
    expect(KEYWORD_DEFS.superwild.desc).toBe(
      'このカード及び次に使うカードではコンボが途切れない。',
    )
  })

  it('defines the four class-mechanic keywords with canonical Japanese wording', () => {
    expect(KEYWORD_DEFS.momentum).toEqual({
      name: '乗勢',
      desc: '効果の実行時、自身のこのターンの最大コンボがX以上であれば適用される。',
    })
    expect(KEYWORD_DEFS.finisher.name).toBe('終撃')
    expect(KEYWORD_DEFS.reprise.name).toBe('再演')
    expect(KEYWORD_DEFS.reprise.desc).toContain('この戦闘中')
    expect(KEYWORD_DEFS.reprise.desc).toContain('効果の解決を完了')
    expect(KEYWORD_DEFS.contingency).toEqual({
      name: '伏策',
      desc: '使用時は効果が実行されず、発動待ちとして除外する。伏策条件を満たした時に効果を実行し、捨札へ移動する。',
    })
  })

  it('defines both hand-wide cost statuses for nested tooltips', () => {
    expect(STATUS_DEFS.handCostReduction).toMatchObject({ name: '節制' })
    expect(STATUS_DEFS.handCostIncrease).toMatchObject({ name: '浪費' })
  })

  it('defines every expanded status with the canonical Japanese wording', () => {
    expect(Object.keys(STATUS_DEFS)).toHaveLength(41)
    expect(STATUS_DEFS.poison.desc).toBe('ターン開始時に[N:X]ダメージを受け、[S:poison]が1減少する。')
    expect(STATUS_DEFS.reflect.desc).toBe(
      'アタックを受けた時、攻撃元にアタックの回数分だけ[N:X]ダメージを返す(この際ブロックを無視する)。',
    )
    expect(STATUS_DEFS.pursuit).toEqual({
      name: '追撃',
      desc: '攻撃直前に、「敵全体に[N:X]アタック。」を得て、[S:pursuit]が1減少する。',
    })
    expect(STATUS_DEFS.confusion.desc).toContain('0～3のランダムな数値')
    expect(STATUS_DEFS.distress.name).toBe('動揺')
    expect(STATUS_DEFS.virulentPoison.desc).toBe('[S:poison]によって受けるダメージが[N:X]倍になる。')
    expect(STATUS_DEFS.scorch).toEqual({
      name: '灼痕',
      desc: '[S:burn]が消滅する時、代わりに[S:scorch]が1減少する。',
    })
    expect(STATUS_DEFS.haste).toEqual({
      name: '加速',
      desc: 'ターン開始時のエナジー回復量を+[N:X]し、[S:haste]が消滅する。',
    })
    expect(STATUS_DEFS.afterimage.name).toBe('残光')
    expect(STATUS_DEFS.steadfast.name).toBe('堅持')
    expect(STATUS_DEFS.cover.name).toBe('護衛')
    expect(STATUS_DEFS.defensiveStance.name).toBe('守勢')
    expect(STATUS_DEFS.comboCarryover.name).toBe('連環')
    expect(STATUS_DEFS.afterglow.name).toBe('余韻')
    expect(STATUS_DEFS.resonance.name).toBe('共鳴')
    expect(STATUS_DEFS.disruption.name).toBe('攪乱')
  })

  it('renders substituted status values and names with highlighted marker classes', () => {
    const { container } = render(
      <CardDesc text="ターン開始時に[N:3]ダメージを受け、[S:poison]が1減少する。" />,
    )

    expect(container.querySelector('.card-desc-num')).toHaveTextContent('3')
    expect(container.querySelector('.card-desc-status')).toHaveTextContent('毒')
  })

  it('renders [T:OnTurnStart] as JP label', () => {
    const { container } = render(<CardDesc text="[T:OnTurnStart]にカードを [N:1] 枚引く。" />)
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

  it('renders the OnHit visual next to its trigger label', () => {
    const { container } = render(<CardDesc text="[T:OnHit]" />)
    const icon = container.querySelector('.card-desc-trigger__icon')
    expect(icon).toHaveAttribute('src', '/assets/icons/status/on_hit.png')
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
    expect(container.querySelector('.card-desc')).toHaveAttribute('data-lines', '2')
  })

  it('caps the layout line band at five for dense in-card descriptions', () => {
    const { container } = render(<CardDesc text={'一。\n二。\n三。\n四。\n五。\n六。'} />)

    expect(container.querySelectorAll('.card-desc-line')).toHaveLength(6)
    expect(container.querySelector('.card-desc')).toHaveAttribute('data-lines', '5')
    expect(container.querySelector('.card-desc')).toHaveAttribute('data-line-overflow', 'true')
  })

  it('marks a fully consumed per-turn usage line with the reached-state class', () => {
    const reached = render(<CardDesc text={'ターン中2度だけ使用可能(2/2)。\n1枚引く。'} />)
    const available = render(<CardDesc text={'ターン中2度だけ使用可能(1/2)。\n1枚引く。'} />)

    expect(reached.container.querySelector('.card-desc-line')).toHaveClass(
      'card-desc-line--usage-limit-reached',
    )
    expect(available.container.querySelector('.card-desc-line')).not.toHaveClass(
      'card-desc-line--usage-limit-reached',
    )
  })

  it('handles literal \\n escape sequence', () => {
    // 文字列としての "\\n" (2 文字) も改行として扱う (テスト経由のフィクスチャ向け)
    const text = '行 1。\\n行 2。'
    const { container } = render(<CardDesc text={text} />)
    const lines = container.querySelectorAll('.card-desc-line')
    expect(lines.length).toBe(2)
  })

  it('keeps a modifyCardCost formatter sentence on one fitted line', () => {
    const text = '手札の全カードのコストを使用されるまで-[N:1]。'
    const compact = render(<CardDesc text={text} />)
    expect(compact.container.querySelectorAll('.card-desc-line')).toHaveLength(1)
    expect(compact.container.querySelector('.card-desc-line')).toHaveTextContent(
      '手札の全カードのコストを使用されるまで-1。',
    )

    const expanded = render(<CardDesc text={text} compress={false} />)
    expect(expanded.container.querySelectorAll('.card-desc-line-plain')).toHaveLength(1)
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

  it('shows a resolved stat reference as a number on the card face', () => {
    const { container } = render(<CardDesc text="敵全体に[R:7|up:自身の攻撃力]アタック。" />)
    const num = container.querySelector('.card-desc-num')
    expect(num?.textContent).toBe('7')
    expect(num).toHaveClass('card-desc-num--up')
    expect(container.textContent).not.toContain('Xは')
  })

  it('reveals the stat reference in expanded text such as tooltips', () => {
    const { container } = render(
      <CardDesc text="敵全体に[R:9|:自身の攻撃力×1.5]アタック。" compress={false} />,
    )
    expect(container.textContent).toBe('敵全体に9(自身の攻撃力×1.5)アタック。')
    expect(container.querySelector('.card-desc-num')?.textContent).toBe('9')
  })
})
