import { useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { fetchCharacterCatalog, getCardCatalog } from '../api/catalog'
import type { CardCatalog, CharacterCatalog, CharacterClass } from '../api/catalog'
import { startNewRun } from '../api/runs'
import type { CharacterCatalogEntryDto, CharacterSkillCatalogEntryDto, RunSnapshotDto } from '../api/types'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import type { CardType } from '../components/Card'
import { ActorStats } from '../components/ActorStats'
import { CardDesc } from '../components/CardDesc'
import type { CardDescTerm } from '../components/CardDesc'
import { rarityCodeFromNumber } from '../components/rarity'
import { useTooltipTarget } from '../components/Tooltip'
import {
  DEFAULT_CHARACTER_PORTRAIT,
  resolveCharacterPortrait,
} from '../presentation/playableCharacterVisuals'
import './CharacterPickScreen.css'

const CHARACTER_CLASSES: Array<{ id: CharacterClass; label: string }> = [
  { id: 'Striker', label: 'ストライカー' },
  { id: 'Guardian', label: 'ガーディアン' },
  { id: 'Tactician', label: 'タクティシャン' },
  { id: 'Enhancer', label: 'エンハンサー' },
]

type Props = {
  accountId: string
  force: boolean
  /** The UI already supports a larger party; current battle integration passes one. */
  selectionLimit?: number
  onStarted: (snapshot: RunSnapshotDto) => void
  onBack: () => void
}

export function CharacterPickScreen({
  accountId,
  force,
  selectionLimit = 1,
  onStarted,
  onBack,
}: Props) {
  const [characters, setCharacters] = useState<CharacterCatalog | null>(null)
  const [cards, setCards] = useState<CardCatalog | null>(null)
  const [activeClass, setActiveClass] = useState<CharacterClass>('Striker')
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [startError, setStartError] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)
  const [starting, setStarting] = useState(false)
  const [deckPreviewCharacterId, setDeckPreviewCharacterId] = useState<string | null>(null)
  const startingRef = useRef(false)

  useEffect(() => {
    let cancelled = false
    setLoadError(null)
    Promise.all([fetchCharacterCatalog(), getCardCatalog()])
      .then(([characterCatalog, cardCatalog]) => {
        if (cancelled) return
        setCharacters(characterCatalog)
        setCards(cardCatalog)
      })
      .catch(() => {
        if (cancelled) return
        setLoadError('キャラクターデータを読み込めませんでした。')
      })
    return () => { cancelled = true }
  }, [reloadKey])

  const visibleCharacters = useMemo(() => Object.values(characters ?? {})
    .filter(character => (character.characterClass ?? 'Striker') === activeClass)
    .sort((left, right) => left.name.localeCompare(right.name, 'ja')),
  [activeClass, characters])

  function toggleCharacter(id: string) {
    if (startingRef.current) return
    setStartError(null)
    setSelectedIds(previous => {
      if (previous.includes(id)) return previous.filter(selectedId => selectedId !== id)
      if (selectionLimit === 1) return [id]
      if (previous.length >= selectionLimit) return previous
      return [...previous, id]
    })
  }

  async function handleStart() {
    if (startingRef.current || selectedIds.length === 0) return
    startingRef.current = true
    setStarting(true)
    setStartError(null)
    try {
      const snapshot = await startNewRun(accountId, force, selectedIds)
      onStarted(snapshot)
    } catch (error) {
      setStartError(error instanceof Error ? error.message : 'ランを開始できませんでした。')
    } finally {
      startingRef.current = false
      setStarting(false)
    }
  }

  return (
    <main className="character-pick">
      <div className="character-pick__pattern" aria-hidden="true" />
      <header className="character-pick__header">
        <div>
          <p className="character-pick__eyebrow">PARTY FORMATION</p>
          <h1>キャラクター選択</h1>
          <p className="character-pick__lead">
            今回の冒険に参加するキャラクターを選択してください。
          </p>
        </div>
        <div className="character-pick__header-actions">
          {force && <span className="character-pick__overwrite">進行中のランを上書き</span>}
          <Button variant="secondary" onClick={onBack} disabled={starting}>戻る</Button>
        </div>
      </header>

      <nav className="character-pick__tabs" aria-label="クラス" role="tablist">
        {CHARACTER_CLASSES.map(characterClass => (
          <button
            key={characterClass.id}
            type="button"
            role="tab"
            aria-selected={activeClass === characterClass.id}
            className={`character-pick__tab character-pick__tab--${characterClass.id.toLowerCase()}`}
            onClick={() => setActiveClass(characterClass.id)}
            disabled={starting}
          >
            {characterClass.label}
            <span>{Object.values(characters ?? {}).filter(character =>
              (character.characterClass ?? 'Striker') === characterClass.id).length}</span>
          </button>
        ))}
      </nav>

      <section className="character-pick__body" aria-live="polite">
        {loadError ? (
          <div className="character-pick__state" role="alert">
            <p>{loadError}</p>
            <Button onClick={() => {
              setCharacters(null)
              setCards(null)
              setReloadKey(value => value + 1)
            }}>再試行</Button>
          </div>
        ) : characters === null || cards === null ? (
          <div className="character-pick__state">
            <span className="character-pick__loading-mark" aria-hidden="true">◆</span>
            <p>キャラクターを読み込み中…</p>
          </div>
        ) : visibleCharacters.length === 0 ? (
          <div className="character-pick__state character-pick__state--empty">
            <strong>{classLabel(activeClass)}</strong>
            <p>このクラスのキャラクターはまだ実装されていません。</p>
          </div>
        ) : (
          <div className="character-pick__grid">
            {visibleCharacters.map(character => {
              const selected = selectedIds.includes(character.id)
              const deck = aggregateDeck(character.deck ?? [], cards)
              const tacticalSkill = character.skills?.find(skill => skill.id === 'tactical')
              const ultimateSkill = character.skills?.find(skill => skill.id === 'ultimate')
              return (
                <article
                  key={character.id}
                  className={`character-pick__character character-pick__character--${activeClass.toLowerCase()}${selected ? ' is-selected' : ''}`}
                >
                  <button
                    type="button"
                    className="character-pick__select"
                    aria-pressed={selected}
                    aria-label={`${character.name}を${selected ? '選択解除' : '選択'}`}
                    onClick={() => toggleCharacter(character.id)}
                    disabled={starting}
                  >
                    <span className="character-pick__select-label" aria-hidden="true">
                      {character.name}
                    </span>
                  </button>
                  <div className="character-pick__summary">
                    <span className="character-pick__portrait" aria-hidden="true">
                      <img
                        src={resolveCharacterPortrait(character.id)}
                        alt=""
                        draggable={false}
                        onError={(event) => {
                          event.currentTarget.onerror = null
                          event.currentTarget.src = DEFAULT_CHARACTER_PORTRAIT
                        }}
                      />
                      <span>{classLabel(character.characterClass ?? 'Striker')}</span>
                    </span>
                    <span className="character-pick__identity">
                      <strong>{character.name}</strong>
                      <span>{classLabel(character.characterClass ?? 'Striker')}</span>
                    </span>
                    <span className="character-pick__resources" aria-label="開始時の能力値">
                      <ActorStats
                        stats={{
                          hp: character.maxHp,
                          energyPerTurn: character.energyPerTurn ?? 3,
                          drawPerTurn: character.drawPerTurn ?? 5,
                          attack: character.attack ?? 0,
                          defense: character.defense ?? 0,
                        }}
                      />
                    </span>
                    <span className="character-pick__selection-mark">
                      {selected ? '選択中' : '選択する'}
                    </span>
                  </div>
                  <div className="character-pick__details">
                    <section>
                      <h2>キャラクター説明</h2>
                      <p>{character.description?.trim() || 'キャラクター説明は未設定です。'}</p>
                    </section>
                    <section>
                      <h2>特性{character.traitName ? `：${character.traitName}` : ''}</h2>
                      <CharacterAbilityText text={
                        character.traitEffects?.trim()
                        || character.traitDescription?.trim()
                        || '特性は未実装です。'
                      } />
                    </section>
                    <CharacterSkillSection kind="戦技" skill={tacticalSkill} />
                    <CharacterSkillSection kind="奥義" skill={ultimateSkill} />
                    <section className="character-pick__deck">
                      <button
                        type="button"
                        className="character-pick__deck-open"
                        onClick={(event) => {
                          event.stopPropagation()
                          setDeckPreviewCharacterId(character.id)
                        }}
                        disabled={starting || deck.length === 0}
                        aria-haspopup="dialog"
                        aria-label={`初期デッキ（${character.deck?.length ?? 0}枚）を表示`}
                      >
                        <span>初期デッキ <strong>{character.deck?.length ?? 0}枚</strong></span>
                        <span>カードを確認</span>
                      </button>
                      {deck.length > 0 ? (
                        <ul>
                          {deck.map(entry => (
                            <li key={entry.id}>
                              <span>{entry.name}</span><strong>×{entry.count}</strong>
                            </li>
                          ))}
                        </ul>
                      ) : <p>初期デッキは空です。</p>}
                    </section>
                  </div>
                </article>
              )
            })}
          </div>
        )}
      </section>

      <footer className="character-pick__footer">
        <div>
          <span>選択人数</span>
          <strong>{selectedIds.length}/{selectionLimit}</strong>
          {selectedIds.length > 0 && characters && (
            <span className="character-pick__party-preview">
              {selectedIds.map(id => (
                <span key={id} className="character-pick__party-member">
                  <img
                    src={resolveCharacterPortrait(id)}
                    alt=""
                    draggable={false}
                    onError={(event) => {
                      event.currentTarget.onerror = null
                      event.currentTarget.src = DEFAULT_CHARACTER_PORTRAIT
                    }}
                  />
                  {characters[id]?.name ?? id}
                </span>
              ))}
            </span>
          )}
        </div>
        <div className="character-pick__start-area">
          {startError && <p role="alert">{startError}</p>}
          <Button
            onClick={() => void handleStart()}
            disabled={selectedIds.length === 0 || starting}
          >
            {starting ? '冒険を準備中…' : 'このキャラクターで開始'}
          </Button>
        </div>
      </footer>
      {deckPreviewCharacterId && characters && cards ? (
        <InitialDeckModal
          character={characters[deckPreviewCharacterId]}
          catalog={cards}
          onClose={() => setDeckPreviewCharacterId(null)}
        />
      ) : null}
    </main>
  )
}

