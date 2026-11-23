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

// Approved production outputs. Besides detecting corrupt PNGs, these hashes
// protect the semantic atlas mapping: swapping two valid slots must not silently
// ship the wrong class or type under a well-formed filename.
const approvedCardFrameHashes: Readonly<Record<string, string>> = {
  '/cards/frames/affiliations/curse.png': '3780fcf2cd94297432a5a5dd158860616358707a3b335122f8482930fa701356',
  '/cards/frames/affiliations/enhancer.png': 'a84f6d18e15d662bf430637cd8dc67331510e74cc7d7a6b2aae9b86bf059095f',
  '/cards/frames/affiliations/guardian.png': '3452b66f5fda8dc1203483aa3d5b73aa25e59b68ee383ed2e0bc4992fb10016d',
  '/cards/frames/affiliations/striker.png': '512ce80124c85f439b40c3db758a6bfc66a0d27a6c8b60b7fe457df42333e93e',
  '/cards/frames/affiliations/tactician.png': '958a8a9670d7b0288410e1ad9c272bd44b3f6db0031801144d586db89a44e8d2',
  '/cards/frames/affiliations/universal.png': '9f198358104b85da52b6ba633d7aaef966872b8a8477c11cd34cca88a4c9b21b',
  '/cards/frames/costs/curse.png': '680a45b0c0fd0ac5695260aacc6ff678ad2b145994b403b92baf19228c9c2614',
  '/cards/frames/costs/enhancer.png': 'bd70d26d95152c6726dd32203476aa608fc1c4202e3bf1f70da30ea5e5a13eb4',
  '/cards/frames/costs/guardian.png': '1d9c2bf91c714b7c7c0609bf074e187728f24872d4fe4f64630241d6ca015d1e',
  '/cards/frames/costs/striker.png': '582200e1edd9102dc6f49d5aa852969bd51a2ba596acb118ac4070c1e5bf67a3',
  '/cards/frames/costs/tactician.png': '68f7b9eb8dcd752def44fe41d21c07a1d05b072fa97f816c00dbc820fc5d2e19',
  '/cards/frames/costs/universal.png': '2ec785487d1360a79d971fa1a06b629c916c3870ace6806d04ef6e7e226e9423',
  '/cards/frames/masks/attack.png': '9f7fba2c2f121432e85473cbf2a487df11c5805faa318c31fa5456af0f344fe8',
  '/cards/frames/masks/curse.png': 'ae74f4d82a1b677416c72fbfed03c0ab2f6d06e81fdce73bc7ceef3531cf82ea',
  '/cards/frames/masks/power.png': '577d2cf69e8bb77e798188084cea8bc4500c3a2e4ca01554c0a9d5b3613ab1c1',
  '/cards/frames/masks/skill.png': 'c4f131fc679d72083e3cf02a099a2249a06420251673b1308e70d9d98a260af9',
  '/cards/frames/masks/status.png': 'ef17149ac100847f137eb57d895a04b3bf8431ceb675e8fd7b4496f05b807f31',
  '/cards/frames/masks/unit.png': 'e1d477fb98e7e997617a4d5ab8ad27fe9434acc2ccfcc38cf31d171de3dac445',
  '/cards/frames/types/attack.png': '768ed73c193d7c97270d02a5633ede5699e021a5a3d9978b580021e9da7e3c8a',
  '/cards/frames/types/curse.png': '9adfd2a86b92dc1d9db6b2d534422d63a1c22a26f94bee0ff82857214447aa47',
  '/cards/frames/types/power.png': '52c914cdf473b29c6d8f53e02c2623029d79b9ae63b6f6efbbfe65bce2b47f4f',
  '/cards/frames/types/skill.png': 'f5a0495a1c36bfd5b27018fe72221277e1ee1a26f8b0405cfeed81ac877a1bff',
  '/cards/frames/types/status.png': 'a4c6759c44c48a67395fe7375bb9e3cc319960aafd3568f86954b2a904ded108',
  '/cards/frames/types/unit.png': '36d2c2f8869b441f742728dac2aa1de5594e32c6d40528fc241db3eb96371ec3',
}

