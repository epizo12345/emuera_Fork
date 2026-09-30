const diagnostics = globalThis.emueraLongRunningDiagnostics ??= [];

function diagnostic(notice, phase, reason = '') {
  const entry = {
    timestamp: new Date().toISOString(),
    sessionId: notice.sessionId,
    promptId: notice.promptId,
    file: notice.file,
    line: notice.line,
    elapsedMilliseconds: notice.elapsedMilliseconds,
    executedLines: notice.executedLines,
    phase,
    reason
  };
  diagnostics.push(entry);
  console.info(`[long-running-prompt] ${phase}`, entry);
}

export function confirmLongRunning(notice) {
  if (typeof globalThis.confirm !== 'function')
    throw new Error('LONG_RUNNING_CONFIRM_UNAVAILABLE');
  const message = [
    '処理に時間がかかっています。続けますか？',
    'OK：続行 ／ キャンセル：中止',
    '',
    `処理位置：${notice.file}:${notice.line}`,
    `前回の入力または続行から：約${(notice.elapsedMilliseconds / 1000).toFixed(1)}秒`,
    `実行行数：${notice.executedLines}行`,
    '',
    '中止すると、保存されていない進行は失われます。'
  ].join('\n');
  diagnostic(notice, 'show');
  try {
    const continueRequested = globalThis.confirm(message) === true;
    const reason = continueRequested ? 'confirm-approved' : 'cancel-or-suppressed';
    diagnostic(notice, 'answer', reason);
    return { continueRequested, reason };
  } catch (error) {
    diagnostic(notice, 'error', error?.message ?? String(error));
    throw error;
  }
}

export { measureText } from './global-persistence.js';
