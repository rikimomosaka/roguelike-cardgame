import { useState } from 'react'
import type { DevContentEnums, DevContentSummary } from '../../api/devContent'

export type ReferenceCatalog = Record<string, DevContentSummary[]>

type Props = {
  value: unknown
  onChange: (value: unknown) => void
  meta: DevContentEnums
  references: ReferenceCatalog
  contentKind?: string
  path?: string
  propertyName?: string
  context?: Record<string, unknown>
  depth?: number
}

export function StructuredJsonEditor({
  value,
  onChange,
  meta,
  references,
  contentKind,
  path = '$',
  propertyName = '',
  context,
  depth = 0,
}: Props) {
  if (Array.isArray(value)) {
    if (propertyName === 'versions') {
      return (
        <VersionArrayEditor
          value={value}
          onChange={onChange}
          meta={meta}
          references={references}
          contentKind={contentKind}
          path={path}
          context={context}
          depth={depth}
        />
      )
    }
    const isCardAbilityArray = contentKind === 'cards'
      && (propertyName === 'abilities' || propertyName === 'upgradedAbilities')
    const cardTypePreventsAbility = isCardAbilityArray
      && context?.cardType !== 'Attack' && context?.cardType !== 'Skill'
    const nextAbilityType = isCardAbilityArray
      ? availableCardAbilityTypes(meta.cardAbilities, value)[0]
      : null
    const cardAbilityAdditionDisabled = cardTypePreventsAbility
      || (isCardAbilityArray && nextAbilityType === undefined)
    return (
      <div className="content-json-array" data-path={path}>
        {value.map((item, index) => (
          <div className="content-json-array__item" key={`${path}:${index}`}>
            <div className="content-json-array__tools">
              <span>#{index + 1}</span>
              <button type="button" disabled={index === 0} onClick={() => onChange(move(value, index, index - 1))}>↑</button>
              <button type="button" disabled={index === value.length - 1} onClick={() => onChange(move(value, index, index + 1))}>↓</button>
              <button type="button" className="is-danger" onClick={() => onChange(value.filter((_, itemIndex) => itemIndex !== index))}>削除</button>
            </div>
            <StructuredJsonEditor
              value={item}
              onChange={(next) => onChange(value.map((entry, itemIndex) => itemIndex === index ? next : entry))}
              meta={meta}
              references={references}
              contentKind={contentKind}
              path={`${path}[${index}]`}
              propertyName={propertyName}
              context={isCardAbilityArray
                ? { ...context, __abilitySiblings: value, __abilityIndex: index }
                : context}
              depth={depth + 1}
            />
          </div>
        ))}
        <button
          type="button"
          className="content-json-add"
          disabled={cardAbilityAdditionDisabled}
          title={cardTypePreventsAbility
            ? '固有能力はアタックまたはスキルにのみ設定できます。'
            : isCardAbilityArray && nextAbilityType === undefined
              ? '追加できる固有能力はありません。'
              : undefined}
          onClick={() => onChange([
            ...value,
            isCardAbilityArray && nextAbilityType
              ? buildCardAbilityForType(nextAbilityType)
              : newArrayItem(propertyName, path, meta, references, context, contentKind),
          ])}
        >
          ＋ 項目を追加
        </button>
      </div>
    )
  }

  if (isObject(value)) {
    const addableProperties = knownPropertiesFor(contentKind, value, path, meta, references)
    const childContext = propertyName === 'statusChanges' && typeof context?.action === 'string'
      ? { ...value, action: context.action }
      : (propertyName === 'abilities' || propertyName === 'upgradedAbilities')
          && Array.isArray(context?.__abilitySiblings)
        ? {
            ...value,
            __abilitySiblings: context.__abilitySiblings,
            __abilityIndex: context.__abilityIndex,
          }
        : value
    return (
      <div className={`content-json-object depth-${Math.min(depth, 4)}`} data-path={path}>
        {Object.entries(value).map(([key, child]) => (
          <div className="content-json-field" key={`${path}.${key}`}>
            <div className="content-json-field__label">
              <label htmlFor={fieldId(`${path}.${key}`, key)}>{contextualFieldLabel(key, childContext)}</label>
              {key !== 'id' && (
                <button
                  type="button"
                  className="content-json-field__remove"
                  aria-label={`${key}を削除`}
                  onClick={() => {
                    const next = { ...value }
                    if (contentKind === 'cards' && path === '$' && key === 'poolKinds') {
                      // Keep an explicit acquisition-group marker in an override. An empty
                      // array means that the card belongs to no normal acquisition pool.
                      next.poolKinds = []
                      delete next.poolClass
                      delete next.eligibleCharacterIds
                    } else if (key === 'amountSource' && isEffectObject(value)
                      && value.amountSource === 'currentCost') {
                      delete next.amountSource
                      if (next.amount === 0) next.amount = -1
                    } else {
                      delete next[key]
                    }
                    onChange(next)
                  }}
                >×</button>
              )}
            </div>
            <StructuredJsonEditor
              value={child}
              onChange={(nextChild) => {
                if (key === 'action' && typeof nextChild === 'string' && isEffectObject(value)) {
                  onChange(buildEffectForAction(value, nextChild, meta, references, contentKind))
                  return
                }
                if (key === 'amountSource' && isEffectObject(value)) {
                  const next: Record<string, unknown> = { ...value, amountSource: nextChild }
                  if (nextChild === 'currentCost' && value.action === 'modifyCardCost') {
                    next.amount = 0
                  } else if (value.amountSource === 'currentCost' && value.amount === 0) {
                    next.amount = -1
                  }
                  onChange(next)
                  return
                }
                if (key === 'scope' && typeof nextChild === 'string' && isEffectObject(value)) {
                  onChange(normalizeEffectSelection(
                    normalizeEffectScope({ ...value, scope: nextChild }, meta),
                  ))
                  return
                }
                if (key === 'trigger' && isEffectObject(value)) {
                  onChange(normalizeEffectSelection(
                    normalizeEffectTrigger({ ...value, trigger: nextChild }),
                  ))
                  return
                }
                if (key === 'trigger' && contentKind === 'characters' && path === '$.trait'
                  && typeof nextChild === 'string') {
                  const next: Record<string, unknown> = { ...value, trigger: nextChild }
                  if (nextChild === 'OnComboReached') {
                    if (typeof next.threshold !== 'number') next.threshold = 1
                  } else {
                    delete next.threshold
                  }
                  onChange(next)
                  return
                }
                if (key === 'select' && typeof nextChild === 'string' && isEffectObject(value)) {
                  onChange(normalizeEffectSelection({ ...value, select: nextChild }))
                  return
                }
                if (key === 'type' && typeof nextChild === 'string'
                  && contentKind === 'events' && /\.effects\[\d+\]$/.test(path)) {
                  onChange(buildEventEffectForType(value, nextChild, references))
                  return
                }
                if (key === 'type' && typeof nextChild === 'string'
                  && contentKind === 'cards' && /\.(?:upgradedA|a)bilities\[\d+\]$/.test(path)) {
                  onChange(buildCardAbilityForType(nextChild))
                  return
                }
                if (key === 'timing' && typeof nextChild === 'string'
                  && /\.(?:upgradedA|a)bilities\[\d+\]\.trigger$/.test(path)) {
                  const next: Record<string, unknown> = { ...value, timing: nextChild }
                  if (nextChild === 'ComboReached') next.comboThreshold = 1
                  else delete next.comboThreshold
                  onChange(next)
                  return
                }
                if (key === 'poolKinds' && contentKind === 'cards' && path === '$'
                  && Array.isArray(nextChild) && nextChild.length === 0) {
                  const next: Record<string, unknown> = { ...value, poolKinds: [] }
                  delete next.poolClass
                  delete next.eligibleCharacterIds
                  onChange(next)
                  return
                }
                onChange({ ...value, [key]: nextChild })
              }}
              meta={meta}
              references={references}
              contentKind={contentKind}
              path={`${path}.${key}`}
              propertyName={key}
              context={childContext}
              depth={depth + 1}
            />
          </div>
        ))}
        {contentKind === 'cards' && isCardSpec(value, path) && !Object.hasOwn(value, 'upgradedEffects') && (
          <div className="content-json-upgrade-helper">
            <span>このカードには強化後の効果がまだありません。</span>
            <button
              type="button"
              onClick={() => onChange({
                ...value,
                upgradedEffects: structuredClone(value.effects),
              })}
            >
              ＋ 通常効果を複製して強化後を作成
            </button>
          </div>
        )}
        {contentKind === 'cards' && isCardSpec(value, path) && !Object.hasOwn(value, 'keywords') && (
          <div className="content-json-upgrade-helper">
            <span>カードへキーワード能力を設定できます。</span>
            <button
              type="button"
              onClick={() => onChange({ ...value, keywords: [] })}
            >
              ＋ 通常版キーワード欄を追加
            </button>
          </div>
        )}
        {contentKind === 'cards' && isCardSpec(value, path)
          && !Object.hasOwn(value, 'upgradedKeywords') && (
          <div className="content-json-upgrade-helper">
            <span>強化後のキーワードは通常版とは独立して設定します。</span>
            <button
              type="button"
              onClick={() => onChange({ ...value, upgradedKeywords: [] })}
            >
              ＋ 強化後キーワード欄を追加
            </button>
          </div>
        )}
        {addableProperties.length > 0 && (
          <label className="content-json-known-property">
            <span>追加できる既知の設定</span>
            <select
              value=""
              onChange={(event) => {
                const selected = addableProperties.find(property => property.key === event.target.value)
                if (selected) onChange({ ...value, [selected.key]: structuredClone(selected.value) })
              }}
            >
              <option value="">設定を選んで追加…</option>
              {addableProperties.map(property => (
                <option value={property.key} key={property.key}>
                  {property.key}({fieldLabel(property.key)})
                </option>
              ))}
            </select>
          </label>
        )}
        <div className="content-json-advanced">
          <button
            type="button"
            className="content-json-add content-json-add--advanced"
            onClick={() => {
              const key = window.prompt('追加するJSONプロパティ名')?.trim()
              if (!key || Object.hasOwn(value, key)) return
              onChange({ ...value, [key]: '' })
            }}
          >
            ＋ 高度なJSON項目を手動追加
          </button>
          <small>一覧にない将来の拡張項目専用です。通常の編集では上の既知設定を使用します。</small>
        </div>
      </div>
    )
  }

  if (value === null) {
    const options = optionsFor(propertyName, context, path, meta, references)
    if (options) {
      return (
        <select
          id={fieldId(path, propertyName)}
          value=""
          onChange={(event) => {
            if (event.target.value === '') { onChange(null); return }
            const selected = options.find(option => String(option.value) === event.target.value)
            onChange(selected?.value ?? event.target.value)
          }}
        >
          <option value="">(なし)</option>
          {options.map(option => <option value={option.value} key={String(option.value)}>{option.label}</option>)}
        </select>
      )
    }
    return (
      <div className="content-json-null">
        <span className="content-json-null__status">未設定（null）</span>
        <small>値を設定する場合は、入力形式を選んでください。</small>
        <div className="content-json-null__choices">
          <button type="button" title="文章やIDを入力します" onClick={() => onChange('')}>文字を設定</button>
          <button type="button" title="コストや効果量などの数値を入力します" onClick={() => onChange(0)}>数字を設定</button>
          <button type="button" title="有効・無効を選択します" onClick={() => onChange(false)}>ON/OFFを設定</button>
          <button type="button" title="複数の項目を並べる一覧を作ります" onClick={() => onChange([])}>一覧を設定</button>
          <button type="button" title="複数の名前付き項目を持つ詳細設定を作ります" onClick={() => onChange({})}>詳細項目を設定</button>
        </div>
      </div>
    )
  }

  if (typeof value === 'boolean') {
    return (
      <select id={fieldId(path, propertyName)} value={value ? 'true' : 'false'} onChange={(event) => onChange(event.target.value === 'true')}>
        <option value="true">有効</option>
        <option value="false">無効</option>
      </select>
    )
  }

  if (typeof value === 'number') {
    const options = optionsFor(propertyName, context, path, meta, references)
    if (options && options.every(option => typeof option.value === 'number')) {
      return (
        <select id={fieldId(path, propertyName)} value={value} onChange={(event) => onChange(Number(event.target.value))}>
          {options.map(option => <option value={option.value} key={String(option.value)}>{option.label}</option>)}
        </select>
      )
    }
    return (
      <input
        id={fieldId(path, propertyName)}
        type="number"
        value={Number.isFinite(value) ? value : 0}
        disabled={propertyName === 'amount' && context?.amountSource === 'currentCost'}
        onChange={(event) => onChange(Number(event.target.value))}
      />
    )
  }

  const stringValue = typeof value === 'string' ? value : String(value ?? '')
  const options = optionsFor(propertyName, context, path, meta, references)
  if (options) {
    return (
      <select id={fieldId(path, propertyName)} value={stringValue} onChange={(event) => onChange(event.target.value)}>
        {!options.some(option => String(option.value) === stringValue) && (
          <option value={stringValue}>{stringValue}(表示名未登録)</option>
        )}
        {options.map(option => <option value={option.value} key={String(option.value)}>{option.label}</option>)}
      </select>
    )
  }

  const referenceOptions = referencesFor(propertyName, references)
  const isLong = /(description|message|flavor|text)$/i.test(propertyName) || stringValue.length > 80
  if (isLong) {
    return <textarea id={fieldId(path, propertyName)} value={stringValue} rows={3} onChange={(event) => onChange(event.target.value)} />
  }
  return (
    <>
      <input
        id={fieldId(path, propertyName)}
        type="text"
        value={stringValue}
        list={referenceOptions ? `refs-${propertyName}` : undefined}
        onChange={(event) => onChange(event.target.value)}
      />
      {referenceOptions && (
        <datalist id={`refs-${propertyName}`}>
          {referenceOptions.map(option => (
            <option value={option.id} label={`${option.id}(${option.name})`} key={option.id} />
          ))}
        </datalist>
      )}
    </>
  )
}

