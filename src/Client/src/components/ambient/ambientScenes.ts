/**
 * 背景の控えめな演出（差し込む光・灯りのゆらぎ・漂う粒・霧）の定義。絵そのものは動かさない。
 * 座標・大きさはすべて元の背景画像のドット単位（戦闘・マップ 640×360、イベント 480×320）。
 * 動きは小さく遅く、光は薄く（最大でも 0.25 程度）して、戦闘や読み物の邪魔をしない。
 * 規格: docs/current-art-asset-spec.md「背景の演出」。
 */

export type AmbientRect = { x: number; y: number; w: number; h: number }

/** 差し込む光の筋（ゆっくり明滅）。上端が細く、下へ行くほど広がる。 */
export type AmbientRay = {
  /** 光の上端の中心。 */
  x: number
  y: number
  w: number
  h: number
  /** 傾き（度。正 = 下へ行くほど左）。 */
  angle: number
  color: string
  /** 明るさの下限と上限（0〜1）。 */
  min: number
  max: number
  period: number
  delay?: number
}

/** 灯り・結晶などの光（ゆらぐ）。 */
export type AmbientGlow = {
  x: number
  y: number
  /** 半径（横）。 */
  r: number
  /** 縦の半径（省略時は r と同じ円。地面に落ちた光だまりは平たい楕円にする）。 */
  ry?: number
  color: string
  min: number
  max: number
  period: number
  delay?: number
  /** 'flicker' = 火のように不規則に、'pulse' = ゆっくり呼吸するように。 */
  style?: 'flicker' | 'pulse'
}

/** 漂う粒。 */
export type AmbientParticles = {
  kind: 'mote' | 'firefly' | 'ember' | 'leaf' | 'sparkle' | 'drip'
  count: number
  area: AmbientRect
  colors: string[]
  /** 1 回の動きの長さ（秒）。粒ごとに ±20% ばらつかせる。 */
  duration: number
  /** 不透明度の上限。 */
  opacity: number
}

/** 低く流れる霧。 */
export type AmbientMist = { y: number; h: number; color: string; opacity: number; period: number }

export type AmbientScene = {
  /** 元の背景画像。 */
  src: string
  width: number
  height: number
  rays?: AmbientRay[]
  glows?: AmbientGlow[]
  particles?: AmbientParticles[]
  mist?: AmbientMist[]
}

const BATTLE = (id: string) => `/assets/backgrounds/battle/${id}.png`
const MAP = (id: string) => `/assets/backgrounds/map/${id}.png`
const EVENT = (id: string) => `/assets/backgrounds/events/${id}.png`

