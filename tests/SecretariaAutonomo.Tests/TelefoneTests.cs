using SecretariaAutonomo.Application.Comum;
using SecretariaAutonomo.Application.WhatsApp;

namespace SecretariaAutonomo.Tests;

public class TelefoneTests
{
    [Theory]
    [InlineData("(11) 99999-8888", "5511999998888")]
    [InlineData("11 3333-4444", "551133334444")]
    [InlineData("+55 11 99999-8888", "5511999998888")]
    public void Normaliza_para_E164_sem_mais(string entrada, string esperado)
    {
        Assert.Equal(esperado, Telefone.Normalizar(entrada));
    }

    [Theory]
    [InlineData("9999-8888")]
    [InlineData("abc")]
    public void Rejeita_telefone_invalido(string entrada)
    {
        Assert.Throws<RegraDeNegocioException>(() => Telefone.Normalizar(entrada));
    }

    [Fact]
    public void Payload_do_botao_ida_e_volta()
    {
        var id = Guid.NewGuid();

        Assert.True(AcoesBotao.TentarLer(AcoesBotao.Cancelar(id), out var confirmar, out var lido));
        Assert.False(confirmar);
        Assert.Equal(id, lido);

        Assert.False(AcoesBotao.TentarLer("qualquer coisa", out _, out _));
    }
}
