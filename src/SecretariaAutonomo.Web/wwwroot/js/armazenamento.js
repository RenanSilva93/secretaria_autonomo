// Acesso ao localStorage com tolerância a navegação privada / armazenamento bloqueado.
window.secretaria = {
    lerJson: function (chave) {
        try {
            const valor = localStorage.getItem(chave);
            return valor ? JSON.parse(valor) : null;
        } catch {
            return null;
        }
    },
    salvarJson: function (chave, valor) {
        try { localStorage.setItem(chave, JSON.stringify(valor)); } catch { }
    },
    remover: function (chave) {
        try { localStorage.removeItem(chave); } catch { }
    }
};
