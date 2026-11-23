import { expect, test } from '@playwright/test'
import { BGM_TRACKS, SE_CUES } from '../src/audio/audioManifest'

type AudioProbe = {
  oscillatorFrequencies: number[]
}

type WindowWithAudioProbe = Window & typeof globalThis & {
  __audioE2eProbe?: AudioProbe
}

const AUDIO_SOURCES = Array.from(new Set([
  ...Object.values(BGM_TRACKS).map(track => track.src),
  ...Object.values(SE_CUES).flatMap(variants => variants.map(variant => variant.src)),
])).sort()

function isAudioRequest(url: string): boolean {
  return new URL(url).pathname.startsWith('/audio/')
}

test('Audio Lab unlocks BGM/SE and every combo tone can be requested', async ({ page }) => {
  const consoleErrors: string[] = []
  const pageErrors: string[] = []
  const failedAudioRequests: Array<{ path: string, error: string }> = []
  const successfulAudioResponses = new Set<string>()

  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text())
  })
  page.on('pageerror', error => pageErrors.push(error.message))
  page.on('response', (response) => {
    if (isAudioRequest(response.url()) && response.status() < 400) {
      successfulAudioResponses.add(new URL(response.url()).pathname)
    }
  })
  page.on('requestfailed', (request) => {
    if (isAudioRequest(request.url())) {
      failedAudioRequests.push({
        path: new URL(request.url()).pathname,
        error: request.failure()?.errorText ?? 'unknown',
      })
    }
  })

  // Headless CI cannot prove that a human heard a sound. Instead, observe the
  // Web Audio oscillator start requests made by the 15 audition controls.
  await page.addInitScript(() => {
    const AudioContextConstructor = window.AudioContext
      ?? (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    ;(window as WindowWithAudioProbe).__audioE2eProbe = { oscillatorFrequencies: [] }
    if (!AudioContextConstructor) return

    const originalCreateOscillator = AudioContextConstructor.prototype.createOscillator
    AudioContextConstructor.prototype.createOscillator = function createProbedOscillator() {
      const oscillator = originalCreateOscillator.call(this)
      const originalSetValueAtTime = oscillator.frequency.setValueAtTime.bind(oscillator.frequency)
      oscillator.frequency.setValueAtTime = (value, startTime) => {
        const probe = (window as WindowWithAudioProbe).__audioE2eProbe
        probe?.oscillatorFrequencies.push(value)
        return originalSetValueAtTime(value, startTime)
      }
      return oscillator
    }
  })

  await page.goto('/?dev=audio')
  await expect(page.getByRole('heading', { name: 'AUDIO LAB' })).toBeVisible()
  await expect(page.getByRole('region', { name: '現在の音量' })).toContainText('VOICE')
  await expect(page.locator('.audio-lab__se-grid button')).toHaveCount(Object.keys(SE_CUES).length)

  const mapButton = page.locator('.audio-lab__button-grid button').filter({
    has: page.locator('small', { hasText: /^map$/ }),
  })
  await expect(mapButton).toBeEnabled()

  const [bgmResponse, seResponse] = await Promise.all([
    page.waitForResponse(response => new URL(response.url()).pathname === '/audio/bgm/map.ogg'),
    page.waitForResponse(response => new URL(response.url()).pathname === '/audio/se/ui-click.ogg'),
    mapButton.click(),
  ])
  expect(bgmResponse.status(), 'map BGM response').toBeLessThan(400)
  expect(seResponse.status(), 'UI click SE response').toBeLessThan(400)
  await expect(mapButton).toHaveAttribute('aria-pressed', 'true')

  const comboButtons = page.locator('.audio-lab__combo-grid button')
  await expect(comboButtons).toHaveCount(15)
  const leadFrequencies: number[] = []
  let emittedFrequencyCount = 0
  for (let combo = 1; combo <= 15; combo++) {
    const button = page.getByRole('button', { name: `コンボ${combo}を試聴`, exact: true })
    await expect(button).toBeEnabled()
    await button.click()
    const emitted = await page.evaluate((offset) => (
      (window as WindowWithAudioProbe).__audioE2eProbe?.oscillatorFrequencies.slice(offset) ?? []
    ), emittedFrequencyCount)
    expect(emitted.length, `combo ${combo} must emit a lead tone`).toBeGreaterThanOrEqual(1)
    leadFrequencies.push(emitted[0])
    emittedFrequencyCount += emitted.length
  }

  const frequencies = await page.evaluate(() => (
    (window as WindowWithAudioProbe).__audioE2eProbe?.oscillatorFrequencies ?? []
  ))
  // 5 / 10 / 15 add milestone harmonics, so the oscillator total is larger
  // than the fifteen lead notes. The first oscillator emitted by each click is
  // always the gameplay pitch whose strictly ascending scale we need to prove.
  expect(frequencies).toHaveLength(20)
  expect(leadFrequencies).toHaveLength(15)
  expect(new Set(leadFrequencies).size, 'combo 1-15 must request 15 distinct lead pitches').toBe(15)
  for (let index = 1; index < leadFrequencies.length; index++) {
    expect(leadFrequencies[index], `combo ${index + 1} pitch`)
      .toBeGreaterThan(leadFrequencies[index - 1])
  }

  // Give rejected media promises and browser console messages one task to surface.
  await page.waitForTimeout(100)
  // Chromium may cancel the unused tail of a streamed BGM range after receiving
  // a valid response. Only a failure with no successful response is actionable.
  expect(failedAudioRequests.filter(failure => !successfulAudioResponses.has(failure.path)))
    .toEqual([])
  expect(pageErrors).toEqual([])
  expect(consoleErrors).toEqual([])
})

