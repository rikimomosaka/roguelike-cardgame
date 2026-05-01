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
      spec: '{"rarity":1,"cardType":"Attack","cost":1,"effects":[{"action":"attack","scope":"Single","side":"Enemy","amount":6}]}',
    },
  ],
})

const sampleMeta = () => ({
  cardTypes: ['Attack', 'Skill', 'Power', 'Curse', 'Status', 'Unit'],
  rarities: [
    { value: 0, label: 'Promo' },
    { value: 1, label: 'Common' },
    { value: 2, label: 'Rare' },
    { value: 3, label: 'Epic' },
    { value: 4, label: 'Legendary' },
    { value: 5, label: 'Token' },
  ],
  effectActions: ['attack', 'block', 'buff', 'debuff', 'heal', 'draw', 'discard', 'addCard'],
  effectScopes: ['Self', 'Single', 'Random', 'All'],
  effectSides: ['Enemy', 'Ally'],
  piles: ['hand', 'draw', 'discard', 'exhaust'],
  selectModes: ['random', 'choose', 'all'],
  triggers: ['OnTurnStart', 'OnPlayCard'],
  amountSources: ['handCount', 'drawPileCount'],
  keywords: [
    { id: 'wild', name: 'ワイルド', description: '...' },
    { id: 'superwild', name: 'スーパーワイルド', description: '...' },
  ],
  statuses: [
    { id: 'weak', jp: '脱力' },
    { id: 'vulnerable', jp: '脆弱' },
  ],
})

/**
 * fetch mock を URL routing で振り分けるヘルパ。
 * /api/dev/cards → cards, /api/dev/meta → meta, それ以外は generic ok。
 */
function setupFetchMock(opts: {
  cards?: ReturnType<typeof sampleCard>[]
  meta?: ReturnType<typeof sampleMeta>
  override?: (input: RequestInfo, init?: RequestInit) => Promise<Response> | null
}) {
  const cards = opts.cards ?? [sampleCard()]
  const meta = opts.meta ?? sampleMeta()
  const calls: { url: string; init: RequestInit | undefined }[] = []
  const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(
    (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : (input as URL | Request).toString()
      calls.push({ url, init })
      const o = opts.override?.(url as RequestInfo, init) ?? null
      if (o) return o
      if (url === '/api/dev/cards' && (!init?.method || init.method === 'GET')) {
        return Promise.resolve({ ok: true, json: async () => cards } as Response)
      }
      if (url === '/api/dev/meta') {
        return Promise.resolve({ ok: true, json: async () => meta } as Response)
      }
      if (url === '/api/dev/cards/preview') {
        return Promise.resolve({
          ok: true,
          json: async () => ({ description: '[N:6] ダメージ' }),
        } as Response)
      }
      // generic fallback
      return Promise.resolve({ ok: true, json: async () => ({}), text: async () => '' } as Response)
    },
  )
  return { fetchMock, calls }
}

