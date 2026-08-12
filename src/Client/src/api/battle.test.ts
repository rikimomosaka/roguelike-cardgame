import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useTacticalSkill, useUltimateSkill } from './battle'
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
