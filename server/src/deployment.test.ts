import { readFile } from 'node:fs/promises'
import { describe, expect, it } from 'vitest'
import { parse } from 'yaml'

async function readRootFile(name: string) {
  return readFile(new URL(`../${name}`, import.meta.url), 'utf8')
}

describe('deployment package', () => {
  it('builds a non-root multi-stage runtime with compiled output only', async () => {
    const dockerfile = await readRootFile('Dockerfile')
    expect(dockerfile).toContain('FROM node:22-slim AS build')
    expect(dockerfile).toContain('FROM node:22-slim AS runtime')
    expect(dockerfile).toContain('pnpm install --frozen-lockfile')
    expect(dockerfile).toContain('pnpm build && pnpm prune --prod')
    expect(dockerfile).toContain('/app/dist ./dist')
    expect(dockerfile).toContain('USER node')
    expect(dockerfile).not.toMatch(/^COPY \. /m)
    expect(dockerfile).not.toContain('/app/src')
  })

  it('waits for healthy PostgreSQL and exposes only loopback', async () => {
    const compose = parse(await readRootFile('docker-compose.yml')) as any
    expect(Object.keys(compose.services).sort()).toEqual(['db', 'server'])
    expect(compose.services.db.image).toBe('postgres:16')
    expect(compose.services.db.ports).toBeUndefined()
    expect(compose.services.db.healthcheck.test).toContain(
      'pg_isready -U $$POSTGRES_USER -d $$POSTGRES_DB')
    expect(compose.services.server.ports).toEqual(['127.0.0.1:3000:3000'])
    expect(compose.services.server.depends_on.db.condition).toBe('service_healthy')
    expect(compose.services.server.healthcheck.test.join(' ')).toContain(
      'http://127.0.0.1:3000/health')
    expect(compose.services.server.env_file).toEqual(['.env.docker'])
    expect(compose.services.db.env_file).toEqual(['.env.docker'])
  })

  it('keeps deployment secrets out of tracked configuration', async () => {
    const environment = await readRootFile('.env.docker.example')
    const compose = await readRootFile('docker-compose.yml')
    expect(environment).toContain('POSTGRES_PASSWORD=replace-with-')
    expect(environment).toContain('JWT_ACCESS_SECRET=replace-with-')
    expect(compose).not.toContain('POSTGRES_PASSWORD=')
    expect(compose).not.toContain('JWT_ACCESS_SECRET=')
  })
})
