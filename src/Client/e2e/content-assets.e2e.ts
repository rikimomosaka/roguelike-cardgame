import { expect, test } from '@playwright/test'

const relicIds = [
  'iron_feather',
  'ember_hourglass',
  'moonlit_mirror',
  'scholar_lens',
  'merchant_scale',
  'bloodstone_ring',
  'storm_drum',
  'golden_sprout',
]

const potionIds = [
  'barrier_potion',
  'regeneration_potion',
  'resistance_potion',
  'reflection_potion',
  'frenzy_potion',
  'insight_potion',
  'mastery_potion',
  'revival_potion',
]

const eventIds = [
  'forgotten_forge',
  'moonlit_garden',
  'sealed_vault',
  'lost_caravan',
]

const affiliationFrameIds = [
  'striker',
  'guardian',
  'tactician',
  'enhancer',
  'universal',
  'curse',
]

const cardTypeFrameIds = ['attack', 'skill', 'power', 'unit', 'status', 'curse']
const costFrameIds = affiliationFrameIds

// 表示用の画像は tools/art-pipeline/build-assets.mjs が規格どおりに作る（docs/current-art-asset-spec.md）。
// ここでは「実際のブラウザで読めて、規格のサイズで、半透明が無い」ことを確かめる。
type ImageSpec = { source: string, width: number, height: number }

const specs: ImageSpec[] = [
  ...relicIds.map(id => ({ source: `/assets/icons/relics/${id}.png`, width: 32, height: 32 })),
  ...potionIds.map(id => ({ source: `/assets/icons/potions/${id}.png`, width: 32, height: 32 })),
  ...eventIds.map(id => ({ source: `/assets/backgrounds/events/${id}.png`, width: 480, height: 320 })),
  ...affiliationFrameIds.map(id => ({ source: `/assets/cards/frames/affiliations/${id}.png`, width: 112, height: 157 })),
  ...cardTypeFrameIds.map(id => ({ source: `/assets/cards/frames/types/${id}.png`, width: 112, height: 157 })),
  ...cardTypeFrameIds.map(id => ({ source: `/assets/cards/frames/masks/${id}.png`, width: 112, height: 157 })),
  ...costFrameIds.map(id => ({ source: `/assets/cards/frames/costs/${id}.png`, width: 28, height: 28 })),
  ...['battle_forest', 'battle_cave', 'battle_hell'].map(id => ({ source: `/assets/backgrounds/battle/${id}.png`, width: 640, height: 360 })),
  ...['default', 'guardian_mireille', 'tactician_yuno', 'enhancer_lyra'].map(id => ({ source: `/assets/portraits/${id}.png`, width: 120, height: 160 })),
  ...['combo', 'tactical', 'ultimate', 'ready'].map(part => ({
    source: `/assets/ui/heroes/default_frame/${part}.png`,
    width: { combo: 56, tactical: 44, ultimate: 44, ready: 76 }[part]!,
    height: part === 'ready' ? 32 : 28,
  })),
]

test('added content is catalogued and every pixel-art PNG decodes at its spec size', async ({ page }) => {
  await page.goto('/')

  const catalog = await page.evaluate(async () => {
    const [relics, potions, events] = await Promise.all([
      fetch('/api/v1/catalog/relics').then(response => response.json()),
      fetch('/api/v1/catalog/potions').then(response => response.json()),
      fetch('/api/v1/catalog/events').then(response => response.json()),
    ])
    return { relics, potions, events }
  })

  expect(catalog.relics.map((relic: { id: string }) => relic.id))
    .toEqual(expect.arrayContaining(relicIds))
  expect(Object.keys(catalog.potions)).toEqual(expect.arrayContaining(potionIds))
  expect(catalog.events.map((event: { id: string }) => event.id))
    .toEqual(expect.arrayContaining(eventIds))

  const results = await page.evaluate(async (specs) => {
    return await Promise.all(specs.map(async spec => {
      const image = new Image()
      image.src = spec.source
      await image.decode()
      const canvas = document.createElement('canvas')
      canvas.width = image.naturalWidth
      canvas.height = image.naturalHeight
      const context = canvas.getContext('2d', { willReadFrequently: true })
      if (!context) throw new Error('2D canvas context is unavailable')
      context.drawImage(image, 0, 0)
      const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data
      let visiblePixels = 0
      let semiTransparent = 0
      for (let index = 3; index < pixels.length; index += 4) {
        if (pixels[index] > 0) visiblePixels++
        if (pixels[index] > 0 && pixels[index] < 255) semiTransparent++
      }
      return { ...spec, actualWidth: image.naturalWidth, actualHeight: image.naturalHeight, visiblePixels, semiTransparent }
    }))
  }, specs)

  for (const result of results) {
    expect(result.actualWidth, result.source).toBe(result.width)
    expect(result.actualHeight, result.source).toBe(result.height)
    expect(result.visiblePixels, result.source).toBeGreaterThan(20)
    expect(result.semiTransparent, result.source).toBe(0)
  }
})

