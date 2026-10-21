import {mkdirSync,writeFileSync} from 'node:fs';
import {dirname,resolve} from 'node:path';
import {translationCatalog} from '../web/i18n.mjs';
const output=resolve(process.argv[2]||'build/translations.json');
mkdirSync(dirname(output),{recursive:true});
writeFileSync(output,JSON.stringify(translationCatalog));
console.log('Prepared shared translations for '+translationCatalog.languages.length+' languages.');
