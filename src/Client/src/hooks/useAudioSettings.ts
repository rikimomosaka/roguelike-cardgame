import { useCallback, useEffect, useRef, useState } from 'react'
import { getAudioSettings, putAudioSettings } from '../api/audioSettings'
import type { AudioSettingsDto } from '../api/types'

type SaveStatus = 'idle' | 'saving' | 'saved' | 'error'

const DEBOUNCE_MS = 500

export function useAudioSettings(accountId: string): {
  settings: AudioSettingsDto | null
  update: (patch: Partial<Omit<AudioSettingsDto, 'schemaVersion'>>) => void
  saveStatus: SaveStatus
} {
  const [settings, setSettings] = useState<AudioSettingsDto | null>(null)
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle')
  const timerRef = useRef<number | null>(null)
  const pendingRef = useRef<AudioSettingsDto | null>(null)

  useEffect(() => {
    let cancelled = false
    getAudioSettings(accountId)
      .then((s) => { if (!cancelled) setSettings(s) })
      .catch(() => { if (!cancelled) setSaveStatus('error') })
    return () => { cancelled = true }
  }, [accountId])

  const flush = useCallback(async () => {
    const next = pendingRef.current
    if (!next) return
    pendingRef.current = null
    setSaveStatus('saving')
    try {
      await putAudioSettings(accountId, next)
      setSaveStatus('saved')
    } catch {
      setSaveStatus('error')
    }
  }, [accountId])

  const update = useCallback(
    (patch: Partial<Omit<AudioSettingsDto, 'schemaVersion'>>) => {
      setSettings((prev) => {
        if (!prev) return prev
        const next = { ...prev, ...patch }
        pendingRef.current = next
        if (timerRef.current !== null) window.clearTimeout(timerRef.current)
        timerRef.current = window.setTimeout(() => { void flush() }, DEBOUNCE_MS)
        return next
      })
    },
    [flush],
  )

  useEffect(() => {
    return () => {
      if (timerRef.current !== null) window.clearTimeout(timerRef.current)
    }
  }, [])

  return { settings, update, saveStatus }
}