function CharacterSkillSection({
  kind,
  skill,
}: {
  kind: '戦技' | '奥義'
  skill?: CharacterSkillCatalogEntryDto
}) {
  return (
    <section className="character-pick__ability">
      <h2>{kind}{skill ? `：${skill.name}` : ''}</h2>
      {skill ? (
        <>
          <div className="character-pick__ability-costs">
            {skill.energyCost > 0 && <span>エナジー {skill.energyCost}</span>}
            {skill.comboGaugeCost > 0 && <span>コンボゲージ {skill.comboGaugeCost}</span>}
            {skill.oncePerTurn && <span>1ターン1回</span>}
          </div>
          <CharacterAbilityText text={skill.description} />
        </>
      ) : (
        <p>未設定です。</p>
      )}
    </section>
  )
}

function CharacterAbilityText({ text }: { text: string }) {
  return (
    <CardDesc
      text={text}
      compress={false}
      renderTerm={renderCharacterTerm}
    />
  )
}

function CharacterTermTooltip({ term, children }: {
  term: CardDescTerm
  children: ReactNode
}) {
  const tip = useTooltipTarget({
    name: term.name,
    desc: term.desc,
    selfReference: { kind: term.kind, id: term.id },
  })
  return (
    <span
      className="character-pick__term"
      tabIndex={0}
      data-term={term.id}
      {...tip}
    >
      {children}
    </span>
  )
}

