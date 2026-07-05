// Porte C# do gerar_danfse.js — helpers de formatação e rótulos.

using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GerarDanfse
{
    public static class DanfseFormat
    {
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        /// <summary>parseFloat do JS: aceita ponto decimal; vazio/inválido = NaN.</summary>
        public static double Num(string? v) =>
            double.TryParse((v ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                ? n : double.NaN;

        public static double NumOr0(string? v) { var n = Num(v); return double.IsNaN(n) ? 0 : n; }

        /// <summary>"2026-05-31..." → "31/05/2026" (mantém o original se não casar).</summary>
        public static string FmtData(string? iso)
        {
            var m = Regex.Match(iso ?? "", @"^(\d{4})-(\d{2})-(\d{2})");
            return m.Success ? $"{m.Groups[3].Value}/{m.Groups[2].Value}/{m.Groups[1].Value}" : (iso ?? "");
        }

        public static string FmtDataHora(string? iso)
        {
            var m = Regex.Match(iso ?? "", @"^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):?(\d{2})?");
            if (!m.Success) return iso ?? "";
            var ss = m.Groups[6].Success && m.Groups[6].Value != "" ? m.Groups[6].Value : "00";
            return $"{m.Groups[3].Value}/{m.Groups[2].Value}/{m.Groups[1].Value} {m.Groups[4].Value}:{m.Groups[5].Value}:{ss}";
        }

        public static string FmtMoeda(string? v)
        {
            var n = Num(v);
            return double.IsNaN(n) ? "" : FmtMoeda(n);
        }

        public static string FmtMoeda(double n) => "R$ " + n.ToString("N2", PtBr);

        public static string FmtCep(string? c)
        {
            c = Regex.Replace(c ?? "", @"\D", "");
            return c.Length == 8 ? c.Substring(0, 5) + "-" + c.Substring(5) : c;
        }

        public static string FmtFone(string? f)
        {
            f = Regex.Replace(f ?? "", @"\D", "");
            if (f.Length == 11) return Regex.Replace(f, @"^(\d{2})(\d{5})(\d{4})$", "($1) $2-$3");
            if (f.Length == 10) return Regex.Replace(f, @"^(\d{2})(\d{4})(\d{4})$", "($1) $2-$3");
            return f;
        }

        public static string FmtDoc(string? d)
        {
            d = Regex.Replace(d ?? "", @"\D", "");
            if (d.Length == 14) return Regex.Replace(d, @"^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$", "$1.$2.$3/$4-$5");
            if (d.Length == 11) return Regex.Replace(d, @"^(\d{3})(\d{3})(\d{3})(\d{2})$", "$1.$2.$3-$4");
            return d;
        }

        /// <summary>Código de Tributação Nacional: "060102" → "06.01.02" (item LC 116/nacional).</summary>
        public static string FmtCTribNac(string? c)
        {
            var d = Regex.Replace(c ?? "", @"\D", "");
            return d.Length == 6 ? $"{d.Substring(0, 2)}.{d.Substring(2, 2)}.{d.Substring(4, 2)}" : (c ?? "");
        }

        /// <summary>Código NBS: 9 dígitos → "n.nnnn.nn.nn" (ex.: "114013900" → "1.1401.39.00").</summary>
        public static string FmtNBS(string? c)
        {
            var d = Regex.Replace(c ?? "", @"\D", "");
            return d.Length == 9 ? $"{d.Substring(0, 1)}.{d.Substring(1, 4)}.{d.Substring(5, 2)}.{d.Substring(7, 2)}" : (c ?? "");
        }

        // Resolução de município pela tabela IBGE (DanfseTables)
        public static string MunNome(string? code)
        {
            code = Regex.Replace(code ?? "", @"\D", "");
            return DanfseTables.IbgeMunicipios.TryGetValue(code, out var n) ? n : code;
        }

        public static string MunUF(string? code, string? fb = null)
        {
            if (!string.IsNullOrEmpty(fb)) return fb!;
            code = Regex.Replace(code ?? "", @"\D", "");
            var pref = code.Length >= 2 ? code.Substring(0, 2) : code;
            return DanfseTables.IbgeUf.TryGetValue(pref, out var uf) ? uf : "";
        }

        /// <summary>Município "Nome / UF" (spec v2.0: concatenar nome do município com a UF).</summary>
        public static string MunUFStr(string? code, string? ufFb)
        {
            var nome = MunNome(code);
            var uf = MunUF(code, ufFb);
            return string.Join(" / ", new[] { nome, uf }.Where(s => !string.IsNullOrEmpty(s)));
        }

        /// <summary>
        /// Descrição da NBS pela tabela oficial — fallback quando o XML não traz &lt;xNBS&gt;.
        /// Chave = dígitos do cNBS.
        /// </summary>
        public static string NbsDesc(string? code)
        {
            code = Regex.Replace(code ?? "", @"\D", "");
            if (code == "") return "";
            return DanfseTables.NbsDesc.TryGetValue(code, out var d) ? d : "";
        }

        public static string RegApLabel(string? v) => v switch
        {
            "1" => "Regime de apuração dos tributos federais e municipal pelo Simples Nacional",
            "2" => "Federal pelo SN e Municipal por fora",
            "3" => "Por fora do SN",
            _ => v ?? "",
        };

        public static string TipoTribLabel(string? v) => v switch
        {
            "1" => "Operação Tributável", "2" => "Imunidade", "3" => "Exportação",
            "4" => "Não Incidência", "5" => "Imune", "6" => "Suspensa",
            _ => v ?? "",
        };

        public static string RetISSLabel(string? v) => v switch
        {
            "1" => "Não Retido", "2" => "Retido pelo Tomador", "3" => "Retido pelo Intermediário",
            _ => v ?? "",
        };

        /// <summary>EMITENTE DA NFS-e (tpEmit) — 1=Prestador, 2=Tomador, 3=Intermediário.</summary>
        public static string TpEmitLabel(string? v) => v switch
        {
            "1" => "Prestador", "2" => "Tomador", "3" => "Intermediário",
            _ => v ?? "",
        };

        /// <summary>
        /// Regime Especial de Tributação Municipal (regEspTrib / tpRegEsp), layout oficial.
        /// Vazio/ausente e código 0 = "Nenhum"; código desconhecido cai pro próprio valor.
        /// </summary>
        public static string RegEspLabel(string? v)
        {
            if (string.IsNullOrEmpty(v)) return "Nenhum";
            return v switch
            {
                "0" => "Nenhum",
                "1" => "Ato Cooperado (Cooperativa)",
                "2" => "Estimativa",
                "3" => "Microempresa Municipal",
                "4" => "Notário ou Registrador",
                "5" => "Profissional Autônomo",
                "6" => "Sociedade de Profissionais",
                "9" => "Outros",
                _ => v!,
            };
        }

        /// <summary>
        /// Tipo de Retenção PIS/COFINS e CSLL (tpRetPisCofins), layout oficial — 0 a 9.
        /// Vazio/ausente = '' (vira "-"); código desconhecido cai pro próprio valor.
        /// </summary>
        public static string RetPisCofinsLabel(string? v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return v switch
            {
                "0" => "PIS/COFINS/CSLL Não Retidos",
                "1" => "PIS/COFINS Retido",
                "2" => "PIS/COFINS Não Retido",
                "3" => "PIS/COFINS/CSLL Retidos",
                "4" => "PIS/COFINS Retidos, CSLL Não Retido",
                "5" => "PIS Retido, COFINS/CSLL Não Retido",
                "6" => "COFINS Retido, PIS/CSLL Não Retido",
                "7" => "PIS Não Retido, COFINS/CSLL Retidos",
                "8" => "PIS/COFINS Não Retidos, CSLL Retido",
                "9" => "COFINS Não Retido, PIS/CSLL Retidos",
                _ => v!,
            };
        }

        public static string SuspLabel(string? v)
        {
            if (string.IsNullOrEmpty(v)) return "Não";
            return (v == "0" || v == "1") ? "Não" : "Sim";
        }
    }
}