type VersionArrayEditorProps = {
  value: unknown[]
  onChange: (value: unknown) => void
  meta: DevContentEnums
  references: ReferenceCatalog
  contentKind?: string
  path: string
  context?: Record<string, unknown>
  depth: number
}

function VersionArrayEditor({
  value,
  onChange,
  meta,
  references,
  contentKind,
  path,
  context,
  depth,
}: VersionArrayEditorProps) {
  const activeVersion = typeof context?.activeVersion === 'string' ? context.activeVersion : ''
  const documentKey = `${String(context?.id ?? '')}:${activeVersion}`
  const [expansion, setExpansion] = useState<{ key: string; versions: Set<string> }>(
    () => ({ key: documentKey, versions: new Set(activeVersion ? [activeVersion] : []) }),
  )
  const expanded = expansion.key === documentKey
    ? expansion.versions
    : new Set(activeVersion ? [activeVersion] : [])

  return (
    <div className="content-json-array content-json-versions" data-path={path}>
      {value.map((item, index) => {
        const version = isObject(item) && typeof item.version === 'string'
          ? item.version
          : `#${index + 1}`
        const label = isObject(item) && typeof item.label === 'string' ? item.label : ''
        const isActive = version === activeVersion
        const isExpanded = expanded.has(version)
        return (
          <section
            className={`content-json-version${isActive ? ' is-active' : ''}`}
            data-version={version}
            key={`${path}:${version}:${index}`}
          >
            <div className="content-json-version__header">
              <button
                type="button"
                className="content-json-version__toggle"
                aria-expanded={isExpanded}
                aria-label={`${version}${isActive ? '（使用中）' : ''}${label ? ` ${label}` : ''}`}
                onClick={() => setExpansion(previous => {
                  const base = previous.key === documentKey
                    ? previous.versions
                    : new Set(activeVersion ? [activeVersion] : [])
                  const next = new Set(base)
                  if (next.has(version)) next.delete(version)
                  else next.add(version)
                  return { key: documentKey, versions: next }
                })}
              >
                <span aria-hidden="true">{isExpanded ? '▼' : '▶'}</span>
                <strong>{version}</strong>
                {isActive && <em>使用中</em>}
                {label && <small>{label}</small>}
              </button>
              <div className="content-json-array__tools">
                <button type="button" disabled={index === 0} onClick={() => onChange(move(value, index, index - 1))}>↑</button>
                <button type="button" disabled={index === value.length - 1} onClick={() => onChange(move(value, index, index + 1))}>↓</button>
                <button type="button" className="is-danger" onClick={() => onChange(value.filter((_, itemIndex) => itemIndex !== index))}>削除</button>
              </div>
            </div>
            {isExpanded && (
              <div className="content-json-version__body">
                <StructuredJsonEditor
                  value={item}
                  onChange={(next) => onChange(value.map((entry, itemIndex) => itemIndex === index ? next : entry))}
                  meta={meta}
                  references={references}
                  contentKind={contentKind}
                  path={`${path}[${index}]`}
                  propertyName="versions"
                  depth={depth + 1}
                />
              </div>
            )}
          </section>
        )
      })}
      <button
        type="button"
        className="content-json-add"
        onClick={() => onChange([...value, newVersionItem(contentKind, value, context)])}
      >
        ＋ バージョンを追加
      </button>
    </div>
  )
}

