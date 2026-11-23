import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { AchievementsScreen } from './AchievementsScreen'
import * as bestiaryApi from '../api/bestiary'
import * as catalogApi from '../api/catalog'
import * as historyApi from '../api/history'
import type { BestiaryDto, RunResultDto } from '../api/types'
import { TooltipHost } from '../components/Tooltip'

const emptyBestiary: BestiaryDto = {
  schemaVersion: 1,
  discoveredCardBaseIds: ['strike', 'starter_summon_3'],
  discoveredRelicIds: [],
  discoveredPotionIds: [],
  encounteredEnemyIds: [],
  allKnownCardBaseIds: ['defend', 'strike', 'zap', 'starter_summon_3'],
  allKnownRelicIds: ['burning_blood'],
  allKnownPotionIds: ['fire_potion'],
  allKnownEnemyIds: ['jaw_worm'],
  playerCharacters: [{
    id: 'default', name: '見習い冒険者', kind: 'player', maxHp: 80,
    characterClass: 'Striker',
    imageId: 'player_stand', heightTier: 5, lifetimeTurns: null, isDiscovered: true,
    description: '駆け出しの冒険者。', traitDescription: '特性は未実装です。',
    actions: [
      { category: '戦技', name: '拡散の構え', description: '自身に[S:strength][N:1]と[S:omnistrike][N:1]を付与。', energyCost: 0, comboGaugeCost: 3 },
      { category: '奥義', name: '全力解放', description: '[N:3]枚引く。', energyCost: 0, comboGaugeCost: 6 },
    ],
    summonCards: [],
  }],
  allyCharacters: [{
    id: 'wisp', name: 'ウィスプ', kind: 'ally', maxHp: 30,
    imageId: 'wisp', heightTier: 3, lifetimeTurns: 3, isDiscovered: true,
    description: '', traitDescription: '',
    actions: [{ category: '毎ターンの行動', name: '行動', description: '敵単体に[N:4]アタック。', energyCost: 0, comboGaugeCost: 0 }],
    summonCards: [{
      id: 'starter_summon_3', name: 'ウィスプ召喚', rarity: 1, cardType: 'Unit',
      cost: 3, description: '[K:wild]\n[U:wisp]を召喚。', upgradedDescription: null,
    }],
  }],
}

const oneRun: RunResultDto = {
  schemaVersion: 2, accountId: 'a', runId: 'r1', outcome: 'Cleared',
  actReached: 3, nodesVisited: 15, playSeconds: 900, characterId: 'default',
  finalHp: 40, finalMaxHp: 80, finalGold: 200,
  finalDeck: [{ id: 'strike', upgraded: false }], finalRelics: ['burning_blood'],
  endedAtUtc: '2026-04-20T12:00:00Z',
  seenCardBaseIds: ['strike'], acquiredRelicIds: ['burning_blood'],
  acquiredPotionIds: [], encounteredEnemyIds: ['jaw_worm'],
  journeyLog: [],
}

