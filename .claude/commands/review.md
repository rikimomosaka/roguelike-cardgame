---
description: Run an automated code review on the current diff (or a specified range) via the code-reviewer subagent.
---

Run an independent code review on $ARGUMENTS.

## Interpret $ARGUMENTS

- Empty → レビュー対象は `git diff HEAD`（unstaged + staged の全変更）
- `staged` → `git diff --staged`
- `<commit>..<commit>` or `<commit>...<commit>` → その範囲
- `<branch>` (e.g. `feature/xyz`) → `git diff main...<branch>`
- `<file path>` → そのファイルの最新差分

## Workflow

1. **範囲を確定する**: 上記ルールで対象 diff を決め、ユーザーに「何をレビューするか」を1行で伝える。
2. **事前チェック**（ローカルで軽く実行）:
   - `git status` で状況把握
   - `git diff --stat <range>` で変更規模把握
   - 差分が空なら「レビュー対象がありません」と伝えて終了
3. **`code-reviewer` サブエージェントを呼び出す**:
   - `Agent` ツールで `subagent_type: "code-reviewer"` を指定
   - プロンプトには以下を含める:
     - レビュー対象の範囲（どの git コマンドで取得するか）
     - このプロジェクトが Roguelike Card Game であること
     - CLAUDE.md のルール遵守チェックを優先すること
     - 指摘は重要度分類（🔴 Blocker / 🟡 Should fix / 🟢 Nit）で返すこと
4. **エージェントの報告をそのまま提示する**:
   - サブエージェントの結果は自動でユーザーに見えないので、主要な指摘をユーザー向けに要約 + 詳細は原文のまま出す
   - ファイル参照は `[file.cs:42](src/file.cs#L42)` 形式のクリック可能リンクに変換
5. **アクション選択肢を最後に提示**:
   - Blocker があれば「この指摘を修正しますか？」と確認
   - 修正依頼が来たら別の作業として着手（レビューと修正は分離）

## Do not

- レビュー中に勝手にコードを書き換えない（修正は別途ユーザー確認後）
- 「LGTM」だけで終わらせない。何も見つからなかった場合もその根拠（確認した観点）を示す
- 差分が大きすぎる場合でも、サブエージェントに丸投げせず範囲を分割して呼ぶ
