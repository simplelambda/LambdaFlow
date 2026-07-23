import { createInterface } from 'node:readline';

const handlers = new Map([
  ['backend.ping', () => ({ status: 'pong', runtime: 'node' })],
  ['uppercase', payload => ({ text: String(payload?.text ?? '').toUpperCase() })]
]);

function writeEnvelope(envelope) {
  process.stdout.write(`${JSON.stringify(envelope)}\n`);
}

async function dispatch(raw) {
  let request;
  try {
    request = JSON.parse(raw);
    if (!request || typeof request.kind !== 'string' || request.kind.trim() === '')
      throw new Error('kind must be a non-empty string');
  } catch (error) {
    console.error(`Invalid LambdaFlow envelope: ${error instanceof Error ? error.message : String(error)}`);
    return;
  }

  const handler = handlers.get(request.kind);
  if (!handler) {
    if (typeof request.id === 'string') {
      writeEnvelope({
        kind: `${request.kind}.result`,
        id: request.id,
        ok: false,
        error: { code: 'HANDLER_NOT_FOUND', message: `No handler for ${request.kind}` }
      });
    }
    return;
  }

  try {
    const payload = await handler(request.payload);
    if (typeof request.id === 'string') {
      writeEnvelope({
        kind: `${request.kind}.result`,
        id: request.id,
        ok: true,
        payload
      });
    }
  } catch (error) {
    if (typeof request.id === 'string') {
      writeEnvelope({
        kind: `${request.kind}.result`,
        id: request.id,
        ok: false,
        error: {
          code: 'HANDLER_ERROR',
          message: error instanceof Error ? error.message : String(error)
        }
      });
    }
  }
}

const input = createInterface({
  input: process.stdin,
  crlfDelay: Infinity,
  terminal: false
});

input.on('line', line => {
  if (line.trim() !== '') void dispatch(line);
});