describe('AchievementsScreen', () => {
  beforeEach(() => {
    vi.spyOn(bestiaryApi, 'getBestiary').mockResolvedValue(emptyBestiary)
    vi.spyOn(historyApi, 'getHistory').mockResolvedValue([oneRun])
    catalogApi.resetCardCatalogCacheForTests()
    catalogApi.resetRelicCatalogCacheForTests()
    catalogApi.resetPotionCatalogCacheForTests()
    vi.spyOn(catalogApi, 'getCardCatalog').mockResolvedValue({
      defend: {
        id: 'defend', name: '防御', displayName: null, rarity: 1, cardType: 'Skill',
        cost: 1, upgradedCost: 1, upgradable: true, description: '自身にブロックを得る。',
        upgradedDescription: null, poolClass: 'Universal',
      },
      strike: {
        id: 'strike', name: 'ストライク', displayName: null, rarity: 1, cardType: 'Attack',
        cost: 1, upgradedCost: 1, upgradable: true, description: '敵単体にアタック。',
        upgradedDescription: null, poolClass: 'Striker',
      },
      zap: {
        id: 'zap', name: '気合溜め', displayName: null, rarity: 1, cardType: 'Power',
        cost: 1, upgradedCost: 1, upgradable: true, description: '自身を強化する。',
        upgradedDescription: null, poolClass: 'Guardian',
      },
      starter_summon_3: {
        id: 'starter_summon_3', name: 'ウィスプ召喚', displayName: null,
        rarity: 1, cardType: 'Unit', cost: 3, upgradedCost: 3, upgradable: true,
        description: '[K:wild]\n[U:wisp]を召喚。', upgradedDescription: null,
      },
    })
    vi.spyOn(catalogApi, 'getRelicCatalog').mockResolvedValue({
      burning_blood: {
        id: 'burning_blood', name: '燃える血', rarity: 'Common',
        description: '戦闘終了時に回復する。', effectText: '戦闘終了時に回復する。', flavor: '',
      },
    })
    vi.spyOn(catalogApi, 'getPotionCatalog').mockResolvedValue({
      fire_potion: {
        id: 'fire_potion', name: '火炎ポーション', rarity: 1,
        usableOutsideBattle: false, description: '敵単体にダメージ。',
      },
    })
    vi.spyOn(catalogApi, 'fetchEnemyCatalog').mockResolvedValue({
      jaw_worm: {
        id: 'jaw_worm', name: 'アギトムシ', imageId: 'jaw_worm', hp: 42,
        initialMoveId: 'chomp', heightTier: 3,
        attack: 7, defense: 5, energyPerTurn: 1, drawPerTurn: 3,
        act: 1, tier: 'Weak', description: '顎で噛みつく虫。',
        deck: [{ cardId: 'strike', count: 2, upgraded: false, priority: 0 }],
        openingActions: [],
      },
    })
  })

  afterEach(() => { vi.restoreAllMocks() })

  it('fetches bestiary and history on mount in parallel', async () => {
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    await waitFor(() => expect(bestiaryApi.getBestiary).toHaveBeenCalledWith('a'))
    expect(historyApi.getHistory).toHaveBeenCalledWith('a')
  })

  it('characters tab separates player and summoned allies', async () => {
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    expect(await screen.findByRole('tab', { name: /キャラクター/ })).toHaveAttribute(
      'aria-selected', 'true')
    expect(screen.getByText('見習い冒険者')).toBeInTheDocument()
    expect(screen.getByText('特性は未実装です。')).toBeInTheDocument()
    expect(screen.getByText('戦技：拡散の構え')).toBeInTheDocument()
    expect(screen.getByText('奥義：全力解放')).toBeInTheDocument()
    expect(screen.getByText('キャラクター説明')).toBeInTheDocument()
    expect(screen.queryByText(/strike|defend/)).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: '味方' }))
    expect(await screen.findByText('ウィスプ')).toBeInTheDocument()
    expect(screen.getByText('毎ターンの行動内容')).toBeInTheDocument()
    expect(screen.getByText('ウィスプ召喚')).toBeInTheDocument()
    expect(screen.queryByText(/召喚寿命/)).not.toBeInTheDocument()
  })

  it('places the card archive between characters and relics and filters summon cards', async () => {
    const { container } = render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    await screen.findByRole('tab', { name: /キャラクター/ })

    const topLabels = Array.from(
      container.querySelectorAll('.achievements__tabs > .achievements__tab'),
      element => element.textContent?.replace(/\s+/g, ' ').trim(),
    )
    expect(topLabels[0]).toMatch(/^キャラクター/)
    expect(topLabels[1]).toMatch(/^カード/)
    expect(topLabels[2]).toMatch(/^レリック/)

    fireEvent.click(screen.getByRole('tab', { name: /^カード/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'サモン' }))
    expect(await screen.findByText('ウィスプ召喚')).toBeInTheDocument()
    expect(screen.queryByText(/starter_summon_3/)).not.toBeInTheDocument()
  })

  it('filters cards by exact pool membership and combines it with the type filter', async () => {
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    fireEvent.click(await screen.findByRole('tab', { name: /^カード/ }))
    await screen.findByText('ストライク')

    const poolFilters = screen.getByRole('group', { name: 'カード所属で絞り込み' })
    fireEvent.click(within(poolFilters).getByRole('button', { name: 'ストライカー' }))
    expect(screen.getByText('ストライク')).toBeInTheDocument()
    expect(screen.queryByText('防御')).not.toBeInTheDocument()

    fireEvent.click(within(poolFilters).getByRole('button', { name: 'ユニバーサル' }))
    expect(screen.getByText('防御')).toBeInTheDocument()
    expect(screen.queryByText('ストライク')).not.toBeInTheDocument()

    fireEvent.click(within(poolFilters).getByRole('button', { name: 'プール外' }))
    expect(screen.getByText('ウィスプ召喚')).toBeInTheDocument()
    const typeFilters = screen.getByRole('group', { name: 'カードタイプで絞り込み' })
    fireEvent.click(within(typeFilters).getByRole('button', { name: 'スキル' }))
    expect(screen.getByText('該当するカードはありません。')).toBeInTheDocument()
  })

  it('カードを名称と発見状態で絞り込める', async () => {
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    fireEvent.click(await screen.findByRole('tab', { name: /^カード/ }))
    await screen.findByText('ストライク')
    fireEvent.change(screen.getByRole('searchbox', { name: 'カードを検索' }), { target: { value: 'ストライク' } })
    expect(screen.getByText('ストライク')).toBeInTheDocument()
    expect(screen.queryByText('ウィスプ召喚')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '未発見' }))
    expect(screen.getByText('該当するカードはありません。')).toBeInTheDocument()
  })

  it('filters player characters by class and hides that filter for allies', async () => {
    vi.spyOn(bestiaryApi, 'getBestiary').mockResolvedValueOnce({
      ...emptyBestiary,
      playerCharacters: [...emptyBestiary.playerCharacters, {
        id: 'guardian', name: '鉄衛騎士', kind: 'player', maxHp: 96,
        characterClass: 'Guardian',
        imageId: null, heightTier: 5, lifetimeTurns: null, isDiscovered: true,
        description: '味方を守る騎士。', traitDescription: 'ブロックを維持する。',
        actions: [], summonCards: [],
      }],
    })
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    await screen.findByText('見習い冒険者')

    const classFilters = screen.getByRole('group', { name: 'キャラクタークラスで絞り込み' })
    fireEvent.click(within(classFilters).getByRole('button', { name: 'ガーディアン' }))
    expect(screen.getByText('鉄衛騎士')).toBeInTheDocument()
    expect(screen.queryByText('見習い冒険者')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: '味方' }))
    expect(screen.queryByRole('group', { name: 'キャラクタークラスで絞り込み' })).not.toBeInTheDocument()
    expect(screen.getByText('ウィスプ')).toBeInTheDocument()
  })

  it('obscures undiscovered artwork without replacing it or exposing hover details', async () => {
    const { container } = render(
      <TooltipHost>
        <AchievementsScreen accountId="a" onBack={() => { }} />
      </TooltipHost>,
    )
    fireEvent.click(await screen.findByRole('tab', { name: /^カード/ }))

    await waitFor(() => {
      expect(container.querySelectorAll('.achievements__card-cell.is-undiscovered')).toHaveLength(2)
    })
    const hiddenCard = container.querySelector('.achievements__card-cell.is-undiscovered .card')!
    expect(screen.getAllByRole('img', { name: '未発見カード' })).toHaveLength(2)
    expect(hiddenCard).toHaveAttribute('aria-hidden', 'true')
    expect(hiddenCard).not.toHaveClass('is-locked')
    expect(hiddenCard.querySelector('.card__name')?.textContent).not.toMatch(/^\?/)
    fireEvent.mouseEnter(hiddenCard)
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: /^レリック/ }))
    await waitFor(() => {
      const image = container.querySelector(
        '.achievements__tile-cell.is-locked img[src="/icons/relics/burning_blood.png"]',
      )
      expect(image).not.toBeNull()
    })
    const relicImage = container.querySelector(
      '.achievements__tile-cell.is-locked img[src="/icons/relics/burning_blood.png"]',
    )!
    fireEvent.mouseEnter(relicImage.closest('.achievements__tile-cell')!)
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: /^ポーション/ }))
    await waitFor(() => expect(container.querySelector(
      '.achievements__tile-cell.is-locked img[src="/icons/potions/fire_potion.png"]',
    )).not.toBeNull())
  })

  it('shows status definitions when character ability terms are hovered', async () => {
    render(
      <TooltipHost>
        <AchievementsScreen accountId="a" onBack={() => { }} />
      </TooltipHost>,
    )
    const strength = await screen.findByText('筋力')
    fireEvent.mouseEnter(strength.closest('[data-term="strength"]')!)
    const tooltip = await screen.findByRole('tooltip')
    expect(within(tooltip).getByText('筋力')).toBeInTheDocument()
    expect(tooltip).toHaveTextContent('アタックで与えるダメージがX増加する。')

    fireEvent.mouseLeave(strength.closest('[data-term="strength"]')!)
    const omnistrike = screen.getByText('拡散')
    fireEvent.mouseEnter(omnistrike.closest('[data-term="omnistrike"]')!)
    await waitFor(() => {
      expect(screen.getByRole('tooltip')).toHaveTextContent(
        '全ての攻撃を全体攻撃に変更する。ターン終了時に拡散が1減少する。',
      )
    })
  })

  it('uses dedicated bust-up portraits for characters and reveals enemy art only after discovery', async () => {
    const discoveredBestiary: BestiaryDto = {
      ...emptyBestiary,
      encounteredEnemyIds: ['jaw_worm'],
    }
    vi.spyOn(bestiaryApi, 'getBestiary').mockResolvedValueOnce(discoveredBestiary)
    const { container } = render(
      <TooltipHost>
        <AchievementsScreen accountId="a" onBack={() => { }} />
      </TooltipHost>,
    )

    const playerPortrait = await waitFor(() => {
      const image = container.querySelector<HTMLImageElement>(
        '.achievements__character-portrait img',
      )
      expect(image).not.toBeNull()
      return image!
    })
    expect(playerPortrait).toHaveAttribute('src', '/characters/portraits/default.png')

    fireEvent.click(screen.getByRole('tab', { name: /^モンスター/ }))
    expect(await screen.findByText('アギトムシ')).toBeInTheDocument()
    expect(container.querySelector(
      '.achievements__character-cell--monster img[src="/characters/enemies/jaw_worm.png"]',
    )).toBeInTheDocument()
  })

  it('shows monster stats, habitat, description, opening actions and a deck list modal', async () => {
    vi.spyOn(bestiaryApi, 'getBestiary').mockResolvedValueOnce({
      ...emptyBestiary,
      encounteredEnemyIds: ['jaw_worm'],
    })
    const { container } = render(
      <TooltipHost>
        <AchievementsScreen accountId="a" onBack={() => { }} />
      </TooltipHost>,
    )
    fireEvent.click(await screen.findByRole('tab', { name: /^モンスター/ }))
    expect(await screen.findByText('アギトムシ')).toBeInTheDocument()

    const cell = container.querySelector('.achievements__character-cell--monster')!
    expect(cell.querySelector('.actor-stats__item--attack')).toHaveTextContent('攻撃力7')
    expect(cell).toHaveTextContent('生息地：Act 1')
    expect(cell).toHaveTextContent('ポジション：通常（弱）')
    expect(cell).toHaveTextContent('顎で噛みつく虫。')
    expect(cell).toHaveTextContent('初期行動（先制行動）')

    fireEvent.click(screen.getByRole('button', { name: 'アギトムシの行動デッキ（2枚）を表示' }))
    expect(screen.getByRole('dialog', { name: 'アギトムシの行動デッキ (2枚)' })).toBeInTheDocument()
  })

  it('does not expose an undiscovered enemy name or image', async () => {
    const { container } = render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    fireEvent.click(await screen.findByRole('tab', { name: /^モンスター/ }))

    expect(screen.getByText('???')).toBeInTheDocument()
    expect(screen.queryByText('アギトムシ')).not.toBeInTheDocument()
    expect(container.querySelector(
      'img[src="/characters/enemies/jaw_worm.png"]',
    )).not.toBeInTheDocument()
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
