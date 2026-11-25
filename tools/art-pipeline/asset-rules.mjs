// 素材の種類ごとの規格と変換方法。docs/current-art-asset-spec.md §2 と一致させる。
// key は art/source/ と src/Client/public/assets/ の下の相対フォルダ。
//   mode: pixelize（縮小＋減色）/ actor（128×128 へ配置）/ hero（主人公のモーション一式）
//   fit:  contain（中身を切り出して枠内に収める）/ cover（はみ出しを切る）/ stretch（枠に合わせて伸縮）
//   w, h: キャンバス（ドット）。files で個別サイズを指定できる。
//   colors: 色数の上限
//   frames: 横並びのコマ数（素材ごとの <id>.json でも指定可）
// 素材ごとの例外は art/source/<key>/<id>.json に書く（mode, colors, frames, height, lift, fit など）。
export const RULES = [
  { key: 'backgrounds/battle', mode: 'pixelize', w: 640, h: 360, colors: 96, fit: 'cover' },
  { key: 'backgrounds/map', mode: 'pixelize', w: 640, h: 360, colors: 96, fit: 'cover' },
  { key: 'backgrounds/events', mode: 'pixelize', w: 480, h: 320, colors: 96, fit: 'cover' },

  { key: 'icons/status', mode: 'pixelize', w: 16, h: 16, colors: 16, fit: 'contain' },
  { key: 'icons/intent', mode: 'pixelize', w: 16, h: 16, colors: 16, fit: 'contain' },
  { key: 'icons/system', mode: 'pixelize', w: 16, h: 16, colors: 16, fit: 'contain' },
  { key: 'icons/relics', mode: 'pixelize', w: 32, h: 32, colors: 24, fit: 'contain', margin: 1 },
  { key: 'icons/potions', mode: 'pixelize', w: 32, h: 32, colors: 24, fit: 'contain', margin: 1 },
  { key: 'icons/map', mode: 'pixelize', w: 32, h: 32, colors: 24, fit: 'contain', margin: 1 },
  // 場面の絵は複数枚を同じ枠に重ねる（焚き火の薪と炎）ので、余白を切らずに縮小する。
  { key: 'icons/scene', mode: 'pixelize', w: 48, h: 48, colors: 24, fit: 'stretch' },

  { key: 'cards/frames/affiliations', mode: 'pixelize', w: 112, h: 157, colors: 48, fit: 'stretch' },
  { key: 'cards/frames/types', mode: 'pixelize', w: 112, h: 157, colors: 48, fit: 'stretch' },
  { key: 'cards/frames/masks', mode: 'pixelize', w: 112, h: 157, colors: 4, fit: 'stretch' },
  { key: 'cards/frames/costs', mode: 'pixelize', w: 28, h: 28, colors: 16, fit: 'stretch' },

  { key: 'portraits', mode: 'pixelize', w: 120, h: 160, colors: 48, fit: 'cover' },

  { key: 'actors/enemies', mode: 'actor', w: 128, h: 128, colors: 32 },
  { key: 'actors/units', mode: 'actor', w: 128, h: 128, colors: 32 },
  { key: 'actors/heroes', mode: 'hero', w: 128, h: 128, colors: 32 },

  // nested: true は「キャラごとのフォルダ」を持つ種類。files でファイル名ごとのキャンバスを決める。
  {
    key: 'ui/heroes', nested: true, mode: 'pixelize', colors: 32, fit: 'stretch',
    files: { combo: { w: 56, h: 28 }, tactical: { w: 44, h: 28 }, ultimate: { w: 44, h: 28 }, ready: { w: 76, h: 32 } },
  },
  // 共通UIの枠（9 分割）。tools/art-pipeline/gen-ui-frames.mjs がコードで描く。
  {
    key: 'ui/common', mode: 'native', colors: 16,
    files: {
      panel: { w: 24, h: 24 }, box: { w: 12, h: 12 }, inset: { w: 12, h: 12 }, inset_active: { w: 12, h: 12 },
      button: { w: 16, h: 16 }, button_hover: { w: 16, h: 16 }, button_secondary: { w: 16, h: 16 },
      button_danger: { w: 16, h: 16 }, button_danger_hover: { w: 16, h: 16 },
      tooltip: { w: 12, h: 12 }, bar: { w: 8, h: 8 },
      node_current: { w: 34, h: 34 }, slot_round: { w: 18, h: 18 }, orb: { w: 32, h: 32 },
      ...Object.fromEntries(['default', 'past', 'far', 'next', 'next_hover', 'elite_next', 'rest_next', 'merchant_next', 'boss_far']
        .flatMap(state => [[`node_${state}`, { w: 28, h: 28 }], [`node_${state}_boss`, { w: 40, h: 40 }]])),
    },
  },
  {
    key: 'ui/enemy', mode: 'pixelize', colors: 32, fit: 'stretch',
    files: { intent_plate: { w: 48, h: 16 }, action_plate: { w: 64, h: 16 }, action_button: { w: 24, h: 12 } },
  },
]

/** 戦闘キャラ共通キャンバスの基準。 */
export const ACTOR = { size: 128, footRow: 123, centerX: 64 }

/** 主人公モーションの標準（コマ数・時間・ループ）。character.json で上書きできる。 */
export const HERO_MOTIONS = {
  stand: { frames: 1, durationMs: 0, loop: false },
  idle: { frames: 8, durationMs: 1200, loop: true },
  attack: { frames: 6, durationMs: 600, loop: false },
  attack_alt: { frames: 6, durationMs: 600, loop: false },
  skill: { frames: 8, durationMs: 800, loop: false },
  hurt: { frames: 4, durationMs: 400, loop: false },
  down: { frames: 6, durationMs: 720, loop: false },
}
