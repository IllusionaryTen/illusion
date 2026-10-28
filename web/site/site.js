(() => {
  'use strict';

  const ru = {
    'skip': 'Перейти к содержимому',
    'nav.features': 'Возможности',
    'nav.security': 'Безопасность',
    'nav.themes': 'Темы',
    'nav.download': 'Скачать',
    'nav.faq': 'Вопросы',
    'nav.open': 'Открыть Illusion',
    'hero.badge': 'Приватный · Настраиваемый · Бесплатный',
    'hero.title': 'Общайтесь в своём <span class="grad">измерении.</span>',
    'hero.lede': 'Illusion — приватный мессенджер с шифрованием сообщений и файлов, исчезающими чатами, голосовыми заметками и десятью аура-темами — для Windows, Android, веба и Telegram.',
    'hero.cta': 'Открыть веб-приложение',
    'hero.download': 'Скачать приложения',
    'hero.meta1': 'Сквозное шифрование',
    'hero.meta2': '34 языка',
    'hero.meta3': 'Без трекеров',
    'hero.platforms': 'Windows · Android · Веб · Telegram',
    'preview.label': 'Живой предпросмотр — выберите ауру',
    'trust.1': 'Шифрование на устройстве',
    'trust.2': 'Без сторонних скриптов',
    'trust.3': '34 языка',
    'trust.4': 'Бесплатно на этапе превью',
    'features.eyebrow': 'Внутри Illusion',
    'features.title': 'Всё, что нужно разговору. И ничего лишнего.',
    'features.sub': 'Мелочи, из которых складывается общение — цитаты, таймеры, голосовые, ауры — и при этом полный контроль над своими словами.',
    'f1.title': 'Шифрование на вашем устройстве',
    'f1.body': 'Сообщения, подписи, ответы и файлы шифруются и подписываются на устройстве, а затем оборачиваются ключом RSA-3072 для каждого получателя. Ретранслятор пересылает только запечатанные конверты.',
    'f2.title': 'На каждом вашем экране',
    'f2.body': 'Портативное приложение Windows, которое умеет обновляться само, нативная оболочка Android с приёмом материалов из других приложений, веб-версия и Mini App в Telegram. Один аккаунт, одна сеть — разговор продолжается там, где вы остановились.',
    'f3.title': 'Голосовые под замком',
    'f3.body': 'Удерживайте микрофон, чтобы записать до 40 секунд. Записи проходят тот же зашифрованный путь, что фото, видео и документы.',
    'f4.title': 'Таймеры убирают за вами',
    'f4.body': 'Исчезающие сообщения от минуты до 30 дней на чат. Таймер может менять любой участник; новые сообщения следуют ему с момента доставки.',
    'f5.title': 'Открыть один раз',
    'f5.body': 'Отправьте текст, фото или видео с режимом «Просмотр один раз». Получатель увидит запечатанную кнопку, а ретранслятор удалит копию после первого открытия.',
    'f6.title': 'Поиск, цитаты и закрепления',
    'f6.body': 'Ищите по загруженным сообщениям прямо в чате и перебирайте совпадения стрелками ↑/↓. Отвечайте цитатой, ставьте реакции из фиксированного набора эмодзи, закрепляйте чаты и отключайте уведомления в беседе локально — список таких бесед не покидает устройство.',
    'f7.title': 'Профили с аурами',
    'f7.body': 'Имя, описание, шрифты и цвета — плюс обои для каждой беседы, переносимые файлы оформления и десять аур-тем, которые перекрашивают весь интерфейс.',
    'f8.title': 'Проверка устройств и ключей',
    'f8.body': 'Сравните отпечатки ключей по другому каналу и отметьте контакт как проверенный. Просматривайте все активные устройства и завершайте сеансы мгновенно.',
    'security.eyebrow': 'Модель безопасности',
    'security.title': 'Ретранслятор только пересылает. Прочитать он не может.',
    'security.sub': 'Ключи остаются на ваших устройствах. По сети идут конверты и маршрутная информация — но не открытый текст.',
    'sec.p1.title': 'На вашем устройстве',
    'sec.p1.body': 'Текст, подписи, ответы и ключи файлов запечатываются алгоритмом AES-256-CBC с кодом HMAC-SHA-256, затем оборачиваются в RSA-OAEP для каждого получателя.',
    'sec.p2.title': 'На ретрансляторе',
    'sec.p2.body': 'Запечатанные конверты и открытые ключи. Ретранслятор видит аккаунты, пары собеседников, время, размеры и IP — но не содержимое.',
    'sec.p3.title': 'На устройстве получателя',
    'sec.p3.body': 'Получатель проверяет подпись и MAC, разворачивает ключ контента закрытым ключом, который никогда не покидал устройство, и расшифровывает данные в памяти.',
    'sec.honest.tag': 'Честные ограничения',
    'sec.honest.title': 'Чего мы не обещаем',
    'sec.honest.body': 'Здесь нет прямой секретности (forward secrecy) и восстановления после компрометации, а собственный протокол ещё не проходил независимый аудит. Скомпрометированный ключ устройства раскрывает зашифрованные для него сообщения. Оператор ретранслятора может хранить шифротекст, а скриншоты и изменённые клиенты предотвратить невозможно.',
    'sec.honest.link': 'Прочитать модель безопасности',
    'sec.fine': 'TLS для хостинга терминируется на Cloudflare, поэтому Cloudflare видит метаданные соединений. Ни на сайте, ни в приложении нет аналитики, сторонних скриптов и удалённых шрифтов.',
    'themes.eyebrow': 'Внешний вид',
    'themes.title': 'Десять аур. Один мессенджер.',
    'themes.sub': 'Каждая палитра взята из арта аур Sol\u2019s RNG и проверена на читаемость контраста, а затем применена в вебе, Windows и Android. Попробуйте их в предпросмотре выше.',
    'theme.base': 'Исходная голубая орбита.',
    'theme.astraios': 'Звёздный бирюзовый.',
    'theme.crimson': 'Багровое затмение.',
    'theme.dreammetric': 'Лиловый дрейф.',
    'theme.eden': 'Серебряная пустота.',
    'theme.hellborn': 'Тлеющий янтарь.',
    'theme.monarch': 'Фиолетовая корона.',
    'theme.oblivion': 'Глубокий пурпур.',
    'theme.oppression': 'Сталь и тень.',
    'theme.tomorrow': 'Силуэт рассвета.',
    'themes.fine': 'Названия аур — имена собственные, они не переводятся.',
    'dl.eyebrow': 'Скачать Illusion',
    'dl.title': 'Начните меньше чем за минуту.',
    'dl.sub': 'Один аккаунт везде. Превью-сборки не подписаны или подписаны отладочным ключом — сверяйте опубликованный SHA-256 перед установкой.',
    'dl.web.title': 'Веб-приложение',
    'dl.web.body': 'Работает в любом современном браузере и устанавливается на главный экран. Ключи хранятся в защищённом хранилище браузера.',
    'dl.web.meta': 'Без установки · работает на телефонах',
    'dl.web.cta': 'Открыть веб-приложение',
    'dl.win.title': 'Windows',
    'dl.win.body': 'Портативный EXE для Windows 10/11 с .NET Framework 4.8. Без установщика; сам проверяет и устанавливает обновления. Сборка не подписана.',
    'dl.win.cta': 'Скачать для Windows',
    'dl.win.zip': 'ZIP',
    'dl.win.conn': 'Файл подключения',
    'dl.android.title': 'Android',
    'dl.android.body': 'Android 6.0 или новее. Оболочка WebView вокруг веб-приложения с голосовыми и приёмом материалов из других приложений.',
    'dl.android.cta': 'Скачать APK',
    'dl.android.sha': 'SHA-256',
    'dl.tg.title': 'Telegram',
    'dl.tg.body': 'Запускайте Mini App и получайте установщики или готовый файл подключения прямо в боте.',
    'dl.tg.meta': '/app · /apk · /download · /connect',
    'dl.tg.cta': 'Открыть @IllusedBot',
    'dl.verify': 'Каждый релиз публикует манифест SHA-256:',
    'dl.verify2': 'Windows-приложение не подписано, Android использует отладочный ключ — если не хотите ставить вручную, используйте веб-приложение.',
    'lang.eyebrow': 'Языки',
    'lang.title': '34 языка. Каждый экран.',
    'lang.sub': 'Весь интерфейс — чаты, таймеры, настройки, восстановление, жалобы — переведён. Illusion и названия аур остаются как есть.',
    'lang.fine': 'Юридические документы пока только на английском.',
    'start.eyebrow': 'Начало работы',
    'start.title': 'Четыре шага — и вы внутри.',
    'start.s1.title': 'Выберите клиент',
    'start.s1.body': 'Откройте веб-приложение, установите Windows или Android либо запустите Mini App в Telegram.',
    'start.s2.title': 'Выберите имя',
    'start.s2.body': 'Имя пользователя — 2–24 буквы, цифры, _ или -, без учёта регистра. Пароль — не короче 10 символов.',
    'start.s3.title': 'Сохраните ключ',
    'start.s3.body': 'Скачайте резервную копию .illusionkey, когда она будет предложена. Это единственный способ читать старые сообщения на другом устройстве.',
    'start.s4.title': 'Найдите собеседника',
    'start.s4.body': 'Ищите по точному имени или начните с «Сохранённых». Ctrl+K находит людей, Ctrl+F ищет по чату.',
    'start.warn': 'Пароль сбрасывает аккаунт, но не шифрование. Без ключа устройства или его резервной копии старые чаты не расшифрует никто. Превью — 18+ и ограничено 1000 активными аккаунтами.',
    'faq.eyebrow': 'Вопросы',
    'faq.title': 'Честные вопросы, прямые ответы.',
    'faq.q1': 'Illusion бесплатный?',
    'faq.a1': 'Да. На этапе превью нет платежей, подписок, рекламы и рассылок. Сервис размещает и финансирует его оператор.',
    'faq.q2': 'Нужен ли Telegram?',
    'faq.a2': 'Нет. Windows, Android и веб-приложение работают сами по себе. Бот @IllusedBot только доставляет Mini App, установщики и файлы подключения.',
    'faq.q3': 'Может ли оператор читать мои чаты?',
    'faq.a3': 'Текст, подписи, ответы и файлы запечатываются на устройстве до отправки. Ретранслятор хранит шифротекст, открытые ключи и метаданные доставки — кто, когда, какого размера и с какого IP. Без ваших ключей он не может превратить это в читаемые сообщения. Уведомления от хоста явно не сквозные.',
    'faq.q4': 'Что будет, если я потеряю ключ?',
    'faq.a4': 'Пароль его не воссоздаст. Восстановите резервную копию .illusionkey или используйте устройство, где ключ ещё есть. Восстановление аккаунта — проверка ключом устройства или код на привязанный Gmail — сбрасывает пароль, но не возвращает историю.',
    'faq.q5': 'Какие устройства поддерживаются?',
    'faq.a5': 'Windows 10/11 с .NET Framework 4.8, Android 6.0+, любой современный браузер и Telegram через Mini App. Приложение Windows портативное и не подписано; превью-APK подписан отладочным ключом.',
    'faq.q6': 'Можно ли редактировать или отзывать сообщения?',
    'faq.a6': 'Нет. Сообщения нельзя редактировать или отзывать. Таймеры удаляют контент после срока, а «Просмотр один раз» — после первого открытия, но уже сделанные копии не отозвать.',
    'faq.q7': 'Illusion — открытый код?',
    'faq.a7': 'Исходный код опубликован на github.com/IllusionaryTen/illusion. Административные инструменты и модерация не публикуются, а у хостинга свои секреты.',
    'faq.q8': 'Как пожаловаться на человека?',
    'faq.a8': 'Нажмите правой кнопкой на входящее сообщение и выберите «Пожаловаться» либо используйте «Профиль / Таймер», чтобы пожаловаться или заблокировать. Жалобы попадают в ручную очередь, блокировка останавливает новые сообщения в обе стороны.',
    'final.title': 'Следующий разговор может быть приватным.',
    'final.sub': 'Откройте веб-приложение или возьмите портативную сборку с собой.',
    'final.cta': 'Открыть Illusion',
    'final.dl': 'Скачать',
    'foot.tag': 'Приватный. Настраиваемый. Бесплатный. Превью-мессенджер независимого оператора.',
    'foot.status': 'Проверяем сервис…',
    'foot.product': 'Продукт',
    'foot.source': 'Исходный код',
    'foot.legal': 'Документы',
    'foot.privacy': 'Конфиденциальность',
    'foot.terms': 'Условия',
    'foot.cookies': 'Cookies',
    'foot.refunds': 'Возвраты',
    'foot.licenses': 'Лицензии',
    'foot.a11y': 'Доступность',
    'foot.security': 'Безопасность',
    'foot.model': 'Модель безопасности',
    'foot.report': 'Сообщить о проблеме',
    'foot.health': 'Состояние сервиса',
    'foot.version': 'Версия',
    'foot.note': 'Без аналитики и сторонних скриптов. Данные оператора уточняются.'
  };

  const metaRu = {
    title: 'Illusion — приватный мессенджер для каждого экрана',
    description: 'Приватный, настраиваемый, бесплатный. Сквозное шифрование сообщений, файлов и голосовых, исчезающие чаты и десять аур-тем — на Windows, Android, в вебе и Telegram.',
    ogTitle: 'Illusion — общайтесь в своём измерении'
  };
  const metaEn = {
    title: document.title,
    description: document.querySelector('meta[name="description"]')?.content || '',
    ogTitle: document.querySelector('meta[property="og:title"]')?.content || ''
  };

  const textEls = Array.from(document.querySelectorAll('[data-i18n]'));
  const htmlEls = Array.from(document.querySelectorAll('[data-i18n-html]'));
  const originals = new Map(textEls.map(el => [el, el.textContent]));
  const originalHtml = new Map(htmlEls.map(el => [el, el.innerHTML]));

  let lang = '';
  try {
    const saved = localStorage.getItem('illusion-site-lang');
    if (saved === 'ru' || saved === 'en') lang = saved;
  } catch {}
  if (!lang) lang = (navigator.language || '').toLowerCase().startsWith('ru') ? 'ru' : 'en';

  const state = { online: false, version: '' };

  function statusText() {
    if (!state.online) return lang === 'ru' ? 'Проверяем сервис…' : 'Checking service…';
    return lang === 'ru' ? 'В сети · v' + state.version : 'Online · v' + state.version;
  }

  function renderStatus() {
    const el = document.getElementById('status');
    if (!el) return;
    el.textContent = statusText();
    document.getElementById('status-dot')?.classList.toggle('ok', state.online);
  }

  function applyMeta(l) {
    const m = l === 'ru' ? metaRu : metaEn;
    document.title = m.title;
    const setMeta = (selector, value) => {
      const node = document.querySelector(selector);
      if (node && value) node.setAttribute('content', value);
    };
    setMeta('meta[name="description"]', m.description);
    setMeta('meta[property="og:title"]', m.ogTitle);
    setMeta('meta[name="twitter:title"]', m.ogTitle);
    setMeta('meta[property="og:description"]', m.description);
    setMeta('meta[name="twitter:description"]', m.description);
  }

  function setLang(l) {
    lang = l === 'ru' ? 'ru' : 'en';
    document.documentElement.lang = lang;
    for (const el of textEls) {
      const key = el.getAttribute('data-i18n');
      el.textContent = lang === 'ru' && ru[key] !== undefined ? ru[key] : originals.get(el);
    }
    for (const el of htmlEls) {
      const key = el.getAttribute('data-i18n-html');
      el.innerHTML = lang === 'ru' && ru[key] !== undefined ? ru[key] : originalHtml.get(el);
    }
    document.querySelectorAll('.lang-btn').forEach(btn => {
      const active = btn.getAttribute('data-lang') === lang;
      btn.classList.toggle('active', active);
      btn.setAttribute('aria-pressed', String(active));
    });
    applyMeta(lang);
    renderStatus();
    try { localStorage.setItem('illusion-site-lang', lang); } catch {}
  }

  document.querySelectorAll('.lang-btn').forEach(btn => {
    btn.addEventListener('click', () => setLang(btn.getAttribute('data-lang')));
  });
  setLang(lang);

  // Mobile menu
  const topbar = document.getElementById('topbar');
  const menu = document.getElementById('menu');
  const nav = document.getElementById('nav');
  function closeMenu() {
    topbar?.classList.remove('open');
    menu?.setAttribute('aria-expanded', 'false');
  }
  menu?.addEventListener('click', () => {
    const open = topbar.classList.toggle('open');
    menu.setAttribute('aria-expanded', String(open));
  });
  nav?.querySelectorAll('a').forEach(a => a.addEventListener('click', closeMenu));
  document.addEventListener('keydown', e => { if (e.key === 'Escape') closeMenu(); });
  document.addEventListener('click', e => {
    if (!topbar?.classList.contains('open')) return;
    if (!topbar.contains(e.target)) closeMenu();
  });

  // Header state
  const onScroll = () => topbar?.classList.toggle('scrolled', window.scrollY > 8);
  document.addEventListener('scroll', onScroll, { passive: true });
  onScroll();

  // Aura preview
  const preview = document.getElementById('preview-window');
  const auraActive = document.getElementById('aura-active');
  const auraButtons = Array.from(document.querySelectorAll('[data-aura-set]'));
  auraButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      const aura = btn.getAttribute('data-aura-set');
      if (!preview || !aura) return;
      preview.setAttribute('data-aura', aura);
      if (auraActive) auraActive.textContent = btn.getAttribute('data-name') || btn.title || '';
      auraButtons.forEach(b => {
        const active = b === btn;
        b.classList.toggle('active', active);
        b.setAttribute('aria-pressed', String(active));
      });
    });
  });

  // Scroll reveal
  const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  if (!reduced && 'IntersectionObserver' in window) {
    document.documentElement.classList.add('motion');
    const targets = Array.from(document.querySelectorAll(
      '.section-head,.card,.theme-card,.pipe,.honest,.step,.faq details,.finale-inner'
    ));
    targets.forEach((el, i) => {
      el.classList.add('reveal');
      if (i % 3 === 1) el.classList.add('d1');
      if (i % 3 === 2) el.classList.add('d2');
    });
    const io = new IntersectionObserver(entries => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          entry.target.classList.add('in');
          io.unobserve(entry.target);
        }
      }
    }, { threshold: 0.12, rootMargin: '0px 0px -40px' });
    targets.forEach(el => io.observe(el));
  }

  // Live release metadata
  const formatBytes = bytes => {
    if (!Number.isFinite(bytes) || bytes <= 0) return '';
    return (bytes / 1048576).toFixed(2) + ' MiB';
  };
  async function loadRelease(url) {
    const res = await fetch(url, { cache: 'no-store' });
    if (!res.ok) throw new Error('manifest ' + res.status);
    return res.json();
  }
  (async () => {
    try {
      const rel = await loadRelease('/download/release.json');
      if (/^\d+\.\d+\.\d+$/.test(rel.version)) {
        const meta = document.getElementById('meta-win');
        if (meta) meta.textContent = 'v' + rel.version + (rel.bytes ? ' · ' + formatBytes(rel.bytes) : '') + ' · SHA-256';
        const exe = document.getElementById('dl-win');
        if (exe && rel.file) exe.setAttribute('href', '/download/' + rel.file);
        const zip = document.getElementById('dl-zip');
        if (zip && rel.archive?.file) zip.setAttribute('href', '/download/' + rel.archive.file);
        const conn = document.getElementById('dl-conn');
        if (conn && rel.connection?.file) conn.setAttribute('href', '/download/' + rel.connection.file);
        const foot = document.getElementById('foot-version');
        if (foot) foot.textContent = rel.version;
      }
    } catch {}
    try {
      const rel = await loadRelease('/download/android/release.json');
      if (/^\d+\.\d+\.\d+$/.test(rel.version)) {
        const meta = document.getElementById('meta-android');
        if (meta) meta.textContent = 'v' + rel.version + (rel.bytes ? ' · ' + formatBytes(rel.bytes) : '') + ' · debug-signed';
        const link = document.getElementById('dl-android');
        if (link && rel.file) link.setAttribute('href', '/download/android/' + rel.file);
      }
    } catch {}
  })();

  // Service health
  (async () => {
    try {
      const res = await fetch('/health', { cache: 'no-store' });
      if (!res.ok) throw new Error('health ' + res.status);
      const body = await res.json();
      if (body && body.app === 'Illusion' && typeof body.version === 'string') {
        state.online = true;
        state.version = body.version;
      }
    } catch {}
    renderStatus();
  })();
})();
