import test from 'node:test';
import assert from 'node:assert/strict';
import { REACTION_EMOJI, collectReactions, summarizeReactions } from '../web/reactions.mjs';
import { messageContent } from '../web/security.mjs';

const entry = (id, senderId, targetId, emoji) => ({ id, senderId, content: { reaction: { targetId, emoji } } });

test('reactions aggregate per target and user', () => {
  const index = collectReactions([
    entry(1, 'a', '10', '👍'),
    entry(2, 'b', '10', '❤️'),
    entry(3, 'a', '10', '❤️')
  ]);
  const summary = summarizeReactions(index.get('10'), 'a');
  assert.equal(summary.mine, '❤️');
  assert.deepEqual(summary.list, [{ emoji: '❤️', count: 2 }]);
});

test('an empty emoji removes only that user reaction', () => {
  const index = collectReactions([
    entry(1, 'a', '10', '👍'),
    entry(2, 'b', '10', '👍'),
    entry(3, 'a', '10', '')
  ]);
  const summary = summarizeReactions(index.get('10'), 'a');
  assert.equal(summary.mine, '');
  assert.deepEqual(summary.list, [{ emoji: '👍', count: 1 }]);
});

test('the latest reaction wins and unknown emoji are ignored', () => {
  const index = collectReactions([
    entry(1, 'a', '10', '👍'),
    entry(2, 'a', '10', 'evil-css'),
    entry(3, 'a', '10', '🔥')
  ]);
  assert.deepEqual(summarizeReactions(index.get('10'), 'a').list, [{ emoji: '🔥', count: 1 }]);
});

test('reaction targets may be numeric or string and other entries are ignored', () => {
  const index = collectReactions([
    { id: 1, senderId: 'a', content: { reaction: { targetId: 10, emoji: '😂' } } },
    { id: 2, senderId: 'a', content: { text: 'normal message' } },
    { id: 3, senderId: 'a', content: { reaction: { targetId: null, emoji: '😂' } } }
  ]);
  assert.deepEqual(summarizeReactions(index.get('10'), 'a').list, [{ emoji: '😂', count: 1 }]);
});

test('the palette is fixed', () => {
  assert.deepEqual([...REACTION_EMOJI], ['👍', '❤️', '😂', '😮', '😢', '🔥']);
});

test('reaction envelopes survive content validation and unsafe targets fail', () => {
  const parsed = messageContent(JSON.stringify({ format: 'illusion.message.v2', text: 'Reacted 👍', reaction: { targetId: 42, emoji: '👍' } }));
  assert.deepEqual(parsed.reaction, { targetId: '42', emoji: '👍' });
  assert.throws(() => messageContent(JSON.stringify({ format: 'illusion.message.v2', text: '', reaction: { targetId: '../x', emoji: '👍' } })), /reaction target/);
  assert.throws(() => messageContent(JSON.stringify({ format: 'illusion.message.v2', text: '', reaction: { targetId: '1', emoji: '\u0000' } })), /Invalid reaction/);
});
