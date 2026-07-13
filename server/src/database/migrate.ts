import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import pg from 'pg'
import { loadConfig } from '../config.js'

export async function migrate(pool: pg.Pool) {
  const name = '001_initial.sql'
  const sql = await readFile(fileURLToPath(new URL(`./${name}`, import.meta.url)), 'utf8')
  const client = await pool.connect()
  try {
    await client.query('BEGIN')
    await client.query('CREATE TABLE IF NOT EXISTS schema_migrations (name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())')
    const found = await client.query('SELECT 1 FROM schema_migrations WHERE name=$1', [name])
    if (found.rowCount === 0) { await client.query(sql); await client.query('INSERT INTO schema_migrations(name) VALUES($1)', [name]) }
    await client.query('COMMIT')
  } catch (error) { await client.query('ROLLBACK'); throw error } finally { client.release() }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const config = loadConfig(); const pool = new pg.Pool({ connectionString: config.databaseUrl, ssl: config.databaseSsl ? { rejectUnauthorized: false } : undefined })
  await migrate(pool); await pool.end()
}
