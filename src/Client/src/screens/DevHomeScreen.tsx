// Phase 10.5.I: 開発者専用 dev menu のトップ画面。
// import.meta.env.DEV ガードで本番ビルドからは tree-shake される想定。
// 後続 phase (10.5.J/K/L) で relic / potion / enemy / unit のリンクが追加される。

type Props = {
  /** ?dev=cards に切り替える */
  onOpenCards: () => void
  /** ?dev 解除 */
  onClose: () => void
}

export function DevHomeScreen({ onOpenCards, onClose }: Props) {
  return (
    <main className="dev-home">
      <h1>開発者メニュー (DEV ONLY)</h1>
      <ul>
        <li>
          <button type="button" onClick={onOpenCards}>
            Cards Viewer
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