type EffectField =
  | 'scope' | 'side' | 'amount' | 'name' | 'unitId' | 'comboMin' | 'maxComboMin' | 'pile'
  | 'battleOnly' | 'cardRefId' | 'select' | 'amountSource' | 'trigger'
  | 'chancePercent' | 'costDuration' | 'cardCount' | 'statusChanges'

type EffectSchema = {
  required: EffectField[]
  optional?: EffectField[]
}

const commonBattleOptions: EffectField[] = [
  'amountSource', 'comboMin', 'maxComboMin', 'trigger', 'chancePercent', 'battleOnly',
]

const effectSchemas: Record<string, EffectSchema> = {
  attack: { required: ['scope', 'side', 'amount'], optional: commonBattleOptions },
  block: { required: ['scope', 'amount'], optional: ['side', ...commonBattleOptions] },
  buff: { required: ['scope', 'statusChanges'], optional: ['side', 'comboMin', 'maxComboMin', 'trigger', 'chancePercent', 'battleOnly'] },
  debuff: { required: ['scope', 'side', 'statusChanges'], optional: ['comboMin', 'maxComboMin', 'trigger', 'chancePercent', 'battleOnly'] },
  heal: { required: ['scope', 'amount'], optional: ['side', ...commonBattleOptions] },
  draw: { required: ['scope', 'amount'], optional: ['side', ...commonBattleOptions] },
  drawCards: { required: ['scope', 'amount'], optional: ['side', ...commonBattleOptions] },
  discard: { required: ['scope', 'amount', 'select'], optional: ['comboMin', 'maxComboMin', 'trigger', 'battleOnly'] },
  gainEnergy: { required: ['scope', 'amount'], optional: ['side', ...commonBattleOptions] },
  gainComboGauge: { required: ['scope', 'amount'], optional: ['side', ...commonBattleOptions] },
  gainMaxEnergy: { required: ['scope', 'amount'], optional: ['side', ...commonBattleOptions] },
  exhaustCard: { required: ['scope', 'amount', 'pile', 'select'], optional: ['comboMin', 'maxComboMin', 'trigger', 'battleOnly'] },
  upgrade: { required: ['scope', 'amount', 'pile', 'select'], optional: ['comboMin', 'maxComboMin', 'trigger', 'battleOnly'] },
  summon: { required: ['scope', 'unitId', 'amount'], optional: ['comboMin', 'maxComboMin', 'trigger', 'battleOnly'] },
  selfDamage: { required: ['scope', 'amount'], optional: commonBattleOptions },
  addCard: { required: ['scope', 'cardRefId', 'amount', 'pile'], optional: ['comboMin', 'maxComboMin', 'trigger', 'battleOnly'] },
  recoverFromDiscard: { required: ['scope', 'amount', 'pile', 'select'], optional: ['comboMin', 'maxComboMin', 'trigger', 'battleOnly'] },
  recoverFromExhaust: { required: ['scope', 'amount', 'pile', 'select'], optional: ['comboMin', 'maxComboMin', 'trigger', 'battleOnly'] },
  modifyCardCost: {
    required: ['scope', 'amount', 'pile', 'select', 'cardCount', 'costDuration'],
    optional: ['side', 'amountSource', 'comboMin', 'maxComboMin', 'trigger', 'battleOnly'],
  },
  gainMaxHp: { required: ['scope', 'amount', 'trigger'] },
  gainGold: { required: ['scope', 'amount', 'trigger'] },
  healHp: { required: ['scope', 'amount', 'trigger'] },
  energyPerTurnBonus: { required: ['scope', 'amount', 'trigger'] },
  cardsDrawnPerTurnBonus: { required: ['scope', 'amount', 'trigger'] },
  goldRewardMultiplier: { required: ['scope', 'amount', 'trigger'] },
  shopPriceMultiplier: { required: ['scope', 'amount', 'trigger'] },
  rewardCardChoicesBonus: { required: ['scope', 'amount', 'trigger'] },
  rewardRerollAvailable: { required: ['scope', 'amount', 'trigger'] },
  unknownTileWeightDelta: { required: ['scope', 'name', 'amount', 'trigger'] },
  restHealBonus: { required: ['scope', 'amount', 'trigger'] },
}

