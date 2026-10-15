// Hands Chrome's downloads to MyDownload through native messaging (host com.mydownload.app).
const HOST = 'com.mydownload.app';
// Small files finish in Chrome before MyDownload could answer; leaving them avoids duplicates.
const MIN_BYTES = 1024 * 1024;

function send(message) {
  return new Promise(resolve => {
    chrome.runtime.sendNativeMessage(HOST, message, response => {
      if (chrome.runtime.lastError) resolve({ ok: false, reason: chrome.runtime.lastError.message });
      else resolve(response || { ok: false });
    });
  });
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({ id: 'mydownload-link', title: 'Baixar com MyDownload', contexts: ['link'] });
});

chrome.contextMenus.onClicked.addListener(info => {
  if (info.menuItemId === 'mydownload-link' && info.linkUrl) send({ action: 'add', url: info.linkUrl, referrer: info.pageUrl || '', check: false });
});

// Chrome waits for this answer before choosing the file name, so its "Save as" window never
// opens for downloads that MyDownload takes. Returning true keeps the decision pending.
chrome.downloads.onDeterminingFilename.addListener((item, suggest) => {
  decide(item).then(taken => { if (!taken) suggest(); });
  return true;
});

async function decide(item) {
  const { enabled = true } = await chrome.storage.local.get('enabled');
  const url = item.finalUrl || item.url;
  if (!enabled || item.byExtensionId === chrome.runtime.id || !/^https?:/i.test(url)) return false;
  const size = item.totalBytes > 0 ? item.totalBytes : (item.fileSize > 0 ? item.fileSize : 0);
  if (size > 0 && size < MIN_BYTES) return false;
  const name = (item.filename || '').split(/[\\/]/).pop();
  // MyDownload first checks that the link works without the browser's login. When it does not,
  // Chrome simply keeps the download.
  const answer = await send({ action: 'add', url, referrer: item.referrer || '', filename: name, size, check: true });
  if (!answer.ok) return false;
  try { await chrome.downloads.cancel(item.id); } catch (e) {}
  try { await chrome.downloads.erase({ id: item.id }); } catch (e) {}
  return true;
}
