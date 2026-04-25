import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { AchievementsScreen } from './AchievementsScreen'
import * as bestiaryApi from '../api/bestiary'
import * as historyApi from '../api/history'
import type { BestiaryDto, RunResultDto } from '../api/types'

const emptyBestiary: BestiaryDto = {
  schemaVersion: 1,
  discoveredCardBaseIds: ['strike'],
  discoveredRelicIds: [],
  discoveredPotionIds: [],
  encounteredEnemyIds: [],
  allKnownCardBaseIds: ['defend', 'strike', 'zap'],
  allKnownRelicIds: ['burning_blood'],
  allKnownPotionIds: ['fire_potion'],
  allKnownEnemyIds: ['jaw_worm'],
}

const oneRun: RunResultDto = {
  schemaVersion: 2, accountId: 'a', runId: 'r1', outcome: 'Cleared',
  actReached: 3, nodesVisited: 15, playSeconds: 900, characterId: 'default',
  finalHp: 40, finalMaxHp: 80, finalGold: 200,
  finalDeck: [{ id: 'strike', upgraded: false }], finalRelics: ['burning_blood'],
  endedAtUtc: '2026-04-20T12:00:00Z',
  seenCardBaseIds: ['strike'], acquiredRelicIds: ['burning_blood'],
  acquiredPotionIds: [], encounteredEnemyIds: ['jaw_worm'],
}

describe('AchievementsScreen', () => {
  beforeEach(() => {
    vi.spyOn(bestiaryApi, 'getBestiary').mockResolvedValue(emptyBestiary)
    vi.spyOn(historyApi, 'getHistory').mockResolvedValue([oneRun])
  })

  afterEach(() => { vi.restoreAllMocks() })

  it('fetches bestiary and history on mount in parallel', async () => {
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    await waitFor(() => expect(bestiaryApi.getBestiary).toHaveBeenCalledWith('a'))
    expect(historyApi.getHistory).toHaveBeenCalledWith('a')
  })

  it('cards tab shows discovered and undiscovered with count header', async () => {
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    await screen.findByText(/1 \/ 3 発見/)
    expect(screen.getByText(/strike \(strike\)/)).toBeInTheDocument()
    expect(screen.getByText(/\?\?\?.*\(defend\)/)).toBeInTheDocument()
  })

  it('history tab shows empty message when no history', async () => {
    vi.spyOn(historyApi, 'getHistory').mockResolvedValueOnce([])
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    await screen.findByRole('tab', { name: /履歴/ })
    fireEvent.click(screen.getByRole('tab', { name: /履歴/ }))
    await screen.findByText('履歴なし')
  })

  it('history row expands on click to show acquired/encountered sets', async () => {
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    await screen.findByRole('tab', { name: /履歴/ })
    fireEvent.click(screen.getByRole('tab', { name: /履歴/ }))
    const row = await screen.findByText(/Cleared.*Act3/)
    fireEvent.click(row)
    expect(screen.getByText(/jaw_worm/)).toBeInTheDocument()
    expect(screen.getByText(/burning_blood/)).toBeInTheDocument()
  })

  it('back button fires onBack', async () => {
    const onBack = vi.fn()
    render(<AchievementsScreen accountId="a" onBack={onBack} />)
    const back = await screen.findByRole('button', { name: /メニューへ戻る/ })
    fireEvent.click(back)
    expect(onBack).toHaveBeenCalled()
  })
})
