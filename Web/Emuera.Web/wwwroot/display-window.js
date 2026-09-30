const bindings = new Map();

function element(elementId) {
  const value = document.getElementById(elementId);
  if (!value) throw new Error(`display window element not found: ${elementId}`);
  return value;
}

function anchor(host) {
  const hostTop = host.getBoundingClientRect().top;
  const row = Array.from(host.querySelectorAll('.game-line[data-line-id]'))
    .find(value => value.getBoundingClientRect().bottom >= hostTop);
  if (!row) return { anchorLineId: 0, anchorOffsetPx: 0 };
  return {
    anchorLineId: Number(row.dataset.lineId),
    anchorOffsetPx: row.getBoundingClientRect().top - hostTop
  };
}

export function readViewport(elementId) {
  const host = element(elementId);
  return {
    scrollTop: host.scrollTop,
    clientHeight: host.clientHeight,
    scrollHeight: host.scrollHeight,
    nearBottom: host.scrollHeight - host.clientHeight - host.scrollTop <= 32,
    ...anchor(host)
  };
}

function schedule(binding) {
  if (binding.frame) return;
  binding.frame = requestAnimationFrame(() => {
    binding.frame = 0;
    binding.dotNetRef.invokeMethodAsync('OnDisplayWindowScroll', readViewport(binding.elementId));
  });
}

export function attach(elementId, dotNetRef) {
  detach(elementId);
  const binding = { elementId, dotNetRef, frame: 0 };
  binding.listener = () => schedule(binding);
  element(elementId).addEventListener('scroll', binding.listener, { passive: true });
  bindings.set(elementId, binding);
  schedule(binding);
}

export function measureRendered(elementId, lineIds) {
  if (!Array.isArray(lineIds) || lineIds.length === 0) return [];
  const requested = new Set(lineIds);
  return Array.from(element(elementId).querySelectorAll('.game-line[data-line-id]'))
    .filter(row => requested.has(Number(row.dataset.lineId)))
    .map(row => ({ lineId: Number(row.dataset.lineId), height: row.getBoundingClientRect().height }))
    .filter(value => Number.isFinite(value.lineId) && Number.isFinite(value.height));
}

export function scrollToBottom(elementId) {
  const host = element(elementId);
  host.scrollTop = host.scrollHeight;
}

let oneInputFrame;
// One owner for the last OneInput DOM state. Only a consecutive movement waits if
// its frame callback is still pending; independent taps are never queued here.
export function oneInputPresentation(session, request, register = false) {
  if (!register) return oneInputFrame?.session === session && oneInputFrame.request === request
    ? oneInputFrame.promise : !document.hidden;
  if (oneInputFrame?.session === session && oneInputFrame.request === request) return;
  oneInputFrame?.finish(false);
  let resolve, frame;
  const promise = new Promise(done => { resolve = done; });
  const hidden = () => { if (document.hidden) finish(false); };
  const finish = visible => {
    cancelAnimationFrame(frame);
    document.removeEventListener('visibilitychange', hidden);
    resolve(visible);
    if (!visible && oneInputFrame?.promise === promise) oneInputFrame = undefined;
  };
  oneInputFrame = { session, request, promise, finish };
  if (document.hidden) { finish(false); return; }
  document.addEventListener('visibilitychange', hidden);
  frame = requestAnimationFrame(() => finish(!document.hidden
    && !!document.activeElement?.closest('#game-screen')));
}

// Two frame callbacks give the previous DOM state a frame opportunity, not a paint guarantee.
// Used only before held OneInput movement; hidden tabs release the handoff without a timer.
export function presentationOpportunity() {
  if (document.hidden) return Promise.resolve(false);
  return new Promise(resolve => {
    let frame;
    const done = visible => { cancelAnimationFrame(frame); document.removeEventListener('visibilitychange', hidden); resolve(visible); };
    const hidden = () => { if (document.hidden) done(false); };
    document.addEventListener('visibilitychange', hidden);
    frame = requestAnimationFrame(() => { frame = requestAnimationFrame(() => done(!document.hidden)); });
  });
}

export function restoreAnchor(elementId, lineId, offsetPx) {
  const host = element(elementId);
  const row = Array.from(host.querySelectorAll('.game-line[data-line-id]'))
    .find(value => Number(value.dataset.lineId) === lineId);
  if (row) host.scrollTop += row.getBoundingClientRect().top - host.getBoundingClientRect().top - offsetPx;
}

export function detach(elementId) {
  const binding = bindings.get(elementId);
  if (!binding) return;
  element(elementId).removeEventListener('scroll', binding.listener);
  if (binding.frame) cancelAnimationFrame(binding.frame);
  bindings.delete(elementId);
}
