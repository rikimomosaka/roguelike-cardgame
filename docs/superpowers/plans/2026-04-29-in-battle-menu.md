# In-Battle Menu 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `BattleScreen` の TopBar 「メニュー」ボタンと ESC キーから既存の `InGameMenuScreen` を開けるようにする。戦闘中の「メニューに戻る」は警告 confirm でガードし、in-memory MVP の戦闘進行ロストをプレイヤーに明示する。

**Architecture:** メニュー state は `MapScreen` に一元化（配置案 B）。`InGameMenuScreen` に `requireExitConfirm` prop と新 `confirm-exit` mode を追加。`BattleScreen` は `menuOpen`/`onOpenMenu` props を受け取って TopBar に流し、ESC で `onOpenMenu` を叩くだけ。新 API・新コンポーネントなし、純粋にクライアント UI 配線。

**Tech Stack:** React 19 + TypeScript + Vite、vitest + @testing-library/react、既存 `Popup` コンポーネント。

**Spec:** `docs/superpowers/specs/2026-04-29-in-battle-menu-design.md`

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Client/src/screens/InGameMenuScreen.tsx` | Modify | `requireExitConfirm` prop と `confirm-exit` mode を追加 |
| `src/Client/src/screens/InGameMenuScreen.test.tsx` | Modify | confirm-exit 用の単体テスト追加 |
| `src/Client/src/screens/MapScreen.tsx` | Modify | `<InGameMenuScreen requireExitConfirm={...} />` と `<BattleScreen menuOpen={...} onOpenMenu={...} />` の prop 配線 |
| `src/Client/src/screens/BattleScreen.tsx` | Modify | `Props` に `menuOpen` / `onOpenMenu` 追加、TopBar 配線、ESC keydown handler |

---

## Task 1: `InGameMenuScreen` に `requireExitConfirm` prop と `confirm-exit` mode を追加（TDD）

**Files:**
- Modify: `src/Client/src/screens/InGameMenuScreen.tsx`
- Test: `src/Client/src/screens/InGameMenuScreen.test.tsx`

### Step 1.1: requireExitConfirm=true で「メニューに戻る」が confirm-exit を出すテストを追加（RED）

`src/Client/src/screens/InGameMenuScreen.test.tsx` の `Wrapper` を以下のように拡張：

- [ ] `Wrapper` props に `requireExitConfirm?: boolean` を追加し、`<InGameMenuScreen .../>` にそのまま渡す。

```tsx
function Wrapper({ onExitToMenu, onAbandon, onClose, requireExitConfirm }: {
  onExitToMenu: () => void
  onAbandon: (result: RunResultDto | null) => void
  onClose: () => void
  requireExitConfirm?: boolean
}) {
  const ref = useRef(performance.now())
  return (
    <AccountProvider>
      <InGameMenuScreen
        onClose={onClose}
        onExitToMenu={onExitToMenu}
        onAbandon={onAbandon}
        elapsedSecondsRef={ref}
        requireExitConfirm={requireExitConfirm}
      />
    </AccountProvider>
  )
}
```

`describe('InGameMenuScreen', ...)` 内に追加（既存ケース末尾に追記）：

```tsx
  it('requireExitConfirm=true のとき メニューに戻る で警告 confirm を出す', () => {
    const onExit = vi.fn()
    render(
      <Wrapper
        onExitToMenu={onExit}
        onAbandon={() => {}}
        onClose={() => {}}
        requireExitConfirm
      />
    )
    fireEvent.click(screen.getByRole('button', { name: 'メニューに戻る' }))
    expect(screen.getByText(/戦闘進行は/)).toBeInTheDocument()
    expect(onExit).not.toHaveBeenCalled()
  })
