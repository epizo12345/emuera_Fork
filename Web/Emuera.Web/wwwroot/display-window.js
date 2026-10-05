import { displayScale } from './scale-fit.js';
const bindings = new Map();

function positionPaint(binding, followTail = false) {
  const host = element(binding.elementId);
  const slot = host.querySelector('.game-window-paint-slot');
  const paint = slot?.querySelector(':scope > .game-window-paint');
  const surface = host.closest('.game-surface');
  if (!paint || !surface) return;
  if (binding.paint !== paint) {
    if (binding.paint) binding.resizeObserver?.unobserve(binding.paint);
    binding.paint = paint;
    binding.resizeObserver?.observe(paint);
  }
  // The placeholder keeps native scroll geometry. Fixed descendants still use
  // the existing transformed surface; this node introduces no transform.
  const height = paint.offsetHeight;
  const heightStyle = `${height}px`;
  if (slot.style.height !== heightStyle) slot.style.height = heightStyle;
  if (followTail) host.scrollTop = host.scrollHeight;
  const scale = displayScale(host), rect = slot.getBoundingClientRect(), origin = surface.getBoundingClientRect();
  paint.style.top = `${(rect.top - origin.top) / scale}px`;
  paint.style.left = `${(rect.left - origin.left) / scale}px`;
  paint.style.width = `${slot.offsetWidth}px`;
  paint.classList.add('is-surface-positioned');
}

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
    anchorOffsetPx: (row.getBoundingClientRect().top - hostTop) / displayScale(host)
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
  const displayStructureGeneration = Number(element(binding.elementId).dataset.displayStructureGeneration || 0);
  binding.frame = requestAnimationFrame(() => {
    binding.frame = 0;
    binding.dotNetRef.invokeMethodAsync('OnDisplayWindowScroll', {
      ...readViewport(binding.elementId),
      displayStructureGeneration
    });
  });
}

export function attach(elementId, dotNetRef) {
  detach(elementId);
  const host = element(elementId);
  const binding = { elementId, dotNetRef, frame: 0, followTail: host.querySelector('.game-window-paint-slot')?.dataset.followTail === 'true' };
  binding.listener = () => {
    binding.followTail = host.scrollHeight - host.clientHeight - host.scrollTop <= 32;
    positionPaint(binding);
    schedule(binding);
  };
  binding.wheel = event => {
    // A surface-fixed descendant is outside the browser's native scroll chain.
    // Preserve the same scroller/units; blank space still uses native default
    // scrolling. Ctrl+wheel belongs to browser zoom.
    if (event.ctrlKey || event.defaultPrevented || !event.cancelable || !event.target.closest?.('.game-window-paint.is-surface-positioned')) return;
    const factor = event.deltaMode === 2 ? host.clientHeight : event.deltaMode === 1
      ? parseFloat(getComputedStyle(host.querySelector('.game-line') || host).lineHeight) || 18 : 1;
    const top = host.scrollTop, left = host.scrollLeft;
    host.scrollTop += event.deltaY * factor;
    host.scrollLeft += event.deltaX * factor;
    if (host.scrollTop !== top || host.scrollLeft !== left) {
      event.preventDefault();
      binding.listener();
    }
  };
  element(elementId).addEventListener('scroll', binding.listener, { passive: true });
  host.addEventListener('wheel', binding.wheel, { passive: false });
  binding.observer = new MutationObserver(records => {
    // Ignore our own placeholder/paint style writes; no update loop.
    if (!records.some(r => r.type === 'childList' || r.target.classList?.contains('game-window-spacer') || r.attributeName === 'data-follow-tail')) return;
    const slot = host.querySelector('.game-window-paint-slot');
    if (records.some(r => r.attributeName === 'data-follow-tail')) binding.followTail = slot?.dataset.followTail === 'true';
    positionPaint(binding, binding.followTail);
  });
  binding.observer.observe(host, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'data-follow-tail'] });
  binding.resizeObserver = new ResizeObserver(() => positionPaint(binding, binding.followTail));
  binding.resizeObserver.observe(host);
  bindings.set(elementId, binding);
  positionPaint(binding, binding.followTail);
  schedule(binding);
}

export function measureRendered(elementId, lineIds) {
  const binding = bindings.get(elementId);
  if (binding) positionPaint(binding);
  if (!Array.isArray(lineIds) || lineIds.length === 0) return [];
  const requested = new Set(lineIds);
  const host = element(elementId);
  const scale = displayScale(host);
  return Array.from(host.querySelectorAll('.game-line[data-line-id]'))
    .filter(row => requested.has(Number(row.dataset.lineId)))
    .map(row => ({ lineId: Number(row.dataset.lineId), height: row.getBoundingClientRect().height / scale }))
    .filter(value => Number.isFinite(value.lineId) && Number.isFinite(value.height));
}

export function scrollToBottom(elementId) {
  const host = element(elementId);
  host.scrollTop = host.scrollHeight;
  const binding = bindings.get(elementId);
  if (binding) { binding.followTail = true; positionPaint(binding, true); }
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
  if (row) {
    host.scrollTop += (row.getBoundingClientRect().top - host.getBoundingClientRect().top) / displayScale(host) - offsetPx;
    const binding = bindings.get(elementId);
    if (binding) positionPaint(binding);
  }
}

export function detach(elementId) {
  const binding = bindings.get(elementId);
  if (!binding) return;
  element(elementId).removeEventListener('scroll', binding.listener);
  element(elementId).removeEventListener('wheel', binding.wheel);
  binding.observer.disconnect();
  binding.resizeObserver.disconnect();
  if (binding.frame) cancelAnimationFrame(binding.frame);
  bindings.delete(elementId);
}