test('added content is catalogued and every generated PNG decodes in a real browser', async ({ page }) => {
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

  const imageResults = await page.evaluate(async ({
    relicIds,
    potionIds,
    eventIds,
    affiliationFrameIds,
    cardTypeFrameIds,
    costFrameIds,
  }) => {
    const paths = [
      ...relicIds.map(id => '/icons/relics/' + id + '.png'),
      ...potionIds.map(id => '/icons/potions/' + id + '.png'),
      ...eventIds.map(id => '/backgrounds/events/' + id + '.png'),
      ...affiliationFrameIds.map(id => '/cards/frames/affiliations/' + id + '.png'),
      ...cardTypeFrameIds.map(id => '/cards/frames/types/' + id + '.png'),
      ...cardTypeFrameIds.map(id => '/cards/frames/masks/' + id + '.png'),
      ...costFrameIds.map(id => '/cards/frames/costs/' + id + '.png'),
    ]

    return await Promise.all(paths.map(async source => {
      const response = await fetch(source)
      const bytes = await response.arrayBuffer()
      const digest = await crypto.subtle.digest('SHA-256', bytes)
      const sha256 = Array.from(new Uint8Array(digest))
        .map(byte => byte.toString(16).padStart(2, '0'))
        .join('')
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
      let visiblePixels = 0
      for (let index = 3; index < pixels.length; index += 4) {
        if (pixels[index] > 0) visiblePixels++
      }
      return {
        source,
        width: image.naturalWidth,
        height: image.naturalHeight,
        cornerAlpha: pixels[3],
        visiblePixels,
        sha256,
      }
    }))
  }, { relicIds, potionIds, eventIds, affiliationFrameIds, cardTypeFrameIds, costFrameIds })

  expect(imageResults).toHaveLength(44)
  for (const result of imageResults) {
    expect(result.visiblePixels, result.source).toBeGreaterThan(200)
    if (result.source.includes('/icons/')) {
      expect(result.width, result.source).toBe(180)
      expect(result.height, result.source).toBe(180)
      expect(result.cornerAlpha, result.source).toBe(0)
    } else if (result.source.includes('/cards/frames/')) {
      const isCostBase = result.source.includes('/cards/frames/costs/')
      expect(result.width, result.source).toBe(isCostBase ? 128 : 560)
      expect(result.height, result.source).toBe(isCostBase ? 128 : 784)
      expect(result.cornerAlpha, result.source).toBe(0)
      expect(result.sha256, result.source).toBe(approvedCardFrameHashes[result.source])
    } else {
      expect(result.width, result.source).toBe(1536)
      expect(result.height, result.source).toBe(1024)
    }
  }
})

