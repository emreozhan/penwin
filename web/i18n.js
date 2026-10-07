// @ts-check
/**
 * Arayüz metinleri (Türkçe / İngilizce).
 * HTML'de: data-i18n (metin), data-i18n-html (sözlükteki sabit işaretleme), data-i18n-aria, data-i18n-title.
 */

/** @typedef {'tr' | 'en'} Lang */

/** @type {Record<Lang, Record<string, string>>} */
const STRINGS = {
  tr: {
    toolbar: 'Araçlar',
    'group.button': 'Kalem tuşu',
    'group.hold': 'Basılı tut',
    'group.keys': 'Tuşlar',
    'group.view': 'Görünüm',
    'btn.left': 'Sol',
    'btn.left.sub': 'tık / çiz',
    'btn.right': 'Sağ',
    'btn.right.sub': 'tek sefer',
    'btn.pan': 'Pan',
    'btn.pan.sub': 'orta tuş',
    'btn.orbit': 'Orbit',
    'btn.orbit.sub': 'shift + orta',
    'key.undo': 'Geri',
    'key.redo': 'İleri',
    'key.delete': 'Sil',
    'key.tab.sub': 'ölçü alanı',
    'mirror.label': 'PC ekranı',
    'mirror.sub': 'arka planda',
    'mirror.failed': 'alınamadı',
    settings: 'Ayarlar',
    close: 'Kapat',
    'hover.title': 'Apple Pencil hover algılandı mı',
    'mode.mouse': 'Fare',
    'mode.pen': 'Kalem',

    'status.connecting': 'Bağlanıyor…',
    'status.connected': 'Bağlı',
    'status.disconnected': 'Bağlantı yok',
    'status.needKey': 'Anahtar gerekli',
    'status.badKey': 'Anahtar hatalı',
    'status.replaced': 'Başka cihaz bağlandı',
    'notice.needKey': "PC'deki PenWin penceresinde yazan 6 haneli anahtarı girin.",
    'notice.badKey': "Bağlantı anahtarı hatalı. PC'deki PenWin penceresindeki anahtarı girin.",
    'notice.replaced': "Başka bir cihaz PenWin'e bağlandı.",
    'action.openSettings': 'Ayarları aç',
    'action.reconnect': 'Yeniden bağlan',

    'set.lang': 'Dil',
    'set.mode': 'Çalışma modu',
    'set.mode.mouse': '<b>Fare</b> — CAD için önerilen. Fusion 360 / Sharp3D her yerde çalışır.',
    'set.mode.pen': '<b>Kalem (Windows Ink)</b> — basınç ve eğim gider; çizim uygulamaları için.',
    'set.screen': 'Ekran',
    'set.screenItem': 'Ekran {n} — {w}×{h}',
    'set.primary': ' (ana)',
    'set.waiting': 'Bağlantı bekleniyor',
    'set.deadzone': 'Tıklama ölü bölgesi',
    'set.deadzone.help': 'Dokunduktan sonra kalem bu kadar kaymadan hareket gönderilmez; titreyen tıklamalar sürüklemeye dönüşmez.',
    'set.threshold': 'Temas eşiği',
    'set.threshold.off': 'kapalı',
    'set.threshold.help': "Hover desteklemeyen iPad'ler için: hafif temas yalnızca imleci gezdirir, bastırınca tıklar. Hover varsa 0 bırakın.",
    'set.opacity': 'PC ekranı saydamlığı',
    'set.opacity.help': 'Araç çubuğundaki "PC ekranı" anahtarı açıkken çizim alanının arkasında saniyede bir yenilenir.',
    'set.gestures': 'İki parmak: sürükle = pan, sıkıştır = zoom',
    'set.trail': "Kalem izini iPad'de göster",
    'set.side': 'Araç çubuğu sağda (solak kullanım)',
    'set.token': 'Bağlantı anahtarı',
    'set.token.help': "PC'deki PenWin penceresinde yazar.",
    'set.fullscreen': 'Tam ekran',
    'set.reconnect': 'Yeniden bağlan',
    'set.hint': "İpucu: Safari'de Paylaş → <b>Ana Ekrana Ekle</b> ile adres çubuğu olmadan tam ekran açılır.",
  },
  en: {
    toolbar: 'Tools',
    'group.button': 'Pen button',
    'group.hold': 'Hold',
    'group.keys': 'Keys',
    'group.view': 'View',
    'btn.left': 'Left',
    'btn.left.sub': 'click / draw',
    'btn.right': 'Right',
    'btn.right.sub': 'one shot',
    'btn.pan': 'Pan',
    'btn.pan.sub': 'middle',
    'btn.orbit': 'Orbit',
    'btn.orbit.sub': 'shift + mid',
    'key.undo': 'Undo',
    'key.redo': 'Redo',
    'key.delete': 'Delete',
    'key.tab.sub': 'next field',
    'mirror.label': 'PC view',
    'mirror.sub': 'in background',
    'mirror.failed': 'unavailable',
    settings: 'Settings',
    close: 'Close',
    'hover.title': 'Has Apple Pencil hover been detected',
    'mode.mouse': 'Mouse',
    'mode.pen': 'Pen',

    'status.connecting': 'Connecting…',
    'status.connected': 'Connected',
    'status.disconnected': 'Disconnected',
    'status.needKey': 'Key required',
    'status.badKey': 'Wrong key',
    'status.replaced': 'Another device connected',
    'notice.needKey': 'Enter the 6-character key shown in the PenWin window on the PC.',
    'notice.badKey': 'The connection key is wrong. Enter the key shown in the PenWin window on the PC.',
    'notice.replaced': 'Another device connected to PenWin.',
    'action.openSettings': 'Open settings',
    'action.reconnect': 'Reconnect',

    'set.lang': 'Language',
    'set.mode': 'Mode',
    'set.mode.mouse': '<b>Mouse</b> — recommended for CAD. Works everywhere in Fusion 360 / Sharp3D.',
    'set.mode.pen': '<b>Pen (Windows Ink)</b> — sends pressure and tilt; for drawing apps.',
    'set.screen': 'Screen',
    'set.screenItem': 'Screen {n} — {w}×{h}',
    'set.primary': ' (primary)',
    'set.waiting': 'Waiting for connection',
    'set.deadzone': 'Click dead zone',
    'set.deadzone.help': 'After touching down, no movement is sent until the pen moves this far, so shaky taps do not turn into drags.',
    'set.threshold': 'Contact threshold',
    'set.threshold.off': 'off',
    'set.threshold.help': 'For iPads without hover: a light touch only moves the cursor, pressing harder clicks. Leave at 0 if hover works.',
    'set.opacity': 'PC view opacity',
    'set.opacity.help': 'While the "PC view" switch in the toolbar is on, the image behind the drawing area refreshes once per second.',
    'set.gestures': 'Two fingers: drag = pan, pinch = zoom',
    'set.trail': 'Show the pen trail on the iPad',
    'set.side': 'Toolbar on the right (left-handed)',
    'set.token': 'Connection key',
    'set.token.help': 'Shown in the PenWin window on the PC.',
    'set.fullscreen': 'Full screen',
    'set.reconnect': 'Reconnect',
    'set.hint': 'Tip: in Safari, Share → <b>Add to Home Screen</b> opens it full screen without the address bar.',
  },
};

