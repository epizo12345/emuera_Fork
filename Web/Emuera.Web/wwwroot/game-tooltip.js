const bindings = new Map();

export function attach(elementId) {
  detach(elementId);
  const host = document.getElementById(elementId);
  if (!host) throw new Error(`tooltip host not found: ${elementId}`);
  const tip = document.createElement('div');
  tip.id = 'game-tooltip';
  tip.className = 'game-tooltip';
  tip.role = 'tooltip';
  tip.hidden = true;
  document.body.append(tip);
  const state = { host, tip, target: null, showTimer: 0, hideTimer: 0, x: 0, y: 0 };
  const clear = () => {
    clearTimeout(state.showTimer);
    clearTimeout(state.hideTimer);
    state.showTimer = state.hideTimer = 0;
    state.target = null;
    tip.hidden = true;
  };
  const position = () => {
    tip.style.left = `${Math.max(4, Math.min(state.x + 12, innerWidth - tip.offsetWidth - 4))}px`;
    tip.style.top = `${Math.max(4, Math.min(state.y + 18, innerHeight - tip.offsetHeight - 4))}px`;
  };
  const start = (target, x, y) => {
    if (target === state.target) { state.x = x; state.y = y; if (!tip.hidden) position(); return; }
    clear();
    if (!target?.dataset.gameTooltip) return;
    state.target = target;
    state.x = x;
    state.y = y;
    const delay = Math.max(0, Number(host.dataset.tooltipDelay) || 0);
    state.showTimer = setTimeout(() => {
      if (state.target !== target || !host.contains(target)) return;
      tip.textContent = target.dataset.gameTooltip;
      tip.style.setProperty('--tooltip-foreground', host.dataset.tooltipForeground || '#000000');
      tip.style.setProperty('--tooltip-background', host.dataset.tooltipBackground || '#FFFFFF');
      tip.hidden = false;
      position();
      const duration = Math.max(0, Number(host.dataset.tooltipDuration) || 0) || 5000;
      state.hideTimer = setTimeout(() => { tip.hidden = true; }, duration);
    }, delay);
  };
  const targetOf = value => value instanceof Element && host.contains(value)
    ? value.closest('[data-game-tooltip]') : null;
  const over = event => start(targetOf(event.target), event.clientX, event.clientY);
  const move = event => { if (state.target) { state.x = event.clientX; state.y = event.clientY; if (!tip.hidden) position(); } };
  const out = event => { if (state.target && !state.target.contains(event.relatedTarget)) clear(); };
  const focus = event => {
    const target = targetOf(event.target);
    if (target) { const rect = target.getBoundingClientRect(); start(target, rect.left, rect.bottom); }
  };
  const unfocus = event => { if (state.target?.contains(event.target)) clear(); };
  const visibility = () => { if (document.hidden) clear(); };
  const mutation = new MutationObserver(() => { if (state.target && !host.contains(state.target)) clear(); });
  host.addEventListener('pointerover', over);
  host.addEventListener('pointermove', move, { passive: true });
  host.addEventListener('pointerout', out);
  host.addEventListener('focusin', focus);
  host.addEventListener('focusout', unfocus);
  host.addEventListener('scroll', clear, { passive: true });
  window.addEventListener('scroll', clear, { passive: true });
  window.addEventListener('blur', clear);
  document.addEventListener('visibilitychange', visibility);
  mutation.observe(host, { childList: true, subtree: true });
  bindings.set(elementId, { host, tip, clear, over, move, out, focus, unfocus, visibility, mutation });
}

export function detach(elementId) {
  const state = bindings.get(elementId);
  if (!state) return;
  const { host, tip, clear, over, move, out, focus, unfocus, visibility, mutation } = state;
  clear();
  mutation.disconnect();
  host.removeEventListener('pointerover', over);
  host.removeEventListener('pointermove', move);
  host.removeEventListener('pointerout', out);
  host.removeEventListener('focusin', focus);
  host.removeEventListener('focusout', unfocus);
  host.removeEventListener('scroll', clear);
  window.removeEventListener('scroll', clear);
  window.removeEventListener('blur', clear);
  document.removeEventListener('visibilitychange', visibility);
  tip.remove();
  bindings.delete(elementId);
}
