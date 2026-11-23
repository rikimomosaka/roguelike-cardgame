/// <reference types="node" />

import { fireEvent, render, screen } from '@testing-library/react'
import { readFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { Card } from './Card'
import type { CardType } from './Card'
import { TooltipHost } from './Tooltip'

const cardCss = readFileSync(resolve(process.cwd(), 'src/components/Card.css'), 'utf8')
const cardCssPath = resolve(process.cwd(), 'src/components/Card.css')

function allCssFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = resolve(directory, entry.name)
    if (entry.isDirectory()) return allCssFiles(path)
    return entry.isFile() && entry.name.endsWith('.css') ? [path] : []
  })
}

vi.mock('../hooks/useCardCatalog', () => ({
  useCardCatalog: () => ({ names: {}, catalog: null }),
}))

vi.mock('../hooks/useUnitCatalog', () => ({
  useUnitCatalog: () => ({ names: {}, catalog: null, loading: false }),
}))

const baseProps = {
  name: '防御撃',
  cost: 1,
  type: 'attack' as const,
  rarity: 'c' as const,
}

function cssRule(selector: string): string {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const match = cardCss.match(new RegExp(`(?:^|\\n)\\s*${escapedSelector}\\s*\\{([^}]*)\\}`))
  expect(match, `${selector} rule should exist`).not.toBeNull()
  return match?.[1] ?? ''
}

