// Host shortcuts never submit a game value directly. The App owns input acceptance.
let dispose;
let epoch = 0;
let composing = false;
const heldKeys = new Map();
let mouseButtons = 0;
let modifiers = { shift: false, control: false, alt: false };
function clearKeyState() {
  heldKeys.clear(); mouseButtons = 0;
  modifiers = { shift: false, control: false, alt: false };
}
function modifierState(event) {
  modifiers = { shift: !!event.shiftKey, control: !!event.ctrlKey, alt: !!event.altKey };
}
const modifierCodes = { ShiftLeft: 160, ShiftRight: 161, ControlLeft: 162, ControlRight: 163, AltLeft: 164, AltRight: 165 };
function recordKey(event, down) {
  if (!down) heldKeys.delete(event.code);
  if (!current(epoch) || event.isComposing || event.keyCode === 229) return;
  // A repeat delivered after blur is not a new physical press.
  if (down && event.repeat && !heldKeys.has(event.code)) return;
  modifierState(event);
  const value = modifierCodes[event.code] ?? event.keyCode;
  if (down && value > 0 && value < 256) heldKeys.set(event.code, value);
}
function recordMouse(event) {
  if (event.type === 'mouseup') mouseButtons &= event.buttons;
  if (!document.hidden && document.hasFocus() && !composing && event.target?.closest?.('#game-surface')) {
    mouseButtons = event.buttons;
    modifierState(event);
  }
}
// Native GETKEY reads the current state without consuming it. No game input is sent here.
export function getKeyState(keycode) {
  if (!current(epoch)) { clearKeyState(); return 0; }
  if (!Number.isInteger(keycode) || keycode < 0 || keycode > 255) return 0;
  const mouseMask = { 1: 1, 2: 2, 4: 4, 5: 8, 6: 16 }[keycode];
  let down = mouseMask !== undefined && !!(mouseButtons & mouseMask);
  for (const value of heldKeys.values())
    if (value === keycode || keycode === 16 && (value === 160 || value === 161)
      || keycode === 17 && (value === 162 || value === 163) || keycode === 18 && (value === 164 || value === 165)) down = true;
  if (keycode === 16) down ||= modifiers.shift;
  if (keycode === 17) down ||= modifiers.control;
  if (keycode === 18) down ||= modifiers.alt;
  return down ? 0x8000 : 0;
}
export function shortcut(event) {
  if (event.metaKey || event.altKey) return null;
  const f = /^F([1-9]|1[0-2])$/.exec(event.code);
  if (f && !event.ctrlKey) return { action: event.shiftKey ? 'register' : 'recall', index: Number(f[1]) - 1 };
  const digit = /^(?:Digit|Numpad)([0-9])$/.exec(event.code);
  return digit && (!event.code.startsWith('Numpad') || event.key === digit[1]) && event.ctrlKey && !event.shiftKey ? { action: 'group', index: Number(digit[1]) } : null;
}
export function gameKey(event) {
  return /^(?:F(?:[1-9]|1[0-2])|Key[A-Z]|Digit[0-9]|Numpad[0-9]|Shift(?:Left|Right)|Control(?:Left|Right)|Alt(?:Left|Right)|Enter|NumpadEnter|Escape|Space|Arrow(?:Left|Right|Up|Down)|Backspace|Tab|Delete|Insert|Home|End|PageUp|PageDown)$/.test(event.code) || event.key.length === 1;
}
function activeTarget(target) {
  if (!target || target === document.body) return true;
  if (target.id === 'runtime-input' || target.id === 'runtime-submit') return true;
  if (target.closest?.('input,textarea,select,[contenteditable="true"],#host-toolbar')) return false;
  return !!target.closest?.('#game-surface');
}
export function current(token) {
  return token === epoch && !document.hidden && document.hasFocus() && activeTarget(document.activeElement) && !composing;
}
export function attach(host) {
  detach();
  const clear = (keepKeys = false) => { epoch++; composing = false; if (!keepKeys) clearKeyState(); host.invokeMethodAsync('ClearKeyboardFocus').catch(() => {}); };
  const keydown = event => {
    recordKey(event, true);
    if (!current(epoch) || event.isComposing || event.keyCode === 229) return;
    const state = document.querySelector('#p1c2-status')?.dataset;
    if (!state || state.keyMacroReady !== 'true') return;
    const primitive = state.pendingKind === 'mousekey';
    const command = shortcut(event);
    if (!(primitive && !event.metaKey && gameKey(event)) && !(state.keyMacroEnabled === 'true' && command)) {
      // The existing game-screen handler owns ordinary WAIT/AnyKey delivery.
      // Cancel only keys that handler accepts, not every key on the element.
      const waitingKey = !event.ctrlKey && !event.altKey && !event.metaKey && event.target?.closest?.('#game-screen')
        && (state.pendingKind === 'enter' && event.key === 'Enter'
          || state.pendingKind === 'anykey' && gameKey(event) && event.code !== 'Tab' && event.code !== 'Backspace'
            && !/^(?:Shift|Control|Alt|Meta)/.test(event.code));
      if (waitingKey && event.cancelable) event.preventDefault();
      return;
    }
    if (event.cancelable) event.preventDefault();
    event.stopImmediatePropagation();
    if (event.repeat && !primitive) return; // register/recall/group is one physical press
    const token = epoch;
    host.invokeMethodAsync('DispatchHostKey', event.code, event.key, event.ctrlKey, event.shiftKey,
      event.altKey, event.metaKey, event.repeat, primitive ? 'game' : command.action,
      primitive ? -1 : command.index, document.getElementById('runtime-input')?.value ?? '',
      Number(state.requestId || 0), Number(state.sessionGeneration || 0), token).catch(() => {});
  };
  const keyup = event => { recordKey(event, false); host.invokeMethodAsync('ReleaseHostKey', event.code, event.key, event.ctrlKey, event.shiftKey, event.altKey).catch(() => {}); };
  // Removing/replacing the game input can focus BODY without leaving the page.
  // Real window/tab loss still clears held state via blur/visibilitychange.
  const focus = event => clear(activeTarget(event.relatedTarget ?? event.target));
  const start = () => { composing = true; epoch++; clearKeyState(); };
  const end = () => { composing = false; };
  const visibility = () => { if (document.hidden) clear(); };
  document.addEventListener('keydown', keydown, true);
  document.addEventListener('keyup', keyup, true);
  document.addEventListener('focusout', focus, true);
  document.addEventListener('compositionstart', start, true);
  document.addEventListener('compositionend', end, true);
  document.addEventListener('visibilitychange', visibility);
  const blur = () => clear();
  document.addEventListener('mousedown', recordMouse, true);
  document.addEventListener('mouseup', recordMouse, true);
  document.addEventListener('click', recordMouse, true);
  window.addEventListener('blur', blur);
  dispose = () => {
    document.removeEventListener('keydown', keydown, true); document.removeEventListener('keyup', keyup, true);
    document.removeEventListener('focusout', focus, true); document.removeEventListener('compositionstart', start, true);
    document.removeEventListener('compositionend', end, true); document.removeEventListener('visibilitychange', visibility);
    document.removeEventListener('mousedown', recordMouse, true); document.removeEventListener('mouseup', recordMouse, true);
    document.removeEventListener('click', recordMouse, true);
    window.removeEventListener('blur', blur); clear();
  };
}
export function detach() { dispose?.(); dispose = null; epoch++; }
export function focusInput() {
  const e = document.getElementById('runtime-input');
  if (e) { e.focus({ preventScroll: true }); e.setSelectionRange(e.value.length, e.value.length); }
}
const storageKey = (game, profile) => 'emuera-key-macros-v1:' + JSON.stringify([game, profile]);
const defaultNames = () => Array.from({ length: 10 }, (_, g) => `マクログループ${g}に設定`);
export function load(game, profile, storage = null) {
  try {
    storage ??= globalThis.localStorage;
    const raw = storage.getItem(storageKey(game, profile));
    if (raw === null) return { slots: Array(120).fill(''), groupNames: defaultNames(), group: 0, message: '' };
    if (raw.length > 2 * 1024 * 1024) throw Error('設定が大きすぎます');
    const v = JSON.parse(raw);
    if (![1, 2].includes(v.version) || !Number.isInteger(v.group) || v.group < 0 || v.group > 9 || !Array.isArray(v.slots)
      || v.slots.length !== 120 || v.slots.some(s => typeof s !== 'string')
      || v.version === 2 && (!Array.isArray(v.groupNames) || v.groupNames.length !== 10 || v.groupNames.some(s => typeof s !== 'string'))) throw Error('形式が不正です');
    return { slots: v.slots, groupNames: v.version === 1 ? defaultNames() : v.groupNames, group: v.group, message: '' };
  } catch (e) { return { slots: Array(120).fill(''), groupNames: defaultNames(), group: 0, message: 'マクロ設定を読めません。このページ内でのみ使用します: ' + e.message }; }
}
export function replace(game, profile, group, slots, groupNames, storage = null) {
  try {
    storage ??= globalThis.localStorage;
    if (!Number.isInteger(group) || group < 0 || group > 9 || !Array.isArray(slots) || slots.length !== 120
      || slots.some(s => typeof s !== 'string') || !Array.isArray(groupNames) || groupNames.length !== 10
      || groupNames.some(s => typeof s !== 'string')) throw Error('形式が不正です');
    const raw = JSON.stringify({ version: 2, group, slots, groupNames });
    if (raw.length > 2 * 1024 * 1024) throw Error('設定が大きすぎます');
    storage.setItem(storageKey(game, profile), raw);
    return { saved: true, message: '保存しました' };
  } catch (e) { return { saved: false, message: '保存できませんでした: ' + e.message }; }
}
export function save(game, profile, group, slots, storage = null, groupNames = defaultNames()) {
  const result = replace(game, profile, group, slots, groupNames, storage);
  return result.saved ? result.message : '未保存: このページ内では使えますが再読込で失われます。' + result.message;
}
export function downloadMacro(bytes) {
  const url = URL.createObjectURL(new Blob([bytes], { type: 'text/plain' }));
  const anchor = document.createElement('a');
  anchor.href = url; anchor.download = 'macro.txt';
  document.body.appendChild(anchor);
  try { anchor.click(); } finally { anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
}
