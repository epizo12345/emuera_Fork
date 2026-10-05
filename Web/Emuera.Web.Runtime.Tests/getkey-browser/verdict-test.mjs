import assert from 'node:assert/strict';
import fs from 'node:fs';
import { verdict } from './verdict.mjs';
const clean = { checks: [{ pass: true }], complete: true };
const cases = [
  ['matching values', clean, 'PASS'],
  ['mismatch', { ...clean, checks: [{ pass: false }] }, 'FAIL'],
  ['console error with matching values', { ...clean, errors: [{ method: 'Runtime.consoleAPICalled' }] }, 'FAIL'],
  ['runtime exception with matching values', { ...clean, errors: [{ method: 'Runtime.exceptionThrown' }] }, 'FAIL'],
  ['network failure with matching values', { ...clean, network: [{ errorText: 'net::ERR_EMPTY_RESPONSE' }] }, 'FAIL'],
  ['required HTTP 404', { ...clean, http: [{ path: '/required.wasm', status: 404 }] }, 'FAIL'],
  ['required HTTP 500', { ...clean, http: [{ path: '/required.js', status: 500 }] }, 'FAIL'],
  ['incomplete', { ...clean, complete: false }, 'FAIL'],
  ['empty checks', { ...clean, checks: [] }, 'FAIL'],
  ['caught runner failure', { ...clean, failures: ['timeout'] }, 'FAIL'],
];
const results = cases.map(([name, input, expected]) => {
  const result = verdict(input); assert.equal(result.status, expected, name);
  return { name, expected, ...result, pass: true };
});
// Reproduce the old final predicate: unexpected errors did not participate in its decision.
assert.equal(clean.checks.some(c => !c.pass), false);
const output = process.argv[2];
if (output) { if (fs.existsSync(output)) throw Error('Refusing evidence overwrite'); fs.writeFileSync(output, JSON.stringify({ status: 'PASS', results }, null, 2)); }
console.log(JSON.stringify({ status: 'PASS', checks: results.length, oldPredicateWouldPassErrorCases: true }));
