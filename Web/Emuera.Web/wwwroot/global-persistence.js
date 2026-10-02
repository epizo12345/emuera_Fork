import { displayScale } from './scale-fit.js';
const DB_NAME = 'emuera-web-saves-v1';
const STORE_NAME = 'files';
const handles = new Map();
const gameAreaClicks = new WeakMap();
const diagnostics = globalThis.emueraPersistenceDiagnostics ??= [];
let lastPointer = null;
let blockedCompositionSubmitRequestId = null;
const rememberPointer = event => { lastPointer = { x: event.clientX, y: event.clientY }; };
globalThis.addEventListener('pointermove', rememberPointer, { passive: true });
globalThis.addEventListener('mousemove', rememberPointer, { passive: true });

function diagnostic(stage, detail = null) {
  diagnostics.push({ time: new Date().toISOString(), stage, detail });
  if (diagnostics.length > 128) diagnostics.splice(0, diagnostics.length - 128);
  console.info(`[global-persistence] ${stage}`, detail ?? '');
}

export { diagnostic };

let textMeasureContext;
export function measureText(text, fontName, fontSize) {
  textMeasureContext ??= document.createElement("canvas").getContext("2d");
  textMeasureContext.font = `${fontSize}px ${JSON.stringify(fontName)}`;
  return textMeasureContext.measureText(text).width;
}

function ensureFocusedInputVisible() {
  const element = document.activeElement;
  if (element?.id !== 'runtime-input') return;
  const rect = element.getBoundingClientRect();
  if (rect.bottom > innerHeight) scrollBy(0, rect.bottom - innerHeight);
  else if (rect.top < 0) scrollBy(0, rect.top);
}

globalThis.addEventListener('resize', ensureFocusedInputVisible);

export function focusElement(elementId) {
  const active = document.activeElement;
  if (active?.closest?.('#host-toolbar details[open]')) return;
  const element = document.getElementById(elementId);
  element?.focus({ preventScroll: true });
  if (elementId === 'runtime-input') ensureFocusedInputVisible();
}

export function waitForPaint() {
  return new Promise(resolve => requestAnimationFrame(() => resolve()));
}

export function positionLockedButtons(elementId) {
  const root = document.getElementById(elementId);
  if (!root) return;
  for (const part of root.querySelectorAll('.game-line > [data-locked-x]')) {
    const line = part.parentElement;
    if (part.dataset.lockedX == null || part.dataset.lockedX === '') continue;
    const target = Number(part.dataset.lockedX);
    if (!Number.isFinite(target)) continue;
    part.style.marginLeft = '0px';
    const current = (part.getBoundingClientRect().left - line.getBoundingClientRect().left) / displayScale(root);
    part.style.marginLeft = `${target - current}px`;
  }
}

export function installInputCompositionGuard(elementId) {
  const input = document.getElementById(elementId);
  if (!input || input.dataset.compositionGuard === 'true') return;
  input.dataset.compositionGuard = 'true';
  let composing = false;
  let compositionEnter = false;
  input.addEventListener('compositionstart', () => { composing = true; });
  input.addEventListener('compositionend', () => { composing = false; });
  input.addEventListener('keydown', event => {
    if (event.key === 'Enter') {
      compositionEnter = composing || event.isComposing;
      blockedCompositionSubmitRequestId = compositionEnter ? input.dataset.requestId : null;
    }
  }, true);
  input.closest('form')?.addEventListener('pointerdown', () => { blockedCompositionSubmitRequestId = null; }, true);
  input.addEventListener('keyup', event => {
    if (event.key === 'Enter') compositionEnter = false;
  }, true);
  input.closest('form')?.addEventListener('submit', event => {
    if (!composing && !compositionEnter) return;
    blockedCompositionSubmitRequestId = input.dataset.requestId;
    event.preventDefault();
    event.stopImmediatePropagation();
  }, true);
}

export function consumeInputCompositionSubmit(elementId) {
  const requestId = document.getElementById(elementId)?.dataset.requestId;
  const blocked = blockedCompositionSubmitRequestId === requestId;
  blockedCompositionSubmitRequestId = null;
  return blocked;
}

