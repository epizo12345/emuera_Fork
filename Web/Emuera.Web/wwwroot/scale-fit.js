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
  const frame = document.querySelector('.game-frame');
  if (!viewport || !slot || !frame) return;
  const update = () => {
    // CSS defines the logical surface plus input area. offset sizes exclude
    // the display transform; toolbar space is already excluded by the viewport.
    const width = frame.offsetWidth;
    const height = frame.offsetHeight;
    if (width <= 0 || height <= 0) return;
    const scale = Math.max(0, Math.min(1, viewport.clientWidth / width, viewport.clientHeight / height));
    slot.style.setProperty('--game-scale', String(scale));
    slot.style.width = `${width * scale}px`;
    slot.style.height = `${height * scale}px`;
    const toolbar = document.getElementById('host-toolbar');
    const status = document.getElementById('host-status');
    if (toolbar && status) {
      const rect = toolbar.getBoundingClientRect();
      const title = toolbar.querySelector('.host-title').getBoundingClientRect();
      const right = toolbar.querySelector('.host-macro-control, #return-title').getBoundingClientRect();
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
