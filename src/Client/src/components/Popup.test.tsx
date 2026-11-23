import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { Popup } from './Popup'

describe('Popup accessibility and presence', () => {
  it('moves focus inside, traps Tab, and restores the previous focus', async () => {
    const view = render(
      <div>
        <button type="button">outside</button>
        <Popup open={false} title="設定">
          <button type="button">first</button>
          <button type="button">last</button>
        </Popup>
      </div>,
    )
    const outside = screen.getByRole('button', { name: 'outside' })
    outside.focus()

    view.rerender(
      <div>
        <button type="button">outside</button>
        <Popup open title="設定">
          <button type="button">first</button>
          <button type="button">last</button>
        </Popup>
      </div>,
    )

    const first = await screen.findByRole('button', { name: 'first' })
    const last = screen.getByRole('button', { name: 'last' })
    await waitFor(() => expect(first).toHaveFocus())
    last.focus()
    fireEvent.keyDown(document, { key: 'Tab' })
    expect(first).toHaveFocus()

    view.rerender(
      <div>
        <button type="button">outside</button>
        <Popup open={false} title="設定">
          <button type="button">first</button>
          <button type="button">last</button>
        </Popup>
      </div>,
    )
    expect(screen.queryByRole('dialog', { name: '設定' })).not.toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'outside' })).toHaveFocus())
  })

  it('only lets the topmost popup consume Escape', () => {
    const closeLower = vi.fn()
    const closeUpper = vi.fn()
    render(
      <>
        <Popup open title="lower" onClose={closeLower}><span>lower body</span></Popup>
        <Popup open title="upper" onClose={closeUpper}><span>upper body</span></Popup>
      </>,
    )

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(closeLower).not.toHaveBeenCalled()
    expect(closeUpper).toHaveBeenCalledOnce()
  })

  it('keeps the exit visual mounted but immediately removes dialog semantics', () => {
    const view = render(<Popup open title="確認"><span>body</span></Popup>)
    view.rerender(<Popup open={false} title="確認"><span>body</span></Popup>)

    expect(screen.queryByRole('dialog', { name: '確認' })).not.toBeInTheDocument()
    expect(view.container.querySelector('.popup[data-state="exiting"]')).toBeInTheDocument()
  })
})
