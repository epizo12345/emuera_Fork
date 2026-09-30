let interval;

export function start(requestId, limitMilliseconds) {
    clearInterval(interval);
    const element = document.getElementById('timed-input-remaining');
    if (!element || element.dataset.requestId !== String(requestId)) return;
    const deadline = performance.now() + limitMilliseconds;
    const update = () => {
        if (!element.isConnected || element.dataset.requestId !== String(requestId)) {
            clearInterval(interval);
            return;
        }
        const remaining = Math.max(0, deadline - performance.now());
        element.textContent = `残り ${(remaining / 1000).toFixed(1)} 秒`;
        if (remaining === 0) clearInterval(interval);
    };
    update();
    interval = setInterval(update, 100);
}

export function stop() { clearInterval(interval); }
