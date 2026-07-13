import fp from 'fastify-plugin'
import jwt from '@fastify/jwt'

export default fp(async function auth(app) {
  await app.register(jwt, { secret: app.config.jwtAccessSecret,
    sign: { expiresIn: `${app.config.accessTokenTtlMinutes}m` } })
  app.decorate('authenticate', async request => { await request.jwtVerify() })
})
