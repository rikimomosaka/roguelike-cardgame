import { describe, expect, it, vi, afterEach, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { DevCardsScreen } from './DevCardsScreen'

const sampleCard = (activeVersion = 'v1') => ({
  id: 'strike',
  name: 'ストライク',
  displayName: null,
  activeVersion,
  versions: [
    {
      version: 'v1',
      createdAt: null,
      label: 'original',
      spec: '{"cardType":"Attack","cost":1,"effects":[]}',
    },
  ],
})

describe('DevCardsScreen', () => {
  beforeEach(() => {
    // confirm() を強制 true (Promote/Delete 用)
    vi.stubGlobal('confirm', vi.fn(() => true))
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('shows loading then renders card list', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      json: async () => [sampleCard()],
    } as Response)

    render(<DevCardsScreen />)
    expect(screen.getByText(/Loading/i)).toBeInTheDocument()
    await waitFor(() => expect(screen.getAllByText(/strike/).length).toBeGreaterThan(0))
  })

  it('renders error state on fetch failure', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new Error('boom'))
    render(<DevCardsScreen />)
    await waitFor(() => expect(screen.getByText(/Error/)).toBeInTheDocument())
  })

  it('shows editor with textarea + 4 buttons after card loads', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      json: async () => [sampleCard()],
    } as Response)

    render(<DevCardsScreen />)
    await waitFor(() => expect(screen.getByLabelText(/card spec editor/i)).toBeInTheDocument())

    expect(screen.getByRole('button', { name: /Save as v2/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Set as active/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Promote to source/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Delete version/i })).toBeInTheDocument()
  })

  it('calls POST /api/dev/cards/{id}/versions when Save clicked', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      // 1st: initial load
      .mockResolvedValueOnce({ ok: true, json: async () => [sampleCard()] } as Response)
      // 2nd: save POST
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ newVersion: 'v2' }),
      } as Response)
      // 3rd: reload after mutation
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [sampleCard('v2')],
      } as Response)

    render(<DevCardsScreen />)
    await waitFor(() => expect(screen.getByLabelText(/card spec editor/i)).toBeInTheDocument())

    const saveBtn = screen.getByRole('button', { name: /Save as v2/i })
    fireEvent.click(saveBtn)

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        (c) => typeof c[0] === 'string' && (c[0] as string).includes('/versions'),
      )
      expect(call).toBeTruthy()
    })
    const saveCall = fetchMock.mock.calls.find(
      (c) => typeof c[0] === 'string' && (c[0] as string).includes('/versions'),
    )!
    expect((saveCall[1] as RequestInit | undefined)?.method).toBe('POST')
  })

  it('shows JSON parse error when textarea contents invalid', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      json: async () => [sampleCard()],
    } as Response)

    render(<DevCardsScreen />)
    const ta = (await waitFor(() =>
      screen.getByLabelText(/card spec editor/i),
    )) as HTMLTextAreaElement
    fireEvent.change(ta, { target: { value: 'not-valid-json {{{' } })

    fireEvent.click(screen.getByRole('button', { name: /Save as v2/i }))

    await waitFor(() => expect(screen.getByText(/Invalid JSON/)).toBeInTheDocument())
  })

  it('calls promote endpoint when Promote clicked', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({ ok: true, json: async () => [sampleCard()] } as Response)
      .mockResolvedValueOnce({ ok: true, text: async () => '' } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => [sampleCard()] } as Response)

    render(<DevCardsScreen />)
    await waitFor(() => expect(screen.getByLabelText(/card spec editor/i)).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: /Promote to source/i }))

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        (c) => typeof c[0] === 'string' && (c[0] as string).includes('/promote'),
      )
      expect(call).toBeTruthy()
    })
  })
})
