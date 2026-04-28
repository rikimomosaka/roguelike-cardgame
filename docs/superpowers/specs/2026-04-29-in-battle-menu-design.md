# 戦闘中ゲームメニュー (In-Battle Menu) Design

**Date:** 2026-04-29
**Phase:** 10.4 prep / 戦闘中 UX 補完
**Status:** Approved (brainstorm 2026-04-29)

## 1. 目的

`BattleScreen` の TopBar 右端「メニュー」ボタン (`onOpenMenu`) は現状 no-op
コメント (`/* battle 中の menu は後続フェーズで対応 */`) で放置されている。
プレイヤーが戦闘途中で「投了したい」「音量を変えたい」「いったんタイトルに
戻りたい」を実行できるよう、`MapScreen` で既に実装済みの `InGameMenuScreen`
を戦闘中も開けるようにする。

## 2. 非ゴール

- 戦闘中の events 再生を一時停止する pause 機能の追加（brainstorm Q2=B
  により今回は対象外、events は menu を開いていても継続再生される）。
- 戦闘中ポーション使用 UI の拡張（Phase 10.5 で対応予定）。
- タイトル復帰後の戦闘 resume 機能（本格 Phase 10.3 = SignalR + 永続化で対応）。
- メニューの新項目追加（既存 4 項目: 続ける / 音量設定 / メニューに戻る /
  あきらめる をそのまま使う）。

## 3. 既存実装の再利用

以下が既に揃っているので、戦闘中メニューは「配線追加」+「exit confirm 拡張」
で済む：

| 既存資産 | 場所 | 役割 |
|---|---|---|
| `InGameMenuScreen` | `src/Client/src/screens/InGameMenuScreen.tsx` | 続ける / 音量設定 / メニューに戻る / あきらめる の Popup モーダル + ホットキー (ESC/S/Q/X) |
| MapScreen の `menuOpen` state + `<InGameMenuScreen .../>` 配線 | `src/Client/src/screens/MapScreen.tsx:102, 757-767` | `setPendingFinish({kind:'abandon'})` までのフロー込み |
| TopBar の `onOpenMenu` / `menuActive` props | `src/Client/src/components/TopBar.tsx:55-56, 256-264` | メニューアイコンボタンが既に配置済み |
| `RunsController.PostAbandon` の戦闘 session cleanup | `src/Server/Controllers/RunsController.cs:368` | F2 で `_sessions.Remove(accountId)` 実装済 |

## 4. 配置方針

**配置案 B（採択）**: メニュー state は `MapScreen` に一元化。`BattleScreen`
は TopBar の `onOpenMenu` を上から流れてきた prop で叩くだけ。

理由:
- メニューの abandon/exit ハンドラ（`pendingFinish` 経由）は既に MapScreen
  に実装されており、`activeBattle` の有無に関わらず正しく動作する。
- `InGameMenuScreen` を MapScreen 側で 1 箇所だけ render することで、
  BattleScreen / MapScreen どちらの TopBar からも同じ menu インスタンスが
  開く（state 重複なし、abandon ハンドラ配線も一箇所）。
- BattleScreen が unmount しても menu は MapScreen に属するので影響なし。

不採択案 A（BattleScreen にローカル menu state を持つ）は、abandon 時に
result を MapScreen に bubble する追加 prop が必要で、かつ menu state が
2 系統に分かれて整合性管理が増えるため採らない。

## 5. UX 仕様

### 5.1 メニューを開くきっかけ
- TopBar 右端のメニューアイコンボタン（既存）。
- ESC キー（戦闘中、menu 未オープン時）。MapScreen と同じ挙動。

### 5.2 メニューを閉じるきっかけ
- 「続ける」ボタン
- ESC キー（`Popup closeOnEsc` が既存で処理）
- いずれかのアクション完了（exit / abandon）

### 5.3 events 再生中に開かれた場合 (Q2=B)
- BattleScreen の events 220ms 順次再生はそのまま継続する。
- menu は modal なので背面のカードクリック・ドラッグは Popup の背景でブロック。
- メニューを閉じれば、その時点までに進んだ state がそのまま見える。

