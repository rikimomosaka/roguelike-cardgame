# 現行カードコスト仕様

> 更新日: 2026-08-10  
> 本文書はカードコストに関する旧Phase文書より優先する。

## 2種類のコスト

- **元コスト**: 強化状態を反映したカード定義上のコスト。コンボの連鎖判定と`LastPlayedOrigCost`には必ずこの値だけを使う。
- **現在コスト**: 実際に支払う値。個別カードの変更、手札限定status、コンボ軽減を順に反映し、0未満は0へ丸める。

現在コストが0でも、元コスト1のカードの次に元コスト2のカードを使えばコンボは継続する。現在コスト同士の0→0はコンボ判定に使わない。

画面では現在コストを上段、現在値と異なる場合だけ元コストを下段に表示する。元コストは「コンボが参照する数字」でもある。

## 計算順

1. 強化状態を反映した元コストを求める。`null`ならプレイ不可。
2. 旧互換の絶対値`CostOverride`があれば基準値を置き換える。
3. カードInstanceの`CardCostModifier.Amount`を付与順に加算する。
4. 手札にあるカードだけ、heroの`handCostIncrease`を加え、`handCostReduction`を引く。
5. 元コストが通常の連鎖条件を満たすか、直前のWild系保護があればコンボ軽減として1引く。
6. 0～`int.MaxValue`へ丸めた値を支払う。

この計算はCoreの`BattleCardCostCalculator`を唯一の規則とし、Serverは結果をDTOへ写し、Clientは再計算しない。

## 個別カードの変更

`CardCostModifier`は加算式で、負数が軽減、正数が増加。複数効果は付与順に保持し合算する。

| 期限 | 未使用時 | カードを使用した時 |
|---|---|---|
| `UntilPlayed` | 領域・ターンを跨いで同じInstanceに残る | 失われる |
| `UntilTurnEnd` | 領域を跨いで残るがターン終了時に失われる | 失われる |

領域移動だけでは変更を消さない。山札、手札、捨札、除外、召喚保持、パワー領域のどこへ移っても同じカードInstanceに追従する。

カード効果`modifyCardCost`は次を使う。

- `amount`: signedのコスト増減値
- `pile`: `hand|draw|discard|exhaust`
- `select`: `all|random|choose`
- `cardCount`: random/chooseの対象枚数。省略時1
- `costDuration`: `untilPlayed|untilTurnEnd`

プレイ中のカード自身は、同じ効果による手札対象の候補から除外する。

## 手札全体へ作用するstatus

- `handCostReduction`（Buff）: 手札中の全カードの現在コストをstack分減らす。
- `handCostIncrease`（Debuff）: 手札中の全カードの現在コストをstack分増やす。

これはカードInstanceへ書き込まない。新しく手札へ来たカードには即時適用され、手札を離れたカードには即時適用されなくなる。個別カードmodifierはそのまま保持される。

## 奥義

奥義は発動時の手札をsnapshotし、各カードへ「その時点のコンボ軽減前の現在コスト」と同じ量の負の`UntilTurnEnd` modifierを加える。そのため発動直後は0になるが、後から手札コスト増加statusを受ければ再び0より大きくなり得る。

発動後に引いたカードは対象外。同じターンに再発動できる場合は新しいmodifierを追加する。対象カードを使用すれば、他の個別変更と同じくそのカードの全コスト変更が失われる。

