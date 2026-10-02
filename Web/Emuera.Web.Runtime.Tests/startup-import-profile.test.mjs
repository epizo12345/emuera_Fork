import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const sourcePath = new URL('../Emuera.Web/wwwroot/startup-import-profile.js', import.meta.url);
const source = readFileSync(sourcePath, 'utf8');
const profileModule = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

class MemoryStorage {
  values = new Map();
  getItem(key) { return this.values.get(key) ?? null; }
  setItem(key, value) { this.values.set(key, String(value)); }
  removeItem(key) { this.values.delete(key); }
}

test('startup/import profile correlates reload pages without mixing their monotonic clocks', () => {
  const storage = new MemoryStorage();
  let monotonicNow = 2_500;
  globalThis.sessionStorage = storage;
  globalThis.performance = {
    now: () => monotonicNow,
    timeOrigin: 1_800_000_000_000,
    getEntriesByType: () => [{ startTime: 0 }]
  };
  globalThis.crypto ??= { randomUUID: () => 'test-operation-id' };

  const importStart = profileModule.beginStartupProfilePage('save-manager');
  assert.equal(importStart, null, 'manager prelude must not start an import measurement before file selection');
  const operationId = profileModule.beginStartupProfileImport('save-manager-import');
  assert.equal(profileModule.beginStartupProfileImport('save-manager-import', { source: 'staged-input' }), operationId,
    'a staged import page reuses the existing operation instead of replacing its ID');
  profileModule.recordStartupProfilePhase('file-copy', 27, { count: 1, bytes: 4096 });
  profileModule.markStartupProfileNavigation('title-navigation-requested');

  monotonicNow = 40;
  globalThis.performance = {
    now: () => monotonicNow,
    timeOrigin: 1_800_000_012_000,
    getEntriesByType: () => [{ startTime: 0 }]
  };
  assert.equal(profileModule.beginStartupProfilePage('title-after-import'), operationId);
  profileModule.recordStartupProfilePhase('title-process-initialize', 310, { calls: 1 });
  profileModule.finishStartupProfile('title-input-ready');

  const profile = JSON.parse(profileModule.getStartupProfileJson());
  assert.equal(profile.operationId, operationId);
  assert.equal(profile.pages.length, 2);
  assert.notEqual(profile.pages[0].pageId, profile.pages[1].pageId);
  assert.notEqual(profile.pages[0].pageTimeOriginMs, profile.pages[1].pageTimeOriginMs);
  assert.equal(profile.events.filter(event => event.name === 'page-entered').length, 2);
  assert.equal(profile.phases.filter(phase => phase.name === 'file-copy').length, 1);
  assert.equal(profile.completed, true);
  assert.equal(profile.completion, 'title-input-ready');
  assert.equal(profile.startedAtClock, 'Date.now-cross-page-wall-clock');
  assert.equal(profile.elapsedClock, 'Date.now-cross-page-approximate');
});

test('startup page summary separates package HTTP resource timing from application cache metrics', () => {
  const storage = new MemoryStorage();
  globalThis.sessionStorage = storage;
  globalThis.performance = {
    now: () => 300,
    timeOrigin: 1_800_000_000_000,
    getEntriesByType: type => type === 'navigation' ? [{ startTime: 0 }] : [
      { name: 'https://example.test/p1b-data/a.zip', transferSize: 1200, encodedBodySize: 1000, duration: 90 },
      { name: 'https://example.test/p1b-data/b.zip', transferSize: 0, encodedBodySize: 2000, duration: 35 },
      { name: 'https://example.test/app.css', transferSize: 500, encodedBodySize: 400, duration: 12 }
    ]
  };
  profileModule.beginStartupProfilePage('normal-startup');
  profileModule.setStartupProfilePageSummary({ packageCacheHits: 2, packageCacheMisses: 0 });

  const profile = JSON.parse(profileModule.getStartupProfileJson());
  assert.deepEqual(profile.pages[0].summary.packageHttpResourceTiming, {
    requestCount: 2,
    transferBytes: 1200,
    encodedBodyBytes: 3000,
    durationMs: 125,
    zeroTransferCount: 1
  });
  assert.equal(profile.pages[0].summary.packageCacheHits, 2);
  assert.equal(profile.pages[0].summary.packageCacheMisses, 0);
});

test('normal startup uses one page-local monotonic clock from navigation start', () => {
  const storage = new MemoryStorage();
  const originalDateNow = Date.now;
  let monotonicNow = 120;
  globalThis.sessionStorage = storage;
  globalThis.performance = {
    now: () => monotonicNow,
    timeOrigin: 2_000,
    getEntriesByType: type => type === 'navigation' ? [{ startTime: 0 }] : []
  };
  Date.now = () => 9_999;
  try {
    profileModule.beginStartupProfilePage('normal-startup');
    monotonicNow = 620;
    profileModule.finishStartupProfile('title-input-ready');
    const profile = JSON.parse(profileModule.getStartupProfileJson());
    assert.equal(profile.elapsedClock, 'performance.timeOrigin+performance.now');
    assert.equal(profile.wallElapsedMs, 620);
    assert.equal(profile.pageMonotonicElapsedMs, 620);
  } finally {
    Date.now = originalDateNow;
  }
});

test('active-runtime import links runtime, validation, and title pages under one operation id', () => {
  const storage = new MemoryStorage();
  let monotonicNow = 100;
  let timeOrigin = 3_000;
  globalThis.sessionStorage = storage;
  globalThis.performance = {
    now: () => monotonicNow,
    get timeOrigin() { return timeOrigin; },
    getEntriesByType: type => type === 'navigation' ? [{ startTime: 0 }] : []
  };
  profileModule.beginStartupProfilePage('normal-startup');
  profileModule.finishStartupProfile('title-input-ready');

  const operationId = profileModule.beginStartupProfileImport('runtime-import-to-title');
  profileModule.recordStartupProfilePhase('cache-stage-import-files', 12, { count: 1, bytes: 32_768 });
  profileModule.markStartupProfileNavigation('runtime-to-save-manager');
  timeOrigin += 4_000;
  monotonicNow = 20;
  assert.equal(profileModule.beginStartupProfilePage('save-manager-staged-import'), operationId);
  profileModule.recordStartupProfilePhase('validation-runtime-bootstrap-inclusive', 2_000);
  profileModule.markStartupProfileNavigation('manager-to-title');
  timeOrigin += 3_000;
  monotonicNow = 30;
  assert.equal(profileModule.beginStartupProfilePage('title-after-import'), operationId);
  profileModule.recordStartupProfilePhase('title-runtime-bootstrap-inclusive', 2_100);
  profileModule.finishStartupProfile('title-input-ready');

  const profile = JSON.parse(profileModule.getStartupProfileJson());
  assert.equal(profile.operationId, operationId);
  assert.deepEqual(profile.pages.map(page => page.route), [
    'runtime-import-to-title', 'save-manager-staged-import', 'title-after-import'
  ]);
  assert.equal(profile.events.filter(event => event.name === 'page-entered').length, 3);
  assert.notEqual(profile.pages[0].pageTimeOriginMs, profile.pages[1].pageTimeOriginMs);
  assert.equal(profile.elapsedClock, 'Date.now-cross-page-approximate');
});
