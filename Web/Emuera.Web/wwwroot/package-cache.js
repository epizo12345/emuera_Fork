const CACHE_NAME = 'emuera-game-package-cache-v1';
const CACHE_PATH = '/.emuera-game-package-cache/v1/';
const streams = new Map();

function valid(value, expression, label) {
  if (typeof value !== 'string' || !expression.test(value)) throw new Error(`${label} is invalid`);
  return value;
}

function keyFor(gameId, manifestVersion, pack, identity) {
  valid(gameId, /^[a-z0-9._-]{1,64}$/i, 'gameId');
  valid(pack, /^[a-z0-9._-]+\.zip$/i, 'pack');
  valid(identity, /^[a-f0-9]{64}$/i, 'package identity');
  if (!Number.isInteger(manifestVersion) || manifestVersion < 1 || manifestVersion > 1_000_000) throw new Error('manifest version is invalid');
  return new Request(new URL(`${CACHE_PATH}${encodeURIComponent(gameId)}/${encodeURIComponent(pack)}/${identity}`, location.origin));
}

async function cache() {
  if (!globalThis.caches) throw new Error('Cache Storage is unavailable');
  return await caches.open(CACHE_NAME);
}

function sourceUrl(value, pack) {
  const url = new URL(value, location.origin);
  if (url.origin !== location.origin || !url.pathname.endsWith(`/p1b-data/${pack}`)) throw new Error('package source must be a same-origin p1b-data URL');
  return url.href;
}

export async function openPack(gameId, manifestVersion, pack, identity, source, forceNetwork = false) {
  const key = keyFor(gameId, manifestVersion, pack, identity);
  const store = await cache();
  let response = forceNetwork ? undefined : await store.match(key);
  const cacheHit = !!response;
  let persisted = cacheHit;
  let cacheError = '';
  if (!response) {
    response = await fetch(sourceUrl(source, pack), { cache: 'no-store', credentials: 'same-origin' });
    if (!response.ok || !response.body) throw new Error(`package fetch failed: ${pack} HTTP ${response.status}`);
    try {
      await store.put(key, response.clone());
      persisted = true;
      if (navigator.storage?.persist) await navigator.storage.persist();
    } catch (error) {
      persisted = false;
      cacheError = `${error?.name ?? 'CacheError'}: ${error?.message ?? String(error)}`;
    }
  }
  if (!response.body) throw new Error(`package body is unavailable: ${pack}`);
  const token = crypto.randomUUID();
  streams.set(token, await response.blob());
  return { token, cacheHit, persisted, cacheError, byteLength: Number(response.headers.get('content-length') ?? -1), cacheKey: key.url };
}

export function openPackStream(token) {
  const stream = streams.get(token);
  streams.delete(token);
  if (!stream) throw new Error('package stream is unavailable');
  return stream;
}

export async function invalidatePack(gameId, manifestVersion, pack, identity) {
  return await (await cache()).delete(keyFor(gameId, manifestVersion, pack, identity));
}

export async function reconcile(gameId, manifestVersion, activePacks) {
  valid(gameId, /^[a-z0-9._-]{1,64}$/i, 'gameId');
  const active = new Set(activePacks.map(value => keyFor(gameId, manifestVersion, value.pack ?? value.Pack, value.identity ?? value.Identity).url));
  const store = await cache();
  let removed = 0;
  for (const key of await store.keys()) {
    const url = new URL(key.url);
    if (url.pathname.startsWith(`${CACHE_PATH}${encodeURIComponent(gameId)}/`) && !active.has(key.url)) {
      if (await store.delete(key)) removed++;
    }
  }
  const estimate = navigator.storage?.estimate ? await navigator.storage.estimate() : {};
  return { removed, usage: Number(estimate.usage ?? -1), quota: Number(estimate.quota ?? -1), persisted: navigator.storage?.persisted ? await navigator.storage.persisted() : false };
}

export async function inspect(gameId, manifestVersion, pack, identity) {
  const entry = await (await cache()).match(keyFor(gameId, manifestVersion, pack, identity));
  return { exists: !!entry, byteLength: Number(entry?.headers.get('content-length') ?? -1) };
}
