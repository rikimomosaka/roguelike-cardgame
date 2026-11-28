import { createContext, useContext } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { actorMetrics, actorSheet, type ActorKind } from '../presentation/assets'

/**
 * 敵カードのイラストに使う「持ち主」のキャラ。
 * 敵カード（ID が enemy_ で始まる）で専用のイラストが無いものは、持ち主のドット絵を背景色付きで表示する。
 * 敵の行動の再生や図鑑の行動デッキなど、持ち主が分かる画面で Provider を置く。
 */
export type CardArtOwner = { kind: ActorKind; imageId: string }

const CardArtOwnerContext = createContext<CardArtOwner | null>(null)

export function CardArtOwnerProvider({ owner, children }: { owner: CardArtOwner | null; children: ReactNode }) {
  return <CardArtOwnerContext.Provider value={owner}>{children}</CardArtOwnerContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useCardArtOwner(): CardArtOwner | null {
  return useContext(CardArtOwnerContext)
}

/** 敵カードかどうか（敵専用の枠・持ち主の絵を使う）。 */
// eslint-disable-next-line react-refresh/only-export-components
export function isEnemyCardId(cardId: string | null | undefined): boolean {
  return typeof cardId === 'string' && cardId.startsWith('enemy_')
}

/**
 * 背景の色: 攻撃だけ = 赤、防御（ブロックを得る）だけ = 青、
 * 両方を含む・どちらも含まない（ミス・強化・弱体化など）= 紫。
 */
export type OwnerArtTone = 'attack' | 'defense' | 'other'

// eslint-disable-next-line react-refresh/only-export-components
export function ownerArtTone(type: string, description: string | null | undefined): OwnerArtTone {
  const text = description ?? ''
  const attacks = type === 'attack' || text.includes('アタック')
  const defends = text.includes('ブロック')
  if (attacks && !defends) return 'attack'
  if (defends && !attacks) return 'defense'
  return 'other'
}

/** キャンバス 128 のうち体の範囲（ビルドが記録した寸法。無ければ中央の 64×64）。 */
function bodyBox(owner: CardArtOwner) {
  const m = actorMetrics(owner.kind, owner.imageId)
  const top = m?.top ?? 32
  const bottom = m?.bottom ?? 95
  const width = m?.footprint ?? 64
  const left = m?.left ?? Math.round((128 - width) / 2)
  return { left, top, width, height: bottom - top + 1 }
}

/**
 * 持ち主のドット絵を、色付きの背景の上に体が収まるよう拡大して置く。
 * 体の高さを枠の 78%、または幅を 88% に合わせる（小さい方）。
 */
export function OwnerCardArt({ owner, tone }: { owner: CardArtOwner; tone: OwnerArtTone }) {
  const box = bodyBox(owner)
  const style = {
    '--body-l': box.left,
    '--body-t': box.top,
    '--body-w': box.width,
    '--body-h': box.height,
  } as CSSProperties
  return (
    <div className={`card-owner-art card-owner-art--${tone}`} style={style} data-owner={owner.imageId}>
      <img src={actorSheet(owner.kind, owner.imageId)} alt="" draggable={false} />
    </div>
  )
}
