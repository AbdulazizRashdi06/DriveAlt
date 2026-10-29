// Verify actual patch bytes/checksums on each platform, before any deployment has SSH credentials.
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import crypto from 'node:crypto'
import { execFileSync } from 'node:child_process'
import assert from 'node:assert/strict'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const script = fs.readFileSync(path.join(root, 'deploy/ensure-terra-gateway.sh'), 'utf8')
const before = /^before=([a-f0-9]{64})$/m.exec(script)[1]
const after = /^after=([a-f0-9]{64})$/m.exec(script)[1]
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex')
const fixture = fs.readFileSync(path.join(root, 'tests/fixtures/codex-gateway-before.mjs'))
assert.equal(hash(fixture), before)
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'drivealt-terra-patch-'))
fs.mkdirSync(path.join(tmp, 'deploy/codex'), { recursive: true })
const gateway = path.join(tmp, 'deploy/codex/gateway.mjs')
fs.writeFileSync(gateway, fixture)
execFileSync('git', ['init', '-q', tmp])
execFileSync('git', ['-C', tmp, 'config', 'core.autocrlf', 'false'])
execFileSync('git', ['-C', tmp, 'apply', '--check', path.join(root, 'deploy/codex-terra-batches.patch')])
execFileSync('git', ['-C', tmp, 'apply', path.join(root, 'deploy/codex-terra-batches.patch')])
assert.equal(hash(fs.readFileSync(gateway)), after, 'Patched gateway must match the production LF checksum')
execFileSync('node', ['--test', path.join(root, 'deploy/codex-terra-gateway.test.mjs')], {
  env: { ...process.env, CODEX_GATEWAY_MODULE: gateway, CODEX_MODELS: 'gpt-6-luna,gpt-6.1-sol,gpt-6.1-terra' }, stdio: 'inherit',
})
console.log('Gateway baseline, patch bytes, target checksum and HTTP contract passed.')
