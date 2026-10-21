import {test} from 'node:test';
import assert from 'node:assert/strict';
import {requestSignal} from '../web/request-signal.mjs';

const legacyRuntime={AbortController,AbortSignal:{},setTimeout,clearTimeout};

test('Mini App request cancellation works without AbortSignal.any or timeout',()=>{
  const parent=new AbortController(),scope=requestSignal(parent.signal,10000,legacyRuntime);
  assert.equal(scope.signal.aborted,false);parent.abort();assert.equal(scope.signal.aborted,true);scope.dispose();
});

test('Mini App request timeout works in older Telegram WebViews',async()=>{
  const scope=requestSignal(new AbortController().signal,5,legacyRuntime);
  await new Promise(resolve=>setTimeout(resolve,20));assert.equal(scope.signal.aborted,true);assert.equal(scope.signal.reason.name,'TimeoutError');scope.dispose();
});
