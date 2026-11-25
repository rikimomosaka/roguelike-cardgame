// カードイラストの生成指示（PixelLab pixflux）。ID はカード ID。
// kind: 'scene' = そのまま 1 枚絵 / 'backdrop' = 背景だけ生成し、compose-card-art.mjs でキャラを合成する。
// クラス固有カードは同じクラスに別キャラが増える前提なので、プレイアブルキャラを描かない
//（道具・エフェクトが主役か、カードに合った無名の人物）。
export const CARD_ART_SIZE = { w: 106, h: 80 }

const STYLE = 'pixel art, fantasy card game illustration, dramatic lighting, rich detail, no text, no letters, no border'
const BACKDROP = 'empty scenery background only, no people, no characters, open ground in the lower middle'

export const CARD_ART = {
  // ---- 初期デッキの基本カード（背景 + 各クラスのキャラ） ----
  striker_basic_attack: { kind: 'backdrop', desc: `moonlit ruined stone courtyard at night, crimson afterglow on the horizon, drifting embers, ${BACKDROP}` },
  striker_basic_defense: { kind: 'backdrop', desc: `dark gothic cathedral hall, tall red stained glass window glowing, stone pillars, ${BACKDROP}` },
  guardian_basic_attack: { kind: 'backdrop', desc: `castle rampart walkway under bright blue sky, blue banners waving, ${BACKDROP}` },
  guardian_basic_defense: { kind: 'backdrop', desc: `inside a castle gate at dusk, iron portcullis, warm torches on stone walls, ${BACKDROP}` },
  tactician_basic_attack: { kind: 'backdrop', desc: `misty forest clearing at dawn, tall pine trees, soft light rays, ${BACKDROP}` },
  tactician_basic_defense: { kind: 'backdrop', desc: `hidden forest camp with wooden palisade and tent, evening, lantern light, ${BACKDROP}` },
  enhancer_basic_attack: { kind: 'backdrop', desc: `arcane library hall with tall bookshelves, floating purple glowing runes, ${BACKDROP}` },
  enhancer_basic_defense: { kind: 'backdrop', desc: `starry night shrine terrace, large violet magic circle glowing on the floor, ${BACKDROP}` },

  // ---- ストライカーのクラス固有カード（キャラに依存しない絵） ----
  striker_basic_quickdraw: { kind: 'scene', desc: `close-up of a katana being drawn fast from its black sheath, bright flash of steel, red spark, dark background, ${STYLE}` },
  striker_basic_step: { kind: 'scene', desc: `close-up of armored boots stepping forward hard on a stone floor, dust burst, speed lines, ${STYLE}` },
  striker_basic_finishing_slash: { kind: 'scene', desc: `two huge crossing crimson sword slash arcs forming an X, sparks and embers, dark night background, ${STYLE}` },
  striker_common_step_in: { kind: 'scene', desc: `silhouette of an unknown swordsman dashing forward across a grass field at dawn, long speed lines, ${STYLE}` },
  striker_common_chain_fang: { kind: 'scene', desc: `two curved fang-shaped sword slash marks striking in a row, white and red energy, dark background, ${STYLE}` },
  striker_common_rising_break: { kind: 'scene', desc: `upward rising sword slash shockwave bursting from cracked ground, rocks flying up, ${STYLE}` },
  striker_common_offensive_breath: { kind: 'scene', desc: `close-up of a gloved hand gripping a sword hilt tightly, glowing red breath mist swirling around it, ${STYLE}` },
  striker_rare_rising_flurry: { kind: 'scene', desc: `a katana blade leaving a zigzag trail of many quick red cuts climbing upward into the air, sparks, dark background, ${STYLE}` },
  striker_rare_sunder: { kind: 'scene', desc: `a sword slash splitting a heavy iron tower shield cleanly in two halves, sparks flying, dark background, ${STYLE}` },
  striker_rare_converging_slash: { kind: 'scene', desc: `several sword trails converging into one blinding point of red light, radial lines, dark background, ${STYLE}` },
  striker_rare_composure: { kind: 'scene', desc: `a katana held still after a cut, blade gleaming, falling red petals, calm night, ${STYLE}` },
  striker_epic_flash_finale: { kind: 'scene', desc: `one gigantic horizontal blinding slash splitting the night sky, crescent moon, red theater curtains at the edges, ${STYLE}` },
  striker_epic_dance_finale: { kind: 'scene', desc: `a ring of many floating swords spinning in a whirlwind of red flames, ${STYLE}` },
  striker_epic_battle_breath: { kind: 'scene', desc: `a swirling circle of red fighting spirit aura cycling around a clenched fist, ${STYLE}` },
  striker_epic_form_copy: { kind: 'scene', desc: `an unknown masked swordsman in a stance with a translucent glowing afterimage copy mirroring the same pose behind him, ${STYLE}` },
}

// 基本カードに合成するキャラ（actors/heroes の表示用シート、コマ番号は 0 始まり）。
export const CARD_ART_ACTORS = {
  striker_basic_attack: { hero: 'default', sheet: 'attack', frame: 2 },
  striker_basic_defense: { hero: 'default', sheet: 'guard', frame: 3 },
  guardian_basic_attack: { hero: 'guardian_mireille', sheet: 'attack', frame: 2 },
  guardian_basic_defense: { hero: 'guardian_mireille', sheet: 'stand', frame: 0 },
  tactician_basic_attack: { hero: 'tactician_yuno', sheet: 'attack', frame: 2 },
  tactician_basic_defense: { hero: 'tactician_yuno', sheet: 'stand', frame: 0 },
  enhancer_basic_attack: { hero: 'enhancer_lyra', sheet: 'attack', frame: 2 },
  enhancer_basic_defense: { hero: 'enhancer_lyra', sheet: 'attack', frame: 1 },
}
