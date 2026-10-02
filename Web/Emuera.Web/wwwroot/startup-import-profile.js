const storageKey = 'emuera-web-startup-profile-v1';
const maxEvents = 512;
let currentPageId = null;

function readProfile() {
  try { return JSON.parse(sessionStorage.getItem(storageKey) || 'null'); }
  catch { return null; }
}

function saveProfile(profile) {
  try { sessionStorage.setItem(storageKey, JSON.stringify(profile)); return true; }
  catch { return false; }
}

function event(profile, name, detail = null) {
  profile.events.push({
    name,
    pageId: currentPageId,
    pageLocalMonotonicMs: performance.now(),
    pageTimeOriginMs: performance.timeOrigin,
    wallClockMs: Date.now(),
    detail
  });
  if (profile.events.length > maxEvents) profile.events.splice(0, profile.events.length - maxEvents);
}

function createProfile(scenario, startedAtNavigation) {
  return {
    schemaVersion: 1,
    operationId: crypto.randomUUID(),
    scenario,
    started: startedAtNavigation,
    active: startedAtNavigation,
    completed: false,
    awaitingNavigation: false,
    startedAtWallClockMs: null,
    startedAtClock: null,
    startedPageId: null,
    startedPageLocalMonotonicMs: null,
    pages: [],
    events: [],
    phases: [],
    inputFiles: [],
    summary: null
  };
}

function addPage(profile, route, measurementStart = false, context = null) {
  currentPageId = crypto.randomUUID();
  const navigation = performance.getEntriesByType('navigation')[0];
  const page = {
    pageId: currentPageId,
    route,
    pageTimeOriginMs: performance.timeOrigin,
    navigationStartLocalMonotonicMs: navigation?.startTime ?? 0,
    appEntryLocalMonotonicMs: performance.now(),
    appEntryWallClockMs: Date.now(),
    measurementStart,
    context
  };
  profile.pages.push(page);
  event(profile, 'page-entered', { route, measurementStart });
  return page;
}

function packageResourceTiming() {
  const totals = { requestCount: 0, transferBytes: 0, encodedBodyBytes: 0, durationMs: 0, zeroTransferCount: 0 };
  try {
    for (const item of performance.getEntriesByType('resource')) {
      if (!new URL(item.name, globalThis.location?.href ?? 'http://localhost/').pathname.startsWith('/p1b-data/')) continue;
      totals.requestCount++;
      totals.transferBytes += item.transferSize ?? 0;
      totals.encodedBodyBytes += item.encodedBodySize ?? 0;
      totals.durationMs += item.duration ?? 0;
      if ((item.transferSize ?? 0) === 0) totals.zeroTransferCount++;
    }
  } catch { /* Timing API is diagnostic-only and must not affect startup. */ }
  return totals;
}

export function beginStartupProfilePage(route, context = null) {
  let profile = readProfile();
  if (profile?.active && profile.awaitingNavigation) {
    profile.awaitingNavigation = false;
    addPage(profile, route, false, context);
    saveProfile(profile);
    return profile.operationId;
  }

  if (route === 'save-manager') {
    profile = createProfile(null, false);
    addPage(profile, route, false, context);
    event(profile, 'save-manager-prelude');
    saveProfile(profile);
    return null;
  }

  profile = createProfile(route === 'title-after-import' ? 'title-startup-after-import' : 'normal-startup', true);
  const page = addPage(profile, route, true, context);
  profile.startedAtWallClockMs = performance.timeOrigin + page.navigationStartLocalMonotonicMs;
  profile.startedAtClock = 'performance.timeOrigin+navigationStart';
  profile.startedPageId = page.pageId;
  profile.startedPageLocalMonotonicMs = page.navigationStartLocalMonotonicMs;
  event(profile, 'page-measurement-started', { clock: 'page-monotonic-from-navigation-start' });
  saveProfile(profile);
  return profile.operationId;
}

