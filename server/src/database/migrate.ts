import { readdir, readFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import pg from 'pg'
import { loadConfig } from '../config.js'

type MigrationPool = Pick<pg.Pool, 'connect'>

export async function listMigrationNames(directory =
  dirname(fileURLToPath(import.meta.url))) {
  return (await readdir(directory))
    .filter(name => /^\d{3}_[a-z0-9_]+\.sql$/.test(name))
    .sort()
}

export async function migrate(pool: MigrationPool, directory =
  dirname(fileURLToPath(import.meta.url))) {
  const names = await listMigrationNames(directory)
  const client = await pool.connect()
  try {
    for (const name of names) {
      await client.query('BEGIN')
      try {
        await client.query("SELECT pg_advisory_xact_lock(hashtext('hm_desktop_calendar_migrations'))")
        await client.query('CREATE TABLE IF NOT EXISTS schema_migrations (name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())')
        const found = await client.query(
          'SELECT 1 FROM schema_migrations WHERE name=$1', [name])
        if (found.rowCount === 0) {
          await client.query(await readFile(join(directory, name), 'utf8'))
          await client.query(
            'INSERT INTO schema_migrations(name) VALUES($1)', [name])
        }
        await client.query('COMMIT')
      } catch (error) {
        await client.query('ROLLBACK')
        throw error
      }
    }
  } finally { client.release() }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const config = loadConfig(); const pool = new pg.Pool({ connectionString: config.databaseUrl, ssl: config.databaseSsl ? { rejectUnauthorized: false } : undefined })
  await migrate(pool); await pool.end()
}
