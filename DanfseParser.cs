// Porte C# do gerar_danfse.js — parser do XML da NFS-e e do XML de evento.
//
// O parser usa System.Xml.Linq com busca por LocalName (à prova de namespace) — não
// depende de prefixos/namespaces, exatamente como o original fazia com o DOMParser.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static GerarDanfse.DanfseFormat;

namespace GerarDanfse
{
    public static class DanfseParser
    {
        // ───────────────────────── helpers de navegação ─────────────────────────

        /// <summary>Filho imediato por localName (à prova de namespace).</summary>
        private static XElement? Child(XElement? el, string name)
        {
            if (el == null) return null;
            foreach (var c in el.Elements())
                if (c.Name.LocalName == name) return c;
            return null;
        }

        private static string Txt(XElement? el, string name)
        {
            var c = Child(el, name);
            return c != null ? c.Value.Trim() : "";
        }

        /// <summary>Busca profunda (BFS) pelo primeiro descendente com o localName.</summary>
        private static XElement? DeepEl(XElement? el, string name)
        {
            if (el == null) return null;
            var st = new Queue<XElement>();
            st.Enqueue(el);
            while (st.Count > 0)
            {
                var e = st.Dequeue();
                foreach (var c in e.Elements())
                {
                    if (c.Name.LocalName == name) return c;
                    st.Enqueue(c);
                }
            }
            return null;
        }

        private static string Deep(XElement? el, string name)
        {
            var c = DeepEl(el, name);
            return c != null ? c.Value.Trim() : "";
        }

        // ───────────────────────── Parser ─────────────────────────

