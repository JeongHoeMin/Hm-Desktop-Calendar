import fp from 'fastify-plugin'
import websocket from '@fastify/websocket'
import type { WebSocket } from 'ws'

export default fp(async function realtime(app) {
  await app.register(websocket)
  const sockets = new Map<string, Set<WebSocket>>()
  const listener = await app.db.connect()
  await listener.query('LISTEN todo_changed')
  listener.on('notification', message => {
    const [userId, cursorText] = (message.payload ?? '').split(':')
    if (!userId || !cursorText) return
    const payload = JSON.stringify({ type: 'todos.changed', cursor: Number(cursorText) })
    for (const socket of sockets.get(userId) ?? []) if (socket.readyState === socket.OPEN) socket.send(payload)
  })
  app.get('/v1/realtime', { websocket: true, preValidation: [app.authenticate] }, (socket, request) => {
    const userId = request.user.sub
    const set = sockets.get(userId) ?? new Set<WebSocket>(); set.add(socket); sockets.set(userId, set)
    socket.send(JSON.stringify({ type: 'connected' }))
    socket.on('close', () => { set.delete(socket); if (set.size === 0) sockets.delete(userId) })
  })
  app.addHook('onClose', async () => { await listener.query('UNLISTEN todo_changed'); listener.release() })
})
