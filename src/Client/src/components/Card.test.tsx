/// <reference types="node" />

import { fireEvent, render } from '@testing-library/react'
import { readFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { Card } from './Card'

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
  const match = cardCss.match(new RegExp(`${escapedSelector}\\s*\\{([^}]*)\\}`))
  expect(match, `${selector} rule should exist`).not.toBeNull()
  return match?.[1] ?? ''
}

describe('Card', () => {
  it('keeps sizing out of inline styles and preserves click semantics', () => {
    const onClick = vi.fn()
    const { container } = render(<Card {...baseProps} onClick={onClick} />)
    const card = container.querySelector('.card')

    expect(card).not.toHaveAttribute('style')
    expect(card).toHaveAttribute('role', 'button')
    expect(card).toHaveAttribute('tabindex', '0')

    fireEvent.click(card as Element)
    expect(onClick).toHaveBeenCalledOnce()
  })

  it('uses one fixed base geometry in every caller', () => {
    const rule = cssRule('.card')

    expect(rule).toMatch(/\bwidth:\s*var\(--card-width\)\s*;/)
    expect(rule).toMatch(/\bheight:\s*var\(--card-height\)\s*;/)
    expect(rule).toMatch(/\bflex:\s*0\s+0\s+auto\s*;/)
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
    expect(originalRule).toMatch(/--card-cost-digit-y:\s*0\s*;/)

    const transformedInternalRules = [...cardCss.matchAll(/(\.card__[^{]+)\{([^}]*)\}/g)]
      .filter(([, , body]) => /\btransform\s*:/.test(body))
      .map(([, selector]) => selector.trim())
    expect(transformedInternalRules).toEqual(['.card__cost-n'])
  })

  it('locks reduced cost circles to one grid column without glowing or bolding the current digit', () => {
    const stackRule = cssRule('.card__cost-stack')
    const currentDigitRule = cssRule('.card__cost--current .card__cost-n')
    const currentCircleRule = cssRule('.card .card__cost--current')
    const originalCircleRule = cssRule('.card .card__cost--orig')

    expect(stackRule).toMatch(/\bdisplay:\s*grid\s*;/)
    expect(stackRule).toMatch(/\bgrid-template-columns:\s*22px\s*;/)
    expect(stackRule).toMatch(/\bgrid-template-rows:\s*22px\s+22px\s*;/)
    expect(currentDigitRule).toMatch(/\bfont-weight:\s*400\s*;/)
    expect(currentDigitRule).toMatch(/\btext-shadow:\s*var\(--text-shadow-pixel\)\s*;/)
    expect(currentDigitRule).not.toMatch(/rgba\(241,\s*201,\s*91/)
    expect(currentCircleRule).toMatch(/\bborder-color:\s*#f1c95b\s*;/)
    expect(originalCircleRule).toMatch(/\bborder-color:\s*#888\s*;/)
    expect(originalCircleRule).toMatch(/\bbox-shadow:\s*none\s*;/)
  })

  it('marks cost length and fits two or more characters inside the fixed circle', () => {
    const { container } = render(
      <Card {...baseProps} cost={12} costOrig={123} />,
    )
    const digits = [...container.querySelectorAll('.card__cost-n')]

    expect(digits).toHaveLength(2)
    expect(digits[0]).toHaveAttribute('data-digits', '2')
    expect(digits[1]).toHaveAttribute('data-digits', '3')
    expect(cssRule(".card__cost-n[data-digits='2']")).toMatch(/\bfont-size:\s*13px\s*;/)
    expect(cssRule(".card__cost-n[data-digits='3']")).toMatch(/\bfont-size:\s*10px\s*;/)
    expect(cssRule('.card__cost')).toMatch(/\boverflow:\s*hidden\s*;/)
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
