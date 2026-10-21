import {createHash,timingSafeEqual} from 'node:crypto';
export function secretMatches(supplied,expected){if(typeof supplied!=='string'||typeof expected!=='string'||!expected||supplied.length>512)return false;const digest=s=>createHash('sha256').update(s).digest();return timingSafeEqual(digest(supplied),digest(expected));}
