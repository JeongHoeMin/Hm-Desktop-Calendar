import { randomUUID } from 'node:crypto'
import argon2 from 'argon2'
import { Type } from '@sinclair/typebox'
import type { FastifyPluginAsync } from 'fastify'
import { hashToken, issueRefreshToken, normalizeEmail } from './token.js'

const credentials = Type.Object({ email: Type.String({ format: 'email', maxLength: 320 }), password: Type.String({ minLength: 8, maxLength: 128 }), deviceName: Type.Optional(Type.String({ maxLength: 100 })) })
const refreshBody = Type.Object({ refreshToken: Type.String({ minLength: 20 }), deviceName: Type.Optional(Type.String({ maxLength: 100 })) })

const routes: FastifyPluginAsync = async app => {
  async function createSession(user: { id: string, email: string }, deviceName = '') {
    const refresh = issueRefreshToken(app.config)
    const expires = new Date(Date.now() + app.config.refreshTokenTtlDays * 86400000)
    await app.db.query('INSERT INTO refresh_sessions(id,user_id,token_hash,device_name,expires_at) VALUES($1,$2,$3,$4,$5)',
      [refresh.id, user.id, refresh.hash, deviceName, expires])
    return { accessToken: app.jwt.sign({ sub: user.id, email: user.email }), refreshToken: refresh.token, user }
  }

  app.post('/v1/auth/register', { schema: { body: credentials } }, async (request, reply) => {
    const body = request.body as typeof credentials.static; const email = normalizeEmail(body.email)
    const hash = await argon2.hash(body.password, { type: argon2.argon2id })
    const id = randomUUID()
    try { await app.db.query('INSERT INTO users(id,email,password_hash) VALUES($1,$2,$3)', [id, email, hash]) }
    catch (error: any) { if (error.code === '23505') return reply.code(409).send({ message: '이미 등록된 이메일입니다.' }); throw error }
    return reply.code(201).send(await createSession({ id, email }, body.deviceName))
  })

  app.post('/v1/auth/login', { schema: { body: credentials } }, async (request, reply) => {
    const body = request.body as typeof credentials.static; const email = normalizeEmail(body.email)
    const result = await app.db.query('SELECT id,email,password_hash FROM users WHERE email=$1', [email]); const user = result.rows[0]
    if (!user || !await argon2.verify(user.password_hash, body.password)) return reply.code(401).send({ message: '이메일 또는 비밀번호가 올바르지 않습니다.' })
    return createSession({ id: user.id, email: user.email }, body.deviceName)
  })

  app.post('/v1/auth/refresh', { schema: { body: refreshBody } }, async (request, reply) => {
    const body = request.body as typeof refreshBody.static; const hash = hashToken(body.refreshToken, app.config)
    const client = await app.db.connect()
    try {
      await client.query('BEGIN')
      const found = await client.query(`SELECT s.id,u.id AS user_id,u.email FROM refresh_sessions s JOIN users u ON u.id=s.user_id
        WHERE s.token_hash=$1 AND s.revoked_at IS NULL AND s.expires_at>now() FOR UPDATE`, [hash])
      const session = found.rows[0]
      if (!session) { await client.query('ROLLBACK'); return reply.code(401).send({ message: '세션이 만료되었습니다.' }) }
      await client.query('UPDATE refresh_sessions SET revoked_at=now() WHERE id=$1', [session.id])
      const next = issueRefreshToken(app.config); const expires = new Date(Date.now() + app.config.refreshTokenTtlDays * 86400000)
      await client.query('INSERT INTO refresh_sessions(id,user_id,token_hash,device_name,expires_at) VALUES($1,$2,$3,$4,$5)', [next.id, session.user_id, next.hash, body.deviceName ?? '', expires])
      await client.query('COMMIT')
      return { accessToken: app.jwt.sign({ sub: session.user_id, email: session.email }), refreshToken: next.token,
        user: { id: session.user_id, email: session.email } }
    } catch (error) { await client.query('ROLLBACK'); throw error } finally { client.release() }
  })

  app.post('/v1/auth/logout', { schema: { body: refreshBody } }, async request => {
    const body = request.body as typeof refreshBody.static
    await app.db.query('UPDATE refresh_sessions SET revoked_at=now() WHERE token_hash=$1 AND revoked_at IS NULL', [hashToken(body.refreshToken, app.config)])
    return { ok: true }
  })
  app.get('/v1/auth/me', { preHandler: [app.authenticate] }, async request => ({ id: request.user.sub, email: request.user.email }))
}
export default routes
