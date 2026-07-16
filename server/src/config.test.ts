import { describe, expect, it } from 'vitest'
import { loadConfig } from './config.js'

const validEnvironment = {
  NODE_ENV: 'production',
  DATABASE_URL: 'postgresql://calendar:strong-password@db.internal:5432/calendar',
  JWT_ACCESS_SECRET: 'a-production-access-secret',
  TOKEN_HASH_SECRET: 'a-production-token-hash-secret'
}

describe('loadConfig', () => {
  it.each([
    ['DATABASE_URL의 change-me 비밀번호', { DATABASE_URL: 'postgresql://calendar:change-me@localhost:5432/calendar' }],
    ['DATABASE_URL의 기본 postgres 자격 증명', { DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/calendar' }],
    ['JWT_ACCESS_SECRET placeholder', { JWT_ACCESS_SECRET: 'replace-with-at-least-32-random-characters' }],
    ['TOKEN_HASH_SECRET placeholder', { TOKEN_HASH_SECRET: 'replace-with-another-32-random-characters' }]
  ])('production에서 %s를 거부한다', (_, override) => {
    expect(() => loadConfig({ ...validEnvironment, ...override })).toThrow(/운영 환경에서는 placeholder/)
  })

  it('development에서는 예제 placeholder를 허용한다', () => {
    const config = loadConfig({
      ...validEnvironment,
      NODE_ENV: 'development',
      DATABASE_URL: 'postgresql://calendar:change-me@localhost:5432/calendar',
      JWT_ACCESS_SECRET: 'replace-with-at-least-32-random-characters',
      TOKEN_HASH_SECRET: 'replace-with-another-32-random-characters'
    })

    expect(config.databaseUrl).toContain('change-me')
  })

  it('보안 한도 기본값과 CORS 비활성을 제공한다', () => {
    const config = loadConfig(validEnvironment)
    expect(config).toMatchObject({
      bodyLimitBytes: 262144,
      rateLimitMax: 300,
      authRateLimitMax: 10,
      rateLimitWindowMs: 60000,
      corsAllowedOrigins: []
    })
  })

  it('보안 한도와 CORS origin 목록을 검증한다', () => {
    expect(() => loadConfig({
      ...validEnvironment, BODY_LIMIT_BYTES: '1.5'
    })).toThrow('Invalid BODY_LIMIT_BYTES')
    expect(() => loadConfig({
      ...validEnvironment, RATE_LIMIT_MAX: '0'
    })).toThrow('Invalid RATE_LIMIT_MAX')
    expect(() => loadConfig({
      ...validEnvironment, CORS_ALLOWED_ORIGINS: '*'
    })).toThrow('Invalid CORS_ALLOWED_ORIGINS')
    expect(() => loadConfig({
      ...validEnvironment, CORS_ALLOWED_ORIGINS: 'https://calendar.example.com/path'
    })).toThrow('Invalid CORS_ALLOWED_ORIGINS')

    const config = loadConfig({
      ...validEnvironment,
      CORS_ALLOWED_ORIGINS: 'https://CALENDAR.example.com/, http://localhost:5173,https://calendar.example.com'
    })
    expect(config.corsAllowedOrigins).toEqual([
      'https://calendar.example.com', 'http://localhost:5173'
    ])
  })
})