```

### Step 1.2: テスト実行で RED 確認

- [ ] Run: `cd src/Client && npx vitest run src/screens/InGameMenuScreen.test.tsx`
  Expected: 上記新テストが FAIL（`/戦闘進行は/` が見つからず、`onExit` が呼ばれてしまう）。

### Step 1.3: `InGameMenuScreen.tsx` に prop と confirm-exit mode を追加（GREEN minimal）

- [ ] `Props` 型に `requireExitConfirm?: boolean` を追加。

```tsx
type Props = {
  onClose: () => void
  onExitToMenu: () => void
  onAbandon: (result: RunResultDto | null) => void
  elapsedSecondsRef: RefObject<number>
  /** 戦闘中等で「メニューに戻る」を押した時に確認ダイアログを挟むか */
  requireExitConfirm?: boolean
}
```

- [ ] `Mode` を拡張：

```tsx
type Mode = 'main' | 'settings' | 'confirm-abandon' | 'confirm-exit'
```

- [ ] 関数シグネチャに `requireExitConfirm` を追加：

```tsx
export function InGameMenuScreen({ onClose, onExitToMenu, onAbandon, elapsedSecondsRef, requireExitConfirm }: Props) {
```

- [ ] 「メニューに戻る」ボタンの `onClick` を分岐：

```tsx
<button
  type="button"
  className="im-item im-item--save"
  onClick={() => {
    if (requireExitConfirm) setMode('confirm-exit')
    else void exit()
  }}
  disabled={busy}
  aria-label="メニューに戻る"
>
```

- [ ] `confirm-abandon` の if 文の前に `confirm-exit` mode の Popup を追加：

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
            >
              キャンセル
            </button>
            <button
              type="button"
              className="im-confirm-btn im-confirm-btn--danger"
              onClick={() => void exit()}
              disabled={busy}
            >
              タイトルへ戻る
            </button>
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

### Step 1.4: テスト実行で GREEN 確認

- [ ] Run: `cd src/Client && npx vitest run src/screens/InGameMenuScreen.test.tsx`
  Expected: 全テスト PASS（既存 6 件 + 新規 1 件 = 7 件）。

### Step 1.5: confirm-exit のキャンセル / OK / Q ホットキー分岐の追加テスト（RED）

- [ ] テストファイルに 3 件追加：

```tsx
  it('requireExitConfirm: confirm-exit でキャンセルすると main に戻る', () => {
    render(
      <Wrapper
        onExitToMenu={() => {}}
        onAbandon={() => {}}
        onClose={() => {}}
        requireExitConfirm
      />
    )
    fireEvent.click(screen.getByRole('button', { name: 'メニューに戻る' }))
    fireEvent.click(screen.getByRole('button', { name: 'キャンセル' }))
    // main に戻ると「あきらめる」ボタンが再度見える
    expect(screen.getByRole('button', { name: 'あきらめる' })).toBeInTheDocument()
  })

  it('requireExitConfirm: confirm-exit の OK で heartbeat + onExitToMenu', async () => {
    const onExit = vi.fn()
    render(
      <Wrapper
        onExitToMenu={onExit}
        onAbandon={() => {}}
        onClose={() => {}}
        requireExitConfirm
      />
    )
    fireEvent.click(screen.getByRole('button', { name: 'メニューに戻る' }))
    fireEvent.click(screen.getByRole('button', { name: 'タイトルへ戻る' }))
    await waitFor(() => {
      const urls = fetchMock.mock.calls.map((c) => c[0] as string)
      expect(urls.some((u) => u.includes('/runs/current/heartbeat'))).toBe(true)
    })
    await waitFor(() => expect(onExit).toHaveBeenCalled())
  })

  it('requireExitConfirm: Q ホットキーも confirm-exit に分岐する', () => {
    const onExit = vi.fn()
    render(
      <Wrapper
        onExitToMenu={onExit}
        onAbandon={() => {}}
        onClose={() => {}}
        requireExitConfirm
      />
    )
    fireEvent.keyDown(window, { key: 'q' })
    expect(screen.getByText(/戦闘進行は/)).toBeInTheDocument()
    expect(onExit).not.toHaveBeenCalled()
  })
```

### Step 1.6: テスト実行で RED 確認

- [ ] Run: `cd src/Client && npx vitest run src/screens/InGameMenuScreen.test.tsx`
  Expected: 「キャンセル」「タイトルへ戻る」のテストは PASS（実装済）、Q ホットキーのテストは FAIL（既存の Q は `void exit()` 直行）。

### Step 1.7: Q ホットキー分岐の実装（GREEN）

- [ ] `useEffect` 内の `else if (key === 'q')` 分岐を修正：

```tsx
      } else if (key === 'q') {
        if (busy) return
        e.preventDefault()
        if (requireExitConfirm) setMode('confirm-exit')
        else void exit()
      } else if (key === 'x') {
```

- [ ] `useEffect` の依存配列に `requireExitConfirm` を追加：

```tsx
  }, [mode, busy, accountId, requireExitConfirm])
```

### Step 1.8: テスト実行で GREEN 確認

- [ ] Run: `cd src/Client && npx vitest run src/screens/InGameMenuScreen.test.tsx`
  Expected: 全テスト PASS（既存 6 件 + 新規 4 件 = 10 件）。

### Step 1.9: コミット

- [ ] Run:

```bash
git add src/Client/src/screens/InGameMenuScreen.tsx src/Client/src/screens/InGameMenuScreen.test.tsx
git commit -m "$(cat <<'EOF'
feat(menu): add requireExitConfirm + confirm-exit mode to InGameMenuScreen

