// Task-only: every observed error is fatal in a normal run, even if GETKEY values match.
export function verdict({ checks = [], errors = [], network = [], http = [], failures = [], complete = false }) {
  const reasons = [];
  if (!complete) reasons.push('run-incomplete');
  if (!checks.length) reasons.push('no-checks');
  if (checks.some(c => !c.pass)) reasons.push('GETKEY-mismatch');
  if (errors.length) reasons.push('console-or-runtime-error');
  if (network.length) reasons.push('network-failure');
  if (http.some(r => r.status >= 400)) reasons.push('HTTP-failure');
  if (failures.length) reasons.push('assertion-or-runner-failure');
  return { status: reasons.length ? 'FAIL' : 'PASS', reasons, checks: checks.length,
    mismatches: checks.filter(c => !c.pass).length, errors: errors.length,
    network: network.length, httpFailures: http.filter(r => r.status >= 400).length };
}
