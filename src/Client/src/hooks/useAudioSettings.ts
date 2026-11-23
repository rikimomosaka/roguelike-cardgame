import { useCallback, useEffect, useRef, useState } from 'react'
import { getAudioSettings, putAudioSettings } from '../api/audioSettings'
import type { AudioSettingsDto } from '../api/types'

export type AudioSettingsSaveStatus = 'idle' | 'saving' | 'saved' | 'error'

export const DEFAULT_AUDIO_SETTINGS: AudioSettingsDto = Object.freeze({
  schemaVersion: 2,
  master: 80,
  bgm: 70,
  se: 80,
  voice: 80,
})

const DEBOUNCE_MS = 500

function clampVolume(value: number): number {
  if (!Number.isFinite(value)) return 0
  return Math.min(100, Math.max(0, Math.round(value)))
}

type PendingSave = {
  accountId: string
  settings: AudioSettingsDto
}

type FlushOptions = {
  keepalive?: boolean
}

type OwnedSettings = {
  accountId: string
  value: AudioSettingsDto
}

type OwnedSaveStatus = {
  accountId: string
  value: AudioSettingsSaveStatus
}

export function useAudioSettings(accountId: string | null): {
  settings: AudioSettingsDto | null
  update: (patch: Partial<Omit<AudioSettingsDto, 'schemaVersion'>>) => void
  flush: (options?: FlushOptions) => Promise<void>
  saveStatus: AudioSettingsSaveStatus
} {
  const [ownedSettings, setOwnedSettings] = useState<OwnedSettings | null>(null)
  const [ownedSaveStatus, setOwnedSaveStatus] = useState<OwnedSaveStatus | null>(null)
  const settings = ownedSettings?.accountId === accountId ? ownedSettings.value : null
  const saveStatus = ownedSaveStatus?.accountId === accountId
    ? ownedSaveStatus.value
    : 'idle'
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const pendingRef = useRef<PendingSave | null>(null)
  // The tracked promise always resolves to a success flag. It must never
  // expose the raw rejected fetch promise to a concurrent flush caller.
  const inFlightRef = useRef<Promise<boolean> | null>(null)
  const mountedRef = useRef(true)

  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  const clearSaveTimer = useCallback(() => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current)
      timerRef.current = null
    }
  }, [])

  const flush = useCallback(async (options?: FlushOptions): Promise<void> => {
    clearSaveTimer()

    while (true) {
      if (inFlightRef.current) {
        const inFlight = inFlightRef.current
        await inFlight
        if (inFlightRef.current === inFlight) inFlightRef.current = null
        continue
      }

      const pending = pendingRef.current
      if (!pending) return
      pendingRef.current = null

      if (mountedRef.current) {
        setOwnedSaveStatus({ accountId: pending.accountId, value: 'saving' })
      }
      const request = (async (): Promise<boolean> => {
        try {
          await putAudioSettings(pending.accountId, pending.settings, {
            keepalive: options?.keepalive,
          })
          if (mountedRef.current) {
            setOwnedSaveStatus({ accountId: pending.accountId, value: 'saved' })
          }
          return true
        } catch {
          // Keep the failed value retryable, but never overwrite a newer
          // optimistic change that was queued while this request was running.
          if (pendingRef.current === null) pendingRef.current = pending
          if (mountedRef.current) {
            setOwnedSaveStatus({ accountId: pending.accountId, value: 'error' })
          }
          return false
        }
      })()
      inFlightRef.current = request
      const succeeded = await request
      if (inFlightRef.current === request) inFlightRef.current = null
      if (!succeeded) return
    }
  }, [clearSaveTimer])

  useEffect(() => {
    let cancelled = false

    if (!accountId) return () => { cancelled = true }

    getAudioSettings(accountId)
      .then((next) => {
        if (!cancelled) {
          setOwnedSettings({ accountId, value: next })
          setOwnedSaveStatus({ accountId, value: 'idle' })
        }
      })
      .catch(() => {
        if (!cancelled) setOwnedSaveStatus({ accountId, value: 'error' })
      })

    return () => {
      cancelled = true
      void flush({ keepalive: true })
    }
  }, [accountId, flush])

  const update = useCallback(
    (patch: Partial<Omit<AudioSettingsDto, 'schemaVersion'>>) => {
      const owner = accountId
      if (!owner) return

      setOwnedSettings((previous) => {
        if (!previous || previous.accountId !== owner) return previous
        const merged = { ...previous.value, ...patch }
        const next: AudioSettingsDto = {
          schemaVersion: 2,
          master: clampVolume(merged.master),
          bgm: clampVolume(merged.bgm),
          se: clampVolume(merged.se),
          voice: clampVolume(merged.voice),
        }
        pendingRef.current = { accountId: owner, settings: next }
        clearSaveTimer()
        timerRef.current = setTimeout(() => { void flush() }, DEBOUNCE_MS)
        return { accountId: owner, value: next }
      })
    },
    [accountId, clearSaveTimer, flush],
  )

  useEffect(() => {
    const onPageHide = () => { void flush({ keepalive: true }) }
    window.addEventListener('pagehide', onPageHide)
    return () => {
      window.removeEventListener('pagehide', onPageHide)
      clearSaveTimer()
      void flush({ keepalive: true })
    }
  }, [clearSaveTimer, flush])

  return { settings, update, flush, saveStatus }
}
