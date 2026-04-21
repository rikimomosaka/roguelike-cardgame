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

export type TileKind =
  | 'Start'
  | 'Enemy'
  | 'Elite'
  | 'Rest'
  | 'Merchant'
  | 'Treasure'
  | 'Unknown'
  | 'Boss'

export type RunProgress = 'InProgress' | 'Cleared' | 'GameOver' | 'Abandoned'

export type RunStateDto = {
  schemaVersion: number
  currentAct: number
  currentNodeId: number
  visitedNodeIds: number[]
  unknownResolutions: Record<number, TileKind>
  currentHp: number
  maxHp: number
  gold: number
  deck: string[]
  relics: string[]
  potions: string[]
  playSeconds: number
  rngSeed: number
  savedAtUtc: string
  progress: RunProgress
}

export type MapNodeDto = {
  id: number
  row: number
  column: number
  kind: TileKind
  outgoingNodeIds: number[]
}

export type MapDto = {
  startNodeId: number
  bossNodeId: number
  nodes: MapNodeDto[]
}

export type RunSnapshotDto = {
  run: RunStateDto
  map: MapDto
}
