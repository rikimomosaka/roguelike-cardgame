import { apiRequest } from './client'

export async function winBattle(accountId: string, elapsedSeconds: number): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/battle/win', {
    accountId,
    body: { elapsedSeconds },
  })
}
