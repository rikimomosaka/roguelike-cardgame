import { apiRequest } from './client'
import type { AudioSettingsDto } from './types'

export function getAudioSettings(accountId: string): Promise<AudioSettingsDto> {
  return apiRequest<AudioSettingsDto>('GET', '/audio-settings', { accountId })
}

export function putAudioSettings(
  accountId: string,
  settings: AudioSettingsDto,
): Promise<void> {
  return apiRequest<void>('PUT', '/audio-settings', { accountId, body: settings })
}
