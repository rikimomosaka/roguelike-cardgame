import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { resetCardCatalogCacheForTests } from '../api/catalog'
import type { CardInstanceDto } from '../api/types'
import { RestScreen } from './RestScreen'

const fetchMock = vi.fn()

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
  resetCardCatalogCacheForTests()
  // Mock card catalog fetch
  fetchMock.mockResolvedValue(new Response(JSON.stringify({
    strike: { id: 'strike', name: 'Strike', displayName: null, rarity: 0, cardType: 'Attack', cost: 1, upgradable: true },
    dazed: { id: 'dazed', name: 'Dazed', displayName: null, rarity: 3, cardType: 'Status', cost: null, upgradable: false },
  }), { status: 200 }))
})

const deck: CardInstanceDto[] = [
  { id: 'strike', upgraded: false },
  { id: 'strike', upgraded: true },
  { id: 'dazed', upgraded: false },
]

describe('RestScreen', () => {
  it('default view shows heal and upgrade buttons', () => {
    render(<RestScreen deck={deck} completed={false} onHeal={vi.fn()} onUpgrade={vi.fn()} onClose={vi.fn()} />)
    expect(screen.getByRole('button', { name: /^heal$/i })).toBeDefined()
    expect(screen.getByRole('button', { name: /upgrade card/i })).toBeDefined()
  })

  it('heal button calls onHeal', async () => {
    const onHeal = vi.fn()
    render(<RestScreen deck={deck} completed={false} onHeal={onHeal} onUpgrade={vi.fn()} onClose={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: /^heal$/i }))
    await waitFor(() => expect(onHeal).toHaveBeenCalled())
  })

  it('upgrade view lists only upgradable cards', async () => {
    render(<RestScreen deck={deck} completed={false} onHeal={vi.fn()} onUpgrade={vi.fn()} onClose={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: /upgrade card/i }))
    // Wait for catalog to load
    await waitFor(() => {
      const buttons = screen.queryAllByRole('button', { name: /upgrade .+ at/i })
      expect(buttons).toHaveLength(1)  // Only deck[0] is upgradable
    })
  })

  it('selecting upgrade card calls onUpgrade with correct index', async () => {
    const onUpgrade = vi.fn()
    render(<RestScreen deck={deck} completed={false} onHeal={vi.fn()} onUpgrade={onUpgrade} onClose={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: /upgrade card/i }))
    const btn = await screen.findByRole('button', { name: /upgrade strike at 0/i })
    fireEvent.click(btn)
    await waitFor(() => expect(onUpgrade).toHaveBeenCalledWith(0))
  })

  it('close button is always visible (even when not completed)', () => {
    render(<RestScreen deck={deck} completed={false} onHeal={vi.fn()} onUpgrade={vi.fn()} onClose={vi.fn()} />)
    expect(screen.getByRole('button', { name: /^close$/i })).toBeDefined()
  })

  it('close button calls onClose', () => {
    const onClose = vi.fn()
    render(<RestScreen deck={deck} completed={false} onHeal={vi.fn()} onUpgrade={vi.fn()} onClose={onClose} />)
    fireEvent.click(screen.getByRole('button', { name: /^close$/i }))
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('completed=true disables both heal and upgrade buttons but keeps close usable', () => {
    render(<RestScreen deck={deck} completed={true} onHeal={vi.fn()} onUpgrade={vi.fn()} onClose={vi.fn()} />)
    const heal = screen.getByRole('button', { name: /^heal$/i }) as HTMLButtonElement
    const upgrade = screen.getByRole('button', { name: /upgrade card/i }) as HTMLButtonElement
    const close = screen.getByRole('button', { name: /^close$/i }) as HTMLButtonElement
    expect(heal.disabled).toBe(true)
    expect(upgrade.disabled).toBe(true)
    expect(close.disabled).toBe(false)
  })
})
