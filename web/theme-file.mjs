const HEX = /^#[0-9a-f]{6}$/i;

export const LOOK_VERSION = 1;

export function validateLookFile(value, { themes, fonts } = {}) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw Error('invalid look file');
  if (value.version !== LOOK_VERSION) throw Error('unsupported look version');
  if (typeof value.theme !== 'string' || !Array.isArray(themes) || !themes.includes(value.theme)) throw Error('unknown theme');
  if (typeof value.font !== 'string' || !Array.isArray(fonts) || !fonts.includes(value.font)) throw Error('unknown font');
  for (const key of ['textColor', 'accent']) {
    if (typeof value[key] !== 'string' || !HEX.test(value[key])) throw Error('invalid ' + key);
  }
  return {
    version: LOOK_VERSION,
    theme: value.theme,
    font: value.font,
    textColor: value.textColor.toLowerCase(),
    accent: value.accent.toLowerCase()
  };
}
