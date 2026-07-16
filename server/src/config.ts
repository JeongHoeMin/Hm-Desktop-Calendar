import 'dotenv/config'

export type AppConfig = ReturnType<typeof loadConfig>
export function loadConfig(env = process.env) {
  const required = (name: string) => {
    const value = env[name]
    if (!value) throw new Error(`Missing environment variable: ${name}. Copy .env.example to .env and fill in the value.`)
    return value
  }
  const number = (name: string, fallback: number) => {
    const value = Number(env[name] ?? fallback)
    if (!Number.isFinite(value) || value <= 0) throw new Error(`Invalid ${name}`)
    return value
  }
  const integer = (name: string, fallback: number) => {
    const value = number(name, fallback)
    if (!Number.isSafeInteger(value)) throw new Error(`Invalid ${name}`)
    return value
  }
  const origins = (env.CORS_ALLOWED_ORIGINS ?? '').split(',')
    .map(value => value.trim()).filter(Boolean).map(value => {
      let url: URL
      try { url = new URL(value) } catch { throw new Error('Invalid CORS_ALLOWED_ORIGINS') }
      if (!['http:', 'https:'].includes(url.protocol) || url.username ||
        url.password || url.pathname !== '/' || url.search || url.hash) {
        throw new Error('Invalid CORS_ALLOWED_ORIGINS')
      }
      return url.origin
    })
  const databaseUrl = required('DATABASE_URL')
  const jwtAccessSecret = required('JWT_ACCESS_SECRET')
  const tokenHashSecret = required('TOKEN_HASH_SECRET')

  if (env.NODE_ENV === 'production') {
    if (isPlaceholderDatabaseUrl(databaseUrl)) {
      throw new Error('운영 환경에서는 placeholder DATABASE_URL을 사용할 수 없습니다.')
    }
    if (jwtAccessSecret.startsWith('replace-with')) {
      throw new Error('운영 환경에서는 placeholder JWT_ACCESS_SECRET을 사용할 수 없습니다.')
    }
    if (tokenHashSecret.startsWith('replace-with')) {
      throw new Error('운영 환경에서는 placeholder TOKEN_HASH_SECRET을 사용할 수 없습니다.')
    }
  }

  return {
    databaseUrl, databaseSsl: env.DATABASE_SSL === 'true',
    jwtAccessSecret, tokenHashSecret,
    accessTokenTtlMinutes: number('ACCESS_TOKEN_TTL_MINUTES', 15),
    refreshTokenTtlDays: number('REFRESH_TOKEN_TTL_DAYS', 30),
    bodyLimitBytes: integer('BODY_LIMIT_BYTES', 262144),
    rateLimitMax: integer('RATE_LIMIT_MAX', 300),
    authRateLimitMax: integer('AUTH_RATE_LIMIT_MAX', 10),
    rateLimitWindowMs: integer('RATE_LIMIT_WINDOW_MS', 60000),
    corsAllowedOrigins: [...new Set(origins)],
    host: env.HOST ?? '127.0.0.1', port: integer('PORT', 3000), logLevel: env.LOG_LEVEL ?? 'info'
  }
}

function isPlaceholderDatabaseUrl(value: string) {
  if (value.toLowerCase().includes('change-me')) return true

  try {
    const url = new URL(value)
    return decodeURIComponent(url.username).toLowerCase() === 'postgres'
      && decodeURIComponent(url.password).toLowerCase() === 'postgres'
  } catch {
    return false
  }
}
