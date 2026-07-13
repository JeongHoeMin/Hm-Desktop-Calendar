import type { Pool } from 'pg'
import type { AppConfig } from './config.js'
declare module 'fastify' {
  interface FastifyInstance { db: Pool; config: AppConfig; authenticate: (request: FastifyRequest) => Promise<void> }
}
declare module '@fastify/jwt' {
  interface FastifyJWT { payload: { sub: string; email: string }; user: { sub: string; email: string } }
}