### 5.4 「メニューに戻る」のガード (Q1=B)
- 現状 MVP は in-memory BattleSession なので、戦闘中にタイトルへ戻ると戦闘
  進行は失われる（次回ログイン時に同じノードを踏み直しになる）。
- 警告 confirm を出す:
  - 「戦闘進行は失われます。それでもタイトルに戻りますか？」
  - キャンセル → main mode に戻る
  - OK → 既存の `exit()` フロー（heartbeat + `onExitToMenu()`）
- MapScreen から（戦闘中以外で）開かれた menu は今まで通り即 exit。

### 5.5 「あきらめる」(放棄)
- 既存の `confirm-abandon` フローをそのまま使う。
- `abandonRun()` → `RunsController.PostAbandon` → 戦闘 session も
  `_sessions.Remove` で cleanup 済 (F2)。
- `onAbandon(result)` で `setPendingFinish({kind:'abandon', result})` が
  発火、ラン終了画面へ遷移。

### 5.6 timer (battleTicks)
- TopBar の経過時間 (`playSeconds + battleTicks`) は menu 表示中も止めない。
- 理由: Q2=B と整合（events 継続 → 時間も継続）。プレイヤーが menu に
  滞在した時間も実プレイ時間としてカウントする。

## 6. コンポーネント変更

### 6.1 `MapScreen.tsx`

```tsx
{menuOpen && (
  <InGameMenuScreen
    onClose={() => setMenuOpen(false)}
    onExitToMenu={onExitToMenu}
    onAbandon={(result) => {
      setMenuOpen(false)
      setPendingFinish({ kind: 'abandon', result })
    }}
    elapsedSecondsRef={mountedAt}
    requireExitConfirm={activeBattle != null}   // ← 追加
  />
)}
```

`BattleScreen` を render している箇所に menu props を渡す:

```tsx
<BattleScreen
  ...
  menuOpen={menuOpen}
  onOpenMenu={() => setMenuOpen(v => !v)}
/>
```

### 6.2 `BattleScreen.tsx`

新 props:

```ts
type Props = {
  // ...既存...
  menuOpen?: boolean
  onOpenMenu?: () => void
}
```

TopBar への配線（既存の no-op を置き換え）:

```tsx
<TopBar
  ...
  onOpenMenu={onOpenMenu ?? (() => {})}
  menuActive={menuOpen ?? false}
/>
```

ESC ハンドラ:

```tsx
useEffect(() => {
  if (menuOpen) return   // 開いている間は Popup 側の closeOnEsc が処理する
  const onKeyDown = (e: KeyboardEvent) => {
    if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return
    const tag = (e.target as HTMLElement | null)?.tagName
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return
    if (e.key === 'Escape') {
      e.preventDefault()
      onOpenMenu?.()
    }
  }
  window.addEventListener('keydown', onKeyDown)
  return () => window.removeEventListener('keydown', onKeyDown)
}, [menuOpen, onOpenMenu])
```

その他の hotkey が将来追加された場合は `if (menuOpen) return` で guard する。
現状 BattleScreen には数字キー hotkey 等は無い（カードはクリック / ドラッグ
のみ）ので追加 guard は不要。

### 6.3 `InGameMenuScreen.tsx`

新 prop:

```ts
type Props = {
  // ...既存...
  /** 戦闘中等で「メニューに戻る」を押した時に確認ダイアログを挟むか */
  requireExitConfirm?: boolean
}

type Mode = 'main' | 'settings' | 'confirm-abandon' | 'confirm-exit'
```

「メニューに戻る」ボタンの onClick を分岐:

```tsx
onClick={() => {
  if (requireExitConfirm) setMode('confirm-exit')
  else void exit()
}}
```

ホットキー Q も同様に `requireExitConfirm` で分岐 (`exit()` の代わりに
`setMode('confirm-exit')`)。

新 mode `confirm-exit` の Popup（既存 `confirm-abandon` と同じパターン）:

