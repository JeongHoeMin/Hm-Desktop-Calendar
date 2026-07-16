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
    host: env.HOST ?? '127.0.0.1', port: number('PORT', 3000), logLevel: env.LOG_LEVEL ?? 'info'
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