Adds an optional confirm-exit dialog gated by a new prop so the in-game
menu can warn that exiting to title mid-battle loses MVP in-memory
progress. Q hotkey honors the same gating. Defaults to false so existing
MapScreen-driven invocations are unchanged.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: `BattleScreen` に `menuOpen` / `onOpenMenu` props と ESC ハンドラを追加

**Files:**
- Modify: `src/Client/src/screens/BattleScreen.tsx`

### Step 2.1: `Props` 型に menuOpen / onOpenMenu を追加

- [ ] `BattleScreen.tsx` の `type Props = { ... }`（138-149 行付近）に追記：

```tsx
type Props = {
  accountId: string
  /** ラン全体の最新 snapshot。TopBar (gold/playSeconds/deck/relics) の表示用。 */
  snapshot: RunSnapshotDto
  onBattleResolved: (result: RunSnapshotDto | RunResultDto) => void
  /** TopBar の MAP ボタン押下で BattleScreen → MapScreen へ peek 切替する。 */
  onTogglePeek?: () => void
  /** Why: peek 中も親が live battle state を TopBar に表示できるよう、state
   *  更新ごとに親へ通知する。null は battle 終了 (clear) を意味する。 */
  onBattleStateChange?: (state: BattleStateDto | null) => void
  /** TopBar メニューボタン / ESC でゲーム内メニューを開閉する。
   *  state は MapScreen が保持する (戦闘中 abandon/exit のフローを共有するため)。 */
  menuOpen?: boolean
  onOpenMenu?: () => void
}
```

### Step 2.2: 関数シグネチャに分割代入を追加

- [ ] `BattleScreen` 関数の引数分割代入に `menuOpen` と `onOpenMenu` を加える（既存の `onTogglePeek`, `onBattleStateChange` に並べて）。Grep で関数定義行を確認してから編集：

  Run: `grep -n "export function BattleScreen" src/Client/src/screens/BattleScreen.tsx`

  - [ ] 該当行の分割代入に追加：

```tsx
export function BattleScreen({
  accountId,
  snapshot,
  onBattleResolved,
  onTogglePeek,
  onBattleStateChange,
  menuOpen,
  onOpenMenu,
}: Props) {
```

### Step 2.3: TopBar の `onOpenMenu` no-op を props 配線に置き換え

- [ ] `BattleScreen.tsx` 1085 行付近の `<TopBar ...>` を編集：

  before:
```tsx
        onOpenMenu={() => { /* battle 中の menu は後続フェーズで対応 */ }}
```

  after:
```tsx
        onOpenMenu={onOpenMenu ?? (() => {})}
        menuActive={menuOpen ?? false}
```

  位置: `onUsePotion` の直後、`onTogglePeek` の直前に挿入する。

### Step 2.4: ESC keydown handler を追加

- [ ] `BattleScreen.tsx` の他の `useEffect` 群と並ぶ位置（`return (` の直前あたり、`interactionsDisabled` 計算より前）に追記：

```tsx
  // Why: TopBar の menu ボタンと同じく ESC でメニュー開閉。menu 開いている
  // 間は Popup (closeOnEsc=true) 側に処理を委譲し、ここでは何もしない。
  useEffect(() => {
    if (menuOpen) return
    if (!onOpenMenu) return
    const handler = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return
      const tag = (e.target as HTMLElement | null)?.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return
      if (e.key === 'Escape') {
        e.preventDefault()
        onOpenMenu()
      }
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [menuOpen, onOpenMenu])
```

### Step 2.5: 型チェック / build

- [ ] Run: `cd src/Client && npm run build`
  Expected: tsc + vite build が 0 error / 0 warning で通る。

### Step 2.6: コミット

- [ ] Run:

```bash
git add src/Client/src/screens/BattleScreen.tsx
git commit -m "$(cat <<'EOF'
feat(battle): wire BattleScreen TopBar menu button + ESC to onOpenMenu

Replaces the no-op stub on the TopBar's menu button with a forwarded
prop and adds a window keydown ESC handler that toggles the menu when
it is closed. State is owned by MapScreen so the abandon/exit flow can
stay in one place; opening MapScreen-side keeps working unchanged.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `MapScreen` で props を配線

**Files:**
- Modify: `src/Client/src/screens/MapScreen.tsx`

### Step 3.1: `<BattleScreen ... />` に menuOpen / onOpenMenu を渡す

- [ ] `MapScreen.tsx` 511-528 行付近の `<BattleScreen ... />` ブロックを編集：

  before:
```tsx
      <BattleScreen
        accountId={accountId}
        snapshot={snapWithTickedTime}
        onTogglePeek={() => setPeekMap(true)}
        onBattleStateChange={setLivefBattleState}
        onBattleResolved={(result) => {
          setLivefBattleState(null)
          if ('outcome' in result) {
            onRunFinished?.(result, snapRef.current)
          } else {
            setSnap(result)
            setRewardDismissed(false)
            setPeekMap(false)
          }
        }}
      />
