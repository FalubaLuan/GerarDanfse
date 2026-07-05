// Baixar NFSe - Geração do DANFSe (Documento Auxiliar da NFS-e) em PDF.
// Porte C# do gerar_danfse.js — modelos de dados do parser.
//
// Estrutura: DanfseParser.ParseDanfse(xml) -> DanfseData; DanfsePdfBuilder.Build(data, deps)
// -> byte[] (PDF). DanfseDeps = { LogoPng, ArimoRegular/Bold, Auditoria, DestacarRet }.

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices.Marshalling;

namespace GerarDanfse
{
    /// <summary>
    /// Inventário das tags COM VALOR na nota — usado pela auditoria pra resolver o fallback
    /// (`a|b`) numa única tag (a que existe no XML). Pairs = TODOS os pares "ancestral&gt;folha";
    /// Leaves = nomes de folha soltos (pra candidatas sem pai, ex.: vPis|vRetPIS).
    /// </summary>
    public sealed class TagInfo
    {
        public HashSet<string> Pairs { get; } = new HashSet<string>();
        public HashSet<string> Leaves { get; } = new HashSet<string>();
    }

    /// <summary>Pessoa v2.0 (Tomador/Destinatário/Intermediário): identidade + endereço.</summary>
    public class Pessoa
    {
        public string CnpjCpf { get; set; } = "";
        public string Im { get; set; } = "";
        public string Fone { get; set; } = "";
        public string Nome { get; set; } = "";
        public string CMun { get; set; } = "";
        public string MunicipioUF { get; set; } = "";
        public string Cep { get; set; } = "";
        public string Endereco { get; set; } = "";
        public string Email { get; set; } = "";
    }

    /// <summary>Prestador (identidade no emit; regime no DPS/prest).</summary>
    public sealed class Prestador : Pessoa
    {
        public string Simples { get; set; } = "";   // rótulo do opSimpNac
        public string RegApSN { get; set; } = "";   // regApTribSN (código)
    }

    public sealed class ServicoInfo
    {
        public string CTribNac { get; set; } = "";
        public string XTribNac { get; set; } = "";
        public string CTribMun { get; set; } = "";
        public string XTribMun { get; set; } = "";     // descrição municipal (v2.0)
        public string CNBS { get; set; } = "";
        public string XNBS { get; set; } = "";         // descrição da NBS (vai nas Infos Compl.)
        public string LocalPrest { get; set; } = "";   // nome do município
        public string UfLocPrest { get; set; } = "";   // UF do local (Tabela IBGE)
        public string PaisPrest { get; set; } = "";    // país (código ISO 2 dígitos, ex.: BR)
        public string XInfComp { get; set; } = "";     // informações complementares (texto livre)
        public string XDescServ { get; set; } = "";
    }

    /// <summary>Tributação Municipal (ISSQN) — completa.</summary>
    public sealed class IssqnInfo
    {
        public string TipoTrib { get; set; } = "";       // 1=Operação tributável...
        public string PaisResult { get; set; } = "";
        public string MunicipioIncid { get; set; } = "";
        public string UfIncid { get; set; } = "";
        public string RegimeEsp { get; set; } = "";
        public string TipoImunidade { get; set; } = "";
        public string Suspensao { get; set; } = "";      // 0/ausente=Não
        public string NProcSusp { get; set; } = "";
        public string BeneficioMun { get; set; } = "";
        public string Deducoes { get; set; } = "";
        public string CalcBM { get; set; } = "";
        public string DescIncond { get; set; } = "";
        public string Bc { get; set; } = "";
        public string Aliq { get; set; } = "";
        public string Retencao { get; set; } = "";       // 1=Não,2=Tomador,3=Interm.
        public string Apurado { get; set; } = "";
    }

    /// <summary>
    /// Tributação Federal: RETENÇÕES (IRRF/CP/CSLL retidos) + PIS/COFINS de débito PRÓPRIO.
    /// Cada campo = UMA tag (o rótulo já diz "Retida"/"Própria"); sem fallback.
    /// </summary>
    public sealed class FederalInfo
    {
        public string Irrf { get; set; } = "";
        public string Cp { get; set; } = "";
        public string Csll { get; set; } = "";
        public string TpRetPisCofins { get; set; } = "";
        public string Pis { get; set; } = "";
        public string Cofins { get; set; } = "";
    }

