import type { DevMeta } from '../../api/dev'
import type {
  CardAbility,
  RepriseCardFilter,
} from './DevSpecTypes'

type ContingencyTiming = Extract<
  CardAbility,
  { type: 'contingency' }
>['trigger']['timing']

type Props = {
  label: string
  value: CardAbility[] | null
  cardType: string
  meta: DevMeta
  onChange: (value: CardAbility[] | null) => void
}

const ABILITY_NAMES: Record<CardAbility['type'], string> = {
  finisher: '終撃',
  reprise: '再演',
  contingency: '伏策',
}

const TYPED_ABILITY_KEYWORDS = new Set([
  'momentum', 'finisher', 'reprise', 'contingency',
])

const FALLBACK_ABILITY_TYPES = ['finisher', 'reprise', 'contingency']
const FALLBACK_CONTINGENCY_TIMINGS = ['TurnStart', 'BeforeEnemyAttack', 'ComboReached']

function isAbilityType(value: string): value is CardAbility['type'] {
  return value === 'finisher' || value === 'reprise' || value === 'contingency'
}

function newAbility(type: CardAbility['type']): CardAbility {
  if (type === 'reprise') {
    return {
      type,
      filter: {
        cardTypes: ['Skill'],
        minOriginalCost: null,
        maxOriginalCost: 3,
        minPaidCost: null,
        maxPaidCost: null,
        requiredKeywords: null,
      },
    }
  }
  if (type === 'contingency') {
    return { type, trigger: { timing: 'TurnStart', comboThreshold: null } }
  }
  return { type: 'finisher' }
}

export function CardAbilityEditor({ label, value, cardType, meta, onChange }: Props) {
  const abilities = value ?? []
  const configured = new Set(abilities.map(ability => ability.type))
  const supportsTypedAbilities = cardType === 'Attack' || cardType === 'Skill'
  // Keep the editor usable while a previously started development server still serves the
  // older metadata shape. A restart will replace these fallbacks with Core's catalog values.
  const abilityTypes = (meta.cardAbilities ?? FALLBACK_ABILITY_TYPES).filter(isAbilityType)
  const addableTypes = supportsTypedAbilities ? abilityTypes.filter(type => {
    if (configured.has(type)) return false
    if (type === 'reprise' && configured.has('contingency')) return false
    if (type === 'contingency' && configured.has('reprise')) return false
    return true
  }) : []

  const updateAt = (index: number, ability: CardAbility) => {
    onChange(abilities.map((entry, itemIndex) => itemIndex === index ? ability : entry))
  }

  return (
    <section className="card-ability-editor" aria-label={label}>
      <div className="card-ability-editor__header">
        <h4>{label}</h4>
        {value === null
          ? <span className="card-ability-editor__unset">未設定</span>
          : (
            <button type="button" onClick={() => onChange(null)}>
              能力欄を未設定に戻す
            </button>
          )}
      </div>

      {!supportsTypedAbilities && (
        <p className="card-ability-editor__notice">
          固有能力はアタックまたはスキルにのみ設定できます。
        </p>
      )}

      {abilities.map((ability, index) => (
        <div className="card-ability-editor__item" key={ability.type}>
          <div className="card-ability-editor__item-header">
            <strong>{ABILITY_NAMES[ability.type]}</strong>
            <button
              type="button"
              aria-label={`${label} ${ABILITY_NAMES[ability.type]}を削除`}
              onClick={() => onChange(abilities.filter((_, itemIndex) => itemIndex !== index))}
            >
              削除
            </button>
          </div>

          {ability.type === 'reprise' && (
            <RepriseFilterEditor
              label={label}
              value={ability.filter}
              meta={meta}
              onChange={filter => updateAt(index, { ...ability, filter })}
            />
          )}

          {ability.type === 'contingency' && (
            <div className="card-ability-editor__fields">
              <label>
                発動タイミング
                <select
                  aria-label={`${label} 伏策発動タイミング`}
                  value={ability.trigger.timing}
                  onChange={event => {
                    const nextTiming = event.target.value as ContingencyTiming
                    updateAt(index, {
                      ...ability,
                      type: 'contingency',
                      trigger: {
                        ...ability.trigger,
                        timing: nextTiming,
                        comboThreshold: nextTiming === 'ComboReached'
                          ? ability.trigger.comboThreshold ?? 1
                          : null,
                      },
                    })
                  }}
                >
                  {(meta.contingencyTriggerTimings ?? FALLBACK_CONTINGENCY_TIMINGS).map(timing => (
                    <option value={timing} key={timing}>{contingencyTimingName(timing)}</option>
                  ))}
                </select>
              </label>
              {ability.trigger.timing === 'ComboReached' && (
                <NullableNumberInput
                  label="達成コンボ数"
                  ariaLabel={`${label} 伏策達成コンボ数`}
                  value={ability.trigger.comboThreshold}
                  min={1}
                  onChange={comboThreshold => updateAt(index, {
                    ...ability,
                    type: 'contingency',
                    trigger: {
                      ...ability.trigger,
                      timing: 'ComboReached',
                      comboThreshold: comboThreshold ?? 1,
                    },
                  })}
                />
              )}
            </div>
          )}
        </div>
      ))}

      <div className="card-ability-editor__add">
        {addableTypes.map(type => (
          <button
            type="button"
            key={type}
            onClick={() => onChange([...abilities, newAbility(type)])}
          >
            ＋ {ABILITY_NAMES[type]}
          </button>
        ))}
      </div>
    </section>
  )
}

