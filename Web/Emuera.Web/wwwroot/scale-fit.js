const bindings = new Map();

// DOM layout and Runtime coordinates remain logical CSS pixels. Rects alone
// contain the host transform; browser zoom needs no separate DPR correction.
export function displayScale(element) {
  return element.offsetWidth > 0 ? element.getBoundingClientRect().width / element.offsetWidth || 1 : 1;
}

export function attach() {
  detach();
  const viewport = document.getElementById('game-fit-viewport');
  const slot = document.getElementById('game-fit-slot');
  if (!viewport || !slot) return;
  const update = () => {
    const scale = Math.max(0, Math.min(1, viewport.clientWidth / 1512, viewport.clientHeight / 850));
    slot.style.setProperty('--game-scale', String(scale));
    slot.style.width = `${1512 * scale}px`;
    slot.style.height = `${850 * scale}px`;
    const toolbar = document.getElementById('host-toolbar');
    const status = document.getElementById('host-status');
    if (toolbar && status) {
      const rect = toolbar.getBoundingClientRect();
      const title = toolbar.querySelector('.host-title').getBoundingClientRect();
      const right = toolbar.querySelector('#return-title').getBoundingClientRect();
      status.style.setProperty('--host-status-width', `${Math.max(0, rect.width - 2 * Math.max(title.right - rect.left, rect.right - right.left) - 16)}px`);
    }
  };
  const observer = new ResizeObserver(update);
  observer.observe(viewport);
  const toolbar = document.getElementById('host-toolbar');
  if (toolbar) observer.observe(toolbar);
  bindings.set('host', observer);
  update();
}

export function detach() {
  bindings.get('host')?.disconnect();
  bindings.delete('host');
}