describe('Card', () => {
  it.each([
    ['Striker', 'striker'],
    ['Guardian', 'guardian'],
    ['Tactician', 'tactician'],
    ['Enhancer', 'enhancer'],
    ['Universal', 'universal'],
  ] as const)('uses the %s PNG as the complete affiliation frame', (poolClass, assetName) => {
    const { container } = render(<Card {...baseProps} poolClass={poolClass} />)
    const card = container.querySelector('.card')
    const frame = container.querySelector<HTMLImageElement>('.card__affiliation-frame')

    expect(card).toHaveClass(`card--pool-${assetName}`)
    expect(card).toHaveAttribute('data-card-pool-class', poolClass)
    expect(card).toHaveAttribute('data-card-affiliation', assetName)
    expect(frame).toHaveAttribute('src', `/cards/frames/affiliations/${assetName}.png`)
    expect(container.querySelector('.card__cost--current .card__cost-base')).toHaveAttribute(
      'src', `/cards/frames/costs/${assetName}.png`,
    )
    expect(frame).toHaveAttribute('alt', '')
    expect(screen.getByText(`AFFILIATION: ${poolClass.toUpperCase()}`)).toHaveClass(
      'card__affiliation-label',
    )
  })

  it('falls back to Universal for missing or unknown affiliations', () => {
    const missing = render(<Card {...baseProps} />)
    const unknown = render(<Card {...baseProps} poolClass={'Unknown' as never} />)

    for (const container of [missing.container, unknown.container]) {
      expect(container.querySelector('.card')).toHaveClass('card--pool-universal')
      expect(container.querySelector('.card')).toHaveAttribute(
        'data-card-pool-class', 'Universal',
      )
      expect(container.querySelector('.card__affiliation-frame')).toHaveAttribute(
        'src', '/cards/frames/affiliations/universal.png',
      )
      expect(container.querySelector('.card__cost--current .card__cost-base')).toHaveAttribute(
        'src', '/cards/frames/costs/universal.png',
      )
    }
  })

  it('gives Curse its dedicated affiliation and type frames before pool resolution', () => {
    const { container } = render(
      <Card {...baseProps} type="curse" poolClass="Striker" />,
    )

    expect(container.querySelector('.card')).toHaveClass(
      'card--type-curse',
      'card--pool-curse',
    )
    expect(container.querySelector('.card')).toHaveAttribute('data-card-pool-class', 'Curse')
    expect(container.querySelector('.card__affiliation-frame')).toHaveAttribute(
      'src', '/cards/frames/affiliations/curse.png',
    )
    expect(container.querySelector('.card__type-frame')).toHaveAttribute(
      'src', '/cards/frames/types/curse.png',
    )
    expect(container.querySelector('.card__cost--current .card__cost-base')).toHaveAttribute(
      'src', '/cards/frames/costs/curse.png',
    )
  })

  it('uses six PNG masks and matching type-frame images', () => {
    const masks = [
      'attack',
      'skill',
      'power',
      'curse',
      'status',
      'unit',
    ].map(type => {
      const { container } = render(<Card {...baseProps} type={type as CardType} />)
      expect(container.querySelector('.card__type-frame')).toHaveAttribute(
        'src', `/cards/frames/types/${type}.png`,
      )
      const rule = cssRule(`.card--type-${type}`)
      const mask = rule.match(/--card-art-mask:\s*url\('([^']+)'\)/)?.[1]
      expect(mask).toBe(`/cards/frames/masks/${type}.png`)
      return mask
    })

    expect(new Set(masks).size).toBe(masks.length)
    const illustrationRule = cssRule('.card__illust')
    expect(illustrationRule).toMatch(/-webkit-mask-image:\s*var\(--card-art-mask\)/)
    expect(illustrationRule).toMatch(/\bmask-image:\s*var\(--card-art-mask\)/)
    expect(illustrationRule).not.toMatch(/\bclip-path:/)
  })

  it('covers the complete type-mask envelope before applying the alpha mask', () => {
    const imageRule = cssRule('.card__art-content > img')
    const contentRule = cssRule('.card__art-content')

    expect(contentRule).toMatch(/\bwidth:\s*94%\s*;/)
    expect(contentRule).toMatch(/\btop:\s*13\.4%\s*;/)
    expect(contentRule).toMatch(/\bheight:\s*50\.4%\s*;/)
    expect(imageRule).toMatch(/\bwidth:\s*100%\s*;/)
    expect(imageRule).toMatch(/\bheight:\s*100%\s*;/)
    expect(imageRule).toMatch(/\bobject-fit:\s*cover\s*;/)
    expect(imageRule).toMatch(/\bimage-rendering:\s*pixelated\s*;/)
  })

  it('stacks raster frames around artwork and keeps live text above them', () => {
    const { container } = render(<Card {...baseProps} description="effect" />)
    const card = container.querySelector('.card')!
    const children = [...card.children]
    const affiliation = card.querySelector('.card__affiliation-frame')!
    const art = card.querySelector('.card__illust')!
    const typeFrame = card.querySelector('.card__type-frame')!
    const top = card.querySelector('.card__top')!

    expect(children.indexOf(affiliation)).toBeLessThan(children.indexOf(art))
    expect(children.indexOf(art)).toBeLessThan(children.indexOf(typeFrame))
    expect(children.indexOf(typeFrame)).toBeLessThan(children.indexOf(top))
    expect(cssRule('.card__affiliation-frame')).toMatch(/\bz-index:\s*0\s*;/)
    expect(cssRule('.card__illust')).toMatch(/\bz-index:\s*1\s*;/)
    expect(cssRule('.card__type-frame')).toMatch(/\bz-index:\s*2\s*;/)
    expect(cssRule('.card__top')).toMatch(/\bz-index:\s*7\s*;/)
    expect(cssRule('.card__desc')).toMatch(/\bz-index:\s*3\s*;/)
  })

  it('uses rarity only for the name, exposes SUMMON for Unit, and adds no Token label', () => {
    const { container } = render(<Card {...baseProps} type="unit" rarity="e" />)

    expect(container.querySelector('.card__name')).toHaveTextContent(baseProps.name)
    expect(container).toHaveTextContent('TYPE: SUMMON')
    expect(container).not.toHaveTextContent('TOKEN')
    expect(cardCss).not.toMatch(/\.card--rarity-[crelt] \.card__(?:cost|illust)/)
    expect(cardCss).not.toMatch(/\.card__bg|--card-frame-pattern|--card-illust-clip/)
    expect(cardCss).not.toMatch(/\.card--pool-(?:striker|guardian|tactician|enhancer|universal)\s*\{/)
  })

  it('keeps selection, playability and contingency above the raster composition', () => {
    expect(cssRule('.card.is-playable')).toMatch(/\bbox-shadow:/)
    expect(cssRule('.card.is-selected::after')).toMatch(/border-color:\s*var\(--gold\)/)
    expect(cssRule('.card__contingency-ring')).toMatch(/\bz-index:\s*6\s*;/)
  })

  it('shows armed Contingency inside the card without taking over existing frame states', () => {
    const { container } = render(
      <Card
        {...baseProps}
        poolClass="Tactician"
        selected
        className="is-playable"
        contingencyArmed
        contingencyTriggerText="3コンボ達成時"
      />,
    )
    const card = container.querySelector('.card')

    expect(card).toHaveClass(
      'card--pool-tactician',
      'is-selected',
      'is-playable',
      'is-contingency-armed',
    )
    expect(screen.getByText('伏策待機中：3コンボ達成時')).toHaveAttribute(
      'title', '伏策待機中：3コンボ達成時',
    )
    expect(container.querySelector('.card__contingency-ring')).not.toBeNull()
    expect(cssRule('.card__contingency-ring')).toMatch(/\binset:\s*3px\s*;/)
    expect(cssRule('.card__contingency-ring')).not.toMatch(/\bwidth:|\bheight:/)
  })

  it('keeps sizing out of inline styles and supports pointer and keyboard activation', () => {
    const onClick = vi.fn()
    const { container } = render(<Card {...baseProps} onClick={onClick} />)
    const card = container.querySelector('.card')

    expect(card).not.toHaveAttribute('style')
    expect(card).toHaveAttribute('role', 'button')
    expect(card).toHaveAttribute('tabindex', '0')

    fireEvent.click(card as Element)
    expect(onClick).toHaveBeenCalledOnce()

    fireEvent.keyDown(card as Element, { key: 'Enter' })
    fireEvent.keyDown(card as Element, { key: ' ' })
    expect(onClick).toHaveBeenCalledTimes(3)

    fireEvent.keyDown(card as Element, { key: 'Escape' })
    expect(onClick).toHaveBeenCalledTimes(3)
  })

  it('uses one fixed base geometry in every caller', () => {
    const rule = cssRule('.card')

    expect(rule).toMatch(/\bwidth:\s*var\(--card-width\)\s*;/)
    expect(rule).toMatch(/\bheight:\s*var\(--card-height\)\s*;/)
    expect(rule).toMatch(/\bflex:\s*0\s+0\s+auto\s*;/)
    expect(rule).toMatch(/\boverflow:\s*visible\s*;/)
  })

  it('keeps type and affiliation labels accessible without shrinking the artwork', () => {
    const { container } = render(<Card {...baseProps} description="effect" />)

    expect(container.querySelector('.card__type')).toHaveTextContent('TYPE: ATTACK')
    expect(container.querySelector('.card__affiliation-label')).toHaveTextContent(
      'AFFILIATION: UNIVERSAL',
    )
    const semanticLabelRule = cssRule('.card__type,\n.card__affiliation-label,\n.card__state-label')
    expect(semanticLabelRule).toMatch(/\bwidth:\s*1px\s*;/)
    expect(cssRule('.card__illust')).toMatch(/\bmask-image:\s*var\(--card-art-mask\)/)
    expect(cssRule('.card__desc')).toMatch(/\btop:\s*100px\s*;/)
    expect(cssRule('.card__desc')).toMatch(/\bright:\s*6px\s*;/)
    expect(cssRule('.card__desc')).toMatch(/\bbottom:\s*5px\s*;/)
    expect(cssRule('.card__desc')).toMatch(/\bleft:\s*10px\s*;/)
    expect(cssRule('.card__desc')).toMatch(/\bpadding:\s*4px 1px 1px\s*;/)
    expect(cssRule('.card__desc')).toMatch(/\bfont-synthesis:\s*none\s*;/)
    expect(cssRule('.card__desc').match(/(?:-1px 0 0 #000|1px 0 0 #000|0 -1px 0 #000|0 1px 0 #000)/g)).toHaveLength(4)
    expect(cardCss).toMatch(/\.card__desc \.card-desc\[data-lines='2'\],\s*\.card__desc \.card-desc\[data-lines='3'\]\s*\{[^}]*\bline-height:\s*1\.35\s*;/s)
    expect(cssRule(".card__desc .card-desc[data-lines='4']")).toMatch(/\bline-height:\s*1\.08\s*;/)
    expect(cssRule(".card__desc .card-desc[data-lines='5']")).toMatch(/\bline-height:\s*1\s*;/)
    expect(cssRule(".card__desc .card-desc[data-lines='5']")).toMatch(/\btransform:\s*scaleY\(0\.88\)\s*;/)
    expect(cssRule(".card__desc .card-desc[data-lines='5']")).toMatch(/\btransform-origin:\s*center top\s*;/)
  })

  it('centers titles on the card while reserving equal space around the cost', () => {
    const nameRule = cssRule('.card__name')

    expect(nameRule).toMatch(/\bleft:\s*0\s*;/)
    expect(nameRule).toMatch(/\bright:\s*0\s*;/)
    expect(nameRule).toMatch(/\bpadding:\s*0 22px\s*;/)
    expect(nameRule).toMatch(/\bjustify-content:\s*center\s*;/)
    expect(nameRule).toMatch(/\btext-align:\s*center\s*;/)
    expect(cssRule('.card__name[data-len="5"]')).toMatch(/\bfont-size:\s*13px\s*;/)
    expect(cssRule('.card__name[data-len="6"]')).toMatch(/\bfont-size:\s*11px\s*;/)
  })

  it('uses the resolved affiliation cost plate for both current and changed original costs', () => {
    const { container } = render(
      <Card {...baseProps} poolClass="Enhancer" cost={1} costOrig={2} />,
    )
    const plates = [...container.querySelectorAll<HTMLImageElement>('.card__cost-base')]

    expect(plates).toHaveLength(2)
    expect(plates[0]).toHaveAttribute('src', '/cards/frames/costs/enhancer.png')
    expect(plates[0]).toHaveAttribute('alt', '')
    expect(plates[0].closest('.card__cost')).toHaveClass('card__cost--current')
    expect(plates[1]).toHaveAttribute('src', '/cards/frames/costs/enhancer.png')
    expect(plates[1].closest('.card__cost')).toHaveClass('card__cost--orig')
    expect(cssRule('.card__cost-base')).toMatch(/\bz-index:\s*0\s*;/)
    expect(cssRule('.card__cost-n')).toMatch(/\bz-index:\s*2\s*;/)
    expect(cssRule('.card__cost--orig .card__cost-base')).toMatch(/\bfilter:\s*grayscale\(0\.72\) brightness\(0\.68\)\s*;/)
  })

  it('uses authoritative costOriginal for the lower cost and semantic color', () => {
    const { container } = render(
      <Card {...baseProps} poolClass="Guardian" cost={1} costOriginal={3} />,
    )

    expect(container.querySelector('.card__cost-stack')).not.toBeNull()
    expect(container.querySelector('.card__cost-stack')).toHaveAttribute('aria-hidden', 'true')
    expect(container.querySelector('.card__cost--orig')).toHaveTextContent('3')
    expect(container.querySelector('.card__cost--orig .card__cost-base')).toHaveAttribute(
      'src', '/cards/frames/costs/guardian.png',
    )
    expect(container.querySelector('.card__cost--current .card__cost-n')).toHaveClass(
      'card__cost-n--reduced',
    )
  })

  it('lets an explicit authoritative null suppress the legacy original-cost alias', () => {
    const { container } = render(
      <Card {...baseProps} cost={1} costOriginal={null} costOrig={3} />,
    )

    expect(container.querySelector('.card__cost-stack')).toBeNull()
    expect(container.querySelector('.card__cost--orig')).toBeNull()
    expect(container.querySelectorAll('.card__cost-base')).toHaveLength(1)
    expect(container.querySelector('.card__cost-n')).toHaveClass('card__cost-n--same')
  })

  it('centers normal cost digits and gives reduced originals their own optical correction', () => {
    const costRule = cssRule('.card__cost')
    const digitRule = cssRule('.card__cost-n')

    expect(costRule).toMatch(/\bdisplay:\s*grid\s*;/)
    expect(costRule).toMatch(/\bplace-items:\s*center\s*;/)
    expect(costRule).toMatch(/\bfont-family:\s*var\(--font-digit\)\s*;/)
    expect(digitRule).toMatch(/\bdisplay:\s*grid\s*;/)
    expect(digitRule).toMatch(/\bplace-items:\s*center\s*;/)
    expect(digitRule).toMatch(/\btransform:\s*translate\(/)

    const originalRule = cssRule('.card__cost--orig')
    expect(originalRule).toMatch(/--card-cost-digit-x:\s*0\s*;/)
    expect(originalRule).toMatch(/--card-cost-digit-y:\s*-1px\s*;/)

    const transformedInternalRules = [...cardCss.matchAll(/(\.card__[^{]+)\{([^}]*)\}/g)]
      .filter(([, selector]) => !selector.includes('.card__combo-pulse'))
      .filter(([, , body]) => /\btransform\s*:/.test(body))
      .map(([, selector]) => selector.trim())
    expect(transformedInternalRules).toEqual([
      '.card__cost-n',
      ".card__desc .card-desc[data-lines='5']",
    ])
  })

  it('projects a larger current plate past the top-left and stacks the original below it', () => {
    const costRule = cssRule('.card__cost')
    const stackRule = cssRule('.card__cost-stack')
    const currentRule = cssRule('.card__cost-stack .card__cost--current')
    const originalRule = cssRule('.card__cost--orig')
    const currentDigitRule = cssRule('.card__cost--current .card__cost-n')
    const currentBaseRule = cssRule('.card__cost--current .card__cost-base')
    const currentCostRule = cssRule('.card .card__cost--current')
    const originalCostRule = cssRule('.card .card__cost--orig')

    expect(costRule).toMatch(/\bleft:\s*-7px\s*;/)
    expect(costRule).toMatch(/\btop:\s*-6px\s*;/)
    expect(costRule).toMatch(/\bwidth:\s*28px\s*;/)
    expect(costRule).toMatch(/\bheight:\s*28px\s*;/)
    expect(costRule).toMatch(/\bfont-size:\s*20px\s*;/)
    expect(stackRule).toMatch(/\bleft:\s*-7px\s*;/)
    expect(stackRule).toMatch(/\btop:\s*-6px\s*;/)
    expect(stackRule).toMatch(/\bwidth:\s*28px\s*;/)
    expect(stackRule).toMatch(/\bheight:\s*50px\s*;/)
    expect(currentRule).toMatch(/\bleft:\s*0\s*;/)
    expect(currentRule).toMatch(/\btop:\s*0\s*;/)
    expect(originalRule).toMatch(/\bleft:\s*3px\s*;/)
    expect(originalRule).toMatch(/\btop:\s*27px\s*;/)
    expect(originalRule).toMatch(/\bwidth:\s*22px\s*;/)
    expect(originalRule).toMatch(/\bheight:\s*22px\s*;/)
    expect(currentDigitRule).toMatch(/\bfont-weight:\s*400\s*;/)
    expect(currentDigitRule).toMatch(/\btext-shadow:\s*var\(--text-shadow-pixel\)\s*;/)
    expect(currentBaseRule).toMatch(/\binset:\s*-1px\s*;/)
    expect(currentBaseRule).toMatch(/\bwidth:\s*calc\(100% \+ 2px\)\s*;/)
    expect(currentBaseRule).toMatch(/\bheight:\s*calc\(100% \+ 2px\)\s*;/)
    expect(currentDigitRule).not.toMatch(/rgba\(241,\s*201,\s*91/)
    expect(currentCostRule).toMatch(/\bbackground:\s*transparent\s*;/)
    expect(originalCostRule).toMatch(/\bborder-color:\s*transparent\s*;/)
    expect(originalCostRule).toMatch(/\bbox-shadow:\s*none\s*;/)
    expect(cssRule('.card__cost--orig .card__cost-n')).toMatch(/\bfont-size:\s*13px\s*;/)
    expect(cssRule('.card__cost--orig .card__cost-n')).not.toMatch(/text-decoration/)
  })

  it('uses combo eligibility for the card aura and compares current with original cost', () => {
    const { container } = render(
      <Card {...baseProps} cost={1} costOrig={2} comboReductionEligible />,
    )
    expect(container.querySelector('.card')).toHaveClass('is-combo-continuing')
    expect(container.querySelectorAll('.card__combo-pulse')).toHaveLength(1)
    expect(container.querySelector('.card__cost--combo-eligible')).toBeNull()
    expect(container.querySelector('.card__cost-n--reduced')).not.toBeNull()

    const increased = render(
      <Card {...baseProps} cost={3} costOrig={2} comboReductionEligible={false} />,
    )
    expect(increased.container.querySelector('.card__cost-n--increased')).not.toBeNull()
    expect(increased.container.querySelector('.card')).not.toHaveClass('is-combo-continuing')

    const same = render(
      <Card {...baseProps} cost={0} costOrig={0} comboContinues />,
    )
    expect(same.container.querySelector('.card')).toHaveClass('is-combo-continuing')
    expect(same.container.querySelectorAll('.card__combo-pulse')).toHaveLength(1)
    expect(same.container.querySelector('.card__cost-n--same')).not.toBeNull()
    expect(cssRule('.card__cost-n--reduced')).toMatch(/\bcolor:\s*var\(--gold\)\s*;/)
    expect(cssRule('.card__cost-n--increased')).toMatch(/\bcolor:\s*var\(--state-block\)\s*;/)
    expect(cssRule('.card__cost-n--same')).toMatch(/\bcolor:\s*var\(--fg-white\)\s*;/)
    expect(cssRule('.card .card__cost-n--reduced')).toMatch(/\bcolor:\s*var\(--gold\)\s*;/)
    expect(cssRule('.card .card__cost-n--increased')).toMatch(/\bcolor:\s*var\(--state-block\)\s*;/)
    expect(cssRule('.card.is-combo-continuing::before')).toMatch(/\binset:\s*1px\s*;/)
    expect(cssRule('.card.is-combo-continuing::before')).toMatch(/\bbox-shadow:/)
    expect(cssRule('.card__combo-pulse')).toMatch(/\banimation:\s*card-combo-pulse\s+1\.65s\s+/)
    expect(cssRule('.card__combo-aura')).toMatch(/\bz-index:\s*2\s*;/)
  })

  it('announces current/original cost, its direction, and combo eligibility without duplicate visual digits', () => {
    const { container } = render(
      <Card
        {...baseProps}
        cost={1}
        costOriginal={2}
        comboReductionEligible
        onClick={() => undefined}
      />,
    )

    const card = screen.getByRole('button')
    expect(card).toHaveAccessibleName(/現在コスト1、元コスト2、軽減、コンボ継続可能。/)
    expect(container.querySelector('.card__state-label')).toHaveTextContent(
      '現在コスト1、元コスト2、軽減、コンボ継続可能。',
    )
    expect(container.querySelector('.card__cost-stack')).toHaveAttribute('aria-hidden', 'true')
  })

  it('suppresses the aura when Continuity cannot grant reduction and overlays unavailable costs', () => {
    const { container } = render(
      <Card
        {...baseProps}
        comboContinues
        comboRingHighlighted={false}
        costUnavailable
      />,
    )

    expect(container.querySelector('.card')).not.toHaveClass('is-combo-continuing')
    expect(container.querySelector('.card__cost-unavailable')).toHaveTextContent('×')
    expect(container.querySelector('.card__cost-unavailable')).toHaveAttribute('aria-hidden', 'true')
    expect(container.querySelector('.card__state-label')).toHaveTextContent('使用不可、現在コスト1。')
    expect(cssRule('.card__cost-unavailable')).toMatch(/\bleft:\s*-7px\s*;/)
    expect(cssRule('.card__cost-unavailable')).toMatch(/\btop:\s*-9px\s*;/)
    expect(cssRule('.card__cost-unavailable')).toMatch(/\bwidth:\s*28px\s*;/)
    expect(cssRule('.card__cost-unavailable')).toMatch(/\bfont-size:\s*28px\s*;/)
    expect(cssRule('.card__cost-unavailable')).toMatch(/\bz-index:\s*6\s*;/)
  })

  it('keeps the unavailable marker above a simultaneous changed-cost stack', () => {
    const { container } = render(
      <Card
        {...baseProps}
        cost={1}
        costOriginal={2}
        costUnavailable
      />,
    )

    expect(container.querySelector('.card__cost-unavailable')).not.toBeNull()
    expect(container.querySelector('.card__cost-stack')).not.toBeNull()
    expect(cssRule('.card__cost-unavailable')).toMatch(/\bz-index:\s*6\s*;/)
    expect(cssRule('.card__cost-stack')).toMatch(/\bz-index:\s*5\s*;/)
  })

  it('marks cost length and fits two or more characters in the fixed cost area', () => {
    const { container } = render(
      <Card {...baseProps} cost={12} costOrig={123} />,
    )
    const digits = [...container.querySelectorAll('.card__cost-n')]

    expect(digits).toHaveLength(2)
    expect(digits[0]).toHaveAttribute('data-digits', '2')
    expect(digits[1]).toHaveAttribute('data-digits', '3')
    expect(cssRule(".card__cost-n[data-digits='2']")).toMatch(/\bfont-size:\s*16px\s*;/)
    expect(cssRule(".card__cost-n[data-digits='3']")).toMatch(/\bfont-size:\s*12px\s*;/)
    expect(cssRule(".card__cost--orig .card__cost-n[data-digits='2']")).toMatch(/\bfont-size:\s*10px\s*;/)
    expect(cssRule(".card__cost--orig .card__cost-n[data-digits='3']")).toMatch(/\bfont-size:\s*8px\s*;/)
    expect(cssRule('.card__cost')).toMatch(/\boverflow:\s*visible\s*;/)
  })

  it('shows the authoritative cost-change sequence even when the final cost equals the original', () => {
    const { container } = render(
      <TooltipHost>
        <Card
          {...baseProps}
          description="テスト効果。"
          cost={2}
          costOriginal={2}
          costChanges={[
            { kind: 'fixed', value: 3, sourceId: 'confusion', sourceName: '混乱' },
            { kind: 'modifier', value: -1, sourceId: 'source', sourceName: '手札整備+' },
            { kind: 'status', value: 1, sourceId: 'handCostIncrease', sourceName: '浪費' },
            { kind: 'status', value: -1, sourceId: 'handCostReduction', sourceName: '節制' },
          ]}
        />
      </TooltipHost>,
    )

    fireEvent.mouseEnter(container.querySelector('.card')!, { clientX: 30, clientY: 30 })

    const panel = screen.getByLabelText('コスト変更の内訳')
    expect(panel).toHaveTextContent('コスト2')
    expect(panel).toHaveTextContent('[混乱]によりコスト3に変更')
    expect(panel).toHaveTextContent('「手札整備+」によりコスト-1')
    expect(panel).toHaveTextContent('[浪費]によりコスト+1')
    expect(panel).toHaveTextContent('[節制]によりコスト-1')
    expect(panel).toHaveTextContent('＝2コスト')
    expect(container.querySelector('.card__cost-stack')).toBeNull()
  })

  it('forbids scene CSS from recomputing card geometry or typography', () => {
    const cardClassToken = /(^|[\s>+~,])\.card(?=$|[\s.#:[>+~,])/
    const forbiddenGeometry = /\b(?:width|height|aspect-ratio|font|font-family|font-size|line-height|padding)\s*:/
    const violations: string[] = []

    for (const file of allCssFiles(resolve(process.cwd(), 'src'))) {
      if (file === cardCssPath) continue
      const css = readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')
      for (const match of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
        const selector = match[1].trim()
        const body = match[2]
        if (selector.includes('.card__')) {
          violations.push(`${file}: internal selector ${selector}`)
        } else if (cardClassToken.test(selector) && forbiddenGeometry.test(body)) {
          violations.push(`${file}: geometry override ${selector}`)
        }
      }
    }

    expect(violations).toEqual([])
  })
})
