// Phase 10.5.I: 開発者専用 dev menu のトップ画面。
// import.meta.env.DEV ガードで本番ビルドからは tree-shake される想定。
// Phase 10.5.L1: relic editor link 追加。後続 phase (10.5.L2/L3/L4) で
// potion / enemy / unit のリンクが追加される。

type Props = {
  /** ?dev=cards に切り替える */
  onOpenCards: () => void
  /** ?dev=relics に切り替える (Phase 10.5.L1) */
  onOpenRelics: () => void
  /** ?dev 解除 */
  onClose: () => void
}

export function DevHomeScreen({ onOpenCards, onOpenRelics, onClose }: Props) {
  return (
    <main className="dev-home">
      <h1>開発者メニュー (DEV 環境限定)</h1>
      <ul>
        <li>
          <button type="button" onClick={onOpenCards}>
            カード編集
          </button>
        </li>
        <li>
          <button type="button" onClick={onOpenRelics}>
            レリック編集
          </button>
        </li>
      </ul>
      <p style={{ marginTop: 24 }}>
        <button type="button" onClick={onClose}>
          通常メニューへ戻る
        </button>
      </p>
    </main>
  )
}