test('all playable-character motion sheets keep every frame complete and genuinely animated', async ({ page }) => {
  await page.goto('/')
  const heroIds = ['default', 'guardian_mireille', 'tactician_yuno', 'enhancer_lyra']
  const motions = [
    { name: 'idle', frameCount: 8, frameWidth: 192, minimumDistinct: 6, maximumTopRun: 32 },
    { name: 'attack', frameCount: 4, frameWidth: 384, minimumDistinct: 4, maximumTopRun: 72 },
    { name: 'hurt', frameCount: 4, frameWidth: 192, minimumDistinct: 4, maximumTopRun: 32 },
  ]

  const results = await page.evaluate(async ({ heroIds, motions }) => {
    return await Promise.all(heroIds.flatMap(heroId => motions.map(async motion => {
      const source = `/characters/${heroId}_${motion.name}.png`
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
      const frames = []
      const hashes = new Set<number>()

      for (let frame = 0; frame < motion.frameCount; frame++) {
        let minX = motion.frameWidth
        let minY = 256
        let maxX = -1
        let maxY = -1
        let hash = 2166136261
        for (let y = 0; y < 256; y++) {
          for (let x = 0; x < motion.frameWidth; x++) {
            const offset = (y * canvas.width + frame * motion.frameWidth + x) * 4
            const alpha = pixels[offset + 3]
            for (let channel = 0; channel < 4; channel++) {
              hash = Math.imul(hash ^ pixels[offset + channel], 16777619)
            }
            if (alpha <= 8) continue
            minX = Math.min(minX, x)
            minY = Math.min(minY, y)
            maxX = Math.max(maxX, x)
            maxY = Math.max(maxY, y)
          }
        }
        hashes.add(hash >>> 0)

        let longestTopRun = 0
        let currentTopRun = 0
        for (let x = 0; x < motion.frameWidth; x++) {
          const alpha = pixels[(minY * canvas.width + frame * motion.frameWidth + x) * 4 + 3]
          if (alpha > 8) {
            currentTopRun++
            longestTopRun = Math.max(longestTopRun, currentTopRun)
          } else {
            currentTopRun = 0
          }
        }
        frames.push({
          gutters: [minX, minY, motion.frameWidth - 1 - maxX, 255 - maxY],
          longestTopRun,
        })
      }

      return {
        source,
        width: image.naturalWidth,
        height: image.naturalHeight,
        expectedFrames: motion.frameCount,
        frameWidth: motion.frameWidth,
        maximumTopRun: motion.maximumTopRun,
        minimumDistinct: motion.minimumDistinct,
        distinctFrames: hashes.size,
        frames,
      }
    })))
  }, { heroIds, motions })

  expect(results).toHaveLength(12)
  for (const result of results) {
    expect(result.width, result.source).toBe(result.expectedFrames * result.frameWidth)
    expect(result.height, result.source).toBe(256)
    expect(result.distinctFrames, result.source).toBeGreaterThanOrEqual(result.minimumDistinct)
    for (const frame of result.frames) {
      expect(Math.min(...frame.gutters), `${result.source} ${frame.gutters.join(',')}`)
        .toBeGreaterThanOrEqual(8)
      expect(frame.longestTopRun, result.source).toBeLessThanOrEqual(result.maximumTopRun)
    }
  }
})

test('playable portraits and the combo plate have production alpha geometry', async ({ page }) => {
  await page.goto('/')
  const heroIds = ['default', 'guardian_mireille', 'tactician_yuno', 'enhancer_lyra']

  const result = await page.evaluate(async heroIds => {
    const inspect = async (source: string) => {
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
      let minX = canvas.width
      let minY = canvas.height
      let maxX = -1
      let maxY = -1
      for (let y = 0; y < canvas.height; y++) {
        for (let x = 0; x < canvas.width; x++) {
          if (pixels[(y * canvas.width + x) * 4 + 3] <= 8) continue
          minX = Math.min(minX, x)
          minY = Math.min(minY, y)
          maxX = Math.max(maxX, x)
          maxY = Math.max(maxY, y)
        }
      }
      const alphaAt = (x: number, y: number) => pixels[(y * canvas.width + x) * 4 + 3]
      return {
        source,
        width: canvas.width,
        height: canvas.height,
        gutters: [minX, minY, canvas.width - 1 - maxX, canvas.height - 1 - maxY],
        corners: [
          alphaAt(0, 0),
          alphaAt(canvas.width - 1, 0),
          alphaAt(0, canvas.height - 1),
          alphaAt(canvas.width - 1, canvas.height - 1),
        ],
        centreAlpha: alphaAt(Math.floor(canvas.width / 2), Math.floor(canvas.height / 2)),
      }
    }

    return {
      portraits: await Promise.all(heroIds.map(id => inspect(`/characters/portraits/${id}.png`))),
      combo: await inspect('/ui/battle-control-combo.png'),
    }
  }, heroIds)

  for (const portrait of result.portraits) {
    expect(portrait.width, portrait.source).toBe(384)
    expect(portrait.height, portrait.source).toBe(512)
    expect(Math.min(...portrait.gutters), portrait.source).toBeGreaterThanOrEqual(8)
    expect(Math.max(...portrait.corners), portrait.source).toBe(0)
  }
  expect(result.combo.width).toBe(220)
  expect(result.combo.height).toBe(104)
  expect(Math.max(...result.combo.corners)).toBeLessThanOrEqual(8)
  expect(result.combo.centreAlpha).toBeGreaterThan(220)
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
      ...await readAlpha(`/cards/frames/masks/${id}.png`),
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
      const frame = await readAlpha(`/cards/frames/affiliations/${id}.png`)
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
