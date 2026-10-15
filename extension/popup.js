const box = document.getElementById('enabled');
const status = document.getElementById('status');

chrome.storage.local.get('enabled').then(({ enabled = true }) => { box.checked = enabled; });
box.addEventListener('change', () => chrome.storage.local.set({ enabled: box.checked }));

chrome.runtime.sendNativeMessage('com.mydownload.app', { action: 'ping' }, response => {
  if (chrome.runtime.lastError || !response || !response.ok) {
    status.textContent = 'O MyDownload não respondeu. Abra o app, vá em Configurações e clique em Instalar de novo.';
    status.className = 'err';
  } else {
    status.textContent = 'Conectado ao MyDownload.';
    status.className = 'ok';
  }
});
