// Compose request cancellation and timeouts with AbortController only. Some
// Telegram Android WebViews do not implement AbortSignal.any/timeout yet.
export function requestSignal(parent, timeoutMs, runtime = globalThis) {
  const controller = new runtime.AbortController();
  const abortFromParent = () => controller.abort();
  if (parent.aborted) abortFromParent();
  else parent.addEventListener('abort', abortFromParent, { once: true });
  const timer = runtime.setTimeout(() => {
    const error = new Error('Request timed out.');
    error.name = 'TimeoutError';
    controller.abort(error);
  }, timeoutMs);
  return {
    signal: controller.signal,
    dispose() {
      runtime.clearTimeout(timer);
      parent.removeEventListener('abort', abortFromParent);
    }
  };
}
