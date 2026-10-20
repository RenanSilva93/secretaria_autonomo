// Inscrição do navegador nas notificações Web Push. Usado pelo serviço NotificacoesPush (C#).
window.secretariaPush = {
    suportado: function () {
        return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
    },

    permissao: function () {
        return Notification.permission;
    },

    // Inscrição existente, refeita se o servidor trocou a chave VAPID. Null se o aparelho não está inscrito.
    inscricaoAtual: async function (chave) {
        const registro = await navigator.serviceWorker.ready;
        const atual = await registro.pushManager.getSubscription();
        if (!atual)
            return null;
        if (mesmaChave(atual, chave))
            return dados(atual);

        await atual.unsubscribe();
        return dados(await registro.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: base64UrlParaBytes(chave) }));
    },

    // Pede a permissão (precisa vir de um clique) e inscreve. Devolve a permissão final e a inscrição.
    inscrever: async function (chave) {
        const permissao = await Notification.requestPermission();
        if (permissao !== 'granted')
            return { permissao, inscricao: null };

        const inscricao = await window.secretariaPush.inscricaoAtual(chave);
        if (inscricao)
            return { permissao, inscricao };

        const registro = await navigator.serviceWorker.ready;
        const nova = await registro.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: base64UrlParaBytes(chave) });
        return { permissao, inscricao: dados(nova) };
    },

    // Cancela a inscrição deste aparelho e devolve o endpoint, para o servidor apagar também.
    cancelar: async function () {
        if (!window.secretariaPush.suportado())
            return null;
        const registro = await navigator.serviceWorker.getRegistration();
        const atual = registro && await registro.pushManager.getSubscription();
        if (!atual)
            return null;
        const endpoint = atual.endpoint;
        await atual.unsubscribe();
        return endpoint;
    }
};

// Toque na notificação com o painel aberto: o service worker pede para a página navegar.
if ('serviceWorker' in navigator) {
    navigator.serviceWorker.addEventListener('message', evento => {
        if (evento.data && evento.data.tipo === 'abrir-url')
            Blazor.navigateTo(evento.data.url);
    });
}

function dados(inscricao) {
    const json = inscricao.toJSON();
    return { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth };
}

function mesmaChave(inscricao, chave) {
    const atual = inscricao.options && inscricao.options.applicationServerKey;
    if (!atual)
        return true;
    const esperada = base64UrlParaBytes(chave);
    const bytes = new Uint8Array(atual);
    return bytes.length === esperada.length && bytes.every((b, i) => b === esperada[i]);
}

function base64UrlParaBytes(texto) {
    const base64 = (texto + '='.repeat((4 - texto.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
    return Uint8Array.from(atob(base64), c => c.charCodeAt(0));
}
