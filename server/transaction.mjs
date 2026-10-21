export function transaction(db, work) {
  if (typeof db.transaction === 'function') return db.transaction(work);
  db.exec('BEGIN IMMEDIATE');
  try { const result = work(); db.exec('COMMIT'); return result; }
  catch (error) { db.exec('ROLLBACK'); throw error; }
}