test('all 51 manifest audio assets load and decode in Chromium', async ({ page }) => {
  test.setTimeout(120_000)
  expect(AUDIO_SOURCES).toHaveLength(51)
  expect(AUDIO_SOURCES.every(source => source.endsWith('.ogg'))).toBe(true)

  await page.goto('/?dev=audio')
  const results = await page.evaluate(async (sources) => {
    const AudioContextConstructor = window.AudioContext
      ?? (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    if (!AudioContextConstructor) throw new Error('Web Audio API is unavailable')

    const context = new AudioContextConstructor()
    const decoded: Array<{
      source: string
      status: number
      contentType: string | null
      byteLength: number
      duration: number
      channels: number
      sampleRate: number
      error?: string
    }> = []

    for (const source of sources) {
      try {
        const response = await fetch(source)
        const data = await response.arrayBuffer()
        const byteLength = data.byteLength
        if (!response.ok) {
          decoded.push({
            source,
            status: response.status,
            contentType: response.headers.get('content-type'),
            byteLength,
            duration: 0,
            channels: 0,
            sampleRate: 0,
            error: `HTTP ${response.status}`,
          })
          continue
        }

        const buffer = await context.decodeAudioData(data)
        decoded.push({
          source,
          status: response.status,
          contentType: response.headers.get('content-type'),
          byteLength,
          duration: buffer.duration,
          channels: buffer.numberOfChannels,
          sampleRate: buffer.sampleRate,
        })
      } catch (error) {
        decoded.push({
          source,
          status: 0,
          contentType: null,
          byteLength: 0,
          duration: 0,
          channels: 0,
          sampleRate: 0,
          error: error instanceof Error ? error.message : String(error),
        })
      }
    }

    await context.close()
    return decoded
  }, AUDIO_SOURCES)

  expect(results).toHaveLength(51)
  for (const result of results) {
    expect(result.error, result.source).toBeUndefined()
    expect(result.status, result.source).toBe(200)
    expect(result.contentType, result.source).toContain('audio')
    expect(result.byteLength, result.source).toBeGreaterThan(1_000)
    expect(result.duration, result.source).toBeGreaterThan(0)
    expect(result.channels, result.source).toBeGreaterThanOrEqual(1)
    expect(result.sampleRate, result.source).toBeGreaterThanOrEqual(8_000)
  }
})
