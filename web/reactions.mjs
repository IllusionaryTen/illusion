export const REACTION_EMOJI = Object.freeze(['👍', '❤️', '😂', '😮', '😢', '🔥']);

export function collectReactions(entries) {
  const ordered = [...entries].sort((a, b) => (Number(a.id) || 0) - (Number(b.id) || 0));
  const byTarget = new Map();
  for (const entry of ordered) {
    const reaction = entry?.content?.reaction;
    if (!reaction || typeof reaction !== 'object') continue;
    const target = reaction.targetId;
    if (typeof target !== 'string' && typeof target !== 'number') continue;
    const emoji = reaction.emoji;
    if (emoji !== '' && !REACTION_EMOJI.includes(emoji)) continue;
    const key = String(target);
    if (!byTarget.has(key)) byTarget.set(key, new Map());
    const users = byTarget.get(key);
    if (emoji === '') users.delete(entry.senderId);
    else users.set(entry.senderId, emoji);
  }
  return byTarget;
}

export function summarizeReactions(users, myId) {
  const counts = new Map();
  let mine = '';
  for (const [userId, emoji] of users || []) {
    counts.set(emoji, (counts.get(emoji) || 0) + 1);
    if (userId === myId) mine = emoji;
  }
  const list = [...counts].map(([emoji, count]) => ({ emoji, count }))
    .sort((a, b) => REACTION_EMOJI.indexOf(a.emoji) - REACTION_EMOJI.indexOf(b.emoji));
  return { mine, list };
}