export function installOneInputGuard(elementId, keyboardHost = null) {
  const screen = document.getElementById(elementId);
  if (!screen || screen.dataset.oneInputGuard === 'true') return;
  screen.dataset.oneInputGuard = 'true';
  if (elementId === 'game-screen' && keyboardHost)
    globalThis.addEventListener('blur', () => keyboardHost.invokeMethodAsync('ClearKeyboardFocus'));
  if (elementId === 'game-screen')
    screen.addEventListener('mousedown', event => {
      if (event.button === 0 && event.detail >= 2) event.preventDefault();
    }, { capture: true });
  let press = null;
  const selectionSnapshot = () => {
    const s = getSelection();
    return { text: s?.toString() ?? '', rangeCount: s?.rangeCount ?? 0,
      anchorNode: s?.anchorNode ?? null, anchorOffset: s?.anchorOffset ?? 0,
      focusNode: s?.focusNode ?? null, focusOffset: s?.focusOffset ?? 0 };
  };
  const sameSelection = (a, b) => a.text === b.text && a.rangeCount === b.rangeCount
    && a.anchorNode === b.anchorNode && a.anchorOffset === b.anchorOffset
    && a.focusNode === b.focusNode && a.focusOffset === b.focusOffset;
  screen.addEventListener('pointerdown', event => {
    const status = document.querySelector('#p1c2-status')?.dataset;
    press = { x: event.clientX, y: event.clientY, button: event.button,
      kind: status?.pendingKind, requestId: status?.requestId,
      sessionGeneration: status?.sessionGeneration,
      moved: false, selection: selectionSnapshot() };
  }, { capture: true });
  screen.addEventListener('pointermove', event => {
    if (press && event.buttons && Math.hypot(event.clientX - press.x, event.clientY - press.y) > 4)
      press.moved = true;
  }, { capture: true, passive: true });
  screen.addEventListener('click', () => {
    const status = document.querySelector('#p1c2-status')?.dataset;
    const selectionNow = selectionSnapshot();
    const selectionChanged = !!press && selectionNow.text.length > 0
      && !sameSelection(press.selection, selectionNow);
    const allowed = press && press.button === 0 && !press.moved
      && (press.kind === 'enter' || press.kind === 'anykey') && !selectionChanged
      && press.requestId === status?.requestId
      && press.sessionGeneration === status?.sessionGeneration;
    gameAreaClicks.set(screen, allowed ? {
      requestId: Number(press.requestId),
      sessionGeneration: Number(press.sessionGeneration)
    } : null);
    press = null;
  }, { capture: true });
  screen.addEventListener('keydown', event => {
      if (event.isComposing || /^F(?:[1-9]|1[0-2])$/.test(event.code)) { event.stopPropagation(); return; }
    const pendingKind = document.querySelector('#p1c2-status')?.dataset.pendingKind;
    if (elementId === 'game-screen' && event.key === 'Enter'
      && (pendingKind === 'enter' || pendingKind === 'anykey'))
      event.preventDefault();
    if (screen.dataset.oneInput === 'true' && event.key.length === 1
      && !event.ctrlKey && !event.altKey && !event.metaKey && !event.isComposing)
      event.preventDefault();
  }, { capture: true });
}

export function installAbsoluteViewport(elementId) {
  const screen = document.getElementById(elementId);
  if (!screen || screen.dataset.absoluteViewport === 'true') return;
  screen.dataset.absoluteViewport = 'true';
  const sync = () => screen.style.setProperty('--game-scroll-top', `${screen.scrollTop}px`);
  screen.addEventListener('scroll', sync, { passive: true });
  sync();
}

export function consumeGameAreaClick(elementId) {
  const screen = document.getElementById(elementId);
  const allowed = screen ? gameAreaClicks.get(screen) ?? null : null;
  if (screen) gameAreaClicks.delete(screen);
  return allowed;
}

