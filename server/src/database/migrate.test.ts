import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import type pg from 'pg'
import { listMigrationNames, migrate } from './migrate.js'

describe('database migrations', () => {
  it('discovers and applies every SQL migration in order', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'hm-migrations-'))
    try {
      await writeFile(join(directory, '002_second.sql'), 'SELECT 2;')
      await writeFile(join(directory, '001_first.sql'), 'SELECT 1;')
      await writeFile(join(directory, 'README.md'), 'ignored')
      expect(await listMigrationNames(directory)).toEqual([
        '001_first.sql', '002_second.sql'
      ])

      const applied: string[] = []
      const client = {
        async query(sql: string, values?: unknown[]) {
          if (sql.startsWith('SELECT 1 FROM schema_migrations'))
            return { rowCount: 0, rows: [] }
          if (sql === 'SELECT 1;' || sql === 'SELECT 2;') applied.push(sql)
          if (sql.startsWith('INSERT INTO schema_migrations'))
            applied.push(String(values?.[0]))
          return { rowCount: 1, rows: [] }
        },
        release() { }
      }
      const pool = {
        async connect() { return client }
      } as unknown as Pick<pg.Pool, 'connect'>

      await migrate(pool, directory)
      expect(applied).toEqual([
        'SELECT 1;', '001_first.sql', 'SELECT 2;', '002_second.sql'
      ])
    } finally { await rm(directory, { recursive: true, force: true }) }
  })

  it('rolls back the failing migration and stops', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'hm-migrations-'))
    try {
      await writeFile(join(directory, '001_failure.sql'), 'BROKEN')
      const calls: string[] = []
      const client = {
        async query(sql: string) {
          calls.push(sql)
          if (sql.startsWith('SELECT 1 FROM schema_migrations'))
            return { rowCount: 0, rows: [] }
          if (sql === 'BROKEN') throw new Error('migration failed')
          return { rowCount: 1, rows: [] }
        },
        release() { calls.push('RELEASE') }
      }
      const pool = {
        async connect() { return client }
      } as unknown as Pick<pg.Pool, 'connect'>

      await expect(migrate(pool, directory)).rejects.toThrow('migration failed')
      expect(calls).toContain('ROLLBACK')
      expect(calls.at(-1)).toBe('RELEASE')
    } finally { await rm(directory, { recursive: true, force: true }) }
  })
})
