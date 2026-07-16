import { Writable } from 'node:stream'
import Fastify, { type FastifyInstance } from 'fastify'
import type { Pool } from 'pg'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createServerOptions } from '../app.js'
import authRoutes from '../auth/routes.js'
import auth from './auth.js'
import { loadConfig, type AppConfig } from '../config.js'
import security from './security.js'

const baseEnvironment = {
  NODE_ENV: 'test',
  DATABASE_URL: 'postgresql://calendar:test@localhost:5432/calendar',
  JWT_ACCESS_SECRET: 'test-access-secret-at-least-32-characters',
  TOKEN_HASH_SECRET: 'test-token-secret-at-least-32-characters'
}
const apps: FastifyInstance[] = []

afterEach(async () => {
  await Promise.all(apps.splice(0).map(app => app.close()))
})

async function createApp(override: Record<string, string> = {}) {
  const config = loadConfig({ ...baseEnvironment, ...override })
  const app = Fastify({ bodyLimit: config.bodyLimitBytes })
  apps.push(app)
  app.decorate('config', config)
  await app.register(security)
  app.post('/echo', async request => request.body)
  await app.ready()
  return app
}

describe('server security', () => {
  it('adds security headers and applies the global IP limit', async () => {
    const app = await createApp({ RATE_LIMIT_MAX: '2' })
    const responses = []
    for (let value = 1; value <= 3; value++) {
      responses.push(await app.inject({ method: 'POST', url: '/echo', payload: { value } }))
    }
    expect(responses.map(response => response.statusCode)).toEqual([200, 200, 429])
    expect(responses[0]!.headers['x-content-type-options']).toBe('nosniff')
    expect(responses[0]!.headers['x-frame-options']).toBe('SAMEORIGIN')
    expect(responses[2]!.headers['retry-after']).toBeDefined()
  })

  it('limits login and registration routes more strictly', async () => {
    const config = loadConfig({
      ...baseEnvironment, RATE_LIMIT_MAX: '100', AUTH_RATE_LIMIT_MAX: '2'
    })
    const app = Fastify({ bodyLimit: config.bodyLimitBytes })
    apps.push(app)
    app.decorate('config', config)
    app.decorate('db', { query: vi.fn() } as unknown as Pool)
    await app.register(security)
    await app.register(auth)
    await app.register(authRoutes)
    await app.ready()

    for (const url of ['/v1/auth/login', '/v1/auth/register']) {
      const responses = []
      for (let index = 0; index < 3; index++) {
        responses.push(await app.inject({ method: 'POST', url, payload: {} }))
      }
      expect(responses.map(response => response.statusCode)).toEqual([400, 400, 429])
    }

    for (const request of [
      { method: 'POST' as const, url: '/v1/auth/password' },
      { method: 'DELETE' as const, url: '/v1/auth/me' }
    ]) {
      const responses = []
      for (let index = 0; index < 3; index++) {
        responses.push(await app.inject({ ...request, payload: {} }))
      }
      expect(responses.map(response => response.statusCode)).toEqual([400, 400, 429])
    }
  })

  it('keeps CORS disabled unless an exact origin is configured', async () => {
    const disabled = await createApp()
    const withoutCors = await disabled.inject({
      method: 'POST', url: '/echo', headers: { origin: 'https://calendar.example.com' }, payload: {}
    })
    expect(withoutCors.headers['access-control-allow-origin']).toBeUndefined()

    const enabled = await createApp({
      CORS_ALLOWED_ORIGINS: 'https://calendar.example.com'
    })
    const allowed = await enabled.inject({
      method: 'POST', url: '/echo', headers: { origin: 'https://calendar.example.com' }, payload: {}
    })
    const denied = await enabled.inject({
      method: 'POST', url: '/echo', headers: { origin: 'https://other.example.com' }, payload: {}
    })
    expect(allowed.headers['access-control-allow-origin']).toBe('https://calendar.example.com')
    expect(denied.headers['access-control-allow-origin']).toBeUndefined()
  })

  it('rejects oversized bodies and redacts authorization values', async () => {
    const app = await createApp({ BODY_LIMIT_BYTES: '64' })
    const oversized = await app.inject({
      method: 'POST', url: '/echo', payload: { value: 'x'.repeat(100) }
    })
    expect(oversized.statusCode).toBe(413)

    let output = ''
    const stream = new Writable({
      write(chunk, _encoding, callback) { output += chunk.toString(); callback() }
    })
    const config: AppConfig = loadConfig(baseEnvironment)
    const loggerApp = Fastify(createServerOptions(config, stream))
    apps.push(loggerApp)
    loggerApp.log.info({ headers: { authorization: 'Bearer top-secret' } }, 'redaction check')
    expect(output).toContain('[Redacted]')
    expect(output).not.toContain('top-secret')
  })
})