export function beginStartupProfileImport(scenario, input = null) {
  let profile = readProfile();
  if (profile?.active && profile.started) {
    event(profile, 'import-files-delivered', input);
    saveProfile(profile);
    return profile.operationId;
  }
  if (!profile || profile.completed) {
    profile = createProfile(scenario, false);
    addPage(profile, scenario, false, { measurementStartsAt: 'file-selection-delivered' });
    profile.started = true;
  } else {
    profile.scenario = scenario;
    profile.started = true;
    profile.active = true;
  }
  const page = profile.pages.find(item => item.pageId === currentPageId);
  const localStart = performance.now();
  const wallStart = Date.now();
  profile.startedAtWallClockMs = wallStart;
  profile.startedAtClock = 'Date.now-cross-page-wall-clock';
  profile.startedPageId = currentPageId;
  profile.startedPageLocalMonotonicMs = localStart;
  if (page) {
    page.measurementStart = true;
    page.measurementStartLocalMonotonicMs = localStart;
    page.measurementStartWallClockMs = wallStart;
  }
  profile.active = true;
  event(profile, 'file-selection-delivered', input);
  saveProfile(profile);
  return profile.operationId;
}

export function recordStartupProfilePhase(name, durationMs, detail = null, classification = 'application') {
  const profile = readProfile();
  if (!profile) return false;
  profile.phases.push({
    name,
    pageId: currentPageId,
    durationMs: Number.isFinite(durationMs) ? durationMs : null,
    clock: 'dotnet-stopwatch',
    classification,
    preMeasurement: !profile.started,
    detail
  });
  event(profile, 'phase-recorded', { name, classification });
  return saveProfile(profile);
}

export function recordStartupProfileEvent(name, detail = null) {
  const profile = readProfile();
  if (!profile) return false;
  event(profile, name, detail);
  return saveProfile(profile);
}

export function setStartupProfileInputFiles(files, persistedGlobalExists) {
  const profile = readProfile();
  if (!profile) return false;
  profile.inputFiles = Array.isArray(files) ? files : [];
  profile.persistedGlobalExistsBeforeImport = !!persistedGlobalExists;
  return saveProfile(profile);
}

export function setStartupProfilePageSummary(summary) {
  const profile = readProfile();
  if (!profile) return false;
  const page = profile.pages.find(item => item.pageId === currentPageId);
  if (!page) return false;
  page.summary = { ...summary, packageHttpResourceTiming: packageResourceTiming() };
  return saveProfile(profile);
}

export function markStartupProfileNavigation(name, detail = null) {
  const profile = readProfile();
  if (!profile) return false;
  event(profile, name, detail);
  const page = profile.pages.find(item => item.pageId === currentPageId);
  if (page && page.pageExitLocalMonotonicMs == null) {
    page.pageExitLocalMonotonicMs = performance.now();
    page.pageExitWallClockMs = Date.now();
    page.pageExitReason = name;
  }
  profile.awaitingNavigation = true;
  return saveProfile(profile);
}

export function finishStartupProfile(completion, summary = null) {
  const profile = readProfile();
  if (!profile || !profile.started || !profile.active) return false;
  event(profile, completion, summary);
  const page = profile.pages.find(item => item.pageId === currentPageId);
  if (page) {
    page.pageExitLocalMonotonicMs = performance.now();
    page.pageExitWallClockMs = Date.now();
    page.pageExitReason = completion;
  }
  profile.completed = true;
  profile.active = false;
  profile.awaitingNavigation = false;
  profile.completion = completion;
  const samePage = profile.startedPageId === currentPageId;
  profile.completedAtWallClockMs = samePage && profile.startedAtClock === 'performance.timeOrigin+navigationStart'
    ? performance.timeOrigin + performance.now() : Date.now();
  profile.elapsedClock = samePage && profile.startedAtClock === 'performance.timeOrigin+navigationStart'
    ? 'performance.timeOrigin+performance.now' : 'Date.now-cross-page-approximate';
  profile.wallElapsedMs = profile.startedAtWallClockMs == null
    ? null : Math.max(0, profile.completedAtWallClockMs - profile.startedAtWallClockMs);
  profile.pageMonotonicElapsedMs = samePage
    ? Math.max(0, performance.now() - profile.startedPageLocalMonotonicMs) : null;
  profile.humanDecisionWaitMs = profile.phases
    .filter(phase => phase.classification === 'user-decision')
    .reduce((total, phase) => total + (phase.durationMs ?? 0), 0);
  profile.wallElapsedMinusUserDecisionMs = profile.wallElapsedMs == null
    ? null : Math.max(0, profile.wallElapsedMs - profile.humanDecisionWaitMs);
  profile.summary = summary == null ? null : { ...summary, packageHttpResourceTiming: packageResourceTiming() };
  return saveProfile(profile);
}

export function getStartupProfileJson() {
  const profile = readProfile();
  return profile ? JSON.stringify(profile, null, 2) : '';
}
