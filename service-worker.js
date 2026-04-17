// Service Worker 비활성화
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => {
  e.waitUntil(
    caches.keys().then(keys => Promise.all(keys.map(k => caches.delete(k))))
  );
  self.clients.claim();
});
// 모든 요청을 캐시 없이 네트워크에서 직접 가져옴
self.addEventListener('fetch', (e) => {
  e.respondWith(fetch(e.request));
});