export const AMBIENT_SCENES: Readonly<Record<string, AmbientScene>> = {
  // ---------------- 戦闘（640×360） ----------------
  battle_forest: {
    src: BATTLE('battle_forest'), width: 640, height: 360,
    // 真上の木々の切れ間（x 330〜370、y 0〜20）から、斜め左下の地面の中央へ光が落ちる。絵の光の向きに合わせた扇。
    rays: [
      { x: 352, y: -6, w: 84, h: 280, angle: 16, color: 'rgba(226, 246, 204, 1)', min: 0.1, max: 0.24, period: 11 },
      { x: 368, y: -6, w: 52, h: 260, angle: 27, color: 'rgba(226, 246, 204, 1)', min: 0.07, max: 0.19, period: 9, delay: -3 },
      { x: 338, y: -6, w: 48, h: 250, angle: 7, color: 'rgba(226, 246, 204, 1)', min: 0.07, max: 0.18, period: 13, delay: -6 },
      { x: 376, y: -6, w: 30, h: 210, angle: 36, color: 'rgba(226, 246, 204, 1)', min: 0.05, max: 0.14, period: 10, delay: -8 },
    ],
    glows: [
      // 切れ間の明るさと、光が地面に落ちた光だまり（筋と同じ周期で明滅）。
      { x: 352, y: 6, r: 40, ry: 26, color: '#e8f6d4', min: 0.1, max: 0.2, period: 11, style: 'pulse' },
      { x: 300, y: 238, r: 120, ry: 26, color: '#e6efb4', min: 0.05, max: 0.13, period: 11, style: 'pulse' },
    ],
    particles: [
      { kind: 'mote', count: 22, area: { x: 250, y: 20, w: 140, h: 210 }, colors: ['#eef7cf', '#dfeebb'], duration: 12, opacity: 0.74 },
      { kind: 'leaf', count: 4, area: { x: 120, y: 0, w: 420, h: 40 }, colors: ['#7f9a45', '#a2b65b', '#5f7a35'], duration: 17, opacity: 0.85 },
    ],
    mist: [{ y: 158, h: 48, color: '#a4c8b8', opacity: 0.12, period: 70 }],
  },
  battle_cave: {
    src: BATTLE('battle_cave'), width: 640, height: 360,
    glows: [
      { x: 45, y: 135, r: 58, color: '#3d8cff', min: 0.12, max: 0.24, period: 5.5, style: 'pulse' },
      { x: 128, y: 96, r: 42, color: '#6cbcff', min: 0.1, max: 0.2, period: 7, delay: -2, style: 'pulse' },
      { x: 590, y: 160, r: 58, color: '#a35cff', min: 0.12, max: 0.24, period: 6, delay: -3, style: 'pulse' },
    ],
    particles: [
      { kind: 'sparkle', count: 7, area: { x: 20, y: 80, w: 100, h: 110 }, colors: ['#a6dcff'], duration: 5, opacity: 0.8 },
      { kind: 'sparkle', count: 6, area: { x: 540, y: 120, w: 95, h: 70 }, colors: ['#dcb0ff'], duration: 5.5, opacity: 0.8 },
      { kind: 'drip', count: 4, area: { x: 200, y: 20, w: 300, h: 30 }, colors: ['#93a9dc'], duration: 6, opacity: 0.6 },
      { kind: 'mote', count: 10, area: { x: 100, y: 60, w: 110, h: 120 }, colors: ['#a3ccff'], duration: 12, opacity: 0.61 },
    ],
    mist: [{ y: 150, h: 52, color: '#7480c0', opacity: 0.14, period: 80 }],
  },
  battle_hell: {
    src: BATTLE('battle_hell'), width: 640, height: 360,
    glows: [
      { x: 95, y: 64, r: 40, color: '#ff6a2a', min: 0.12, max: 0.24, period: 3.2 },
      { x: 565, y: 112, r: 40, color: '#ff6a2a', min: 0.12, max: 0.24, period: 3.8, delay: -1 },
      { x: 320, y: 168, r: 150, color: '#ff5a1e', min: 0.06, max: 0.14, period: 6, style: 'pulse' },
    ],
    particles: [
      { kind: 'ember', count: 22, area: { x: 0, y: 150, w: 640, h: 50 }, colors: ['#ffe6b0', '#ffd08a', '#fff0c8'], duration: 9, opacity: 0.8 },
    ],
  },

  // ---------------- マップ（640×360） ----------------
  map_act1: {
    src: MAP('act1'), width: 640, height: 360,
    // 上中央の木々の切れ間（x 300〜360）から、右下へ斜めに差す光と、道に落ちた光だまり。
    rays: [
      { x: 338, y: -6, w: 80, h: 250, angle: -14, color: 'rgba(232, 248, 210, 1)', min: 0.08, max: 0.2, period: 12 },
      { x: 318, y: -6, w: 50, h: 270, angle: 2, color: 'rgba(232, 248, 210, 1)', min: 0.05, max: 0.14, period: 10, delay: -5 },
    ],
    glows: [
      { x: 330, y: 10, r: 50, ry: 30, color: '#eef8dc', min: 0.08, max: 0.18, period: 12, style: 'pulse' },
      { x: 322, y: 262, r: 70, ry: 16, color: '#e8efb8', min: 0.05, max: 0.12, period: 12, style: 'pulse' },
    ],
    particles: [
      { kind: 'firefly', count: 12, area: { x: 40, y: 150, w: 560, h: 190 }, colors: ['#e8ff7d', '#d2f56a'], duration: 9, opacity: 0.85 },
      { kind: 'mote', count: 14, area: { x: 270, y: 20, w: 140, h: 200 }, colors: ['#f2ffd6'], duration: 12, opacity: 0.6 },
      { kind: 'leaf', count: 3, area: { x: 160, y: 0, w: 320, h: 40 }, colors: ['#7f9a45', '#a2b65b'], duration: 17, opacity: 0.8 },
    ],
    mist: [{ y: 170, h: 60, color: '#9cc6bc', opacity: 0.1, period: 80 }],
  },
  map_act2: {
    src: MAP('act2'), width: 640, height: 360,
    glows: [
      // 道沿いのランタン（手前ほど大きく）
      { x: 197, y: 285, r: 34, color: '#ffb44a', min: 0.16, max: 0.3, period: 2.6 },
      { x: 435, y: 258, r: 30, color: '#ffb44a', min: 0.16, max: 0.3, period: 3.1, delay: -1 },
      { x: 263, y: 226, r: 18, color: '#ffb44a', min: 0.14, max: 0.26, period: 2.8, delay: -0.5 },
      { x: 357, y: 206, r: 14, color: '#ffb44a', min: 0.14, max: 0.26, period: 3.4, delay: -2 },
      { x: 285, y: 197, r: 12, color: '#ffb44a', min: 0.12, max: 0.24, period: 2.9, delay: -1.4 },
      // 奥の青白い光
      { x: 320, y: 160, r: 95, ry: 80, color: '#8fd8ec', min: 0.06, max: 0.14, period: 9, style: 'pulse' },
      // 壁の結晶
      { x: 57, y: 178, r: 16, color: '#8f8cff', min: 0.08, max: 0.22, period: 6, style: 'pulse' },
      { x: 129, y: 190, r: 14, color: '#6fd2ff', min: 0.08, max: 0.22, period: 6.8, delay: -2, style: 'pulse' },
      { x: 515, y: 268, r: 18, color: '#6fd2ff', min: 0.08, max: 0.22, period: 6.2, delay: -3, style: 'pulse' },
      { x: 595, y: 148, r: 14, color: '#6fd2ff', min: 0.08, max: 0.22, period: 7, delay: -1, style: 'pulse' },
      { x: 485, y: 240, r: 12, color: '#a78cff', min: 0.08, max: 0.2, period: 5.6, delay: -4, style: 'pulse' },
    ],
    particles: [
      { kind: 'sparkle', count: 8, area: { x: 30, y: 130, w: 590, h: 150 }, colors: ['#9fe4ff', '#c8a8ff'], duration: 5, opacity: 0.75 },
      { kind: 'mote', count: 12, area: { x: 260, y: 100, w: 120, h: 120 }, colors: ['#bdf0ff'], duration: 12, opacity: 0.55 },
      { kind: 'drip', count: 4, area: { x: 150, y: 30, w: 340, h: 40 }, colors: ['#93a9dc'], duration: 6, opacity: 0.55 },
    ],
    mist: [{ y: 180, h: 60, color: '#7f9fc0', opacity: 0.1, period: 90 }],
  },
  map_act3: {
    src: MAP('act3'), width: 640, height: 360,
    glows: [
      // 奥の赤い光と、溶岩だまり
      { x: 320, y: 168, r: 100, ry: 80, color: '#ff5a1e', min: 0.06, max: 0.14, period: 7, style: 'pulse' },
      { x: 320, y: 207, r: 40, ry: 14, color: '#ff7a2a', min: 0.12, max: 0.24, period: 3.4 },
      { x: 240, y: 242, r: 22, ry: 12, color: '#ff7a2a', min: 0.12, max: 0.24, period: 3 },
      { x: 198, y: 270, r: 16, ry: 10, color: '#ff7a2a', min: 0.12, max: 0.22, period: 3.6, delay: -1 },
      // 細く流れ落ちる溶岩
      { x: 172, y: 150, r: 14, ry: 100, color: '#ff6a2a', min: 0.08, max: 0.18, period: 4.2 },
      { x: 427, y: 170, r: 14, ry: 85, color: '#ff6a2a', min: 0.08, max: 0.18, period: 4.6, delay: -2 },
      { x: 228, y: 165, r: 10, ry: 70, color: '#ff6a2a', min: 0.06, max: 0.14, period: 5, delay: -1 },
      // 岩の割れ目
      { x: 550, y: 230, r: 24, color: '#ff5a1e', min: 0.06, max: 0.16, period: 4, delay: -1.5 },
      { x: 600, y: 160, r: 20, color: '#ff5a1e', min: 0.06, max: 0.16, period: 4.4, delay: -2.5 },
      { x: 75, y: 220, r: 24, color: '#ff5a1e', min: 0.06, max: 0.16, period: 4.8, delay: -0.5 },
    ],
    particles: [
      { kind: 'ember', count: 22, area: { x: 60, y: 180, w: 520, h: 140 }, colors: ['#ffe6b0', '#ffd08a', '#fff0c8'], duration: 9, opacity: 0.8 },
    ],
  },

  // ---------------- イベント（480×320） ----------------
  event_blessing_fountain: {
    src: EVENT('blessing_fountain'), width: 480, height: 320,
    // 2026-11-28 描き直し（PixelLab）。上の切れ間（x 280、y 0）から左下の泉へ差す木漏れ日。
    // 水は岩壁の魚の吐水口（x 161、y 132）から水盤（中心 x 170、y 184）へ落ちる。
    rays: [
      { x: 282, y: 0, w: 76, h: 200, angle: 26, color: 'rgba(226, 246, 196, 1)', min: 0.06, max: 0.18, period: 11 },
      { x: 312, y: 0, w: 42, h: 180, angle: 30, color: 'rgba(226, 246, 196, 1)', min: 0.04, max: 0.13, period: 9, delay: -4 },
    ],
    glows: [
      { x: 172, y: 184, r: 66, ry: 12, color: '#7fe8ff', min: 0.08, max: 0.2, period: 6, style: 'pulse' },
      { x: 163, y: 156, r: 7, ry: 24, color: '#b6f4ff', min: 0.06, max: 0.18, period: 2.4, style: 'pulse' },
    ],
    particles: [
      { kind: 'mote', count: 16, area: { x: 180, y: 40, w: 140, h: 130 }, colors: ['#f5ffd2', '#e2f5b6'], duration: 11, opacity: 0.7 },
      { kind: 'sparkle', count: 8, area: { x: 115, y: 176, w: 125, h: 16 }, colors: ['#d4fdff'], duration: 4.5, opacity: 0.8 },
      { kind: 'sparkle', count: 4, area: { x: 150, y: 172, w: 24, h: 10 }, colors: ['#ffffff'], duration: 2.5, opacity: 0.7 },
      { kind: 'sparkle', count: 5, area: { x: 60, y: 265, w: 340, h: 50 }, colors: ['#8ec8ff'], duration: 6, opacity: 0.6 },
    ],
  },
  event_forgotten_forge: {
    src: EVENT('forgotten_forge'), width: 480, height: 320,
    rays: [{ x: 196, y: 10, w: 42, h: 200, angle: -20, color: 'rgba(190, 210, 236, 1)', min: 0.06, max: 0.16, period: 12 }],
    glows: [
      { x: 380, y: 170, r: 58, color: '#ff7a2a', min: 0.16, max: 0.3, period: 2.4 },
      { x: 380, y: 175, r: 26, color: '#ffb04a', min: 0.14, max: 0.26, period: 1.8, delay: -0.6 },
    ],
    particles: [
      { kind: 'ember', count: 12, area: { x: 355, y: 150, w: 45, h: 35 }, colors: ['#ffe6b0', '#ffd08a'], duration: 6, opacity: 0.8 },
      { kind: 'mote', count: 9, area: { x: 170, y: 30, w: 70, h: 170 }, colors: ['#c4d4ec'], duration: 12, opacity: 0.61 },
    ],
  },
  event_lost_caravan: {
    src: EVENT('lost_caravan'), width: 480, height: 320,
    glows: [
      { x: 318, y: 120, r: 36, color: '#ffb04a', min: 0.16, max: 0.3, period: 2.6 },
      { x: 318, y: 120, r: 14, color: '#ffd98a', min: 0.18, max: 0.3, period: 1.9, delay: -0.7 },
    ],
    particles: [
      { kind: 'firefly', count: 10, area: { x: 30, y: 70, w: 260, h: 120 }, colors: ['#ddff7d'], duration: 9, opacity: 0.85 },
      { kind: 'sparkle', count: 3, area: { x: 285, y: 252, w: 55, h: 26 }, colors: ['#d2b4ff'], duration: 5, opacity: 0.6 },
    ],
  },
  event_moonlit_garden: {
    src: EVENT('moonlit_garden'), width: 480, height: 320,
    // 花と月の光は、ゆっくり大きく強弱させて「光っている」ことを見せる（6〜9 秒で、ほぼ消える所から明るい所まで）。
    glows: [
      { x: 335, y: 55, r: 52, color: '#cfe3ff', min: 0.05, max: 0.22, period: 9, style: 'pulse' },
      { x: 35, y: 250, r: 22, color: '#6fa8ff', min: 0.03, max: 0.3, period: 6.5, style: 'pulse' },
      { x: 90, y: 215, r: 20, color: '#6fa8ff', min: 0.03, max: 0.3, period: 7.2, delay: -2.4, style: 'pulse' },
      { x: 195, y: 205, r: 16, color: '#6fa8ff', min: 0.03, max: 0.28, period: 7.8, delay: -1.2, style: 'pulse' },
      { x: 255, y: 190, r: 16, color: '#6fa8ff', min: 0.03, max: 0.28, period: 6.8, delay: -4, style: 'pulse' },
      { x: 392, y: 190, r: 20, color: '#6fa8ff', min: 0.03, max: 0.3, period: 7.5, delay: -5.2, style: 'pulse' },
    ],
    particles: [
      { kind: 'firefly', count: 8, area: { x: 50, y: 170, w: 400, h: 130 }, colors: ['#c9ff9c', '#a8d8ff'], duration: 10, opacity: 0.8 },
      { kind: 'sparkle', count: 5, area: { x: 290, y: 222, w: 45, h: 26 }, colors: ['#e6f0ff'], duration: 4.5, opacity: 0.7 },
    ],
  },
  event_old_library: {
    src: EVENT('old_library'), width: 480, height: 320,
    // 2026-11-28 描き直し（PixelLab）。窓（x 300、y 30〜160）から左下の床の光だまりへ月明かり。机のろうそく。
    rays: [{ x: 292, y: 100, w: 70, h: 150, angle: 33, color: 'rgba(170, 214, 255, 1)', min: 0.06, max: 0.18, period: 12 }],
    glows: [
      { x: 300, y: 85, r: 34, ry: 70, color: '#a8d4ff', min: 0.06, max: 0.16, period: 12, style: 'pulse' },
      { x: 198, y: 232, r: 58, ry: 18, color: '#a8d4ff', min: 0.06, max: 0.16, period: 12, style: 'pulse' },
      { x: 362, y: 177, r: 24, color: '#ffc66a', min: 0.18, max: 0.32, period: 2.3 },
      { x: 362, y: 190, r: 46, ry: 22, color: '#ffb44a', min: 0.06, max: 0.14, period: 2.9, delay: -0.8 },
    ],
    particles: [
      { kind: 'mote', count: 20, area: { x: 200, y: 100, w: 100, h: 140 }, colors: ['#dcefff', '#c4e0ff'], duration: 13, opacity: 0.7 },
    ],
  },
  event_sealed_vault: {
    src: EVENT('sealed_vault'), width: 480, height: 320,
    glows: [{ x: 320, y: 150, r: 115, color: '#3cff9a', min: 0.05, max: 0.13, period: 7, style: 'pulse' }],
    particles: [
      { kind: 'sparkle', count: 8, area: { x: 140, y: 250, w: 270, h: 55 }, colors: ['#ffe08a'], duration: 5.5, opacity: 0.75 },
      { kind: 'mote', count: 8, area: { x: 180, y: 40, w: 260, h: 200 }, colors: ['#a8f0c8'], duration: 13, opacity: 0.47 },
    ],
  },
  event_shady_merchant: {
    src: EVENT('shady_merchant'), width: 480, height: 320,
    // 2026-11-28 描き直し（PixelLab）。ランタン 3 つと、台の上の青い宝玉・小瓶。
    glows: [
      { x: 215, y: 128, r: 26, color: '#ffb44a', min: 0.16, max: 0.3, period: 2.5 },
      { x: 362, y: 115, r: 24, color: '#ffb44a', min: 0.16, max: 0.3, period: 2.9, delay: -1 },
      { x: 383, y: 135, r: 22, color: '#ffb44a', min: 0.16, max: 0.3, period: 2.2, delay: -0.4 },
      { x: 287, y: 182, r: 22, color: '#6f9cff', min: 0.12, max: 0.28, period: 5, style: 'pulse' },
      { x: 255, y: 185, r: 12, color: '#d46cff', min: 0.08, max: 0.2, period: 4.2, delay: -2, style: 'pulse' },
      { x: 187, y: 194, r: 10, color: '#8f7cff', min: 0.08, max: 0.2, period: 4.8, delay: -1, style: 'pulse' },
    ],
    particles: [
      { kind: 'sparkle', count: 4, area: { x: 277, y: 170, w: 20, h: 20 }, colors: ['#bfd8ff'], duration: 4, opacity: 0.8 },
      { kind: 'mote', count: 10, area: { x: 200, y: 90, w: 200, h: 120 }, colors: ['#ffe2a8'], duration: 13, opacity: 0.45 },
    ],
  },
  // ---------------- イベント（2026-11-28 追加。PixelLab で直接生成） ----------------
  event_forest_shrine: {
    src: EVENT('forest_shrine'), width: 480, height: 320,
    rays: [{ x: 175, y: -6, w: 70, h: 200, angle: -30, color: 'rgba(240, 236, 190, 1)', min: 0.06, max: 0.17, period: 11 }],
    glows: [
      { x: 259, y: 176, r: 12, color: '#ffd27a', min: 0.12, max: 0.26, period: 2.6 },
      { x: 369, y: 180, r: 12, color: '#ffd27a', min: 0.12, max: 0.26, period: 3, delay: -1 },
      { x: 320, y: 150, r: 60, ry: 40, color: '#f2f0b8', min: 0.04, max: 0.1, period: 11, style: 'pulse' },
    ],
    particles: [
      { kind: 'firefly', count: 10, area: { x: 40, y: 120, w: 400, h: 150 }, colors: ['#e8ff7d', '#d2f56a'], duration: 9, opacity: 0.85 },
      { kind: 'mote', count: 10, area: { x: 170, y: 30, w: 150, h: 130 }, colors: ['#f5ffd2'], duration: 12, opacity: 0.6 },
    ],
  },
  event_fairy_ring: {
    src: EVENT('fairy_ring'), width: 480, height: 320,
    glows: [
      { x: 239, y: 224, r: 120, ry: 30, color: '#8fe8ff', min: 0.04, max: 0.2, period: 7, style: 'pulse' },
      { x: 239, y: 224, r: 70, ry: 18, color: '#c49cff', min: 0.04, max: 0.16, period: 5.5, delay: -2, style: 'pulse' },
      { x: 266, y: 36, r: 22, color: '#e6f0ff', min: 0.06, max: 0.16, period: 9, style: 'pulse' },
    ],
    particles: [
      { kind: 'sparkle', count: 12, area: { x: 130, y: 180, w: 220, h: 60 }, colors: ['#c8f6ff', '#e2ccff'], duration: 4.5, opacity: 0.85 },
      { kind: 'mote', count: 14, area: { x: 140, y: 120, w: 200, h: 100 }, colors: ['#c8f6ff', '#e2ccff'], duration: 10, opacity: 0.65 },
    ],
    mist: [{ y: 190, h: 60, color: '#9fc4c8', opacity: 0.12, period: 80 }],
  },
  event_woodcutter_hut: {
    src: EVENT('woodcutter_hut'), width: 480, height: 320,
    glows: [
      { x: 346, y: 166, r: 22, color: '#ffb44a', min: 0.14, max: 0.28, period: 2.7 },
      { x: 300, y: 220, r: 70, ry: 18, color: '#ffb44a', min: 0.04, max: 0.1, period: 3.2, delay: -1 },
    ],
    particles: [
      { kind: 'mote', count: 8, area: { x: 345, y: 20, w: 30, h: 40 }, colors: ['#c8c8d0'], duration: 8, opacity: 0.45 },
    ],
  },
  event_underground_lake: {
    src: EVENT('underground_lake'), width: 480, height: 320,
    rays: [{ x: 158, y: 30, w: 60, h: 190, angle: -6, color: 'rgba(220, 240, 255, 1)', min: 0.06, max: 0.18, period: 12 }],
    glows: [
      { x: 196, y: 207, r: 80, ry: 14, color: '#d8f0ff', min: 0.06, max: 0.18, period: 12, style: 'pulse' },
      { x: 101, y: 222, r: 16, color: '#7fd8ff', min: 0.06, max: 0.22, period: 6, style: 'pulse' },
      { x: 331, y: 222, r: 16, color: '#7fd8ff', min: 0.06, max: 0.22, period: 6.8, delay: -2, style: 'pulse' },
      { x: 281, y: 166, r: 10, color: '#7fd8ff', min: 0.06, max: 0.2, period: 5.4, delay: -3, style: 'pulse' },
    ],
    particles: [
      { kind: 'sparkle', count: 8, area: { x: 130, y: 196, w: 140, h: 22 }, colors: ['#ffffff', '#d4f4ff'], duration: 4.5, opacity: 0.75 },
      { kind: 'mote', count: 10, area: { x: 130, y: 40, w: 70, h: 160 }, colors: ['#e2f2ff'], duration: 12, opacity: 0.55 },
      { kind: 'drip', count: 3, area: { x: 60, y: 20, w: 360, h: 30 }, colors: ['#93a9dc'], duration: 6, opacity: 0.55 },
    ],
  },
  event_abandoned_mine: {
    src: EVENT('abandoned_mine'), width: 480, height: 320,
    glows: [
      { x: 380, y: 104, r: 26, color: '#ffb44a', min: 0.16, max: 0.3, period: 2.5 },
      { x: 330, y: 170, r: 90, ry: 50, color: '#ff9a3a', min: 0.03, max: 0.08, period: 3.5, delay: -1 },
    ],
    particles: [{ kind: 'mote', count: 14, area: { x: 280, y: 70, w: 140, h: 140 }, colors: ['#ffe0b0', '#d8c4a0'], duration: 12, opacity: 0.6 }],
  },
  event_catacombs: {
    src: EVENT('catacombs'), width: 480, height: 320,
    glows: [
      { x: 109, y: 62, r: 22, color: '#8fc8ff', min: 0.08, max: 0.26, period: 5, style: 'pulse' },
      { x: 213, y: 129, r: 22, color: '#8fc8ff', min: 0.08, max: 0.26, period: 5.8, delay: -2, style: 'pulse' },
      { x: 341, y: 100, r: 22, color: '#8fc8ff', min: 0.08, max: 0.26, period: 6.4, delay: -4, style: 'pulse' },
      { x: 141, y: 99, r: 12, color: '#ffb44a', min: 0.1, max: 0.22, period: 2.6 },
    ],
    particles: [{ kind: 'mote', count: 10, area: { x: 80, y: 40, w: 300, h: 130 }, colors: ['#b8dcff'], duration: 13, opacity: 0.5 }],
  },
  event_lava_altar: {
    src: EVENT('lava_altar'), width: 480, height: 320,
    glows: [
      { x: 346, y: 161, r: 46, ry: 26, color: '#ff3a2a', min: 0.08, max: 0.26, period: 4.5, style: 'pulse' },
      { x: 101, y: 246, r: 60, ry: 22, color: '#ff7a2a', min: 0.12, max: 0.24, period: 3.4 },
    ],
    particles: [{ kind: 'ember', count: 20, area: { x: 40, y: 160, w: 420, h: 140 }, colors: ['#ffe6b0', '#ffd08a', '#fff0c8'], duration: 9, opacity: 0.8 }],
  },
  event_caged_prisoner: {
    src: EVENT('caged_prisoner'), width: 480, height: 320,
    glows: [{ x: 280, y: 300, r: 200, ry: 70, color: '#ff4a1e', min: 0.06, max: 0.16, period: 6, style: 'pulse' }],
    particles: [{ kind: 'ember', count: 18, area: { x: 100, y: 200, w: 340, h: 110 }, colors: ['#ffe6b0', '#ffd08a'], duration: 9, opacity: 0.75 }],
  },
  event_travelers_camp: {
    src: EVENT('travelers_camp'), width: 480, height: 320,
    glows: [
      { x: 174, y: 201, r: 30, color: '#ffb44a', min: 0.18, max: 0.32, period: 2 },
      { x: 174, y: 215, r: 110, ry: 40, color: '#ff9a3a', min: 0.05, max: 0.12, period: 2.7, delay: -0.7 },
    ],
    particles: [
      { kind: 'ember', count: 12, area: { x: 160, y: 185, w: 28, h: 20 }, colors: ['#ffe6b0', '#ffd08a'], duration: 6, opacity: 0.85 },
      { kind: 'sparkle', count: 6, area: { x: 20, y: 10, w: 440, h: 80 }, colors: ['#e6eeff'], duration: 6, opacity: 0.6 },
    ],
  },
  event_fortune_teller: {
    src: EVENT('fortune_teller'), width: 480, height: 320,
    glows: [
      { x: 342, y: 170, r: 30, color: '#a86cff', min: 0.1, max: 0.3, period: 5, style: 'pulse' },
      { x: 204, y: 168, r: 30, color: '#ffb44a', min: 0.14, max: 0.28, period: 2.4 },
    ],
    particles: [
      { kind: 'sparkle', count: 6, area: { x: 325, y: 152, w: 34, h: 34 }, colors: ['#e2ccff'], duration: 4, opacity: 0.8 },
      { kind: 'mote', count: 8, area: { x: 150, y: 60, w: 260, h: 140 }, colors: ['#ffe2a8'], duration: 13, opacity: 0.4 },
    ],
  },
}