    /// <summary>
    /// IBS/CBS (reforma tributária) — alíquotas/indicadores em infDPS/IBSCBS; totais apurados
    /// em infNFSe/IBSCBS/totCIBS. Ausente nas notas atuais (v1.01) → tudo vazio.
    /// </summary>
    public sealed class IbsCbsInfo
    {
        public string Cst { get; set; } = "";
        public string CClassTrib { get; set; } = "";
        public string CIndOp { get; set; } = "";
        public string CLocIncid { get; set; } = "";
        public string XLocIncid { get; set; } = "";
        public string UfIncid { get; set; } = "";
        public string VBC { get; set; } = "";
        public string PRedAliqUF { get; set; } = "";
        public string PRedAliqMun { get; set; } = "";
        public string PRedAliqCBS { get; set; } = "";
        public string PIBSUF { get; set; } = "";
        public string PIBSMun { get; set; } = "";
        public string PAliqEfetMun { get; set; } = "";
        public string PAliqEfetUF { get; set; } = "";
        public string PCBS { get; set; } = "";
        public string PAliqEfetCBS { get; set; } = "";
        public string VIBSMun { get; set; } = "";
        public string VIBSUF { get; set; } = "";
        public string VIBSTot { get; set; } = "";
        public string VCBS { get; set; } = "";
        public string VTotNF { get; set; } = "";
    }

    public sealed class TotaisInfo
    {
        public string VServ { get; set; } = "";
        public string DescCond { get; set; } = "";
        public string DescIncond { get; set; } = "";
        public string IssqnRetido { get; set; } = "";
        public string TotRetFed { get; set; } = "";
        public string PisCofinsProprio { get; set; } = "";
        public string TotalRet { get; set; } = "";
        public string VLiq { get; set; } = "";
    }

    /// <summary>
    /// Totais aproximados dos tributos (Lei 12.741) — 3 formas: VALOR (vTotTrib), PERCENTUAL
    /// (pTotTrib) ou Simples Nacional (pTotTribSN). A nota usa só UMA delas.
    /// </summary>
    public sealed class TotaisAproxInfo
    {
        public string Fed { get; set; } = "";
        public string Est { get; set; } = "";
        public string Mun { get; set; } = "";
        public string PFed { get; set; } = "";   // forma percentual
        public string PEst { get; set; } = "";
        public string PMun { get; set; } = "";
        public string PSN { get; set; } = "";
    }

    /// <summary>Campos das Informações Complementares (v2.0) — raros, só quando existem.</summary>
    public sealed class InfoComplInfo
    {
        public string ChSubstda { get; set; } = "";     // NFS-e substituída (subst)
        public string DocRef { get; set; } = "";        // documento de referência
        public string CObra { get; set; } = "";         // código da obra (CNO)
        public string InscImobFisc { get; set; } = "";  // inscrição imobiliária fiscal (IBSCBS/imovel)
        public string IdAtvEvt { get; set; } = "";      // atividade de evento
        public string IdDocTec { get; set; } = "";      // documento técnico
        public string XPed { get; set; } = "";          // número do pedido
        public string XItemPed { get; set; } = "";      // item do pedido (1º; gItemPed pode repetir)
        public string XOutInf { get; set; } = "";       // outras informações (gerado pelo fisco)
    }

    /// <summary>Objeto de dados retornado por DanfseParser.ParseDanfse (espelha o JS).</summary>
    public sealed class DanfseData
    {
        public string Chave { get; set; } = "";
        public TagInfo TagInfo { get; set; } = new TagInfo();

        // Cabeçalho
        public string MunicipioEmit { get; set; } = "";
        public string UfEmit { get; set; } = "";
        public string AmbGer { get; set; } = "";  // 1=Sistema Próprio do Município, 2=Sefin Nacional NFS-e
        public string TpAmb { get; set; } = "";   // 1=Produção, 2=Homologação

