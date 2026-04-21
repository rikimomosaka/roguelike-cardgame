export type AccountDto = {
  id: string
  createdAt: string
}

export type AudioSettingsDto = {
  schemaVersion: number
  master: number
  bgm: number
  se: number
  ambient: number
}

export type RunStateDto = {
  schemaVersion: number
  currentAct: number
  currentTileIndex: number
  currentHp: number
  maxHp: number
  gold: number
  deck: string[]
  relics: string[]
  potions: string[]
  playSeconds: number
  rngSeed: number
  savedAtUtc: string
  progress: 'InProgress' | 'Completed' | 'Abandoned'
}
