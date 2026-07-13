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
  return {
    databaseUrl: required('DATABASE_URL'), databaseSsl: env.DATABASE_SSL === 'true',
    jwtAccessSecret: required('JWT_ACCESS_SECRET'), tokenHashSecret: required('TOKEN_HASH_SECRET'),
    accessTokenTtlMinutes: number('ACCESS_TOKEN_TTL_MINUTES', 15),
    refreshTokenTtlDays: number('REFRESH_TOKEN_TTL_DAYS', 30),
    host: env.HOST ?? '127.0.0.1', port: number('PORT', 3000), logLevel: env.LOG_LEVEL ?? 'info'
  }
}
