#!/usr/bin/env node
// Main-path smoke test against a deployed API (staging):
// rider requests → driver matched → arrives → starts with the PIN → drop-off
// → complete → rider rates. Run before every release.
//
//   node tests/smoke/ride-flow.mjs \
//     --api https://zenride-api-tisa.azurewebsites.net \
//     --rider +2250700000001 --driver +2250700000002
//
// Needs: Node 18+, a rider account and a KYC-approved driver account on that
// server, and Otp__ExposeDevCode=true on that server only (never production),
// so the script can read the login code. Uses a cash ride, so no money moves.

const args = Object.fromEntries(
  process.argv.slice(2).reduce((acc, a, i, all) => (a.startsWith('--') ? [...acc, [a.slice(2), all[i + 1]]] : acc), []),
);
const API = (args.api ?? process.env.SMOKE_API ?? '').replace(/\/$/, '');
const RIDER = args.rider ?? process.env.SMOKE_RIDER_PHONE;
const DRIVER = args.driver ?? process.env.SMOKE_DRIVER_PHONE;
if (!API || !RIDER || !DRIVER) {
  console.error('Usage: node tests/smoke/ride-flow.mjs --api <url> --rider <phone> --driver <phone>');
  process.exit(2);
}

const PICKUP = { label: 'Plateau (smoke test)', lat: 5.3236, lng: -4.0205 };
const DROPOFF = { label: 'Cocody (smoke test)', lat: 5.36, lng: -3.99 };
const started = Date.now();

async function call(method, path, token, body, headers = {}) {
  const res = await fetch(`${API}/api/v1${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  let json;
  try { json = text ? JSON.parse(text) : {}; } catch { json = { raw: text }; }
  if (!res.ok || json.success === false) {
    const err = json.error?.message ?? json.message ?? json.raw ?? res.statusText;
    throw new Error(`${method} ${path} → ${res.status}: ${typeof err === 'string' ? err : JSON.stringify(err)}`);
  }
  return json.data ?? json;
}

async function step(name, fn) {
  const t = Date.now();
  try {
    const out = await fn();
    console.log(`  ✓ ${name} (${Date.now() - t} ms)`);
    return out;
  } catch (e) {
    console.error(`  ✗ ${name}\n    ${e.message}`);
    throw e;
  }
}

async function login(phone, role) {
  const otp = await call('POST', '/auth/otp/request', null, { phone, role, purpose: 'login' });
  if (!otp.dev_code) throw new Error('No dev_code in the OTP response: set Otp__ExposeDevCode=true on this (non-production) server.');
  const session = await call('POST', '/auth/otp/verify', null, {
    otp_token: otp.otp_token,
    code: otp.dev_code,
    device: { device_id: `smoke-${role}`, platform: 'android', app_version: 'smoke' },
  });
  return session.access_token;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let driverToken;
try {
  console.log(`Ride smoke test against ${API}`);
  const riderToken = await step('Rider logs in', () => login(RIDER, 'rider'));
  driverToken = await step('Driver logs in', () => login(DRIVER, 'driver'));

  await step('Driver goes online next to the pickup', async () => {
    await call('POST', '/driver/status', driverToken, { online: true });
    await call('POST', '/driver/location', driverToken, { lat: PICKUP.lat - 0.002, lng: PICKUP.lng });
  });

  const quote = await step('Rider gets a quote', () =>
    call('POST', '/quotes', riderToken, { vertical: 'ride', pickup: PICKUP, dropoff: DROPOFF }));
  const ride = await step('Rider books a cash ride', () =>
    call('POST', '/rides', riderToken,
      { quote_id: quote.quote_id, class_code: quote.options[0].class_code, payment_method: 'cash' },
      { 'Idempotency-Key': `smoke-${Date.now()}` }));
  const id = ride.trip_id;

  await step('Driver receives the offer', async () => {
    for (let i = 0; i < 30; i++) {
      const offer = await call('GET', '/driver/jobs/offer', driverToken).catch(() => null);
      if (offer?.trip_id === id) return;
      await sleep(2000);
    }
    throw new Error('No offer for this ride within 60 s. Is the driver KYC-approved, online and within range?');
  });

  await step('Driver accepts', () => call('POST', `/driver/jobs/${id}/accept`, driverToken));
  await step('Driver heads to the pickup', () => call('POST', `/driver/jobs/${id}/en-route`, driverToken));
  await step('Driver arrives at the pickup', () => call('POST', `/driver/jobs/${id}/arrived-pickup`, driverToken));
  const pin = await step('Rider sees the start PIN', async () => {
    const r = await call('GET', `/rides/${id}`, riderToken);
    if (!r.start_otp) throw new Error('Ride has no start_otp');
    return r.start_otp;
  });
  await step('Driver starts the trip with the PIN', () => call('POST', `/driver/jobs/${id}/start`, driverToken, { otp: pin }));
  await step('Driver heads to the drop-off', () => call('POST', `/driver/jobs/${id}/en-route`, driverToken));
  await step('Driver arrives at the drop-off', () => call('POST', `/driver/jobs/${id}/arrived-dropoff`, driverToken));
  await step('Driver completes the trip', () => call('POST', `/driver/jobs/${id}/complete`, driverToken));
  await step('Rider rates the trip', () => call('POST', `/rides/${id}/rate`, riderToken, { stars: 5, comment: 'Smoke test' }));

  console.log(`\nPASS: ride ${ride.code ?? id} went from request to rating in ${((Date.now() - started) / 1000).toFixed(1)} s`);
} catch {
  console.error('\nFAIL: see the step above.');
  process.exitCode = 1;
} finally {
  if (driverToken) await call('POST', '/driver/status', driverToken, { online: false }).catch(() => {});
}
