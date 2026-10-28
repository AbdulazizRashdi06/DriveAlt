import { afterEach, expect, it, vi } from 'vitest'
import { sendChat } from './api'

afterEach(() => vi.unstubAllGlobals())

it('surfaces a streamed AI failure without inventing a bot message', async () => {
  const onMessage = vi.fn()
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('event: error\ndata: {"error":"The AI could not complete this reply."}\n\n', {
    status: 200, headers: { 'content-type': 'text/event-stream' },
  })))
  await expect(sendChat('test', 'hello', { onMessage })).rejects.toThrow('The AI could not complete this reply.')
  expect(onMessage).not.toHaveBeenCalled()
})