export function closeSavePanel(elementId) {
  const panel = document.getElementById(elementId);
  if (!panel) return;
  panel.open = false;
  panel.querySelector('summary')?.focus();
}

// Staging is separate from committed saves and is never served over HTTP.
const importStageName = nonce => `emuera-save-import-stage-v1-${nonce}`;
const importStageKey = (nonce, name) => new URL(`/__emuera_import_stage__/${nonce}/${encodeURIComponent(name)}`, location.origin);
const stagedCapture = () => {
  try { return JSON.parse(sessionStorage.getItem('emuera-active-import-capture') || 'null'); }
  catch { return null; }
};

export async function stageImportFiles(handleId, elementId, includeCaptureMetadata = false) {
  requireHandle(handleId);
  const files = [...(document.getElementById(elementId)?.files ?? [])];
  if (files.length === 0) return null;
  if (files.length > 64) throw new Error('import件数が不正です');
  const names = files.map(file => normalizeFilename(file.name));
  if (new Set(names).size !== names.length || files.some(file => file.size < 1 || file.size > 256 * 1024 * 1024)
    || files.reduce((sum, file) => sum + file.size, 0) > 512 * 1024 * 1024)
    throw new Error('保存ファイルの名前またはサイズが不正です');
  const nonce = crypto.randomUUID();
  const cache = await caches.open(importStageName(nonce));
  try {
    for (let index = 0; index < files.length; index++)
      await cache.put(importStageKey(nonce, names[index]), new Response(files[index]));
    const createdAt = Date.now();
    await cache.put(importStageKey(nonce, '__manifest__'), new Response(JSON.stringify({
      gameId: requireHandle(handleId).gameId, profileId: requireHandle(handleId).profileId,
      files: names.map((name, index) => ({ name, size: files[index].size })), createdAt
    })));
    const bytes = files.reduce((sum, file) => sum + file.size, 0);
    sessionStorage.setItem('emuera-active-import-capture', JSON.stringify({ nonce, count: files.length, bytes, at: performance.timeOrigin + performance.now(), createdAt }));
    diagnostic('active-import-captured', { count: files.length, bytes });
    return includeCaptureMetadata ? { nonce, count: files.length, bytes, files: names.map((name, index) => ({ name, size: files[index].size })) } : nonce;
  } catch (error) {
    await caches.delete(importStageName(nonce));
    throw error;
  }
}

export async function applyStagedImport(handleId, nonce, elementId) {
  const handle = requireHandle(handleId);
  if (!/^[0-9a-f-]{36}$/i.test(nonce)) throw new Error('import stage IDが不正です');
  const cache = await caches.open(importStageName(nonce));
  const manifestResponse = await cache.match(importStageKey(nonce, '__manifest__'));
  if (!manifestResponse) throw new Error('import stageが見つかりません');
  const manifest = await manifestResponse.json();
  const capture = stagedCapture();
  if (manifest.gameId !== handle.gameId || manifest.profileId !== handle.profileId
    || capture?.nonce !== nonce || capture.createdAt !== manifest.createdAt
    || Date.now() - manifest.createdAt < 0 || Date.now() - manifest.createdAt > 15 * 60 * 1000
    || !Array.isArray(manifest.files) || manifest.files.length !== capture.count)
    throw new Error('import stageの保存領域が一致しません');
  const transfer = new DataTransfer();
  let totalBytes = 0;
  for (const entry of manifest.files) {
    const name = normalizeFilename(entry.name);
    const response = await cache.match(importStageKey(nonce, name));
    if (!response) throw new Error(`import stageが不足しています: ${name}`);
    const bytes = await response.blob();
    if (bytes.size !== entry.size) throw new Error(`import stageが不完全です: ${name}`);
    totalBytes += bytes.size;
    transfer.items.add(new File([bytes], name, { type: 'application/octet-stream' }));
  }
  if (totalBytes !== capture.bytes) throw new Error('import stageの総量が一致しません');
  const input = document.getElementById(elementId);
  if (!input) throw new Error('import欄が見つかりません');
  if (!(await clearStagedImport(nonce))) throw new Error('import stageを消去できません');
  input.files = transfer.files;
  input.dispatchEvent(new Event('change', { bubbles: true }));
  diagnostic('active-import-applied', { count: manifest.files.length });
  return manifest.files.length;
}

