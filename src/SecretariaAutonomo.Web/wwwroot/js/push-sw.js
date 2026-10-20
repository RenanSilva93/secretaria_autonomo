// Notificações Web Push do painel. Importado pelos dois service workers (desenvolvimento e publicado).

self.addEventListener('push', event => {
    let aviso = {};
    try {
        aviso = event.data ? event.data.json() : {};
    } catch { }

    event.waitUntil(self.registration.showNotification(aviso.titulo || 'Secretária', {
        body: aviso.texto || '',
        icon: 'icon-192.png',
        badge: 'icon-192.png',
        tag: aviso.tag,
        data: { url: new URL(aviso.url || './', self.registration.scope).href }
    }));
});

// Toque na notificação: usa o painel já aberto, se houver, ou abre um novo.
self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = (event.notification.data && event.notification.data.url) || self.registration.scope;

    event.waitUntil((async () => {
        const janelas = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        const painel = janelas.find(j => j.url.startsWith(self.registration.scope));
        if (painel) {
            await painel.focus();
            // A própria página navega sem recarregar o app (ver js/push.js).
            painel.postMessage({ tipo: 'abrir-url', url });
            return;
        }
        await self.clients.openWindow(url);
    })());
});