        // Dados da NFS-e
        public string NNFSe { get; set; } = "";
        public string Competencia { get; set; } = "";
        public string DhEmiNFSe { get; set; } = "";
        public string NDPS { get; set; } = "";
        public string SerieDPS { get; set; } = "";
        public string DhEmiDPS { get; set; } = "";
        public string EmitenteNome { get; set; } = "";
        public string TpEmit { get; set; } = "";  // 1=Prestador, 2=Tomador, 3=Intermediário
        public string Situacao { get; set; } = "";
        public string CStat { get; set; } = "";

        /// <summary>
        /// Marca d'água (NT-008): 'CANCELADA' / 'SUBSTITUÍDA' / '' (regular). O chamador seta
        /// isto cruzando a chave com os eventos; o cStat≠100 também pode indicar.
        /// </summary>
        public string MarcaDagua { get; set; } = "";
        public string Finalidade { get; set; } = "";

        public Prestador Prest { get; set; } = new Prestador();
        public Pessoa Toma { get; set; } = new Pessoa();
        public Pessoa? Interm { get; set; }   // v2.0 — mesma estrutura de pessoa do Tomador
        public Pessoa? Dest { get; set; }     // DESTINATÁRIO DA OPERAÇÃO (IBSCBS/dest)

        public ServicoInfo Serv { get; set; } = new ServicoInfo();
        public IssqnInfo Issqn { get; set; } = new IssqnInfo();
        public FederalInfo Federal { get; set; } = new FederalInfo();
        public IbsCbsInfo IbsCbs { get; set; } = new IbsCbsInfo();
        public TotaisInfo Totais { get; set; } = new TotaisInfo();
        public TotaisAproxInfo TotaisAprox { get; set; } = new TotaisAproxInfo();
        public InfoComplInfo InfoCompl { get; set; } = new InfoComplInfo();
    }

    /// <summary>Dependências do gerador (equivalente ao `deps` do JS).</summary>
    public sealed class DanfseDeps
    {
        /// <summary>Logo em PNG (opcional).</summary>
        public byte[]? LogoPng { get; set; } = GetAsset("Icons.logo-nfs-e-horizontal.png");

        /// <summary>
        /// Fontes Arimo (clone de licença livre métrica-idêntico à Arial) em TTF. Se ausentes,
        /// cai pra "Arial" resolvida pela plataforma (no Windows funciona; em Linux registre
        /// um IFontResolver ou forneça os bytes) — correção &gt; tamanho num documento fiscal.
        /// </summary>
        public byte[]? ArimoRegular { get; set; } = GetAsset("Fonts.Arimo-Regular.ttf");
        public byte[]? ArimoBold { get; set; } = GetAsset("Fonts.Arimo-Bold.ttf");

        /// <summary>Modo conferência: imprime a tag XML (vermelho) em cada campo.</summary>
        public bool Auditoria { get; set; }

        /// <summary>Destaca os campos de RETENÇÃO com fundo amarelo.</summary>
        public bool DestacarRet { get; set; } = true;
        public bool isCancelada { get; set; } = false;
        private static byte[] GetAsset(string assetPath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream($"GerarDanfse.Assets.{assetPath}") ?? throw new FileNotFoundException($"Recurso {assetPath} não encontrado.");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    /// <summary>
    /// Tabelas auxiliares (equivalentes aos globais IBGE_MUNICIPIOS / IBGE_UF / NBS_DESC dos
    /// scripts ibge-municipios.js e nbs-tabela.js). Popule na inicialização do app (ex.: a
    /// partir de JSON embarcado). Chaves: código IBGE de 7 dígitos; prefixo UF de 2 dígitos;
    /// dígitos do cNBS.
    /// </summary>
    public static class DanfseTables
    {
        public static Dictionary<string, string> IbgeMunicipios { get; } = new Dictionary<string, string>();
        public static Dictionary<string, string> IbgeUf { get; } = new Dictionary<string, string>();
        public static Dictionary<string, string> NbsDesc { get; } = new Dictionary<string, string>();
    }
}