export async function clearStagedImport(nonce) {
  if (!/^[0-9a-f-]{36}$/i.test(nonce)) return false;
  const capture = stagedCapture();
  if (capture?.nonce === nonce) sessionStorage.removeItem('emuera-active-import-capture');
  return caches.delete(importStageName(nonce));
}

function requireHandle(id) {
  const handle = handles.get(id);
  if (!handle) throw new Error('保存領域のhandleが無効です');
  return handle;
}

function requestResult(request, label) {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => {
      diagnostic(`${label}-success`);
      resolve(request.result);
    };
    request.onerror = () => {
      diagnostic(`${label}-error`, request.error?.message ?? 'IndexedDB request failed');
      reject(request.error ?? new Error('IndexedDB request failed'));
    };
  });
}

function transactionDone(transaction, label, detail = null) {
  return new Promise((resolve, reject) => {
    transaction.oncomplete = () => {
      diagnostic(`${label}-transaction-complete`, detail);
      resolve();
    };
    transaction.onerror = () => {
      diagnostic(`${label}-transaction-error`, { ...detail, error: transaction.error?.message ?? 'IndexedDB transaction failed' });
      reject(transaction.error ?? new Error('IndexedDB transaction failed'));
    };
    transaction.onabort = () => {
      diagnostic(`${label}-transaction-abort`, { ...detail, error: transaction.error?.message ?? 'IndexedDB transaction aborted' });
      reject(transaction.error ?? new Error('IndexedDB transaction aborted'));
    };
  });
}

function openDatabase(fault) {
  diagnostic('openDatabase-start');
  if (fault === 'open') throw new Error('fault injection: IndexedDB open');
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(STORE_NAME, {
      keyPath: ['gameId', 'profileId', 'logicalFilename']
    });
    request.onsuccess = () => {
      diagnostic('openDatabase-success');
      resolve(request.result);
    };
    request.onerror = () => {
      diagnostic('openDatabase-error', request.error?.message ?? 'IndexedDBを開けません');
      reject(request.error ?? new Error('IndexedDBを開けません'));
    };
    request.onblocked = () => {
      diagnostic('openDatabase-blocked');
      reject(new Error('IndexedDB upgradeが別ページでblockされました'));
    };
  });
}

export async function acquire(gameId, profileId) {
  if (!/^[a-z0-9._-]{1,64}$/i.test(gameId) || !/^[a-z0-9._-]{1,64}$/i.test(profileId))
    throw new Error('gameId/profileIdが不正です');
  if (!navigator.locks) throw new Error('Web Locksを利用できません');
  const id = crypto.randomUUID();
  let release;
  let lockRequest;
  const released = new Promise(resolve => { release = resolve; });
  const acquired = new Promise((resolve, reject) => {
    lockRequest = navigator.locks.request(`emuera:${gameId}:${profileId}`, { mode: 'exclusive', ifAvailable: true }, async lock => {
      if (!lock) {
        diagnostic('web-lock-unavailable');
        resolve({ acquired: false, reason: 'この保存領域は別タブで使用中です' });
        return;
      }
      diagnostic('web-lock-acquired');
      handles.set(id, { gameId, profileId, revisions: new Map(), release, lockRequest: () => lockRequest });
      resolve({ acquired: true, handleId: id });
      await released;
      handles.delete(id);
    });
    lockRequest.catch(reject);
  });
  return acquired;
}

function normalizeFilename(value) {
  if (typeof value !== 'string') throw new Error('保存ファイル名が不正です');
  if (/^global\.sav$/i.test(value)) return 'global.sav';
  const match = /^save([0-9]+)\.sav$/i.exec(value);
  if (!match) throw new Error(`保存ファイル名が不正です: ${value}`);
  const index = BigInt(match[1]);
  if (index > 2147483647n) throw new Error(`保存番号が範囲外です: ${value}`);
  const canonical = `save${index.toString().padStart(2, '0')}.sav`;
  if (value.toLowerCase() !== canonical)
    throw new Error(`非canonicalな保存ファイル名です: ${value}`);
  return canonical;
}

