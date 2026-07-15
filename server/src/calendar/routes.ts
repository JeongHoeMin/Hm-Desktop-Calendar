import { Type } from '@sinclair/typebox'
import type { FastifyPluginAsync } from 'fastify'

const idParams = Type.Object({ id: Type.String({ format: 'uuid' }) })
const nullableTime = Type.Union([
  Type.String({ pattern: '^([01]\\d|2[0-3]):[0-5]\\d$' }), Type.Null()
])
const recurrence = Type.Union([Type.Object({
  frequency: Type.Union([
    Type.Literal('daily'), Type.Literal('weekly'),
    Type.Literal('monthly'), Type.Literal('yearly')
  ]),
  interval: Type.Integer({ minimum: 1 }),
  daysOfWeek: Type.Array(Type.Integer({ minimum: 0, maximum: 6 }),
    { maxItems: 7, uniqueItems: true }),
  until: Type.Union([Type.String({ format: 'date' }), Type.Null()]),
  count: Type.Union([Type.Integer({ minimum: 1 }), Type.Null()])
}), Type.Null()])
const calendarItemBody = Type.Object({
  kind: Type.Union([Type.Literal('schedule'), Type.Literal('anniversary')]),
  title: Type.String({ minLength: 1, maxLength: 500 }),
  notes: Type.String({ maxLength: 10000 }),
  startDate: Type.String({ format: 'date' }),
  endDate: Type.String({ format: 'date' }),
  startTime: nullableTime,
  endTime: nullableTime,
  allDay: Type.Boolean(),
  completed: Type.Boolean(),
  color: Type.String({ pattern: '^#[0-9A-Fa-f]{6}$' }),
  recurrence,
  reminders: Type.Array(Type.Object({
    minutesBefore: Type.Integer({ minimum: 0, maximum: 525600 }),
    timeOfDay: Type.Optional(nullableTime)
  }), { maxItems: 20, uniqueItems: true })
})
const decorationBody = Type.Object({
  date: Type.String({ format: 'date' }),
  kind: Type.Union([Type.Literal('highlight'), Type.Literal('colorDot'),
    Type.Literal('label')]),
  color: Type.String({ pattern: '^#[0-9A-Fa-f]{6}$' }),
  label: Type.String({ maxLength: 100 })
})
const syncQuery = Type.Object({
  after: Type.Optional(Type.Integer({ minimum: 0, default: 0 })),
  limit: Type.Optional(Type.Integer({ minimum: 1, maximum: 500, default: 500 }))
})

const calendarItemPayload = `jsonb_build_object(
  'id', id, 'kind', kind, 'title', title, 'notes', notes,
  'startDate', start_date::text, 'endDate', end_date::text,
  'startTime', CASE WHEN start_time IS NULL THEN NULL ELSE to_char(start_time, 'HH24:MI') END,
  'endTime', CASE WHEN end_time IS NULL THEN NULL ELSE to_char(end_time, 'HH24:MI') END,
  'allDay', all_day, 'completed', completed, 'color', color,
  'recurrence', recurrence, 'reminders', reminders, 'deleted', deleted,
  'revision', revision, 'updatedAt', updated_at)`
const decorationPayload = `jsonb_build_object(
  'id', id, 'date', date::text, 'kind', kind, 'color', color,
  'label', label, 'deleted', deleted, 'revision', revision,
  'updatedAt', updated_at)`

