import { readFile } from 'node:fs/promises'
import argon2 from 'argon2'
import Fastify, { type FastifyInstance } from 'fastify'
import type { Pool } from 'pg'
import { afterEach, describe, expect, it } from 'vitest'
import { loadConfig } from '../config.js'
import auth from '../plugins/auth.js'
import routes from './routes.js'
import { hashToken } from './token.js'

const environment = {
  NODE_ENV: 'test',
  DATABASE_URL: 'postgresql://calendar:test@localhost:5432/calendar',
  JWT_ACCESS_SECRET: 'test-access-secret-at-least-32-characters',
  TOKEN_HASH_SECRET: 'test-token-secret-at-least-32-characters'
}
const apps: FastifyInstance[] = []

afterEach(async () => {
  await Promise.all(apps.splice(0).map(app => app.close()))
})

async function createApp() {
  const config = loadConfig(environment)
  const userId = '6eca79b0-bd83-4e6b-bf5b-1cc5ba6dd881'
  const refreshToken = 'existing-refresh-token-with-enough-length'
  const state = {
    user: { id: userId, email: 'user@example.com', passwordHash: await argon2.hash('old-password') } as
      { id: string, email: string, passwordHash: string } | null,
    refresh: { hash: hashToken(refreshToken, config), revoked: false },
    dependents: { refreshSessions: 1, todos: 1, calendarItems: 1, decorations: 1, syncChanges: 1 }
  }
  const client = {
    async query(sql: string, values: unknown[] = []) {
      if (['BEGIN', 'COMMIT', 'ROLLBACK'].includes(sql)) return { rows: [] }
      if (sql.startsWith('SELECT password_hash FROM users')) {
        return { rows: state.user ? [{ password_hash: state.user.passwordHash }] : [] }
      }
      if (sql.startsWith('UPDATE users SET password_hash')) {
        if (state.user) state.user.passwordHash = values[0] as string
        return { rows: [] }
      }
      if (sql.startsWith('UPDATE refresh_sessions SET revoked_at=now() WHERE user_id')) {
        state.refresh.revoked = true
        return { rows: [] }
      }
      if (sql.startsWith('SELECT s.id,u.id AS user_id')) {
        const valid = state.user && !state.refresh.revoked && values[0] === state.refresh.hash
        return { rows: valid ? [{ id: 'session-id', user_id: userId, email: state.user!.email }] : [] }
      }
      if (sql.startsWith('DELETE FROM users')) {
        state.user = null
        state.refresh.revoked = true
        for (const key of Object.keys(state.dependents) as Array<keyof typeof state.dependents>) {
          state.dependents[key] = 0
        }
        return { rows: [] }
      }
      throw new Error(`Unexpected query: ${sql}`)
    },
    release() {}
  }
  const db = {
    connect: async () => client,
    query: client.query
  } as unknown as Pool
  const app = Fastify()
  apps.push(app)
  app.decorate('config', config)
  app.decorate('db', db)
  await app.register(auth)
  await app.register(routes)
  await app.ready()
  const authorization = `Bearer ${app.jwt.sign({ sub: userId, email: 'user@example.com' })}`
  return { app, state, refreshToken, authorization }
}

describe('account management routes', () => {
  it('changes the password and revokes every refresh session', async () => {
    const { app, state, refreshToken, authorization } = await createApp()
    const response = await app.inject({
      method: 'POST', url: '/v1/auth/password', headers: { authorization },
      payload: { currentPassword: 'old-password', newPassword: 'new-password' }
    })

    expect(response.statusCode).toBe(200)
    expect(state.user && await argon2.verify(state.user.passwordHash, 'new-password')).toBe(true)
    expect(state.refresh.revoked).toBe(true)

    const refresh = await app.inject({
      method: 'POST', url: '/v1/auth/refresh', payload: { refreshToken }
    })
    expect(refresh.statusCode).toBe(401)
  })

  it('rejects an incorrect current password without changing sessions', async () => {
    const { app, state, authorization } = await createApp()
    const response = await app.inject({
      method: 'POST', url: '/v1/auth/password', headers: { authorization },
      payload: { currentPassword: 'wrong-password', newPassword: 'new-password' }
    })

    expect(response.statusCode).toBe(401)
    expect(state.user && await argon2.verify(state.user.passwordHash, 'old-password')).toBe(true)
    expect(state.refresh.revoked).toBe(false)
  })

  it('rejects account deletion when the password does not match', async () => {
    const { app, state, authorization } = await createApp()
    const response = await app.inject({
      method: 'DELETE', url: '/v1/auth/me', headers: { authorization },
      payload: { password: 'wrong-password' }
    })

    expect(response.statusCode).toBe(401)
    expect(state.user).not.toBeNull()
  })

  it('deletes the user and relies on verified cascade constraints', async () => {
    const { app, state, authorization } = await createApp()
    const response = await app.inject({
      method: 'DELETE', url: '/v1/auth/me', headers: { authorization },
      payload: { password: 'old-password' }
    })

    expect(response.statusCode).toBe(204)
    expect(state.user).toBeNull()
    expect(Object.values(state.dependents)).toEqual([0, 0, 0, 0, 0])

    const migrations = await Promise.all(['001_initial.sql', '002_calendar_v2.sql'].map(name =>
      readFile(new URL(`../database/${name}`, import.meta.url), 'utf8')))
    expect(migrations.join('\n').match(/REFERENCES users\(id\) ON DELETE CASCADE/g)).toHaveLength(5)
  })
})