export async function prepareFiles(handleId, fault = '') {
  const handle = requireHandle(handleId);
  const database = await openDatabase(fault);
  try {
    const transaction = database.transaction(STORE_NAME, 'readonly');
    const done = transactionDone(transaction, 'prepare-files');
    diagnostic('prepare-transaction-start');
    const records = await requestResult(transaction.objectStore(STORE_NAME).getAll(), 'prepare-files-get-all');
    await done;
    handle.revisions.clear();
    const files = records
      .filter(record => record.gameId === handle.gameId && record.profileId === handle.profileId)
      .map(record => {
        const logicalFilename = normalizeFilename(record.logicalFilename);
        handle.revisions.set(logicalFilename, record.revision ?? null);
        return {
          logicalFilename,
          deleted: record.deleted === true,
          bytes: record.deleted === true ? null : new Uint8Array(record.bytes),
          revision: record.revision ?? null,
          lastOperationId: record.lastOperationId ?? null
        };
      });
    return { files };
  } finally {
    database.close();
  }
}

export async function prepare(handleId, fault = '') {
  const prepared = await prepareFiles(handleId, fault);
  const record = prepared.files.find(file => file.logicalFilename === 'global.sav' && !file.deleted);
  return {
    exists: !!record,
    bytes: record?.bytes ?? null,
    revision: record?.revision ?? null,
    lastOperationId: record?.lastOperationId ?? null
  };
}

export async function commitMutations(handleId, mutations, fault = '') {
  const handle = requireHandle(handleId);
  if (!Array.isArray(mutations) || mutations.length < 1 || mutations.length > 128)
    throw new Error('保存batchの件数が不正です');
  const batch = mutations.map(mutation => {
    const logicalFilename = normalizeFilename(mutation.logicalFilename);
    const put = mutation.kind === 0 || /^put$/i.test(String(mutation.kind));
    const remove = mutation.kind === 1 || /^delete$/i.test(String(mutation.kind));
    if (!put && !remove) throw new Error('保存mutation種別が不正です');
    const bytes = put ? new Uint8Array(mutation.bytes) : null;
    if (put && bytes.byteLength === 0) throw new Error(`空の保存データはcommitできません: ${logicalFilename}`);
    return { logicalFilename, put, bytes, operationId: mutation.operationId };
  });
  const database = await openDatabase(fault);
  let transaction;
  try {
    try {
      transaction = database.transaction(STORE_NAME, 'readwrite', { durability: 'strict' });
    } catch {
      transaction = database.transaction(STORE_NAME, 'readwrite');
    }
    const store = transaction.objectStore(STORE_NAME);
    const transactionDetail = { operations: batch.map(({ operationId, logicalFilename }) => ({ operationId, logicalFilename })) };
    const done = transactionDone(transaction, 'commit', transactionDetail);
    diagnostic('commit-transaction-start', transactionDetail);
    const currentByName = new Map();
    for (const logicalFilename of new Set(batch.map(mutation => mutation.logicalFilename))) {
      const current = await requestResult(store.get([handle.gameId, handle.profileId, logicalFilename]), `commit-get-${logicalFilename}`);
      currentByName.set(logicalFilename, current);
      const expected = fault === 'conflict' ? 'fault-stale-revision' : (handle.revisions.get(logicalFilename) ?? null);
      if ((current?.revision ?? null) !== expected) {
        transaction.abort();
        try { await done; } catch { }
        throw new Error(`保存revisionが競合しました: ${logicalFilename}`);
      }
    }
    if (fault === 'abort') {
      transaction.abort();
      await done;
    }
    if (fault === 'quota') {
      transaction.abort();
      try { await done; } catch { }
      throw new DOMException('fault injection: quota', 'QuotaExceededError');
    }
    const results = [];
    for (const mutation of batch) {
      const revision = crypto.randomUUID();
      store.put({
        gameId: handle.gameId,
        profileId: handle.profileId,
        logicalFilename: mutation.logicalFilename,
        ...(mutation.put ? { bytes: mutation.bytes, deleted: false } : { deleted: true }),
        revision,
        lastOperationId: mutation.operationId
      });
      results.push({ logicalFilename: mutation.logicalFilename, revision, deleted: !mutation.put, byteLength: mutation.bytes?.byteLength ?? 0, lastOperationId: mutation.operationId });
    }
    if (fault === 'abort-after-put') {
      transaction.abort();
      await done;
    }
    const durability = transaction.durability ?? 'default';
    await done;
    for (const result of results) handle.revisions.set(result.logicalFilename, result.revision);
    return { files: results, durability };
  } finally {
    database.close();
  }
}