        /// <summary>
        /// Lê o XML da NFS-e (leiaute nacional) e devolve o objeto de dados do DANFSe.
        /// Retorna null se o XML for inválido ou não tiver infNFSe.
        /// </summary>
        public static DanfseData? ParseDanfse(string xmlString, bool isCancelada)
        {
            XDocument doc;
            try { doc = XDocument.Parse(xmlString); }
            catch { return null; }

            var nfse = doc.Root;
            if (nfse == null) return null;

            var infNFSe = Child(nfse, "infNFSe");
            if (infNFSe == null) return null;

            var emit = Child(infNFSe, "emit");
            var emitEnd = Child(emit, "enderNac") ?? Child(emit, "enderExt");
            var valNFSe = Child(infNFSe, "valores");
            var dps = Child(infNFSe, "DPS");
            var infDPS = Child(dps, "infDPS");
            var prest = Child(infDPS, "prest");
            var regTrib = Child(prest, "regTrib");
            var toma = Child(infDPS, "toma");
            var tomaEnd = Child(toma, "end");
            var tomaEndNac = tomaEnd != null ? (Child(tomaEnd, "endNac") ?? Child(tomaEnd, "endExt")) : null;
            var interm = Child(infDPS, "interm") ?? Child(infDPS, "intermediario");
            var intermEnd = Child(interm, "end");
            var intermEndNac = intermEnd != null ? (Child(intermEnd, "endNac") ?? Child(intermEnd, "endExt")) : null;
            var ibsCbsDPS = Child(infDPS, "IBSCBS");     // v2.0: bloco IBS/CBS sob infDPS (alíquotas/indicadores)
            var ibsCbsNFSe = Child(infNFSe, "IBSCBS");   // bloco IBS/CBS sob infNFSe (totais apurados / totCIBS)
            var dest = Child(ibsCbsDPS, "dest");         // DESTINATÁRIO DA OPERAÇÃO (IBSCBS/dest)
            var destEnd = Child(dest, "end");
            var destEndNac = destEnd != null ? (Child(destEnd, "endNac") ?? Child(destEnd, "endExt")) : null;
            var serv = Child(infDPS, "serv");
            var cServ = Child(serv, "cServ");
            var locPrest = Child(serv, "locPrest");      // local da prestação (cLocPrestacao/cPaisPrestacao)
            var valDPS = Child(infDPS, "valores");
            var trib = Child(valDPS, "trib");
            var tribMun = Child(trib, "tribMun");
            var tribFed = Child(trib, "tribFed");
            var totTrib = Child(trib, "totTrib");

            // Endereço concatenado (logradouro, nº, compl, bairro)
            string EndStr(XElement? lgrEl)
            {
                if (lgrEl == null) return "";
                var p = new[] { Txt(lgrEl, "xLgr"), Txt(lgrEl, "nro"), Txt(lgrEl, "xCpl"), Txt(lgrEl, "xBairro") }
                    .Where(s => s != "");
                return string.Join(", ", p);
            }
            // Para o tomador o logradouro fica no <end> (não no endNac)
            string EndTomaStr() => EndStr(tomaEnd);

            var idAttr = infNFSe.Attribute("Id")?.Value ?? "";
            var chave = Regex.Replace(Regex.Replace(idAttr, "^NFS", ""), @"\D", "");

            var opSimp = Txt(regTrib, "opSimpNac"); // 1=Não optante, 2=MEI, 3=ME/EPP
            var opSimpLabel = opSimp switch
            {
                "1" => "Não Optante", "2" => "Optante - MEI", "3" => "Optante - ME/EPP", _ => ""
            };
            var cStat = Txt(infNFSe, "cStat");
            var situacaoLabel = cStat == "100"
                ? "NFS-e regular (Autorizada)"
                : (cStat != "" ? "Situação " + cStat : "");

            // Pessoa v2.0 (Tomador/Destinatário/Intermediário): identidade + endereço.
            Pessoa? PessoaDe(XElement? el, XElement? end, XElement? endNac) => el == null ? null : new Pessoa
            {
                CnpjCpf = FirstNonEmpty(Txt(el, "CNPJ"), Txt(el, "CPF"), Txt(el, "NIF")),
                Im = Txt(el, "IM"),
                Fone = Txt(el, "fone"),
                Nome = Txt(el, "xNome"),
                CMun = Txt(endNac, "cMun"),
                MunicipioUF = MunUFStr(Txt(endNac, "cMun"), Txt(endNac, "UF")),
                Cep = FirstNonEmpty(Txt(endNac, "CEP"), Txt(endNac, "cEndPost")),
                Endereco = EndStr(end),
                Email = Txt(el, "email"),
            };

            // Inventário das tags COM VALOR nesta nota — usado pela auditoria pra resolver o
            // fallback (`a|b`) numa única tag (a que existe no XML). Pairs = TODOS os pares
            // "ancestral>folha" (casa leitura direta e busca profunda; o "ancestral real"
            // desambigua CNPJ/CPF/NIF que repetem em emit/toma); Leaves = folhas soltas.
            var tagInfo = new TagInfo();
            foreach (var e in nfse.Descendants())
            {
                if (!e.HasElements && !string.IsNullOrWhiteSpace(e.Value))
                {
                    tagInfo.Leaves.Add(e.Name.LocalName);
                    for (var a = e.Parent; a != null; a = a.Parent)
                        tagInfo.Pairs.Add(a.Name.LocalName + ">" + e.Name.LocalName);
                }
            }

            return new DanfseData
            {
                Chave = chave,
                TagInfo = tagInfo,
                // Cabeçalho
                MunicipioEmit = Txt(infNFSe, "xLocEmi"),
                UfEmit = MunUF(Txt(emitEnd, "cMun"), Txt(emitEnd, "UF")),
                AmbGer = Txt(infNFSe, "ambGer"),   // 1=Sistema Próprio do Município, 2=Sefin Nacional NFS-e
                TpAmb = Txt(infDPS, "tpAmb"),      // 1=Produção, 2=Homologação
                // Dados da NFS-e
                NNFSe = Txt(infNFSe, "nNFSe"),
                Competencia = Txt(infDPS, "dCompet"),
                DhEmiNFSe = Txt(infNFSe, "dhProc"),
                NDPS = Txt(infDPS, "nDPS"),
                SerieDPS = Txt(infDPS, "serie"),
                DhEmiDPS = Txt(infDPS, "dhEmi"),
                EmitenteNome = Txt(emit, "xNome"),
                TpEmit = Txt(infDPS, "tpEmit"),    // campo "EMITENTE DA NFS-e"
                Situacao = situacaoLabel,
                CStat = cStat,
                // Marca d'água (NT-008): o chamador seta cruzando a chave com os eventos.
                MarcaDagua = isCancelada ? "CANCELADA" : "",
                Finalidade = FirstNonEmpty(Txt(infDPS, "finNFSe"), "NFS-e"),
                // Prestador (identidade no emit; regime no DPS/prest)
                Prest = new Prestador
                {
                    CnpjCpf = FirstNonEmpty(Txt(emit, "CNPJ"), Txt(emit, "CPF"), Txt(emit, "NIF"), Txt(prest, "CNPJ"), Txt(prest, "CPF")),
                    Im = FirstNonEmpty(Txt(emit, "IM"), Txt(prest, "IM")),
                    Fone = Txt(emit, "fone"),
                    Nome = Txt(emit, "xNome"),
                    CMun = Txt(emitEnd, "cMun"),
                    MunicipioUF = MunUFStr(Txt(emitEnd, "cMun"), Txt(emitEnd, "UF")),
                    Cep = FirstNonEmpty(Txt(emitEnd, "CEP"), Txt(emitEnd, "cEndPost")),
                    Endereco = EndStr(emitEnd),
                    Email = Txt(emit, "email"),
                    Simples = opSimpLabel,
                    RegApSN = Txt(regTrib, "regApTribSN"),
                },
                // Tomador
                Toma = new Pessoa
                {
                    CnpjCpf = FirstNonEmpty(Txt(toma, "CNPJ"), Txt(toma, "CPF"), Txt(toma, "NIF")),
                    Im = Txt(toma, "IM"),
                    Fone = Txt(toma, "fone"),
                    Nome = Txt(toma, "xNome"),
                    CMun = Txt(tomaEndNac, "cMun"),
                    MunicipioUF = MunUFStr(Txt(tomaEndNac, "cMun"), Txt(tomaEndNac, "UF")),
                    Cep = FirstNonEmpty(Txt(tomaEndNac, "CEP"), Txt(tomaEndNac, "cEndPost")),
                    Endereco = EndTomaStr(),
                    Email = Txt(toma, "email"),
                },
                // Intermediário e Destinatário (v2.0) — mesma estrutura de pessoa do Tomador
                Interm = PessoaDe(interm, intermEnd, intermEndNac),
                Dest = PessoaDe(dest, destEnd, destEndNac),
                // Serviço
                Serv = new ServicoInfo
                {
                    CTribNac = Txt(cServ, "cTribNac"),
                    XTribNac = Txt(infNFSe, "xTribNac"),
                    CTribMun = Txt(cServ, "cTribMun"),
                    XTribMun = Txt(cServ, "xTribMun"),                  // descrição municipal (v2.0)
                    CNBS = Txt(cServ, "cNBS"),
                    XNBS = Txt(infNFSe, "xNBS"),                        // descrição da NBS (Infos Compl.)
                    LocalPrest = Txt(infNFSe, "xLocPrestacao"),         // nome do município
                    UfLocPrest = MunUF(Txt(locPrest, "cLocPrestacao"), Txt(locPrest, "UF")),
                    PaisPrest = Txt(locPrest, "cPaisPrestacao"),        // país (ISO 2 dígitos, ex.: BR)
                    XInfComp = Deep(serv, "xInfComp"),                  // infos complementares (texto livre)
                    XDescServ = Txt(cServ, "xDescServ"),
                },
                // Tributação Municipal (ISSQN) — completa
                Issqn = new IssqnInfo
                {
                    TipoTrib = Txt(tribMun, "tribISSQN"),               // 1=Operação tributável...
                    PaisResult = Deep(tribMun, "cPaisResult"),
                    MunicipioIncid = Txt(infNFSe, "xLocIncid"),
                    UfIncid = MunUF(Txt(infNFSe, "cLocIncid")),         // UF de incidência (Tabela IBGE)
                    RegimeEsp = FirstNonEmpty(Txt(tribMun, "tpRegEsp"), Txt(regTrib, "regEspTrib")),
                    TipoImunidade = Txt(tribMun, "tpImunidade"),
                    Suspensao = Deep(tribMun, "tpSusp"),                // 0/ausente=Não
                    NProcSusp = Deep(tribMun, "nProcesso"),
                    BeneficioMun = FirstNonEmpty(Deep(tribMun, "cBenef"), Deep(tribMun, "tBM")),
                    Deducoes = FirstNonEmpty(Deep(tribMun, "vDeducao"), Deep(tribMun, "vRedBCBM")),
                    CalcBM = FirstNonEmpty(Deep(tribMun, "pRedBCBM"), Deep(tribMun, "pAliqAplicBM")),
                    DescIncond = FirstNonEmpty(Txt(valNFSe, "vDescIncond"), Deep(tribMun, "vDescIncond")),
                    Bc = Txt(valNFSe, "vBC"),
                    Aliq = FirstNonEmpty(Txt(valNFSe, "pAliqAplic"), Txt(tribMun, "pAliq")),
                    Retencao = Txt(tribMun, "tpRetISSQN"),              // 1=Não,2=Tomador,3=Interm.
                    Apurado = Txt(valNFSe, "vISSQN"),
                },
                // Tributação Federal (busca ampla no valores p/ não perder retenções)
                Federal = new FederalInfo
                {
                    Irrf = Deep(valDPS, "vRetIRRF"),
                    Cp = Deep(valDPS, "vRetCP"),
                    Csll = Deep(valDPS, "vRetCSLL"),
                    // "Descrição Contrib. Sociais - Retidas" = Tipo de Retenção PIS/COFINS e CSLL
                    TpRetPisCofins = Deep(tribFed, "tpRetPisCofins"),
                    Pis = Deep(valDPS, "vPis"),
                    Cofins = Deep(valDPS, "vCofins"),
                },
                // IBS/CBS (reforma tributária) — busca profunda porque o aninhamento é complexo
                // (valores/uf|mun|fed, totCIBS/gIBS|gCBS). Ausente nas notas v1.01 → tudo vazio.
                IbsCbs = new IbsCbsInfo
                {
                    Cst = Deep(ibsCbsDPS, "CST"),
                    CClassTrib = Deep(ibsCbsDPS, "cClassTrib"),
                    CIndOp = Deep(ibsCbsDPS, "cIndOp"),
                    CLocIncid = Deep(ibsCbsDPS, "cLocalidadeIncid"),
                    XLocIncid = Deep(ibsCbsDPS, "xLocalidadeIncid"),
                    UfIncid = MunUF(Deep(ibsCbsDPS, "cLocalidadeIncid")),
                    VBC = Deep(ibsCbsDPS, "vBC"),
                    PRedAliqUF = Deep(ibsCbsDPS, "pRedAliqUF"),
                    PRedAliqMun = Deep(ibsCbsDPS, "pRedAliqMun"),
                    PRedAliqCBS = Deep(ibsCbsDPS, "pRedAliqCBS"),
                    PIBSUF = Deep(ibsCbsDPS, "pIBSUF"),
                    PIBSMun = Deep(ibsCbsDPS, "pIBSMun"),
                    PAliqEfetMun = Deep(ibsCbsDPS, "pAliqEfetMun"),
                    PAliqEfetUF = Deep(ibsCbsDPS, "pAliqEfetUF"),
                    PCBS = Deep(ibsCbsDPS, "pCBS"),
                    PAliqEfetCBS = Deep(ibsCbsDPS, "pAliqEfetCBS"),
                    VIBSMun = Deep(ibsCbsNFSe, "vIBSMun"),
                    VIBSUF = Deep(ibsCbsNFSe, "vIBSUF"),
                    VIBSTot = Deep(ibsCbsNFSe, "vIBSTot"),
                    VCBS = Deep(ibsCbsNFSe, "vCBS"),
                    VTotNF = Deep(ibsCbsNFSe, "vTotNF"),
                },
                // Totais
                Totais = new TotaisInfo
                {
                    VServ = FirstNonEmpty(Txt(valNFSe, "vServ"), Deep(valDPS, "vServ")),
                    DescCond = FirstNonEmpty(Txt(valNFSe, "vDescCond"), Deep(tribMun, "vDescCond")),
                    DescIncond = FirstNonEmpty(Txt(valNFSe, "vDescIncond"), Deep(tribMun, "vDescIncond")),
                    IssqnRetido = Txt(valNFSe, "vISSQNRet"),
                    TotRetFed = Txt(valNFSe, "vTotalRetFed"),
                    PisCofinsProprio = Deep(tribFed, "vPisCofins"),
                    TotalRet = Txt(valNFSe, "vTotalRet"),
                    VLiq = Txt(valNFSe, "vLiq"),
                },
                // Totais aproximados dos tributos (Lei 12.741)
                TotaisAprox = new TotaisAproxInfo
                {
                    Fed = Deep(totTrib, "vTotTribFed"),
                    Est = Deep(totTrib, "vTotTribEst"),
                    Mun = Deep(totTrib, "vTotTribMun"),
                    PFed = Deep(totTrib, "pTotTribFed"),  // forma percentual
                    PEst = Deep(totTrib, "pTotTribEst"),
                    PMun = Deep(totTrib, "pTotTribMun"),
                    PSN = Deep(valDPS, "pTotTribSN"),
                },
                // Campos das Informações Complementares (v2.0) — raros, só quando existem.
                InfoCompl = new InfoComplInfo
                {
                    ChSubstda = Deep(infDPS, "chSubstda"),          // NFS-e substituída (subst)
                    DocRef = Deep(serv, "docRef"),                  // documento de referência
                    CObra = Deep(serv, "cObra"),                    // código da obra (CNO)
                    InscImobFisc = Deep(ibsCbsDPS, "inscImobFisc"), // inscrição imobiliária (IBSCBS/imovel)
                    IdAtvEvt = Deep(serv, "idAtvEvt"),              // atividade de evento
                    IdDocTec = Deep(serv, "idDocTec"),              // documento técnico
                    XPed = Deep(serv, "xPed"),                      // número do pedido
                    XItemPed = Deep(serv, "xItemPed"),              // item do pedido (1º)
                    XOutInf = Txt(infNFSe, "xOutInf"),              // outras infos (gerado pelo fisco)
                },
            };
        }

        /// <summary>Equivalente ao `a || b || c` do JS para strings.</summary>
        internal static string FirstNonEmpty(params string[] vs)
        {
            foreach (var v in vs) if (!string.IsNullOrEmpty(v)) return v;
            return "";
        }
    }
}
