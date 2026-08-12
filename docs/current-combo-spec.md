# 現行コンボ仕様

> 更新日: 2026-08-09  
> 本文書は Phase 10 時点の旧設計書より優先する。

## 通常のコンボ

- 直前に使ったカードの元コストより 1 大きいカードを使うとコンボが続く。
- カード種別に関係なく、通常条件に合うか、直前の Wild / SuperWild が予約した「次の 1 枚」の保護中なら、支払いコストを 1 減らす。
- Wild / SuperWild 自身のキーワード効果だけでコンボを継続する場合は軽減しない。そのカードが「直前の保護」の対象であれば、Wild / SuperWild でも元コストに関係なく軽減する。
- 通常条件も Wild 系の保護もない場合、そのカードから 1 コンボとして数え直す。

## Wild

表示文:

> このカード及び次に使うカードではコンボが途切れない。このコンボ中、以降のワイルドを無効にする。

- Wild 自身は通常のコンボ条件に合わなくてもコンボを継続する。
- Wild の直後に使う 1 枚も、コストに関係なくコンボを継続する。そのカードで保護を消費する。
- 一度 Wild が発動したコンボでは、後続の Wild は効果を発動しない。SuperWild は無効化しない。
- コンボの先頭で Wild を使った場合も、Wild を使用済みにし、次の 1 枚を保護する。
- 使用済みの状態で後続 Wild が旧コンボを切った場合、そのプレイで Wild 効果を再発動しない。新コンボ側の Wild 使用済み状態は `false` に戻る。

## SuperWild

表示文:

> このカード及び次に使うカードではコンボが途切れない。

- SuperWild 自身と、その直後に使う 1 枚を保護する。
- SuperWild は Wild 使用済み状態に関係なく常に発動する。
- SuperWild は後続の Wild / SuperWild を無効化しない。連続した SuperWild は、それぞれが次の 1 枚の保護を予約する。
- 1 枚が `wild` と `superwild` の両方を持つ場合、SuperWild として扱う。Wild 使用済み状態は新たに立てない。

## 判定順

1. 通常条件に合うかを判定する。
2. SuperWild があれば、Wild 使用済みに関係なく SuperWild を有効にする。
3. SuperWild がなく Wild がある場合、現在のコンボで Wild 未使用のときだけ有効にする。
4. 「直前のカードから予約された保護」→「通常条件」→「今回の Wild / SuperWild」のいずれかが有効ならコンボを継続する。
5. カード種別に関係なく、手順 1 の通常条件に合うか、手順 4 の「直前のカードから予約された保護」があれば 1 軽減する。「今回の Wild / SuperWild」だけで継続する場合は軽減しない。
6. 今回の Wild / SuperWild が有効なら、次の 1 枚の保護を予約する。そうでなければ、入力側の保護は今回で消費される。
7. コンボが切れた場合は Wild 使用済みを解除する。続いた場合は旧状態を引き継ぎ、今回 Wild が有効だったときは使用済みにする。

## 状態フィールド

| フィールド | 意味 |
|---|---|
| `ComboCount` | 現在のコンボ数 |
| `MaxComboThisTurn` | このターン中に到達した最大コンボ数。現在のコンボが切れても下がらない |
| `LastPlayedOrigCost` | 直前に使ったカードの元コスト |
| `NextCardComboFreePass` | Wild / SuperWild が予約した「次の 1 枚」のコンボ保護 |
| `WildUsedInCurrentCombo` | 現在のコンボで Wild が発動済みか。SuperWild はこの値を立てず、この値で無効にもならない |

カードを使うたびに `MaxComboThisTurn = max(それまでの MaxComboThisTurn, 更新後の ComboCount)` とする。
コンボが切れて `ComboCount` が 1 に戻っても、`MaxComboThisTurn` はそのターンの最高値を維持する。

ターン終了時は `ComboCount / MaxComboThisTurn / LastPlayedOrigCost / NextCardComboFreePass / WildUsedInCurrentCombo` を
`0 / 0 / null / false / false` へリセットする。次ターン開始処理でも `MaxComboThisTurn=0` を保証する。
`OnTurnEnd` の効果はリセット前に処理するため、そのターンの最大コンボを参照できる。

## 処理例

| 操作 | 結果 |
|---|---|
| コンボ先頭で Wild | `ComboCount=1`、次カード保護あり、Wild 使用済み |
| Wild → 条件外の通常カード | 通常カードまでコンボ継続。通常カードを 1 軽減し、保護は消費 |
| SuperWild → 条件外の通常カード | 通常カードまでコンボ継続。通常カードを 1 軽減し、保護は消費 |
| 直前の保護なしで、Wild / SuperWild 自身が条件外 | キーワード効果でコンボは継続するが、そのカード自身は軽減しない |
| SuperWild → 条件外の Wild / SuperWild | 2 枚目もコンボ継続。2 枚目は直前の保護で 1 軽減し、有効なキーワード効果で次の 1 枚も保護 |
| Wild / SuperWild 自身が通常条件に一致 | コンボを継続し、そのカード自身も 1 軽減する |
| Wild → 条件外の Wild | 2 枚目は直前の保護で 1 軽減され、コンボも継続。ただし 2 枚目の Wild 効果は無効なので、さらに次のカードを保護しない |
| Wild → 条件外の通常カード → Wild | 2 枚目まで継続。3 枚目の Wild 効果は無効で、通常条件にも合わなければ新コンボになる |
| SuperWild → SuperWild → Wild | 全て有効。最後の Wild で初めて Wild 使用済みになる |
| Wild 使用済み中に SuperWild | SuperWild は有効。次カードを保護する |
