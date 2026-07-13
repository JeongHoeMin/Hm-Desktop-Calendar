import fp from 'fastify-plugin'
import pg from 'pg'
import { migrate } from '../database/migrate.js'

export default fp(async function database(app) {
  const pool = new pg.Pool({ connectionString: app.config.databaseUrl,
    ssl: app.config.databaseSsl ? { rejectUnauthorized: false } : undefined })
  await migrate(pool); await pool.query('SELECT 1')
  app.decorate('db', pool)
  app.addHook('onClose', async () => pool.end())
})