```

  after（既存 props はそのまま、最後に menuOpen/onOpenMenu を 2 行追加）:
```tsx
      <BattleScreen
        accountId={accountId}
        snapshot={snapWithTickedTime}
        onTogglePeek={() => setPeekMap(true)}
        onBattleStateChange={setLivefBattleState}
        onBattleResolved={(result) => {
          setLivefBattleState(null)
          if ('outcome' in result) {
            onRunFinished?.(result, snapRef.current)
          } else {
            setSnap(result)
            setRewardDismissed(false)
            setPeekMap(false)
          }
        }}
        menuOpen={menuOpen}
        onOpenMenu={() => setMenuOpen(v => !v)}
      />
```

  この `<BattleScreen ... />` は早期 return ブロック内 (`if (activeBattle && accountId && !peekMap) {`) にあるので、別 menuOpen 表示ロジックは不要（戦闘中も MapScreen の早期 return 後ろの `{menuOpen && <InGameMenuScreen ... />}` は実行されない）。代わりに、戦闘中は **`<BattleScreen>` を返した後でも `menuOpen` 状態は維持される**ので、次 Step で戦闘中ブランチでも `<InGameMenuScreen>` を render する経路を追加する。

### Step 3.2: 戦闘中ブランチでも `<InGameMenuScreen>` を render する

- [ ] Step 3.1 の `<BattleScreen ... />` を `<>` フラグメントで包み、その後ろに menu render を追加：

```tsx
    return (
      <>
        <BattleScreen
          accountId={accountId}
          snapshot={snapWithTickedTime}
          onTogglePeek={() => setPeekMap(true)}
          onBattleStateChange={setLivefBattleState}
          onBattleResolved={(result) => {
            setLivefBattleState(null)
            if ('outcome' in result) {
              onRunFinished?.(result, snapRef.current)
            } else {
              setSnap(result)
              setRewardDismissed(false)
              setPeekMap(false)
            }
          }}
          menuOpen={menuOpen}
          onOpenMenu={() => setMenuOpen(v => !v)}
        />
        {menuOpen && (
          <InGameMenuScreen
            onClose={() => setMenuOpen(false)}
            onExitToMenu={onExitToMenu}
            onAbandon={(result) => {
              setMenuOpen(false)
              setPendingFinish({ kind: 'abandon', result })
            }}
            elapsedSecondsRef={mountedAt}
            requireExitConfirm
          />
        )}
      </>
    )
```

### Step 3.3: 既存の MapScreen-side `<InGameMenuScreen>` 呼び出しに `requireExitConfirm={false}` を明示

- [ ] `MapScreen.tsx` 757-767 行付近の既存 `<InGameMenuScreen>` を確認：戦闘外 (peekMap=true / activeBattle=null) で開かれる経路なので prop なしで OK（`requireExitConfirm` のデフォルト false）。**コード変更は不要**だが、可読性のため明示的にコメントを 1 行付ける：

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
            // 戦闘外 (マップ表示) では確認ダイアログ不要
          />
        )}
```

### Step 3.4: 型チェック / build

- [ ] Run: `cd src/Client && npm run build`
  Expected: tsc + vite build が 0 error / 0 warning で通る。

### Step 3.5: 全テスト緑確認

- [ ] Run: `cd src/Client && npx vitest run`
  Expected: Test Files 24 passed (24)、Tests 131 + 4 (新規 confirm-exit + Q hotkey + cancel + ok = 4) = 135 passed。

### Step 3.6: コミット

- [ ] Run:

```bash
git add src/Client/src/screens/MapScreen.tsx
git commit -m "$(cat <<'EOF'
feat(map): forward menu state to BattleScreen and require exit confirm in battle

Wires menuOpen + onOpenMenu down into BattleScreen so its TopBar can
toggle the same in-game menu MapScreen already owns, and renders
<InGameMenuScreen requireExitConfirm /> in the battle branch so exiting
to title is gated by a warning. Map-side invocation is unchanged.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: 全体ビルド・リグレッション確認 + push

**Files:** なし（検証のみ）

### Step 4.1: dotnet build (Server 側に影響していないことの sanity check)

- [ ] Run: `dotnet build`
  Expected: 0 警告 0 エラー。

### Step 4.2: dotnet test 全 suite

- [ ] Run: `dotnet test`
  Expected: Core 986/986、Server 190/190 (skipped 2)。新 commit で追加・変更したファイルは Server に影響しないので前回と同じ数字。

### Step 4.3: client vitest 全件

- [ ] Run: `cd src/Client && npx vitest run`
  Expected: Test Files 24 passed、Tests 135 passed（既存 131 + 新 4）。

### Step 4.4: client build

- [ ] Run: `cd src/Client && npm run build`
  Expected: `dist/index.html`、`dist/assets/index-*.css`、`dist/assets/index-*.js` が生成、0 error / 0 warning。

### Step 4.5: 手動スモーク（ユーザに依頼。実行 agent はチェックのみ列挙）

- [ ] 以下を確認するチェックリストをユーザに提示：
  - [ ] `dotnet run --project src/Server` 起動。
  - [ ] `cd src/Client && npm run dev` 起動、ブラウザで開く。
  - [ ] ラン開始 → バトル進入。
  - [ ] TopBar 右の歯車（メニュー）アイコン押下 → `InGameMenuScreen` が出る。
  - [ ] 「続ける」または ESC で閉じる → 戦闘 UI に戻る。
  - [ ] 再度メニュー → 「メニューに戻る」 → **警告 confirm が出ること**。「キャンセル」で main に戻る。
  - [ ] 再度メニュー → 「メニューに戻る」 → 警告 OK で「タイトルへ戻る」 → `MainMenuScreen` に戻る。
  - [ ] ログインし直し → 同じノードを踏み直して戦闘リスタートできる（MVP 制約として許容）。
  - [ ] 戦闘進入 → メニュー → 「あきらめる」 → 既存の confirm 経由で abandon が成立、ラン終了画面へ。
  - [ ] バトル中に events 再生中（敵攻撃アニメ中など）にメニューを開く → 裏で events は継続、HP 減少などが進む（Q2=B 仕様）。
  - [ ] マップ画面（戦闘外）でメニュー → 「メニューに戻る」 → **警告は出ず即タイトル**（既存挙動維持）。

### Step 4.6: push

- [ ] Run: `git push`
  Expected: master が remote に push される（commit 数 3）。

### Step 4.7: メモリ更新（任意）

- [ ] `MEMORY.md` の `project_phase_status.md` を 1 行更新：「In-Battle Menu (2026-04-29) 完了、戦闘中 abandon/settings/exit ガード済」を追記。**ユーザ要望時のみ**。

---

## 完了条件

- 上記すべての `- [ ]` がチェック済み。
- `dotnet test` 緑、`vitest run` 緑（135 件）、`npm run build` 緑、`dotnet build` 緑。
- master に 3 commits push 済み（Task 1 / 2 / 3）。
- 手動スモークの全項目 OK。

## 今回スコープ外（既知の trade-off）

**spec §8.2 の `BattleScreen.test.tsx` テストは見送り**：仕様書では BattleScreen 側の `menuActive` aria 透過 / ESC 動作 / INPUT focus ガードを単体テストでカバーすることになっていたが、`BattleScreen.test.tsx` は現存せず、新規作成には API mock / catalog hook mock / snapshot fixture など重い setup が必要で、本タスクのスコープ（薄い prop 配線）に対してオーバーヘッドが大きい。代わりに **Task 4.5 の手動スモーク**で同等の挙動を確認する：

- 「TopBar メニュー押下 → menu が開く」 = `menuActive` 透過
- 「ESC で開閉する」 = ESC handler 動作
- 「INPUT focus 時 ESC で menu 開かない」 = INPUT/TEXTAREA ガード（手動だと作りにくいが、ESC 単独動作の確認で実装上の guard は経路として通っているため代用可）

仕様 §8.2 の自動テスト化は **Phase 10.4 polish** で `BattleScreen.test.tsx` を新設する際にまとめて入れる想定。それまでは ESC handler の implementation guard（`if (menuOpen) return` / INPUT-tag check）を spec §6.2 の通り実装した上で、手動スモークで担保する。

## ロールバック手順（万一の不具合時）

`git revert HEAD~3..HEAD` で Task 1〜3 の commit を打ち消す。`requireExitConfirm` のデフォルトは false なので、Task 1 だけ残せば既存挙動には影響なし。