const knownEffectFields = new Set<EffectField>([
  'scope', 'side', 'amount', 'name', 'unitId', 'comboMin', 'maxComboMin', 'pile', 'battleOnly',
  'cardRefId', 'select', 'amountSource', 'trigger', 'chancePercent',
  'costDuration', 'cardCount', 'statusChanges',
])

const passiveActions = new Set([
  'energyPerTurnBonus', 'cardsDrawnPerTurnBonus', 'goldRewardMultiplier',
  'shopPriceMultiplier', 'rewardCardChoicesBonus', 'rewardRerollAvailable',
  'unknownTileWeightDelta', 'restHealBonus',
])

const nonBattleActions = new Set(['gainMaxHp', 'gainGold', 'healHp'])

const selfOnlyActions = new Set([
  'exhaustCard', 'upgrade', 'summon', 'selfDamage', 'addCard', 'recoverFromDiscard', 'recoverFromExhaust',
  ...nonBattleActions,
  ...passiveActions,
])

const participantResourceActions = new Set([
  'draw', 'drawCards', 'gainEnergy', 'gainComboGauge', 'gainMaxEnergy', 'modifyCardCost',
])

const pilesByAction: Record<string, readonly string[]> = {
  exhaustCard: ['hand', 'draw', 'discard'],
  recoverFromDiscard: ['hand', 'exhaust'],
  recoverFromExhaust: ['hand', 'draw', 'discard'],
  upgrade: ['hand', 'draw', 'discard', 'exhaust'],
  addCard: ['hand', 'draw', 'discard', 'exhaust'],
  modifyCardCost: ['hand', 'draw', 'discard', 'exhaust'],
}

function isEffectObject(value: Record<string, unknown>): boolean {
  return typeof value.action === 'string'
}

function buildEffectForAction(
  current: Record<string, unknown>,
  action: string,
  meta: DevContentEnums,
  references: ReferenceCatalog,
  contentKind?: string,
): Record<string, unknown> {
  const schema = effectSchemas[action] ?? { required: ['scope', 'amount'], optional: commonBattleOptions }
  const required = contentKind === 'relics' && !schema.required.includes('trigger')
    ? [...schema.required, 'trigger' as EffectField]
    : schema.required
  const next: Record<string, unknown> = { action }

  for (const field of required) {
    next[field] = defaultEffectField(field, action, current, meta, references, contentKind)
  }
  for (const field of schema.optional ?? []) {
    if (Object.hasOwn(current, field)) next[field] = current[field]
  }
  for (const [key, fieldValue] of Object.entries(current)) {
    if (key === 'action' || knownEffectFields.has(key as EffectField)) continue
    next[key] = fieldValue
  }
  if (action !== 'modifyCardCost' && next.amountSource === 'currentCost') {
    delete next.amountSource
    if (next.amount === 0) next.amount = 1
  }

  return normalizeEffectSelection(normalizeEffectTrigger(normalizeEffectScope(next, meta)))
}

