import cors from '@fastify/cors'
import helmet from '@fastify/helmet'
import rateLimit from '@fastify/rate-limit'
import fp from 'fastify-plugin'

export default fp(async function security(app) {
  await app.register(helmet)
  await app.register(rateLimit, {
    global: true,
    max: app.config.rateLimitMax,
    timeWindow: app.config.rateLimitWindowMs
  })
  if (app.config.corsAllowedOrigins.length > 0) {
    await app.register(cors, {
      origin: app.config.corsAllowedOrigins
    })
  }
})
