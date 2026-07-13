import Fastify, { type FastifyError } from 'fastify'
import type { AppConfig } from './config.js'
import database from './plugins/database.js'
import auth from './plugins/auth.js'
import realtime from './realtime/hub.js'
import authRoutes from './auth/routes.js'
import todoRoutes from './todos/routes.js'

export async function buildApp(config: AppConfig) {
  const app = Fastify({ logger: { level: config.logLevel } })
  app.decorate('config', config)
  await app.register(database)
  await app.register(auth)
  await app.register(realtime)
  await app.register(authRoutes)
  await app.register(todoRoutes)
  app.get('/health', async () => { await app.db.query('SELECT 1'); return { status: 'ok' } })
  app.setErrorHandler((error: FastifyError, _request, reply) => {
    if (error.validation) return reply.code(400).send({ message: '요청 형식이 올바르지 않습니다.', details: error.validation })
    app.log.error(error); return reply.code(error.statusCode ?? 500).send({ message: error.statusCode ? error.message : '서버 오류가 발생했습니다.' })
  })
  return app
}