function defaultEffectField(
  field: EffectField,
  action: string,
  current: Record<string, unknown>,
  meta: DevContentEnums,
  references: ReferenceCatalog,
  contentKind?: string,
): unknown {
  switch (field) {
    case 'scope':
      if (action === 'attack' || action === 'debuff') return 'single'
      if (action === 'discard') return 'random'
      return 'self'
    case 'side': return action === 'attack' || action === 'debuff' ? 'enemy' : 'ally'
    case 'amount':
      if (action === 'modifyCardCost') return -1
      return typeof current.amount === 'number' ? current.amount : 1
    case 'name':
      if (action === 'unknownTileWeightDelta') return 'enemy'
      return firstStatusId(meta, action === 'debuff' ? 'Debuff' : 'Buff')
    case 'statusChanges': return [{
      name: firstStatusId(meta, action === 'debuff' ? 'Debuff' : 'Buff'),
      amount: 1,
    }]
    case 'unitId': return references.units?.[0]?.id ?? ''
    case 'cardRefId': return references.cards?.[0]?.id ?? ''
    case 'pile':
      if (action === 'recoverFromDiscard') return 'hand'
      return 'hand'
    case 'select': return action === 'discard' ? 'random' : 'choose'
    case 'cardCount': return 1
    case 'costDuration': return 'untilPlayed'
    case 'trigger':
      if (passiveActions.has(action)) return 'Passive'
      if (nonBattleActions.has(action)) return 'OnPickup'
      if (contentKind === 'relics') return 'OnBattleStart'
      return typeof current.trigger === 'string' ? current.trigger : null
    case 'amountSource': return null
    case 'comboMin': return 1
    case 'maxComboMin': return 1
    case 'chancePercent': return 100
    case 'battleOnly': return false
  }
}

function firstStatusId(meta: DevContentEnums, kind: 'Buff' | 'Debuff'): string {
  return meta.statuses.find(status => status.kind.toLowerCase() === kind.toLowerCase())?.id ?? ''
}

function normalizeEffectScope(
  effect: Record<string, unknown>,
  meta: DevContentEnums,
): Record<string, unknown> {
  const action = typeof effect.action === 'string' ? effect.action : ''
  const next = { ...effect }
  if (action === 'attack' && next.scope === 'self') next.scope = 'single'
  if (action === 'discard' && next.scope !== 'random' && next.scope !== 'all') next.scope = 'random'
  if (next.scope === 'self' || action === 'discard') {
    delete next.side
  } else if (action === 'attack') {
    next.side = 'enemy'
  } else if (action === 'heal' || participantResourceActions.has(action)) {
    next.side = 'ally'
  } else if (typeof next.side !== 'string') {
    next.side = action === 'debuff' ? 'enemy' : 'ally'
  }
  if ((action === 'buff' || action === 'debuff') && !Array.isArray(next.statusChanges)) {
    next.statusChanges = [{
      name: typeof next.name === 'string'
        ? next.name
        : firstStatusId(meta, action === 'debuff' ? 'Debuff' : 'Buff'),
      amount: typeof next.amount === 'number' ? next.amount : 1,
    }]
    delete next.name
    delete next.amount
    delete next.amountSource
  }
  return next
}

function normalizeEffectTrigger(effect: Record<string, unknown>): Record<string, unknown> {
  const next = { ...effect }
  if (next.trigger === 'OnHit') {
    if (typeof next.chancePercent !== 'number') next.chancePercent = 100
  } else {
    delete next.chancePercent
  }
  return next
}

