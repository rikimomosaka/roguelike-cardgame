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
    const { container } = render(<RestScreen deck={deck} completed={false} onHeal={vi.fn()} onUpgrade={vi.fn()} onClose={vi.fn()} />)
    expect(screen.getByRole('button', { name: /^heal$/i })).toBeDefined()
    expect(screen.getByRole('button', { name: /upgrade card/i })).toBeDefined()
    const fire = container.querySelector('.rs-fire')
    const logs = container.querySelector('.rs-fire__logs')
    const flameViewport = container.querySelector('.rs-fire__flame-viewport')
    const flame = container.querySelector('.rs-fire__flame')
    expect(flame).toHaveAttribute('src', '/icons/ui/rest_flame.png?v=2')
    expect(flame).toHaveAttribute('width', '1080')
    expect(flame).toHaveAttribute('height', '180')
    expect(logs).toHaveAttribute('src', '/icons/ui/rest_logs.png')
    expect(logs).toHaveAttribute('width', '180')
    expect(logs).toHaveAttribute('height', '180')
    expect(flameViewport).toHaveAttribute('data-frame-count', '6')
    expect(flameViewport).toHaveAttribute('data-frame-width', '180')
    expect(flameViewport?.contains(flame)).toBe(true)
    expect(fire?.children[0]).toBe(logs)
    expect(fire?.children[1]).toBe(flameViewport)
    expect(screen.getByText('焚き火')).toBeInTheDocument()
    expect(screen.queryByText('休息所')).not.toBeInTheDocument()
    expect(screen.getByText('鍛造')).toBeInTheDocument()
    expect(screen.getByText('デッキの未強化カードから1枚を強化する。')).toBeInTheDocument()
    expect(screen.getByText('+カード強化×1')).toBeInTheDocument()
  })

  it('shows the authoritative relic-adjusted HP preview and correction tag', () => {
    render(
      <RestScreen
        deck={deck}
        completed={false}
        healPreview={{
          currentHpBefore: 30,
          maxHpBefore: 80,
          basePercent: 30,
          baseHealAmount: 24,
          passiveBonusAmount: 5,
          currentHpAfter: 59,
          maxHpAfter: 80,
        }}
        onHeal={vi.fn()}
        onUpgrade={vi.fn()}
        onClose={vi.fn()}
      />,
    )

    expect(screen.getByText('最大HPの30％を回復する(HP30→59)。')).toBeInTheDocument()
    expect(screen.getByText('HP+30%')).toBeInTheDocument()
    expect(screen.getByText('HP+5')).toBeInTheDocument()
  })

  it('heal button calls onHeal', async () => {
    const onHeal = vi.fn()
    render(<RestScreen deck={deck} completed={false} onHeal={onHeal} onUpgrade={vi.fn()} onClose={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: /^heal$/i }))
    await waitFor(() => expect(onHeal).toHaveBeenCalled())
  })

  it('休息処理中は選択肢と閉じるをロックして二重送信しない', async () => {
    let finish!: () => void
    const onHeal = vi.fn(() => new Promise<void>(resolve => { finish = resolve }))
    render(<RestScreen deck={deck} completed={false} onHeal={onHeal} onUpgrade={vi.fn()} onClose={vi.fn()} />)
    const heal = screen.getByRole('button', { name: 'Heal' })
    fireEvent.click(heal)
    fireEvent.click(heal)
    expect(onHeal).toHaveBeenCalledTimes(1)
    expect(screen.getByText('炎のぬくもりを受けています…')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled()
    finish()
    await waitFor(() => expect(screen.queryByText('炎のぬくもりを受けています…')).toBeNull())
  })

  it('upgrade view lists the complete deck and disables unavailable cards', async () => {
    render(<RestScreen deck={deck} completed={false} onHeal={vi.fn()} onUpgrade={vi.fn()} onClose={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: /upgrade card/i }))
    // Wait for catalog to load
    await waitFor(() => {
      const buttons = screen.queryAllByRole('button', { name: /upgrade .+ at/i })
      expect(buttons).toHaveLength(3)
      expect((buttons[0] as HTMLButtonElement).disabled).toBe(false)
      expect((buttons[1] as HTMLButtonElement).disabled).toBe(true) // already upgraded
      expect((buttons[2] as HTMLButtonElement).disabled).toBe(true) // not upgradable
    })
  })

  it('selecting upgrade card then confirming calls onUpgrade with correct index', async () => {
    const onUpgrade = vi.fn()
    render(<RestScreen deck={deck} completed={false} onHeal={vi.fn()} onUpgrade={onUpgrade} onClose={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: /upgrade card/i }))
    const pick = await screen.findByRole('button', { name: /upgrade strike at 0/i })
    // The deck is rendered before the asynchronous catalog arrives.  Wait for
    // the catalog-backed upgradable flag instead of clicking the temporarily
    // disabled placeholder button.
    await waitFor(() => expect(pick).toBeEnabled())
    fireEvent.click(pick)
    // Rendering both full-size card previews in the confirmation dialog can
    // exceed Testing Library's 1s default while the complete Vitest suite is
    // running in parallel. The interaction itself is synchronous; allow the
    // concurrent React render enough time to commit.
    const confirm = await screen.findByRole(
      'button',
      { name: /confirm upgrade/i },
      { timeout: 3_000 },
    )
    fireEvent.click(confirm)
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
