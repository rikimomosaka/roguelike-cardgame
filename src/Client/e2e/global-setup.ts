import { rm } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

export default async function globalSetup() {
  const e2eDirectory = path.dirname(fileURLToPath(import.meta.url))
  const dataDirectory = path.resolve(e2eDirectory, '..', '.e2e-data')
  await rm(dataDirectory, { recursive: true, force: true })
}
