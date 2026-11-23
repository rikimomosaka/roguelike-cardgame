import { StrictMode } from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { RunResultDto, RunSnapshotDto } from './api/types'
import App from './App'

const audio = vi.hoisted(() => ({
  playBgm: vi.fn(),
  playSe: vi.fn(async (cue: { id: string }) => cue.id.length > 0),
  stopBgm: vi.fn(),
}))
const account = vi.hoisted(() => ({ logout: vi.fn() }))

vi.mock('./api/accounts', () => ({
  getAccount: vi.fn(async () => ({ id: 'alice' })),
}))

vi.mock('./context/AccountContext', () => ({
  useAccount: () => ({ accountId: 'alice', logout: account.logout }),
}))

vi.mock('./audio', () => ({
  useAudio: () => audio,
}))

const snapshot = {} as RunSnapshotDto
const gameOverResult: RunResultDto = {
  schemaVersion: 1,
  accountId: 'alice',
  runId: 'run-game-over-audio',
  outcome: 'GameOver',
  actReached: 1,
  nodesVisited: 3,
  playSeconds: 42,
  characterId: 'default',
  finalHp: 0,
  finalMaxHp: 60,
  finalGold: 12,
  finalDeck: [],
  finalRelics: [],
  endedAtUtc: '2026-08-25T00:00:00Z',
  seenCardBaseIds: [],
  acquiredRelicIds: [],
  acquiredPotionIds: [],
  encounteredEnemyIds: [],
  journeyLog: [],
}

vi.mock('./screens/MainMenuScreen', () => ({
  MainMenuScreen: ({ onStartRun }: { onStartRun: (value: RunSnapshotDto) => void }) => (
    <button type="button" data-audio-ui="off" onClick={() => onStartRun(snapshot)}>
      start mocked run
    </button>
  ),
}))

vi.mock('./screens/MapScreen', () => ({
  MapScreen: ({
    onRunFinished,
  }: {
    onRunFinished: (result: RunResultDto, value: RunSnapshotDto) => void
  }) => (
    <button
      type="button"
      data-audio-ui="off"
      onClick={() => onRunFinished(gameOverResult, snapshot)}
    >
      finish mocked run
    </button>
  ),
}))

vi.mock('./screens/RunResultScreen', () => ({
  RunResultScreen: ({
    result,
    onReturnToMenu,
  }: {
    result: RunResultDto
    onReturnToMenu: () => void
  }) => (
    <div>
      <span>{result.outcome}</span>
      <button type="button" data-audio-ui="off" onClick={onReturnToMenu}>
        return mocked menu
      </button>
    </div>
  ),
}))

describe('App run-result audio ownership', () => {
  beforeEach(() => {
    audio.playBgm.mockClear()
    audio.playSe.mockClear()
    audio.stopBgm.mockClear()
  })

  it('plays one game-over stinger for a result even through StrictMode and revisiting it', async () => {
    render(<StrictMode><App /></StrictMode>)

    fireEvent.click(await screen.findByRole('button', { name: 'start mocked run' }))
    fireEvent.click(await screen.findByRole('button', { name: 'finish mocked run' }))

    await waitFor(() => {
      expect(audio.playSe.mock.calls.filter(([cue]) => cue.id === 'game-over-stinger'))
        .toHaveLength(1)
    })

    fireEvent.click(screen.getByRole('button', { name: 'return mocked menu' }))
    fireEvent.click(await screen.findByRole('button', { name: 'start mocked run' }))
    fireEvent.click(await screen.findByRole('button', { name: 'finish mocked run' }))

    expect(audio.playSe.mock.calls.filter(([cue]) => cue.id === 'game-over-stinger'))
      .toHaveLength(1)
  })
})
