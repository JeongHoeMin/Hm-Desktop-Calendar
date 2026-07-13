import { Type } from '@sinclair/typebox'
import type { FastifyPluginAsync } from 'fastify'

const idParams = Type.Object({ id: Type.String({ format: 'uuid' }) })
const todoBody = Type.Object({ date: Type.String({ format: 'date' }), title: Type.String({ minLength: 1, maxLength: 500 }),
  time: Type.Union([Type.String({ pattern: '^([01]\\d|2[0-3]):[0-5]\\d$' }), Type.Null()]), notes: Type.String({ maxLength: 10000 }), completed: Type.Boolean() })
const syncQuery = Type.Object({ after: Type.Optional(Type.Integer({ minimum: 0, default: 0 })), limit: Type.Optional(Type.Integer({ minimum: 1, maximum: 500, default: 500 })) })

const routes: FastifyPluginAsync = async app => {
  app.get('/v1/sync', { preHandler: [app.authenticate], schema: { querystring: syncQuery } }, async request => {
    const q = request.query as typeof syncQuery.static; const after = q.after ?? 0; const limit = q.limit ?? 500
    const result = await app.db.query(`SELECT id,date::text,title,time::text,notes,completed,deleted,revision,cursor,updated_at AS "updatedAt"
      FROM todos WHERE user_id=$1 AND cursor>$2 ORDER BY cursor LIMIT $3`, [request.user.sub, after, limit + 1])
    const rows = result.rows.slice(0, limit); return { changes: rows, nextCursor: Number(rows.at(-1)?.cursor ?? after), hasMore: result.rows.length > limit }
  })
  app.put('/v1/todos/:id', { preHandler: [app.authenticate], schema: { params: idParams, body: todoBody } }, async request => {
    const { id } = request.params as typeof idParams.static; const body = request.body as typeof todoBody.static
    const result = await app.db.query(`INSERT INTO todos(user_id,id,date,title,time,notes,completed,deleted) VALUES($1,$2,$3,$4,$5,$6,$7,false)
      ON CONFLICT(user_id,id) DO UPDATE SET date=excluded.date,title=excluded.title,time=excluded.time,notes=excluded.notes,
      completed=excluded.completed,deleted=false,revision=todos.revision+1,cursor=nextval('sync_cursor_seq'),updated_at=now()
      RETURNING id,date::text,title,time::text,notes,completed,deleted,revision,cursor,updated_at AS "updatedAt"`,
      [request.user.sub, id, body.date, body.title.trim(), body.time, body.notes, body.completed])
    const todo = result.rows[0]; await app.db.query(`SELECT pg_notify('todo_changed',$1)`, [`${request.user.sub}:${todo.cursor}`]); return todo
  })
  app.delete('/v1/todos/:id', { preHandler: [app.authenticate], schema: { params: idParams } }, async (request, reply) => {
    const { id } = request.params as typeof idParams.static
    const result = await app.db.query(`UPDATE todos SET deleted=true,revision=revision+1,cursor=nextval('sync_cursor_seq'),updated_at=now()
      WHERE user_id=$1 AND id=$2 RETURNING cursor`, [request.user.sub, id])
    if (!result.rows[0]) return reply.code(404).send({ message: '할 일을 찾을 수 없습니다.' })
    await app.db.query(`SELECT pg_notify('todo_changed',$1)`, [`${request.user.sub}:${result.rows[0].cursor}`]); return reply.code(204).send()
  })
}
export default routes