function renderCharacterTerm(term: CardDescTerm, rendered: ReactNode, key: number): ReactNode {
  return (
    <CharacterTermTooltip term={term} key={`${term.kind}:${term.id}:${key}`}>
      {rendered}
    </CharacterTermTooltip>
  )
}

function InitialDeckModal({
  character,
  catalog,
  onClose,
}: {
  character?: CharacterCatalogEntryDto
  catalog: CardCatalog
  onClose: () => void
}) {
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  if (!character) return null
  const deck = character.deck ?? []
  return (
    <div className="character-pick__deck-backdrop" onClick={onClose} role="presentation">
      <div
        className="character-pick__deck-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="character-pick-deck-title"
        onClick={event => event.stopPropagation()}
      >
        <header className="character-pick__deck-modal-header">
          <span id="character-pick-deck-title">{character.name}の初期デッキ ({deck.length}枚)</span>
          <button type="button" onClick={onClose} aria-label="閉じる">×</button>
        </header>
        {deck.length === 0 ? (
          <p className="character-pick__deck-modal-empty">初期デッキは空です。</p>
        ) : (
          <ul className="character-pick__deck-modal-list">
            {deck.map((cardId, index) => {
              const definition = catalog[cardId]
              return (
                <li key={`${cardId}-${index}`}>
                  <Card
                    name={definition?.displayName ?? definition?.name ?? cardId}
                    cost={definition?.cost ?? 'X'}
                    costOriginal={definition?.cost ?? null}
                    type={cardTypeCode(definition?.cardType ?? 'Skill')}
                    rarity={rarityCodeFromNumber(definition?.rarity ?? 0)}
                    poolClass={definition?.poolClass ?? null}
                    description={definition?.description ?? ''}
                    upgradedDescription={definition?.upgradedDescription ?? null}
                  />
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </div>
  )
}

function classLabel(characterClass: CharacterClass): string {
  return CHARACTER_CLASSES.find(entry => entry.id === characterClass)?.label ?? characterClass
}

function aggregateDeck(ids: readonly string[], catalog: CardCatalog) {
  const counts = new Map<string, number>()
  for (const id of ids) counts.set(id, (counts.get(id) ?? 0) + 1)
  return [...counts].map(([id, count]) => ({
    id,
    count,
    name: catalog[id]?.displayName ?? catalog[id]?.name ?? id,
  }))
}

function cardTypeCode(value: string): CardType {
  switch (value.toLowerCase()) {
    case 'attack': return 'attack'
    case 'power': return 'power'
    case 'unit': return 'unit'
    case 'curse': return 'curse'
    case 'status': return 'status'
    default: return 'skill'
  }
}
