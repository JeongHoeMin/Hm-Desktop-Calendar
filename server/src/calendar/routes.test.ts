import Fastify, { type FastifyInstance } from 'fastify'
import type { Pool } from 'pg'
import { afterEach, describe, expect, it, vi } from 'vitest'
import todoRoutes from '../todos/routes.js'
import calendarRoutes from './routes.js'

const userId = '00000000-0000-4000-8000-000000000001'
const entityId = '00000000-0000-4000-8000-000000000002'
const apps: FastifyInstance[] = []

afterEach(async () => {
  await Promise.all(apps.splice(0).map(app => app.close()))
})

function createApp(query: ReturnType<typeof vi.fn>) {
  const app = Fastify()
  apps.push(app)
  app.decorate('db', { query } as unknown as Pool)
  app.decorate('authenticate', async request => {
    Object.defineProperty(request, 'user', {
      value: { sub: userId, email: 'test@example.com' }
    })
  })
  return app
}

describe('calendar v2 routes', () => {
  it('keeps the v1 synchronization contract registered', async () => {
    const query = vi.fn(async () => ({
      rows: [{
        id: entityId, date: '2026-07-15', title: 'v1', time: null,
        notes: '', completed: false, deleted: false, revision: 1,
        cursor: 9, updatedAt: '2026-07-15T00:00:00.000Z'
      }]
    }))
    const app = createApp(query)
    await app.register(todoRoutes)

    const response = await app.inject({
      method: 'GET', url: '/v1/sync?after=0&limit=10'
    })
    expect(response.statusCode).toBe(200)
    expect(response.json()).toMatchObject({
      changes: [{ title: 'v1' }], nextCursor: 9, hasMore: false
    })
  })

  it('writes both entity types and exposes a unified cursor page', async () => {
    const changes = [
      { entityType: 'calendarItem', payload: { id: entityId, deleted: false }, cursor: 10 },
      { entityType: 'dateCellDecoration', payload: { id: entityId, deleted: false }, cursor: 11 }
    ]
    const query = vi.fn(async (sql: string) => {
      if (sql.includes('INSERT INTO calendar_items'))
        return { rows: [{ cursor: 10, payload: changes[0]!.payload }] }
      if (sql.includes('INSERT INTO date_cell_decorations'))
        return { rows: [{ cursor: 11, payload: changes[1]!.payload }] }
      if (sql.includes('FROM calendar_sync_changes')) return { rows: changes }
      if (sql.includes('UPDATE calendar_items')) return { rows: [{ cursor: 12 }] }
      if (sql.includes('UPDATE date_cell_decorations')) return { rows: [{ cursor: 13 }] }
      return { rows: [] }
    })
    const app = createApp(query)
    await app.register(calendarRoutes)

    const itemResponse = await app.inject({
      method: 'PUT', url: `/v2/calendar-items/${entityId}`,
      payload: {
        kind: 'schedule', title: '일정', notes: '',
        startDate: '2026-07-15', endDate: '2026-07-15',
        startTime: null, endTime: null, allDay: true, completed: false,
        color: '#3B82F6', recurrence: null,
        reminders: [{ minutesBefore: 30 }]
      }
    })
    expect(itemResponse.statusCode).toBe(200)
    expect(itemResponse.json()).toMatchObject({ id: entityId, cursor: 10 })

    const decorationResponse = await app.inject({
      method: 'PUT', url: `/v2/date-cell-decorations/${entityId}`,
      payload: {
        date: '2026-07-15', kind: 'colorDot', color: '#FF0000', label: '휴일'
      }
    })
    expect(decorationResponse.statusCode).toBe(200)
    expect(decorationResponse.json()).toMatchObject({ id: entityId, cursor: 11 })

    const syncResponse = await app.inject({
      method: 'GET', url: '/v2/sync?after=9&limit=10'
    })
    expect(syncResponse.statusCode).toBe(200)
    expect(syncResponse.json()).toEqual({
      changes, nextCursor: 11, hasMore: false
    })

    expect((await app.inject({ method: 'DELETE',
      url: `/v2/calendar-items/${entityId}` })).statusCode).toBe(204)
    expect((await app.inject({ method: 'DELETE',
      url: `/v2/date-cell-decorations/${entityId}` })).statusCode).toBe(204)
  })

  it('rejects an invalid date range before writing', async () => {
    const query = vi.fn(async () => ({ rows: [] }))
    const app = createApp(query)
    await app.register(calendarRoutes)
    const response = await app.inject({
      method: 'PUT', url: `/v2/calendar-items/${entityId}`,
      payload: {
        kind: 'schedule', title: '잘못된 일정', notes: '',
        startDate: '2026-07-16', endDate: '2026-07-15',
        startTime: null, endTime: null, allDay: true, completed: false,
        color: '#3B82F6', recurrence: null, reminders: []
      }
    })
    expect(response.statusCode).toBe(400)
    expect(query).not.toHaveBeenCalled()
  })
})