const routes: FastifyPluginAsync = async app => {
  app.get('/v2/sync', {
    preHandler: [app.authenticate], schema: { querystring: syncQuery }
  }, async request => {
    const query = request.query as typeof syncQuery.static
    const after = query.after ?? 0
    const limit = query.limit ?? 500
    const result = await app.db.query(
      `SELECT entity_type AS "entityType", payload, cursor
       FROM calendar_sync_changes
       WHERE user_id=$1 AND cursor>$2 ORDER BY cursor LIMIT $3`,
      [request.user.sub, after, limit + 1])
    const rows = result.rows.slice(0, limit).map(row => ({
      ...row, cursor: Number(row.cursor)
    }))
    return {
      changes: rows,
      nextCursor: Number(rows.at(-1)?.cursor ?? after),
      hasMore: result.rows.length > limit
    }
  })

  app.put('/v2/calendar-items/:id', {
    preHandler: [app.authenticate],
    schema: { params: idParams, body: calendarItemBody }
  }, async (request, reply) => {
    const { id } = request.params as typeof idParams.static
    const body = request.body as typeof calendarItemBody.static
    if (body.title.trim().length === 0)
      return reply.code(400).send({ message: '일정 제목을 입력해야 합니다.' })
    if (body.endDate < body.startDate)
      return reply.code(400).send({
        message: '종료 날짜는 시작 날짜보다 빠를 수 없습니다.'
      })
    if (body.recurrence && body.endDate !== body.startDate)
      return reply.code(400).send({
        message: '기간 일정과 반복 일정은 함께 사용할 수 없습니다.'
      })
    if (body.recurrence?.count != null)
      return reply.code(400).send({ message: '반복 횟수는 지원하지 않습니다.' })
    if (body.recurrence?.until && body.recurrence.until < body.startDate)
      return reply.code(400).send({
        message: '반복 종료일은 시작 날짜보다 빠를 수 없습니다.'
      })
    if (body.recurrence?.frequency === 'weekly' &&
        body.recurrence.daysOfWeek.length === 0)
      return reply.code(400).send({
        message: '매주 반복은 한 개 이상의 요일이 필요합니다.'
      })
    if (body.startTime == null && body.reminders.some(reminder =>
        reminder.timeOfDay == null))
      return reply.code(400).send({
        message: '시간 없는 일정의 알림에는 알림 시각이 필요합니다.'
      })
    if (body.startTime != null && body.reminders.some(reminder =>
        reminder.timeOfDay != null))
      return reply.code(400).send({
        message: '시간 있는 일정의 알림 시각은 일정 시작 시각을 사용합니다.'
      })
    if (body.kind === 'anniversary' && (body.completed ||
        body.recurrence?.frequency !== 'yearly' ||
        body.recurrence.interval !== 1 || body.recurrence.until != null ||
        body.recurrence.count != null))
      return reply.code(400).send({
        message: '기념일은 완료되지 않은 무기한 연간 반복 일정이어야 합니다.'
      })
    if (!hasAccessibleTextContrast(body.color))
      return reply.code(400).send({
        message: '일정 텍스트 색상은 중립 배경에서 4.5:1 이상의 대비가 필요합니다.'
      })
    const result = await app.db.query(
      `WITH changed AS (
         INSERT INTO calendar_items(user_id,id,kind,title,notes,start_date,end_date,
           start_time,end_time,all_day,completed,color,recurrence,reminders,deleted)
         VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,false)
         ON CONFLICT(user_id,id) DO UPDATE SET kind=excluded.kind,title=excluded.title,
           notes=excluded.notes,start_date=excluded.start_date,end_date=excluded.end_date,
           start_time=excluded.start_time,end_time=excluded.end_time,all_day=excluded.all_day,
           completed=excluded.completed,color=excluded.color,recurrence=excluded.recurrence,
           reminders=excluded.reminders,deleted=false,revision=calendar_items.revision+1,
           updated_at=now()
         RETURNING *
       ), logged AS (
         INSERT INTO calendar_sync_changes(user_id,entity_type,entity_id,payload)
         SELECT $1,'calendarItem',id,${calendarItemPayload} FROM changed
         RETURNING cursor,payload
       ) SELECT cursor,payload FROM logged`,
      [request.user.sub, id, body.kind, body.title.trim(), body.notes,
        body.startDate, body.endDate, body.startTime, body.endTime, body.allDay,
        body.completed, body.color, JSON.stringify(body.recurrence),
        JSON.stringify(body.reminders)])
    await notify(app, request.user.sub, result.rows[0].cursor)
    return reply.send({
      ...result.rows[0].payload, cursor: Number(result.rows[0].cursor)
    })
  })

  app.delete('/v2/calendar-items/:id', {
    preHandler: [app.authenticate], schema: { params: idParams }
  }, async (request, reply) => {
    const { id } = request.params as typeof idParams.static
    const result = await app.db.query(
      `WITH changed AS (
         UPDATE calendar_items SET deleted=true,revision=revision+1,updated_at=now()
         WHERE user_id=$1 AND id=$2 RETURNING *
       ), logged AS (
         INSERT INTO calendar_sync_changes(user_id,entity_type,entity_id,payload)
         SELECT $1,'calendarItem',id,${calendarItemPayload} FROM changed
         RETURNING cursor
       ) SELECT cursor FROM logged`, [request.user.sub, id])
    if (!result.rows[0])
      return reply.code(404).send({ message: '일정을 찾을 수 없습니다.' })
    await notify(app, request.user.sub, result.rows[0].cursor)
    return reply.code(204).send()
  })

  app.put('/v2/date-cell-decorations/:id', {
    preHandler: [app.authenticate],
    schema: { params: idParams, body: decorationBody }
  }, async request => {
    const { id } = request.params as typeof idParams.static
    const body = request.body as typeof decorationBody.static
    const result = await app.db.query(
      `WITH changed AS (
         INSERT INTO date_cell_decorations(user_id,id,date,kind,color,label,deleted)
         VALUES($1,$2,$3,$4,$5,$6,false)
         ON CONFLICT(user_id,id) DO UPDATE SET date=excluded.date,kind=excluded.kind,
           color=excluded.color,label=excluded.label,deleted=false,
           revision=date_cell_decorations.revision+1,updated_at=now()
         RETURNING *
       ), logged AS (
         INSERT INTO calendar_sync_changes(user_id,entity_type,entity_id,payload)
         SELECT $1,'dateCellDecoration',id,${decorationPayload} FROM changed
         RETURNING cursor,payload
       ) SELECT cursor,payload FROM logged`,
      [request.user.sub, id, body.date, body.kind, body.color, body.label])
    await notify(app, request.user.sub, result.rows[0].cursor)
    return { ...result.rows[0].payload, cursor: Number(result.rows[0].cursor) }
  })

  app.delete('/v2/date-cell-decorations/:id', {
    preHandler: [app.authenticate], schema: { params: idParams }
  }, async (request, reply) => {
    const { id } = request.params as typeof idParams.static
    const result = await app.db.query(
      `WITH changed AS (
         UPDATE date_cell_decorations SET deleted=true,revision=revision+1,
           updated_at=now() WHERE user_id=$1 AND id=$2 RETURNING *
       ), logged AS (
         INSERT INTO calendar_sync_changes(user_id,entity_type,entity_id,payload)
         SELECT $1,'dateCellDecoration',id,${decorationPayload} FROM changed
         RETURNING cursor
       ) SELECT cursor FROM logged`, [request.user.sub, id])
    if (!result.rows[0])
      return reply.code(404).send({ message: '날짜 장식을 찾을 수 없습니다.' })
    await notify(app, request.user.sub, result.rows[0].cursor)
    return reply.code(204).send()
  })
}

async function notify(app: Parameters<FastifyPluginAsync>[0], userId: string,
  cursor: string | number) {
  await app.db.query(`SELECT pg_notify('todo_changed',$1)`,
    [`${userId}:${cursor}`])
}

function hasAccessibleTextContrast(color: string) {
  const foreground = relativeLuminance(color)
  const background = relativeLuminance('#F8F9FC')
  return (Math.max(foreground, background) + 0.05) /
    (Math.min(foreground, background) + 0.05) >= 4.5
}

function relativeLuminance(color: string) {
  const channels = [1, 3, 5].map(index =>
    Number.parseInt(color.slice(index, index + 2), 16) / 255)
  const linear = channels.map(channel => channel <= 0.04045
    ? channel / 12.92
    : ((channel + 0.055) / 1.055) ** 2.4)
  return 0.2126 * linear[0]! + 0.7152 * linear[1]! + 0.0722 * linear[2]!
}

export default routes