test('all playable-character motion sheets use 128x128 frames that are genuinely animated', async ({ page }) => {
  await page.goto('/')
  const heroIds = ['default', 'guardian_mireille', 'tactician_yuno', 'enhancer_lyra']
  const motions = [
    { name: 'idle', minimumDistinct: 4 },
    { name: 'attack', minimumDistinct: 4 },
    { name: 'hurt', minimumDistinct: 3 },
  ]

  const results = await page.evaluate(async ({ heroIds, motions }) => {
    const SIZE = 128
    return await Promise.all(heroIds.flatMap(heroId => motions.map(async motion => {
      const source = `/assets/actors/heroes/${heroId}/${motion.name}.png`
      const image = new Image()
      image.src = source
      await image.decode()
      const canvas = document.createElement('canvas')
      canvas.width = image.naturalWidth
      canvas.height = image.naturalHeight
      const context = canvas.getContext('2d', { willReadFrequently: true })
      if (!context) throw new Error('2D canvas context is unavailable')
      context.drawImage(image, 0, 0)
      const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data
      const hashes = new Set<number>()
      const footRows: number[] = []
      const frameCount = Math.floor(canvas.width / SIZE)
      for (let frame = 0; frame < frameCount; frame++) {
        let hash = 2166136261
        let maxY = -1
        for (let y = 0; y < SIZE; y++) {
          for (let x = 0; x < SIZE; x++) {
            const offset = (y * canvas.width + frame * SIZE + x) * 4
            for (let channel = 0; channel < 4; channel++) hash = Math.imul(hash ^ pixels[offset + channel], 16777619)
            if (pixels[offset + 3] > 0) maxY = Math.max(maxY, y)
          }
        }
        hashes.add(hash >>> 0)
        footRows.push(maxY)
      }
      return {
        source,
        width: canvas.width,
        height: canvas.height,
        minimumDistinct: motion.minimumDistinct,
        distinctFrames: hashes.size,
        footRows,
      }
    })))
  }, { heroIds, motions })

  expect(results).toHaveLength(12)
  for (const result of results) {
    expect(result.height, result.source).toBe(128)
    expect(result.width % 128, result.source).toBe(0)
    expect(result.distinctFrames, result.source).toBeGreaterThanOrEqual(result.minimumDistinct)
    // 足元は地面の線（123 行目）付近にそろう。
    expect(result.footRows[0], result.source).toBeGreaterThanOrEqual(118)
    expect(result.footRows[0], result.source).toBeLessThanOrEqual(124)
  }
})

test('affiliation artwork backing covers every distinct type aperture', async ({ page }) => {
  await page.goto('/')

  const result = await page.evaluate(async ({ affiliationFrameIds, cardTypeFrameIds }) => {
    const readAlpha = async (source: string) => {
      const image = new Image()
      image.src = source
      await image.decode()
      const canvas = document.createElement('canvas')
      canvas.width = image.naturalWidth
      canvas.height = image.naturalHeight
      const context = canvas.getContext('2d', { willReadFrequently: true })
      if (!context) throw new Error('2D canvas context is unavailable')
      context.drawImage(image, 0, 0)
      const rgba = context.getImageData(0, 0, canvas.width, canvas.height).data
      const alpha = new Uint8Array(canvas.width * canvas.height)
      for (let pixel = 0; pixel < alpha.length; pixel++) alpha[pixel] = rgba[pixel * 4 + 3]
      return { alpha, width: canvas.width, height: canvas.height }
    }

    const masks = await Promise.all(cardTypeFrameIds.map(async id => ({
      id,
      ...await readAlpha(`/assets/cards/frames/masks/${id}.png`),
    })))
    const apertureUnion = new Uint8Array(masks[0].alpha.length)
    for (const mask of masks) {
      for (let pixel = 0; pixel < apertureUnion.length; pixel++) {
        if (mask.alpha[pixel] > 0) apertureUnion[pixel] = 1
      }
    }

    const pairDifferences: { pair: string, ratio: number }[] = []
    for (let leftIndex = 0; leftIndex < masks.length; leftIndex++) {
      for (let rightIndex = leftIndex + 1; rightIndex < masks.length; rightIndex++) {
        let unionPixels = 0
        let differentPixels = 0
        for (let pixel = 0; pixel < apertureUnion.length; pixel++) {
          const leftVisible = masks[leftIndex].alpha[pixel] > 0
          const rightVisible = masks[rightIndex].alpha[pixel] > 0
          if (leftVisible || rightVisible) unionPixels++
          if (leftVisible !== rightVisible) differentPixels++
        }
        pairDifferences.push({
          pair: `${masks[leftIndex].id}/${masks[rightIndex].id}`,
          ratio: differentPixels / unionPixels,
        })
      }
    }

    const affiliations = await Promise.all(affiliationFrameIds.map(async id => {
      const frame = await readAlpha(`/assets/cards/frames/affiliations/${id}.png`)
      let nonOpaqueAperturePixels = 0
      for (let pixel = 0; pixel < apertureUnion.length; pixel++) {
        if (apertureUnion[pixel] > 0 && frame.alpha[pixel] !== 255) {
          nonOpaqueAperturePixels++
        }
      }

      let opaqueEdgePixels = 0
      for (let x = 0; x < frame.width; x++) {
        if (frame.alpha[x] > 0) opaqueEdgePixels++
        if (frame.alpha[(frame.height - 1) * frame.width + x] > 0) opaqueEdgePixels++
      }
      for (let y = 1; y < frame.height - 1; y++) {
        if (frame.alpha[y * frame.width] > 0) opaqueEdgePixels++
        if (frame.alpha[y * frame.width + frame.width - 1] > 0) opaqueEdgePixels++
      }

      return { id, nonOpaqueAperturePixels, opaqueEdgePixels }
    }))

    return { affiliations, pairDifferences }
  }, { affiliationFrameIds, cardTypeFrameIds })

  expect(result.pairDifferences).toHaveLength(15)
  for (const comparison of result.pairDifferences) {
    expect(comparison.ratio, comparison.pair).toBeGreaterThanOrEqual(0.02)
  }
  for (const affiliation of result.affiliations) {
    expect(affiliation.nonOpaqueAperturePixels, affiliation.id).toBe(0)
    expect(affiliation.opaqueEdgePixels, affiliation.id).toBe(0)
  }
})
