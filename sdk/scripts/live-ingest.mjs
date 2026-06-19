// Live check: drive the built SDK against a running Dimes API. Sends two signals sharing a
// fingerprint to exercise server-side aggregation. Usage: node live-ingest.mjs <endpoint> <sourceId>
import { init } from '../dist/index.js'

const [endpoint, sourceId] = process.argv.slice(2)
if (!endpoint || !sourceId) {
  console.error('usage: node live-ingest.mjs <endpoint> <sourceId>')
  process.exit(2)
}

const client = init({ endpoint, sourceId, captureErrors: false, captureRageClicks: false })
client.identify({ role: 'tester', segment: 'beta' })

const fingerprint = 'sdk-live-fp'
client.capture({ kind: 'ExplicitFeedback', payload: { message: 'SDK live test' }, fingerprint }, { force: true })
client.capture({ kind: 'ExplicitFeedback', payload: { message: 'SDK live test' }, fingerprint }, { force: true })

await client.flush()
console.log('sent 2 signals with fingerprint', fingerprint)
