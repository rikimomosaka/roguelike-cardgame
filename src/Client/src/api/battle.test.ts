import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  endTurn,
  getBattleReplay,
  setBattleReady,
  useTacticalSkill,
  useUltimateSkill,
} from './battle'
import type { BattleEventKind } from './types'

const characterSkillEventKinds = [
  'UseCharacterSkill',
  'ComboGaugeChanged',
  'ModifyCardCost',
] satisfies BattleEventKind[]

describe('combo skill battle api', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it.each([
    ['tactical', useTacticalSkill, '/api/v1/runs/current/battle/use-tactical-skill'],
    ['ultimate', useUltimateSkill, '/api/v1/runs/current/battle/use-ultimate-skill'],
  ] as const)('POSTs the %s skill and returns its action response unchanged', async (
    _kind,
    invoke,
    expectedPath,
  ) => {
    const responsePayload = {
      state: { comboGauge: 4 },
      steps: characterSkillEventKinds.map(kind => ({
        event: { kind },
        snapshotAfter: { comboGauge: 4 },
      })),
    }
    fetchMock.mockResolvedValue(new Response(JSON.stringify(responsePayload), { status: 200 }))

    const result = await invoke('alice')

    expect(result).toEqual(responsePayload)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(expectedPath)
    expect(init.method).toBe('POST')
    expect(new Headers(init.headers).get('X-Account-Id')).toBe('alice')
    expect(JSON.parse(init.body as string)).toEqual({})
  })
})

describe('participant battle api', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('posts an optimistic Ready toggle with the participant id', async () => {
    const payload = { state: { actionRevision: 8 }, steps: [] }
    fetchMock.mockResolvedValue(new Response(JSON.stringify(payload), { status: 200 }))

    await setBattleReady('alice', {
      participantId: 'participant 1',
      isReady: true,
      expectedRevision: 7,
    })

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/v1/runs/current/battle/set-ready')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({
      participantId: 'participant 1',
      isReady: true,
      expectedRevision: 7,
    })
  })

  it('posts the legacy end-turn command with a retry-safe envelope', async () => {
    const payload = { state: { actionRevision: 9 }, steps: [] }
    fetchMock.mockResolvedValue(new Response(JSON.stringify(payload), { status: 200 }))

    await endTurn('alice', {
      commandId: 'end-turn:one',
      expectedRevision: 8,
    })

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/v1/runs/current/battle/end-turn')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({
      commandId: 'end-turn:one',
      expectedRevision: 8,
    })
  })

  it('encodes replay filters and keeps the request read-only', async () => {
    const payload = { entries: [], feedState: 'live', latestSequence: 12 }
    fetchMock.mockResolvedValue(new Response(JSON.stringify(payload), { status: 200 }))

    await getBattleReplay('alice', {
      participantId: 'participant 1',
      actorInstanceId: 'wisp/1',
      turn: 3,
      afterSequence: 9,
      throughSequence: 12,
    })

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(
      '/api/v1/runs/current/battle/replay?participantId=participant+1&actorInstanceId=wisp%2F1&turn=3&afterSequence=9&throughSequence=12',
    )
    expect(init.method).toBe('GET')
    expect(init.body).toBeUndefined()
  })
})