function normalizeEffectSelection(effect: Record<string, unknown>): Record<string, unknown> {
  const next = { ...effect }
  if (next.action === 'modifyCardCost') {
    const isTriggered = typeof next.trigger === 'string' && next.trigger.length > 0
    if ((next.scope !== 'self' || isTriggered) && next.select === 'choose') next.select = 'all'
    if (next.select === 'all') delete next.cardCount
    else if (typeof next.cardCount !== 'number') next.cardCount = 1
  }
  return next
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isCardSpec(value: Record<string, unknown>, path: string): value is Record<string, unknown> & { effects: unknown[] } {
  if (!Array.isArray(value.effects)) return false
  return path === '$' || /\.versions\[\d+\]\.spec$/.test(path)
}

function move<T>(values: T[], from: number, to: number): T[] {
  const result = [...values]
  const [item] = result.splice(from, 1)
  result.splice(to, 0, item)
  return result
}

function newArrayItem(
  propertyName: string,
  path: string,
  meta: DevContentEnums,
  references: ReferenceCatalog,
  context?: Record<string, unknown>,
  contentKind?: string,
): unknown {
  switch (propertyName) {
    case 'effects': return path.includes('.choices[')
      ? { type: 'heal', amount: 1 }
      : { action: 'block', scope: 'self', amount: 1 }
    case 'abilities': case 'upgradedAbilities': return { type: 'finisher' }
    case 'moves': return { id: 'new_move', kind: 'Attack', nextMoveId: 'new_move', effects: [] }
    case 'choices': return { label: '新しい選択肢', effects: [], resultMessage: '' }
    case 'skills': return { id: 'new_skill', name: '新しい技', comboGaugeCost: 1, usesPerTurn: 1, effects: [] }
    case 'deck': return contentKind === 'units'
      ? { cardId: '', count: 1, upgraded: false, priority: 0 }
      : references.cards?.[0]?.id ?? ''
    case 'poolKinds': return meta.cardPoolKinds[0] ?? 'Reward'
    case 'eligibleCharacterIds': return references.characters?.[0]?.id ?? ''
    case 'ownedRelicIds': case 'potions': return ''
    case 'fixedRows': return { row: 1, kind: 'Rest' }
    case 'rowKindExclusions': return { row: 1, excludedKind: 'Rest' }
    case 'forbiddenConsecutive': return { first: 'Rest', second: 'Rest' }
    case 'tiers': return 1
    case 'keywords': case 'upgradedKeywords': return meta.keywords[0]?.id ?? ''
    case 'cardTypes': return meta.cardTypes.find(type => type === 'Skill' || type === 'Attack') ?? 'Skill'
    case 'requiredKeywords': return meta.keywords[0]?.id ?? ''
    case 'statusChanges': return {
      name: firstStatusId(meta, context?.action === 'debuff' ? 'Debuff' : 'Buff'),
      amount: 1,
    }
    default: return ''
  }
}

function newVersionItem(
  contentKind: string | undefined,
  versions: unknown[],
  document: Record<string, unknown> | undefined,
): Record<string, unknown> {
  const version = nextVersionId(versions)
  const activeVersion = typeof document?.activeVersion === 'string' ? document.activeVersion : ''
  const active = versions.find(entry => isObject(entry) && entry.version === activeVersion)
  const activeSpec = isObject(active) && isObject(active.spec) ? active.spec : null
  return {
    version,
    spec: activeSpec ? structuredClone(activeSpec) : defaultVersionSpec(contentKind),
  }
}

function nextVersionId(versions: unknown[]): string {
  const largest = versions.reduce<number>((max, entry) => {
    if (!isObject(entry) || typeof entry.version !== 'string') return max
    const match = /^v(\d+)$/.exec(entry.version)
    return match ? Math.max(max, Number(match[1])) : max
  }, 0)
  return `v${largest + 1}`
}

function defaultVersionSpec(contentKind: string | undefined): Record<string, unknown> {
  if (contentKind === 'cards') {
    return {
      rarity: 1,
      cardType: 'Skill',
      cost: 1,
      effects: [],
      upgradedEffects: [],
      keywords: [],
      upgradedKeywords: [],
    }
  }
  if (contentKind === 'relics') {
    return {
      rarity: 1,
      description: '',
      effects: [],
      implemented: true,
    }
  }
  return { effects: [] }
}

type KnownProperty = { key: string; value: unknown }

function knownPropertiesFor(
  contentKind: string | undefined,
  value: Record<string, unknown>,
  path: string,
  meta: DevContentEnums,
  references: ReferenceCatalog,
): KnownProperty[] {
  const candidates: KnownProperty[] = []
  const add = (key: string, propertyValue: unknown) => candidates.push({ key, value: propertyValue })

  if (contentKind === 'cards' && isCardSpec(value, path)) {
    if (typeof value.cost === 'number') add('upgradedCost', value.cost)
    add('description', '')
    add('upgradedDescription', '')
    add('comboGaugeCost', 0)
    add('usesPerTurn', 1)
    add('upgradedUsesPerTurn', 1)
    add('abilities', [])
    add('upgradedAbilities', [])
  }

  if (contentKind === 'cards' && /\.(?:upgradedA|a)bilities\[\d+\]$/.test(path)) {
    if (value.type === 'reprise') add('filter', {})
    if (value.type === 'contingency') add('trigger', { timing: 'TurnStart' })
  }
  if (contentKind === 'cards' && /\.(?:upgradedA|a)bilities\[\d+\]\.filter$/.test(path)) {
    add('cardTypes', [])
    add('minOriginalCost', 0)
    add('maxOriginalCost', 3)
    add('minPaidCost', 0)
    add('maxPaidCost', 3)
    add('requiredKeywords', [])
  }
  if (contentKind === 'cards' && /\.(?:upgradedA|a)bilities\[\d+\]\.trigger$/.test(path)
    && value.timing === 'ComboReached') add('comboThreshold', 1)

  if (typeof value.action === 'string') {
    const schema = effectSchemas[value.action]
      ?? { required: ['scope', 'amount'] as EffectField[], optional: commonBattleOptions }
    for (const field of [...schema.required, ...(schema.optional ?? [])]) {
      if (field === 'side' && value.scope === 'self') continue
      if (field === 'chancePercent' && value.trigger !== 'OnHit') continue
      if (field === 'cardCount' && value.select === 'all') continue
      add(field, defaultEffectField(field, value.action, value, meta, references, contentKind))
    }
  }

  if (contentKind === 'potions' && path === '$') add('usesPerTurn', 1)

  if (contentKind === 'characters' && /\.skills\[\d+\]$/.test(path)) {
    add('oncePerTurn', false)
    add('usesPerTurn', 1)
    add('description', '')
    add('requiresCardsInHand', false)
    add('keywords', [])
  }

  if (contentKind === 'characters' && path === '$') {
    add('characterClass', meta.characterClasses[0] ?? 'Striker')
    add('heightTier', 5)
    add('energyPerTurn', 3)
    add('drawPerTurn', 5)
    add('trait', {
      id: 'new_trait',
      name: '新しい特性',
      trigger: 'OnTurnStart',
      usesPerTurn: 1,
      description: '',
      effects: [],
    })
    add('skills', [])
  }
  if (contentKind === 'characters' && path === '$.trait') {
    add('id', 'new_trait')
    add('name', '新しい特性')
    add('trigger', meta.characterTraitTriggers[0] ?? 'OnTurnStart')
    if (value.trigger === 'OnComboReached') add('threshold', 1)
    add('usesPerTurn', 1)
    add('description', '')
    add('effects', [])
  }
  if (contentKind === 'enemies' && path === '$') add('heightTier', 5)
  if (contentKind === 'units' && path === '$') {
    add('heightTier', 5)
    add('initialEnergy', 1)
    add('energyPerTurn', 1)
    add('drawPerTurn', 3)
    add('handLimit', 5)
    add('aiProfileId', 'low_cost_combo')
    add('traitDescription', '')
    add('deck', [])
    add('skills', [])
    add('ownedRelicIds', [])
    add('potions', [])
  }
  if (contentKind === 'relics' && (/\.spec$/.test(path) || path === '$')) {
    add('description', '')
    add('implemented', true)
  }
  if (contentKind === 'events' && /\.choices\[\d+\]$/.test(path)) {
    add('condition', { type: 'minGold', amount: 1 })
  }
  if (contentKind === 'events' && path === '$') add('condition', { type: 'minGold', amount: 1 })
  if (contentKind === 'events' && /\.condition$/.test(path)) add('amount', 1)
  if (contentKind === 'events' && /\.effects\[\d+\]$/.test(path)) {
    if (value.type === 'gainRelicRandom') add('rarity', 1)
    else if (value.type === 'gainCard') add('cardId', references.cards?.[0]?.id ?? '')
    else if (value.type !== 'grantCardReward') add('amount', 1)
  }

  if ((contentKind === 'cards' || contentKind === 'relics') && path === '$') add('displayName', null)
  if (contentKind === 'cards' && path === '$') {
    add('poolKinds', ['Reward'])
    add('poolClass', 'Striker')
    add('eligibleCharacterIds', [])
  }
  if ((contentKind === 'cards' || contentKind === 'relics') && /\.versions\[\d+\]$/.test(path)) {
    add('createdAt', null)
    add('label', 'edited')
  }

  return candidates.filter(property => !Object.hasOwn(value, property.key))
}

function optionsFor(
  propertyName: string,
  context: Record<string, unknown> | undefined,
  path: string,
  meta: DevContentEnums,
  references: ReferenceCatalog,
): Array<{ value: string | number; label: string }> | null {
  const choices = (category: string, values: Array<string | number>) =>
    values.map(value => ({ value, label: optionLabel(meta, category, value) }))
  switch (propertyName) {
    case 'action': return choices('actions', meta.actions)
    case 'scope': {
      const action = typeof context?.action === 'string' ? context.action : ''
      if (action === 'attack') return choices('scopes', meta.scopes.filter(scope => scope !== 'self'))
      if (action === 'discard') return choices('scopes', meta.scopes.filter(scope => scope === 'random' || scope === 'all'))
      if (selfOnlyActions.has(action)) return choices('scopes', ['self'])
      return choices('scopes', meta.scopes)
    }
    case 'side': {
      const action = typeof context?.action === 'string' ? context.action : ''
      if (action === 'attack') return choices('sides', ['enemy'])
      if (action === 'heal' || participantResourceActions.has(action)) return choices('sides', ['ally'])
      return choices('sides', meta.sides)
    }
    case 'select': {
      const isRestrictedCardCostChoice = context?.action === 'modifyCardCost'
        && (context.scope !== 'self'
          || (typeof context.trigger === 'string' && context.trigger.length > 0))
      return choices('selects', meta.selects.filter(select =>
        select !== 'choose' || !isRestrictedCardCostChoice,
      ))
    }
    case 'pile': {
      const action = typeof context?.action === 'string' ? context.action : ''
      const allowed = pilesByAction[action]
      return choices('piles', allowed
        ? meta.piles.filter(pile => allowed.includes(pile))
        : meta.piles)
    }
    case 'trigger':
      if (path === '$.trait.trigger')
        return choices('characterTraitTriggers', meta.characterTraitTriggers)
      return choices('triggers', meta.triggers)
    case 'amountSource': {
      const action = typeof context?.action === 'string' ? context.action : ''
      return choices('amountSources', meta.amountSources.filter(source =>
        source !== 'currentCost' || action === 'modifyCardCost',
      ))
    }
    case 'cardType': {
      const hasTypedAbilities = (Array.isArray(context?.abilities) && context.abilities.length > 0)
        || (Array.isArray(context?.upgradedAbilities) && context.upgradedAbilities.length > 0)
      return choices('cardTypes', hasTypedAbilities
        ? meta.cardTypes.filter(cardType => cardType === 'Attack' || cardType === 'Skill')
        : meta.cardTypes)
    }
    case 'characterClass': return choices('characterClasses', meta.characterClasses)
    case 'poolClass': return choices('cardPoolClasses', meta.cardPoolClasses)
    case 'poolKinds': return choices('cardPoolKinds', meta.cardPoolKinds)
    case 'rarity': return typeof context?.rarity === 'number'
      ? choices('rarities', meta.rarities)
      : choices('eventRarities', meta.eventRarities)
    case 'act': case 'tiers': return choices('acts', [1, 2, 3])
    case 'tier': return choices('tiers', meta.tiers)
    case 'costDuration': return choices('costDurations', ['untilPlayed', 'untilTurnEnd'])
    case 'aiProfileId': return [{
      value: 'low_cost_combo',
      label: 'low_cost_combo(低コスト・コンボ優先)',
    }]
    case 'activeVersion': {
      if (!Array.isArray(context?.versions)) return null
      return context.versions.flatMap(version => {
        if (!isObject(version) || typeof version.version !== 'string') return []
        const suffix = typeof version.label === 'string' && version.label.length > 0
          ? `(${version.label})`
          : ''
        return [{ value: version.version, label: `${version.version}${suffix}` }]
      })
    }
    case 'kind': return 'row' in (context ?? {})
      ? choices('tileKinds', meta.tileKinds)
      : choices('moveKinds', ['Attack', 'Defend', 'Buff', 'Debuff', 'Multi'])
    case 'excludedKind': case 'first': case 'second': return choices('tileKinds', meta.tileKinds)
    case 'type':
      if (/\.(?:upgradedA|a)bilities\[\d+\]\.type$/.test(path)) {
        const siblings = Array.isArray(context?.__abilitySiblings)
          ? context.__abilitySiblings
          : []
        const currentIndex = typeof context?.__abilityIndex === 'number'
          ? context.__abilityIndex
          : -1
        return choices(
          'cardAbilities',
          availableCardAbilityTypes(meta.cardAbilities, siblings, currentIndex),
        )
      }
      return path.includes('.condition')
        ? choices('eventConditionTypes', meta.eventConditionTypes)
        : choices('eventEffectTypes', meta.eventEffectTypes)
    case 'timing': return choices('contingencyTriggers', meta.contingencyTriggerTimings)
    case 'cardTypes': return choices('cardTypes', meta.cardTypes)
    case 'requiredKeywords':
      return meta.keywords.map(keyword => ({ value: keyword.id, label: `${keyword.id}(${keyword.name})` }))
    case 'keywords': case 'upgradedKeywords':
      return meta.keywords.map(keyword => ({ value: keyword.id, label: `${keyword.id}(${keyword.name})` }))
    case 'name':
      if (context?.action === 'unknownTileWeightDelta') {
        return meta.tileKinds
          .filter(kind => ['Enemy', 'Elite', 'Merchant', 'Rest', 'Treasure', 'Event'].includes(kind))
          .map(kind => ({ value: kind.toLowerCase(), label: optionLabel(meta, 'tileKinds', kind) }))
      }
      return context?.action === 'buff' || context?.action === 'debuff'
        ? meta.statuses
            .filter(status => status.kind.toLowerCase() === String(context.action).toLowerCase())
            .map(status => ({ value: status.id, label: `${status.id}(${status.name})` }))
        : null
    case 'cardRefId': case 'cardId': return referenceSelect(references.cards)
    case 'deck': return referenceSelect(references.cards)
    case 'eligibleCharacterIds': return referenceSelect(references.characters)
    case 'unitId': return referenceSelect(references.units)
    case 'enemyId': case 'enemyIds': return referenceSelect(references.enemies)
    case 'relicId': case 'relicIds': case 'ownedRelicIds': return referenceSelect(references.relics)
    case 'potions': return referenceSelect(references.potions)
    default: return null
  }
}

function availableCardAbilityTypes(
  allTypes: string[],
  abilities: unknown[],
  currentIndex = -1,
): string[] {
  const currentType = currentIndex >= 0 && isObject(abilities[currentIndex])
    && typeof abilities[currentIndex].type === 'string'
    ? abilities[currentIndex].type
    : null
  const configured = new Set(abilities.flatMap((ability, index) =>
    index !== currentIndex && isObject(ability) && typeof ability.type === 'string'
      ? [ability.type]
      : [],
  ))
  return allTypes.filter(type => {
    if (type === currentType) return true
    if (configured.has(type)) return false
    if (type === 'reprise' && configured.has('contingency')) return false
    if (type === 'contingency' && configured.has('reprise')) return false
    return true
  })
}

function referenceSelect(items?: DevContentSummary[]): Array<{ value: string; label: string }> | null {
  return items?.map(item => ({ value: item.id, label: `${item.id}(${item.name})` })) ?? null
}

function buildEventEffectForType(
  current: Record<string, unknown>,
  type: string,
  references: ReferenceCatalog,
): Record<string, unknown> {
  const next: Record<string, unknown> = { type }
  if (type === 'gainRelicRandom') {
    next.rarity = typeof current.rarity === 'number' ? current.rarity : 1
  } else if (type === 'gainCard') {
    next.cardId = typeof current.cardId === 'string'
      ? current.cardId
      : references.cards?.[0]?.id ?? ''
  } else if (type !== 'grantCardReward') {
    next.amount = typeof current.amount === 'number' ? current.amount : 1
  }
  for (const [key, value] of Object.entries(current)) {
    if (key === 'type' || key === 'amount' || key === 'rarity' || key === 'cardId') continue
    next[key] = value
  }
  return next
}

function buildCardAbilityForType(type: string): Record<string, unknown> {
  if (type === 'reprise') return { type, filter: { cardTypes: ['Skill'], maxOriginalCost: 3 } }
  if (type === 'contingency') return { type, trigger: { timing: 'TurnStart' } }
  return { type: 'finisher' }
}

function optionLabel(meta: DevContentEnums, category: string, value: string | number): string {
  const id = String(value)
  const name = meta.optionLabels[`${category}:${id}`]
  return name ? `${id}(${name})` : `${id}(表示名未登録)`
}

function referencesFor(propertyName: string, references: ReferenceCatalog): DevContentSummary[] | null {
  if (propertyName === 'cardRefId' || propertyName === 'cardId' || propertyName === 'deck') return references.cards ?? null
  if (propertyName === 'unitId') return references.units ?? null
  if (propertyName === 'enemyId' || propertyName === 'enemyIds') return references.enemies ?? null
  if (propertyName === 'relicId' || propertyName === 'relicIds' || propertyName === 'ownedRelicIds') return references.relics ?? null
  if (propertyName === 'potions') return references.potions ?? null
  return null
}

function fieldId(path: string, key: string): string {
  return `json-${path}-${key}`.replace(/[^a-zA-Z0-9_-]/g, '-')
}

const LABELS: Record<string, string> = {
  id: 'ID', name: '名前', displayName: '表示名', description: '説明', flavor: 'フレーバー',
  rarity: 'レアリティ', cardType: 'カード種別', cost: 'コスト', effects: '効果', upgradedEffects: '強化後の効果',
  upgradedCost: '強化後コスト', upgradedDescription: '強化後説明', versions: 'バージョン', activeVersion: '使用中バージョン',
  hp: 'HP', maxHp: '最大HP', act: '層', tier: '区分', moves: '行動', initialMoveId: '最初の行動',
  deck: '初期デッキ', cardId: 'カードID', count: '枚数', upgraded: '強化済み', priority: 'CPU優先度',
  characterClass: 'キャラクタークラス', poolKinds: '用途', poolClass: '所属カードプール',
  eligibleCharacterIds: '取得可能キャラクター',
  trait: '特性', threshold: '発動しきい値',
  skills: '固有スキル', choices: '選択肢', pools: 'プール', relicIds: 'レリックID', ownedRelicIds: '所持レリック', potions: '所持ポーション', enemyIds: '敵ID',
  action: '効果種別', scope: '範囲', side: '対象側', amount: '数値', trigger: '発動条件', usesPerTurn: '本体の1ターン使用回数', upgradedUsesPerTurn: '強化版本体の1ターン使用回数',
  amountSource: '参照する変数', type: 'イベント処理', cardRefId: '参照カード', unitId: '召喚キャラクター',
  select: '選択方法', pile: '対象領域', cardCount: '対象カード枚数', costDuration: 'コスト変更期限',
  statusChanges: '付与する状態変化',
  keywords: 'キーワード', upgradedKeywords: '強化後キーワード', nextMoveId: '次の行動',
  tiers: '対象の層', startMessage: '開始メッセージ', resultMessage: '結果メッセージ',
  comboMin: '必要コンボ数', battleOnly: '戦闘中のみ', chancePercent: '発動確率（%）',
  maxComboMin: '乗勢（最大コンボ最小値）', abilities: '型付きカード能力', upgradedAbilities: '強化後の型付きカード能力',
  filter: '再演条件', cardTypes: '対象カード種別', minOriginalCost: '元コスト下限', maxOriginalCost: '元コスト上限',
  minPaidCost: '支払コスト下限', maxPaidCost: '支払コスト上限', requiredKeywords: '必須キーワード',
  timing: '伏策発動タイミング', comboThreshold: '達成コンボ数',
  comboGaugeCost: 'コンボゲージ消費', oncePerTurn: '1ターン1回（旧互換）',
  requiresCardsInHand: '手札必須', heightTier: '表示サイズ段階', lifetimeTurns: '生存ターン数（旧互換）',
  initialEnergy: '初期エナジー', energyPerTurn: 'ターン開始時エナジー回復量', drawPerTurn: 'ターン開始時ドロー枚数', handLimit: '手札上限',
  aiProfileId: 'CPU行動方針', traitDescription: '特性説明',
  implemented: '実装済み', condition: '選択条件', imageId: '画像ID', startingGold: '初期ゴールド',
  potionSlotCount: 'ポーション枠数', createdAt: '作成日時', version: 'バージョンID', label: '管理ラベル',
}

function fieldLabel(key: string): string {
  return LABELS[key] ?? key
}

function contextualFieldLabel(key: string, parent: Record<string, unknown>): string {
  if (key === 'name' && (parent.action === 'buff' || parent.action === 'debuff'))
    return 'ステータス'
  if (key === 'name' && parent.action === 'unknownTileWeightDelta')
    return 'タイル種別'
  if (key === 'usesPerTurn' && typeof parent.trigger === 'string'
    && typeof parent.id === 'string' && Array.isArray(parent.effects))
    return '特性の1ターン発動回数（0は無制限）'
  return fieldLabel(key)
}
