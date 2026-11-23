# Third-party audio

Acquired on 2026-08-25. Every asset listed here is released under
[Creative Commons Zero 1.0](https://creativecommons.org/publicdomain/zero/1.0/)
(CC0). Attribution is therefore not legally required, but the authors and
source pages are retained here for provenance and thanks.

## Background music and stingers

All BGM outputs were converted to 48 kHz Ogg Vorbis (`libvorbis`, quality 5)
with FFmpeg 9.0.1. Looping tracks were kept at their full source duration and
normalized with `loudnorm=I=-18:TP=-1.5:LRA=11`. Victory and game-over
stingers use `I=-16` with the same true-peak and loudness-range limits. No
source was trimmed or rearranged.

The shop source contained one non-monotonic packet timestamp. Its output
timestamps were regenerated during the same transcode; the decoded samples
and full duration were retained.

| Output | Title | Author | Source | Original file | Original SHA-256 |
|---|---|---|---|---|---|
| `bgm/menu.ogg` | Epic Main Menu Theme ~ Loop | Cleyton Kauffman (`CleytonKauffman`) | [OpenGameArt](https://opengameart.org/content/epic-main-menu-theme-loop) | `Übermensch [ Main Menu ].wav` | `7453FDFABED7129BED2F4CE29511DD0A8B6A683F3E1452D1FF1AB5FD238FBE61` |
| `bgm/map.ogg` | Unexplored Expansion | Bo Jingles, based on work by TAD | [OpenGameArt](https://opengameart.org/content/unexplored-expansion) | `tad_-_unexplored_-_expanded_1_by_bo_jingles_0.mp3` | `A39323B0CBC83DEA018FB693FB5B89F5CCE6F06761B05407A489F9ACE6CC094E` |
| `bgm/shop.ogg` | Welcome to the Item Shop | congusbongus | [OpenGameArt](https://opengameart.org/content/welcome-to-the-item-shop) | `welcome_to_the_item_shop_1.ogg` | `A3B783D367A7D1299BC3C34D7D7E1E7A7E23C3E91A89CF63223C6A6AF82EFD03` |
| `bgm/battle-normal.ogg` | 8-Bit RPG Battle/Encounter Theme | Wolfgang_ / Ted Kerr | [OpenGameArt](https://opengameart.org/content/8-bit-rpg-battleencounter-theme) | `RPGBattleTheme-Loopable_0.ogg` | `00EE019C1BBDAC37E5800ED1131C38DFE39D943FDD0B69DE0F830B81C8E81926` |
| `bgm/battle-elite.ogg` | JRPG Epic Rock Battle Theme #1 | HydroGene | [OpenGameArt](https://opengameart.org/content/jrpg-epic-rock-battle-theme-1) | `jrpg_battle_loop.mp3` | `006BE3310CB9A2612384B99D8087035904EAAD45F285A4F74170FF7EC6CA7F81` |
| `bgm/battle-boss.ogg` | Boss Battle Music / Epic Boss Battle | Juhani Junkala (`SubspaceAudio`) | [OpenGameArt](https://opengameart.org/content/boss-battle-music) | `Juhani Junkala - Epic Boss Battle [Seamlessly Looping].wav` | `35F75B4381DFBB053992876F7DFC567D9FD959D61B73954D5B1CD519753E7DF1` |
| `bgm/victory.ogg` | Victory Fanfare | ARoachIFoundOnMyPillow | [OpenGameArt](https://opengameart.org/content/victory-fanfare) | `fanfare1_0.ogg` | `C120CF8B3C60AACDCB9D48711AA250A701F6FE0CA97CD9A178DAA9E3B9D5C4A1` |
| `bgm/game-over.ogg` | Game Over Theme: Retro No Hope | Cleyton Kauffman (`CleytonKauffman`) | [OpenGameArt](https://opengameart.org/content/game-over-theme) | `Retro_No hope.ogg` (from `GameOver-Music.zip`) | `B7045EE946A9721DB4307371F224FB348C103EB01123E749944B1621ECDCA178` |

The source page for the menu and game-over music asks users to retain the
notice: "Music by Cleyton Kauffman". It is recorded here even though the
assets are CC0.

## Sound effects

The sound effects below are from Kenney's CC0 packs. The original Ogg bytes
were copied without transcoding or signal processing; only filenames were
changed to game-facing semantic names.

| Pack | Author | Source | Download archive SHA-256 |
|---|---|---|---|
| Interface Sounds 1.0 | Kenney | [Kenney](https://kenney.nl/assets/interface-sounds) | `F2193D072726D6758A5F7871B2DCC54DCCE0D5C35C6F0A62F92549B327C81232` |
| Casino Audio 1.1 | Kenney Vleugels | [Kenney](https://kenney.nl/assets/casino-audio) | `F36250766AC5BC378C13708DDF12A23A8E54A3251F8D482C7536E51B5DBAFA18` |
| Impact Sounds 1.0 | Kenney | [Kenney](https://kenney.nl/assets/impact-sounds) | `029D734AF1582474EDF3A694D1B0CEBC97C1C152F2F39FA34D4C2BAFC5DE77F8` |
| RPG Audio | Kenney Vleugels | [Kenney](https://kenney.nl/assets/rpg-audio) | `6DBEAF8544DA958D8F2ADCB4A4A4B76C1ADE34A05F8AB9EDCCD327DA7375F38B` |

### Interface Sounds mappings

| Output | Original file |
|---|---|
| `se/ui-click.ogg` | `click_001.ogg` |
| `se/ui-confirm.ogg` | `confirmation_002.ogg` |
| `se/ui-error.ogg` | `error_006.ogg` |
| `se/reward.ogg` | `maximize_005.ogg` |
| `se/turn-start.ogg` | `confirmation_003.ogg` |
| `se/turn-end.ogg` | `switch_001.ogg` |
| `se/card-upgrade.ogg` | `maximize_004.ogg` |
| `se/buff.ogg` | `maximize_009.ogg` |
| `se/debuff.ogg` | `error_005.ogg` |
| `se/heal.ogg` | `confirmation_001.ogg` |
| `se/poison.ogg` | `scratch_003.ogg` |
| `se/energy.ogg` | `pluck_001.ogg` |
| `se/summon.ogg` | `open_004.ogg` |
| `se/potion.ogg` | `glass_004.ogg` |
| `se/tactical.ogg` | `question_004.ogg` |
| `se/contingency-arm.ogg` | `toggle_003.ogg` |
| `se/contingency-trigger.ogg` | `confirmation_003.ogg` |
| `se/trait.ogg` | `maximize_003.ogg` |
| `se/ready.ogg` | `select_003.ogg` |
| `se/resolution.ogg` | `confirmation_004.ogg` |

### Casino Audio mappings

| Output | Original file |
|---|---|
| `se/card-play.ogg` | `card-place-2.ogg` |
| `se/card-draw-1.ogg` | `card-slide-1.ogg` |
| `se/card-draw-2.ogg` | `card-slide-2.ogg` |
| `se/card-draw-3.ogg` | `card-slide-3.ogg` |
| `se/card-discard.ogg` | `card-shove-2.ogg` |
| `se/card-exhaust.ogg` | `card-shove-4.ogg` |
| `se/card-add.ogg` | `card-place-3.ogg` |
| `se/card-recover.ogg` | `card-fan-1.ogg` |
| `se/card-shuffle.ogg` | `card-shuffle.ogg` |
| `se/reenact.ogg` | `card-fan-2.ogg` |

### Impact Sounds mappings

| Output | Original file |
|---|---|
| `se/battle-start.ogg` | `impactBell_heavy_000.ogg` |
| `se/hurt-1.ogg` | `impactPunch_heavy_000.ogg` |
| `se/hurt-2.ogg` | `impactPunch_heavy_001.ogg` |
| `se/hurt-3.ogg` | `impactPunch_heavy_002.ogg` |
| `se/block-1.ogg` | `impactMetal_medium_000.ogg` |
| `se/block-2.ogg` | `impactMetal_medium_001.ogg` |
| `se/block-3.ogg` | `impactMetal_medium_002.ogg` |
| `se/gain-block.ogg` | `impactMetal_light_003.ogg` |
| `se/death.ogg` | `impactSoft_heavy_004.ogg` |
| `se/ultimate.ogg` | `impactBell_heavy_004.ogg` |
| `se/finisher.ogg` | `impactMetal_heavy_004.ogg` |

### RPG Audio mappings

| Output | Original file |
|---|---|
| `se/purchase.ogg` | `handleCoins.ogg` |
| `se/attack.ogg` | `knifeSlice.ogg` |
