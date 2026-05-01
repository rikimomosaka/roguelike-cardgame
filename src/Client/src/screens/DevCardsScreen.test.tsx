import { describe, expect, it, vi, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { DevCardsScreen } from './DevCardsScreen'

describe('DevCardsScreen', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows loading then renders card list', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      json: async () => [
        {
          id: 'strike',
          name: 'ストライク',
          displayName: null,
          activeVersion: 'v1',
          versions: [
            {
              version: 'v1',
              createdAt: null,
              label: 'original',
              spec: '{"cardType":"Attack","cost":1}',
            },
          ],
        },
      ],
    } as Response)

    render(<DevCardsScreen />)
    expect(screen.getByText(/Loading/i)).toBeInTheDocument()
    // 一覧 li の中で id 表示を確認 (`strike (v1)` のような文字列の一部)
    await waitFor(() => expect(screen.getAllByText(/strike/).length).toBeGreaterThan(0))
  })

  it('renders error state on fetch failure', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new Error('boom'))
    render(<DevCardsScreen />)
    await waitFor(() => expect(screen.getByText(/Error/)).toBeInTheDocument())
  })
})
