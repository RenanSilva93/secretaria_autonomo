using System.Net.Http.Json;
using SecretariaAutonomo.Contracts;

namespace SecretariaAutonomo.Web.Services;

/// <summary>Erro devolvido pela API com uma mensagem pronta para mostrar ao usuário.</summary>
public class ApiException(string mensagem) : Exception(mensagem);

/// <summary>Todas as chamadas do front para a API passam por aqui.</summary>
public class ApiClient(HttpClient http)
{
    // ----- Painel -----

    public Task<List<ProfissionalDto>> ListarProfissionaisAsync() => GetAsync<List<ProfissionalDto>>("api/profissionais");

    public Task<List<ServicoDto>> ListarServicosAsync() => GetAsync<List<ServicoDto>>("api/servicos");
    public Task<ServicoDto> CriarServicoAsync(SalvarServicoRequest req) => PostAsync<ServicoDto>("api/servicos", req);
    public Task<ServicoDto> AtualizarServicoAsync(Guid id, SalvarServicoRequest req) => PutAsync<ServicoDto>($"api/servicos/{id}", req);
    public Task DesativarServicoAsync(Guid id) => DeleteAsync($"api/servicos/{id}");

    public Task<List<ClienteDto>> ListarClientesAsync(string? busca = null) =>
        GetAsync<List<ClienteDto>>($"api/clientes?busca={Uri.EscapeDataString(busca ?? "")}");
    public Task<ClienteDto> CriarClienteAsync(SalvarClienteRequest req) => PostAsync<ClienteDto>("api/clientes", req);
    public Task<ClienteDto> AtualizarClienteAsync(Guid id, SalvarClienteRequest req) => PutAsync<ClienteDto>($"api/clientes/{id}", req);

    public Task<List<AgendamentoDto>> ListarAgendamentosAsync(DateTimeOffset de, DateTimeOffset ate) =>
        GetAsync<List<AgendamentoDto>>($"api/agendamentos?de={Iso(de)}&ate={Iso(ate)}");
    public Task<AgendamentoDto> CriarAgendamentoAsync(CriarAgendamentoRequest req) => PostAsync<AgendamentoDto>("api/agendamentos", req);
    public Task<AgendamentoDto> ConfirmarAgendamentoAsync(Guid id) => PostAsync<AgendamentoDto>($"api/agendamentos/{id}/confirmar", null);
    public Task<AgendamentoDto> CancelarAgendamentoAsync(Guid id) => PostAsync<AgendamentoDto>($"api/agendamentos/{id}/cancelar", null);
    public Task<AgendamentoDto> RemarcarAgendamentoAsync(Guid id, RemarcarAgendamentoRequest req) =>
        PostAsync<AgendamentoDto>($"api/agendamentos/{id}/remarcar", req);

    public Task<AgendamentoDto> ReceberAgendamentoAsync(Guid id, ReceberAgendamentoRequest req) =>
        PostAsync<AgendamentoDto>($"api/agendamentos/{id}/receber", req);

    public Task<ExtratoDto> ObterExtratoAsync(DateOnly de, DateOnly ate) =>
        GetAsync<ExtratoDto>($"api/financeiro?de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}");
    public Task<List<EvolucaoMensalDto>> ObterEvolucaoMensalAsync(DateOnly de, DateOnly ate) =>
        GetAsync<List<EvolucaoMensalDto>>($"api/financeiro/evolucao?de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}");
    public Task<List<string>> ListarCategoriasAsync() => GetAsync<List<string>>("api/financeiro/categorias");
    public Task<LancamentoDto> CriarLancamentoAsync(SalvarLancamentoRequest req) => PostAsync<LancamentoDto>("api/financeiro/lancamentos", req);
    public Task<LancamentoDto> AtualizarLancamentoAsync(Guid id, SalvarLancamentoRequest req) =>
        PutAsync<LancamentoDto>($"api/financeiro/lancamentos/{id}", req);
    public Task<LancamentoDto> MarcarLancamentoPagoAsync(Guid id) => PostAsync<LancamentoDto>($"api/financeiro/lancamentos/{id}/pagar", null);
    public Task RemoverLancamentoAsync(Guid id) => DeleteAsync($"api/financeiro/lancamentos/{id}");

