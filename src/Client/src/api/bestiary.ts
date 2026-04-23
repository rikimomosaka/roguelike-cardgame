import { apiRequest } from './client'
import type { BestiaryDto } from './types'

export async function getBestiary(accountId: string): Promise<BestiaryDto> {
  return await apiRequest<BestiaryDto>('GET', '/bestiary', { accountId })
}