type RepriseFilterProps = {
  label: string
  value: RepriseCardFilter
  meta: DevMeta
  onChange: (value: RepriseCardFilter) => void
}

function RepriseFilterEditor({ label, value, meta, onChange }: RepriseFilterProps) {
  const set = (patch: Partial<RepriseCardFilter>) => onChange({ ...value, ...patch })
  const supportedCardTypes = meta.cardTypes.filter(type => type === 'Attack' || type === 'Skill')
  const supportedKeywords = meta.keywords.filter(keyword => !TYPED_ABILITY_KEYWORDS.has(keyword.id))

  return (
    <div className="card-ability-editor__fields">
      <label>
        対象カード種別（未選択なら全て）
        <select
          multiple
          aria-label={`${label} 再演対象カード種別`}
          value={value.cardTypes ?? []}
          onChange={event => set({
            cardTypes: Array.from(event.currentTarget.selectedOptions, option => option.value),
          })}
        >
          {supportedCardTypes.map(cardType => (
            <option value={cardType} key={cardType}>{cardTypeName(cardType)}</option>
          ))}
        </select>
      </label>
      <NullableNumberInput
        label="元コスト下限"
        ariaLabel={`${label} 再演元コスト下限`}
        value={value.minOriginalCost}
        min={0}
        onChange={minOriginalCost => set({ minOriginalCost })}
      />
      <NullableNumberInput
        label="元コスト上限"
        ariaLabel={`${label} 再演元コスト上限`}
        value={value.maxOriginalCost}
        min={0}
        onChange={maxOriginalCost => set({ maxOriginalCost })}
      />
      <NullableNumberInput
        label="支払コスト下限"
        ariaLabel={`${label} 再演支払コスト下限`}
        value={value.minPaidCost}
        min={0}
        onChange={minPaidCost => set({ minPaidCost })}
      />
      <NullableNumberInput
        label="支払コスト上限"
        ariaLabel={`${label} 再演支払コスト上限`}
        value={value.maxPaidCost}
        min={0}
        onChange={maxPaidCost => set({ maxPaidCost })}
      />
      <label>
        必須キーワード（未選択なら指定なし）
        <select
          multiple
          aria-label={`${label} 再演必須キーワード`}
          value={value.requiredKeywords ?? []}
          onChange={event => set({
            requiredKeywords: Array.from(
              event.currentTarget.selectedOptions,
              option => option.value,
            ),
          })}
        >
          {supportedKeywords.map(keyword => (
            <option value={keyword.id} key={keyword.id}>{keyword.name}</option>
          ))}
        </select>
      </label>
    </div>
  )
}

type NullableNumberInputProps = {
  label: string
  ariaLabel: string
  value: number | null
  min: number
  onChange: (value: number | null) => void
}

function NullableNumberInput({
  label,
  ariaLabel,
  value,
  min,
  onChange,
}: NullableNumberInputProps) {
  return (
    <label>
      {label}
      <input
        type="number"
        min={min}
        aria-label={ariaLabel}
        value={value ?? ''}
        placeholder="指定なし"
        onChange={event => onChange(
          event.target.value === ''
            ? null
            : Math.max(min, Number.parseInt(event.target.value, 10) || min),
        )}
      />
    </label>
  )
}

function contingencyTimingName(timing: string): string {
  if (timing === 'TurnStart') return 'ターン開始時'
  if (timing === 'BeforeEnemyAttack') return '相手のアタック時'
  if (timing === 'ComboReached') return 'コンボ達成時'
  return timing
}

function cardTypeName(cardType: string): string {
  if (cardType === 'Attack') return 'アタック'
  if (cardType === 'Skill') return 'スキル'
  return cardType
}