    public Task<List<HorarioDisponivelDto>> ListarDisponibilidadeAsync(Guid profissionalId, Guid servicoId, DateOnly data) =>
        GetAsync<List<HorarioDisponivelDto>>($"api/disponibilidade?profissionalId={profissionalId}&servicoId={servicoId}&data={data:yyyy-MM-dd}");

    public Task<ContaDto> ObterContaAsync() => GetAsync<ContaDto>("api/conta");

    public Task<List<HorarioTrabalhoDto>> ListarHorariosAsync(Guid profissionalId) =>
        GetAsync<List<HorarioTrabalhoDto>>($"api/profissionais/{profissionalId}/horarios");

    public async Task SalvarHorariosAsync(Guid profissionalId, List<HorarioTrabalhoDto> faixas) =>
        await GarantirSucessoAsync(await http.PutAsJsonAsync($"api/profissionais/{profissionalId}/horarios", faixas));

    public Task<List<BloqueioDto>> ListarBloqueiosAsync(Guid profissionalId, DateTimeOffset? de = null, DateTimeOffset? ate = null)
    {
        var filtros = (de is null ? "" : $"&de={Iso(de.Value)}") + (ate is null ? "" : $"&ate={Iso(ate.Value)}");
        return GetAsync<List<BloqueioDto>>($"api/profissionais/{profissionalId}/bloqueios?{filtros.TrimStart('&')}");
    }

    public Task<BloqueioCriadoDto> CriarBloqueioAsync(Guid profissionalId, SalvarBloqueioRequest req) =>
        PostAsync<BloqueioCriadoDto>($"api/profissionais/{profissionalId}/bloqueios", req);

    public Task RemoverBloqueioAsync(Guid profissionalId, Guid bloqueioId) =>
        DeleteAsync($"api/profissionais/{profissionalId}/bloqueios/{bloqueioId}");

    // ----- Página pública -----

    public Task<PaginaPublicaDto> ObterPaginaPublicaAsync(string slug) => GetAsync<PaginaPublicaDto>($"publico/{slug}");

    public Task<List<HorarioDisponivelDto>> ListarDisponibilidadePublicaAsync(string slug, Guid profissionalId, Guid servicoId, DateOnly data) =>
        GetAsync<List<HorarioDisponivelDto>>($"publico/{slug}/disponibilidade?profissionalId={profissionalId}&servicoId={servicoId}&data={data:yyyy-MM-dd}");

    public Task<HorarioDisponivelDto> AgendarPublicoAsync(string slug, AgendamentoPublicoRequest req) =>
        PostAsync<HorarioDisponivelDto>($"publico/{slug}/agendamentos", req);

    // ----- Infra -----

    private static string Iso(DateTimeOffset data) => Uri.EscapeDataString(data.ToUniversalTime().ToString("O"));

    private async Task<T> GetAsync<T>(string url) => await LerAsync<T>(await http.GetAsync(url));
    private async Task<T> PostAsync<T>(string url, object? corpo) => await LerAsync<T>(await http.PostAsJsonAsync(url, corpo));
    private async Task<T> PutAsync<T>(string url, object corpo) => await LerAsync<T>(await http.PutAsJsonAsync(url, corpo));

    private async Task DeleteAsync(string url)
    {
        var response = await http.DeleteAsync(url);
        await GarantirSucessoAsync(response);
    }

    private static async Task<T> LerAsync<T>(HttpResponseMessage response)
    {
        await GarantirSucessoAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    internal static async Task GarantirSucessoAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        ErroDto? erro = null;
        try
        {
            erro = await response.Content.ReadFromJsonAsync<ErroDto>();
        }
        catch
        {
            // Corpo não é um ErroDto (ex.: 401, 500 sem corpo).
        }

        throw new ApiException(erro?.Mensagem ?? $"Erro ao falar com o servidor ({(int)response.StatusCode}).");
    }
}