describe('DevCardsScreen', () => {
  beforeEach(() => {
    vi.stubGlobal('confirm', vi.fn(() => true))
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('shows loading then renders card list', async () => {
    setupFetchMock({})
    render(<DevCardsScreen />)
    expect(screen.getByText(/読込中/)).toBeInTheDocument()
    await waitFor(() =>
      expect(screen.getAllByText(/strike/).length).toBeGreaterThan(0),
    )
  })

  it('renders error state on cards fetch failure', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(
      (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString()
        if (url === '/api/dev/cards') return Promise.reject(new Error('boom'))
        return Promise.resolve({ ok: true, json: async () => sampleMeta() } as Response)
      },
    )
    render(<DevCardsScreen />)
    await waitFor(() => expect(screen.getByText(/エラー/)).toBeInTheDocument())
  })

  it('shows structured form (rarity / cardType / cost) and 4 action buttons', async () => {
    setupFetchMock({})
    render(<DevCardsScreen />)
    await waitFor(() =>
      expect(screen.getByLabelText(/card rarity/i)).toBeInTheDocument(),
    )
    expect(screen.getByLabelText(/card type/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/^card cost$/i)).toBeInTheDocument()

    expect(screen.getByRole('button', { name: /v2 として保存/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /このバージョンを有効化/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /ソースに昇格/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /このバージョンを削除/ })).toBeInTheDocument()
  })

  it('shows Delete Card button + opens delete modal', async () => {
    setupFetchMock({})
    render(<DevCardsScreen />)
    const delBtn = await waitFor(() =>
      screen.getByRole('button', { name: /delete card/i }),
    )
    fireEvent.click(delBtn)
    expect(
      await waitFor(() => screen.getByRole('dialog', { name: /Delete Card/i })),
    ).toBeInTheDocument()
    // checkbox + confirm button が出る
    expect(screen.getByLabelText(/also delete base file/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /削除を確定/ })).toBeInTheDocument()
  })

  it('calls DELETE /api/dev/cards/{id} when confirmed', async () => {
    const { fetchMock } = setupFetchMock({})
    render(<DevCardsScreen />)
    const delBtn = await waitFor(() =>
      screen.getByRole('button', { name: /delete card/i }),
    )
    fireEvent.click(delBtn)
    fireEvent.click(
      await waitFor(() => screen.getByRole('button', { name: /削除を確定/ })),
    )
    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        (c) =>
          typeof c[0] === 'string' &&
          (c[0] as string).startsWith('/api/dev/cards/strike') &&
          (c[1] as RequestInit | undefined)?.method === 'DELETE',
      )
      expect(call).toBeTruthy()
    })
  })

  it('calls POST /api/dev/cards/{id}/versions when Save clicked', async () => {
    const { fetchMock } = setupFetchMock({})
    render(<DevCardsScreen />)
    await waitFor(() =>
      expect(screen.getByLabelText(/card rarity/i)).toBeInTheDocument(),
    )
    fireEvent.click(screen.getByRole('button', { name: /v2 として保存/ }))
    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        (c) =>
          typeof c[0] === 'string' &&
          (c[0] as string).includes('/versions') &&
          (c[1] as RequestInit | undefined)?.method === 'POST',
      )
      expect(call).toBeTruthy()
    })
  })

  it('calls promote endpoint when Promote clicked', async () => {
    const { fetchMock } = setupFetchMock({})
    render(<DevCardsScreen />)
    await waitFor(() =>
      expect(screen.getByLabelText(/card rarity/i)).toBeInTheDocument(),
    )
    fireEvent.click(screen.getByRole('button', { name: /ソースに昇格/ }))
    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        (c) =>
          typeof c[0] === 'string' && (c[0] as string).includes('/promote'),
      )
      expect(call).toBeTruthy()
    })
  })

  // Phase 10.5.K: New Card modal
  it('opens New Card modal when "+ 新規カード" button clicked', async () => {
    setupFetchMock({})
    render(<DevCardsScreen />)
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /\+ 新規カード/ })).toBeInTheDocument(),
    )
    fireEvent.click(screen.getByRole('button', { name: /\+ 新規カード/ }))
    expect(screen.getByRole('dialog', { name: /New Card/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/new card id/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/new card name/i)).toBeInTheDocument()
  })

  it('creates new card via modal and calls POST /api/dev/cards', async () => {
    const { fetchMock } = setupFetchMock({})
    render(<DevCardsScreen />)
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /\+ 新規カード/ })).toBeInTheDocument(),
    )
    fireEvent.click(screen.getByRole('button', { name: /\+ 新規カード/ }))
    fireEvent.change(screen.getByLabelText(/new card id/i), {
      target: { value: 'new_test' },
    })
    fireEvent.change(screen.getByLabelText(/new card name/i), {
      target: { value: 'テスト' },
    })
    fireEvent.click(screen.getByRole('button', { name: /^作成$/ }))
    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        (c) =>
          typeof c[0] === 'string' &&
          c[0] === '/api/dev/cards' &&
          (c[1] as RequestInit | undefined)?.method === 'POST',
      )
      expect(call).toBeTruthy()
    })
  })
})