```tsx
if (mode === 'confirm-exit') {
  return (
    <Popup
      open
      variant="confirm"
      title="メニューに戻る"
      width={420}
      closeOnEsc={false}
      footer={
        <div className="im-confirm-foot">
          <button
            type="button"
            className="im-confirm-btn im-confirm-btn--cancel"
            onClick={() => setMode('main')}
            disabled={busy}
          >キャンセル</button>
          <button
            type="button"
            className="im-confirm-btn im-confirm-btn--danger"
            onClick={() => void exit()}
            disabled={busy}
          >タイトルへ戻る</button>
        </div>
      }
    >
      <p className="im-confirm-body">
        戦闘進行は <em>失われます</em>。<br />
        次回ログイン時、このマスを最初から踏み直すことになります。<br />
        それでもタイトルに戻りますか？
      </p>
    </Popup>
  )
}
```

### 6.4 CSS

`InGameMenuScreen.css` に追加スタイルは不要（`im-confirm-*` クラスを
そのまま流用できる）。

## 7. データフロー

```
[BattleScreen TopBar の menu ボタン押下]
   ↓
   props.onOpenMenu()  (= MapScreen.setMenuOpen(v => !v))
   ↓
[MapScreen menuOpen=true]
   ↓
[MapScreen が <InGameMenuScreen requireExitConfirm={activeBattle != null} ... /> を render]
   ↓
   BattleScreen の TopBar の上に Popup モーダルが重なる (z-index)
   ↓
+--- 「続ける」 → onClose() → setMenuOpen(false)
+--- 「音量設定」 → SettingsScreen embedded
+--- 「メニューに戻る」 → confirm-exit mode (戦闘中) or 直接 exit
|       ↓ OK
|       heartbeat() + onExitToMenu()
+--- 「あきらめる」 → confirm-abandon mode
        ↓ OK
        abandonRun() → onAbandon(result) → setPendingFinish({kind:'abandon'})
        (Server 側で _sessions.Remove(accountId) も実行)
```

## 8. テスト

### 8.1 既存 `InGameMenuScreen.test.tsx` への追加

- `requireExitConfirm=true` で「メニューに戻る」ボタンを押すと
  confirm-exit Popup が表示される。
- confirm-exit の「キャンセル」で main mode に戻る。
- confirm-exit の OK で `heartbeat` と `onExitToMenu` が呼ばれる
  （既存の Q ホットキー直 exit と等価）。
- `requireExitConfirm=false`（既存呼び出し）の挙動は変わらず即 exit する。
- ホットキー Q も `requireExitConfirm` で confirm-exit に分岐する。

### 8.2 `BattleScreen.test.tsx`（新規 or 既存に追加）

- `menuOpen` prop が true の時、TopBar の menu ボタンに
  `aria-pressed="true"` が付く（既存 `menuActive` の透過）。
- ESC キー push で `onOpenMenu` が呼ばれる。
- `menuOpen=true` の時に ESC を押しても `onOpenMenu` は再呼び出しされない
  （Popup 側に処理を委譲）。
- INPUT/TEXTAREA にフォーカスがある時は ESC を捕まえない。

### 8.3 手動確認チェックリスト

- 戦闘開始 → TopBar メニューボタン → InGameMenuScreen が出る
- ESC でも開く / 閉じる
- 「メニューに戻る」→ 警告 confirm が出る
- 「あきらめる」→ 既存の確認 → 放棄 → ラン終了画面へ
- menu 開いている間、events 再生は継続 (HP 減少アニメ等が裏で進む)
- battleTicks (TopBar 時計) も menu 中継続

## 9. ロールアウト

破壊的変更なし。`requireExitConfirm` のデフォルトが false なので、MapScreen
側の既存呼び出しは挙動変わらず。`InGameMenuScreen.test.tsx` の既存テストも
すべて pass のまま。

## 10. 関連ドキュメント

- 親 spec: `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md`
- Phase 10.3-MVP: `docs/superpowers/specs/2026-04-27-phase10-3-mvp-design.md`
  （§7-2 ロードマップで MVP 制約 = 戦闘中タイトル復帰時の戦闘ロスト
  については本格 10.3 で resume 対応予定）
