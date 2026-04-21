import { apiRequest } from './client'
import type { AccountDto } from './types'

export function createAccount(accountId: string): Promise<AccountDto> {
  return apiRequest<AccountDto>('POST', '/accounts', { body: { accountId } })
}

export function getAccount(accountId: string): Promise<AccountDto> {
  return apiRequest<AccountDto>('GET', `/accounts/${encodeURIComponent(accountId)}`)
}
