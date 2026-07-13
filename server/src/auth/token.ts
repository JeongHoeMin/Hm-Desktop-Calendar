import { createHash, createHmac, randomBytes, randomUUID } from 'node:crypto'
import type { AppConfig } from '../config.js'

export function issueRefreshToken(config: AppConfig) {
  const secret = randomBytes(32).toString('base64url')
  return { id: randomUUID(), token: secret, hash: hashToken(secret, config) }
}
export function hashToken(token: string, config: AppConfig) {
  return createHmac('sha256', config.tokenHashSecret).update(token).digest('hex')
}
export function normalizeEmail(email: string) { return email.trim().toLowerCase() }
export function tokenFingerprint(token: string) { return createHash('sha256').update(token).digest('hex').slice(0, 12) }