/** @type {Lang} */
let current = 'tr';

/**
 * Kayıtlı tercih yoksa iPad'in diline göre seçer.
 * @param {string | undefined} preferred
 * @returns {Lang}
 */
export function resolveLang(preferred) {
  if (preferred === 'tr' || preferred === 'en') return preferred;
  return (navigator.language || '').toLowerCase().startsWith('tr') ? 'tr' : 'en';
}

/**
 * @param {string} key
 * @param {Record<string, string | number>} [params] {ad} yer tutucuları
 */
export function t(key, params) {
  let text = STRINGS[current][key] ?? STRINGS.tr[key] ?? key;
  if (params) {
    for (const [name, value] of Object.entries(params)) text = text.replace(`{${name}}`, String(value));
  }
  return text;
}

/**
 * Dili değiştirir ve sayfadaki işaretli metinleri günceller.
 * @param {Lang} lang
 */
export function applyLang(lang) {
  current = lang;
  document.documentElement.lang = lang;
  for (const el of document.querySelectorAll('[data-i18n]')) {
    el.textContent = t(/** @type {HTMLElement} */ (el).dataset.i18n ?? '');
  }
  // Yalnızca yukarıdaki sabit sözlükten gelen, güvenilir işaretleme.
  for (const el of document.querySelectorAll('[data-i18n-html]')) {
    el.innerHTML = t(/** @type {HTMLElement} */ (el).dataset.i18nHtml ?? '');
  }
  for (const el of document.querySelectorAll('[data-i18n-aria]')) {
    el.setAttribute('aria-label', t(/** @type {HTMLElement} */ (el).dataset.i18nAria ?? ''));
  }
  for (const el of document.querySelectorAll('[data-i18n-title]')) {
    el.setAttribute('title', t(/** @type {HTMLElement} */ (el).dataset.i18nTitle ?? ''));
  }
}