export async function commit(handleId, bytes, operationId, fault = '') {
  const committed = await commitMutations(handleId, [{ logicalFilename: 'global.sav', kind: 'Put', bytes, operationId }], fault);
  const result = committed.files[0];
  return { revision: result.revision, durability: committed.durability, byteLength: result.byteLength, lastOperationId: result.lastOperationId };
}

export async function readCommitted(handleId, logicalFilename) {
  const handle = requireHandle(handleId);
  const name = normalizeFilename(logicalFilename);
  const database = await openDatabase('');
  try {
    const transaction = database.transaction(STORE_NAME, 'readonly');
    const done = transactionDone(transaction, 'read-committed');
    const record = await requestResult(transaction.objectStore(STORE_NAME).get([handle.gameId, handle.profileId, name]), `read-${name}`);
    await done;
    handle.revisions.set(name, record?.revision ?? null);
    return {
      exists: !!record && record.deleted !== true,
      deleted: record?.deleted === true,
      bytes: record && record.deleted !== true ? new Uint8Array(record.bytes) : null,
      revision: record?.revision ?? null,
      lastOperationId: record?.lastOperationId ?? null
    };
  } finally {
    database.close();
  }
}

export async function downloadCommitted(handleId, filename = 'global.sav') {
  const logicalFilename = normalizeFilename(filename);
  const state = await readCommitted(handleId, logicalFilename);
  if (!state.exists) throw new Error(`commit済み${logicalFilename}がありません`);
  const url = URL.createObjectURL(new Blob([state.bytes], { type: 'application/octet-stream' }));
  try {
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = logicalFilename;
    anchor.click();
  } finally {
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
  return { revision: state.revision, byteLength: state.bytes.byteLength };
}

export async function release(handleId) {
  const handle = handles.get(handleId);
  if (!handle) return false;
  handle.release();
  await handle.lockRequest();
  return true;
}

export function measureElement(id) {
  const element = document.getElementById(id);
  if (!element) throw new Error(`element not found: ${id}`);
  return { width: element.clientWidth, height: element.clientHeight };
}

export function inputContext(id, clientX = null, clientY = null) {
  const element = document.getElementById(id);
  if (!element) throw new Error(`element not found: ${id}`);
  const point = clientX === null || clientY === null ? lastPointer : { x: clientX, y: clientY };
  const rect = element.getBoundingClientRect();
  const scale = displayScale(element);
  const width = element.clientWidth, height = element.clientHeight;
  if (!point) return { x: 0, y: height, width, height, buttonValue: null, buttonIsInteger: true };
  lastPointer = point;
  const target = document.elementFromPoint(point.x, point.y)?.closest('[data-input]');
  const ownsTarget = target && (element.contains(target) || document.getElementById('game-island-layer')?.contains(target));
  return {
    x: Math.trunc((point.x - rect.left) / scale - element.clientLeft),
    y: Math.trunc((point.y - rect.top) / scale - element.clientTop),
    width, height,
    buttonValue: ownsTarget ? target.dataset.input : null,
    buttonIsInteger: ownsTarget ? target.dataset.inputInteger === 'true' : true
  };
}
