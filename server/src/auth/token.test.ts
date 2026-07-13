import { describe, expect, it } from 'vitest'
import { hashToken, normalizeEmail } from './token.js'
import type { AppConfig } from '../config.js'

const config = { tokenHashSecret: 'test-secret' } as AppConfig
describe('auth token utilities', () => {
  it('normalizes email', () => expect(normalizeEmail('  User@Example.COM ')).toBe('user@example.com'))
  it('hashes tokens deterministically without storing plaintext', () => {
    expect(hashToken('abc', config)).toBe(hashToken('abc', config))
    expect(hashToken('abc', config)).not.toBe('abc')
    expect(hashToken('abc', config)).toHaveLength(64)
  })
})
