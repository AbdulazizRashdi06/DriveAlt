import { test } from 'node:test'
import assert from 'node:assert/strict'
import { pathToFileURL } from 'node:url'

const { parseChat, createGateway } = await import(pathToFileURL(process.env.CODEX_GATEWAY_MODULE).href)
const request = () => ({ model: 'gpt-6.1-terra', reasoning_effort: 'low', messages: [
  { role: 'system', content: 'Extract a batch' },
  { role: 'user', content: [{ type: 'text', text: 'Eight pages' }, ...Array.from({ length: 8 }, () =>
    ({ type: 'image_url', image_url: { url: 'data:image/jpeg;base64,YWJj' } }))] },
], response_format: { json_schema: { schema: { type: 'object' } } } })

test('eight image slots and Terra low effort survive parsing', () => {
  const parsed = parseChat(request())
  assert.equal(parsed.model, 'gpt-6.1-terra')
  assert.equal(parsed.reasoningEffort, 'low')
  assert.equal(parsed.images.length, 8)
})
test('legacy callers retain medium effort; arbitrary effort and ninth images fail', () => {
  let body = request(); body.model = 'gpt-6-luna'; delete body.reasoning_effort
  assert.equal(parseChat(body).reasoningEffort, 'medium')
  body = request(); body.reasoning_effort = 'arbitrary'; assert.throws(() => parseChat(body))
  body = request(); body.messages[1].content.push(body.messages[1].content[1]); assert.throws(() => parseChat(body))
})
test('HTTP contract forwards Terra low and eight images without invoking the CLI', async () => {
  const server = createGateway(async input => {
    assert.equal(input.reasoningEffort, 'low'); assert.equal(input.images.length, 8)
    return { parsed: { ready: true }, usage: {} }
  }, async () => true)
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
  try {
    const response = await fetch(`http://127.0.0.1:${server.address().port}/v1/chat/completions`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request()),
    })
    assert.equal(response.status, 200)
    assert.deepEqual(JSON.parse((await response.json()).choices[0].message.content), { ready: true })
  } finally { await new Promise(resolve => server.close(resolve)) }
})
