import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ScreenTransition } from './ScreenTransition'

describe('ScreenTransition', () => {
  it('replaces screens atomically instead of mounting old and new together', () => {
    const view = render(
      <ScreenTransition transitionKey="menu"><main>menu screen</main></ScreenTransition>,
    )
    expect(screen.getByText('menu screen')).toBeInTheDocument()

    view.rerender(
      <ScreenTransition transitionKey="settings"><main>settings screen</main></ScreenTransition>,
    )
    expect(screen.queryByText('menu screen')).not.toBeInTheDocument()
    expect(screen.getByText('settings screen')).toBeInTheDocument()
    expect(view.container.querySelectorAll('.screen-transition__content')).toHaveLength(1)
  })
})
