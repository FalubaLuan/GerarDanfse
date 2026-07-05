// Porte C# do gerar_danfse.js — layout do DANFSe v2.0 com PDFsharp + QRCoder.
//
// Layout espelhado no DANFSe v1.0 oficial (portal nacional): títulos de Emitente/Tomador
// "na 1ª célula", rótulos em negrito caixa-normal, "-" em campos vazios, Tributação
// Municipal completa, Totais Aproximados (Lei 12.741) e NBS nas Infos.
//
// NOTA DE COORDENADAS: o pdf-lib usa origem no CANTO INFERIOR esquerdo (y cresce pra cima);
// o PDFsharp usa origem no canto SUPERIOR esquerdo (y cresce pra baixo). Todo o cálculo de
// layout abaixo foi mantido no sistema do pdf-lib (idêntico ao JS) e a conversão é feita
// só na hora de desenhar: yPdfSharp = A4H - yPdfLib. O texto é desenhado com alinhamento
// de BASELINE (XLineAlignment.BaseLine), igual ao drawText do pdf-lib.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using QRCoder;
using static GerarDanfse.DanfseFormat;

namespace GerarDanfse
{
    public static class DanfsePdfBuilder
    {
        private const double A4W = 595.28, A4H = 841.89;
        private const double CmPt = 28.3464567;
        private static double Cm(double v) => v * CmPt;

        // Célula de uma linha de campos (equivalente a {f, l, v, opt} do JS)
        private sealed class Cell
        {
            public double F; public string L = ""; public string V = ""; public Opt O = new Opt();
        }

        private sealed class Opt
        {
            public bool Raw, Shaded, Hl, ValBold;
            public double? ValSize, LabSize;
            public string? Tag;
        }

        private sealed class PTags
        {
            public string? Cnpj, Im, Fone, Nome, Mun, IbgeCep, End, Email;
        }

        public static byte[] Build(DanfseData data, DanfseDeps deps)
        {
            bool audit = deps.Auditoria;      // modo conferência: imprime a tag XML (vermelho) em cada campo
            bool hlRet = deps.DestacarRet;    // destaca os campos de RETENÇÃO com fundo amarelo

            // Só destaca quando HÁ retenção (valor retido > 0 ou indicador "retido").
            bool TemVal(string? v) => Num(v) > 0;
            bool issRetido = data.Issqn.Retencao == "2" || data.Issqn.Retencao == "3"; // 2=Tomador, 3=Interm.
            var tpPC = data.Federal.TpRetPisCofins;
            bool pisCofRetido = !string.IsNullOrEmpty(tpPC) && tpPC != "0" && tpPC != "2"; // 0/2 = não retido

            var pdf = new PdfDocument();
            var pages = new List<(PdfPage Page, XGraphics G)>();
            var disposables = new List<IDisposable>();
            XGraphics gfx = null!;

            void NovaPaginaCore()
            {
                var p = pdf.AddPage();
                p.Width = XUnit.FromPoint(A4W);
                p.Height = XUnit.FromPoint(A4H);
                gfx = XGraphics.FromPdfPage(p);
                pages.Add((p, gfx));
            }
            NovaPaginaCore();

            // ── Fontes (norma 2.4): por padrão Arimo — clone de LICENÇA LIVRE métrica-idêntico
            //    à Arial — registrada via IFontResolver. Se os bytes não vierem em deps, cai pra
            //    "Arial" resolvida pela plataforma (Windows). Correção > tamanho num doc fiscal. ──
            string family = "Arial";
            if (deps.ArimoRegular is { Length: > 0 } && deps.ArimoBold is { Length: > 0 })
            {
                DanfseFontResolver.Register(deps.ArimoRegular, deps.ArimoBold);
                family = DanfseFontResolver.FamilyName;
            }
            var fontCache = new Dictionary<(bool Bold, double Size), XFont>();
            XFont F(bool bold, double size)
            {
                var key = (bold, size);
                if (!fontCache.TryGetValue(key, out var f))
                {
                    f = new XFont(family, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
                    fontCache[key] = f;
                }
                return f;
            }
            var baseline = new XStringFormat { Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.BaseLine };

            var GRAY = XColor.FromArgb(242, 242, 242);   // cinza claro 5% de densidade (norma 2.2.3)
            var GRAY_DESTAQUE = XColor.FromArgb(200, 200, 200); // destaque dos campos de retenção
            var K = XColors.Black;
            var RED = XColor.FromArgb(217, 0, 0);
            var K35 = XColor.FromArgb(166, 166, 166);    // 35% preto (marca d'água e nº de folha)
            const double LINE = 0.5;
            var penK = new XPen(K, LINE);
            var brushK = new XSolidBrush(K);

            // Altura padrão da linha: 2 linhas (rótulo + valor). No modo auditoria sobe pra 3
            // linhas (rótulo + valor + tag em vermelho embaixo).
            double RH = audit ? Cm(0.88) : Cm(0.64);

            double mg = Cm(0.20);      // moldura externa (margem corpo→papel: norma 2.2.2)
            double pad = Cm(0.15);     // RESPIRO lateral/inferior entre a moldura e a tabela interna
            double padTop = Cm(0.32);  // RESPIRO SUPERIOR
            double L = mg + pad, R = A4W - mg - pad;   // área da tabela
            double W = R - L;
            double contentTop = A4H - mg - padTop;
            double y = contentTop;

            // ── primitivas de desenho (conversão pdf-lib → PDFsharp acontece AQUI) ──
            void T(string s, double x, double yBase, double size, bool bold, XColor col)
            {
                if (string.IsNullOrEmpty(s)) return;
                gfx.DrawString(s, F(bold, size), new XSolidBrush(col), new XPoint(x, A4H - yBase), baseline);
            }
            double TW(string s, double size, bool bold = false) =>
                string.IsNullOrEmpty(s) ? 0 : gfx.MeasureString(s, F(bold, size)).Width;
            // yTop = borda SUPERIOR do retângulo em coordenadas pdf-lib
            void FillRect(double x, double yTop, double w, double h, XColor col) =>
                gfx.DrawRectangle(new XSolidBrush(col), x, A4H - yTop, w, h);
            void HLine(double x1, double x2, double yy) =>
                gfx.DrawLine(penK, x1, A4H - yy, x2, A4H - yy);
            void VLine(double x, double yA, double yB) =>
                gfx.DrawLine(penK, x, A4H - yA, x, A4H - yB);

            // ── helpers de texto (portes diretos do JS) ──
            string Clip(string? s0, int max)
            {
                var s = s0 ?? "";
                return s.Length > max ? s.Substring(0, Math.Max(0, max - 1)) + "…" : s;
            }
            // Corte por LARGURA REAL (mede a fonte) — não estoura a célula.
            string ClipW(string? s0, double size, bool bold, double maxW)
            {
                var s = s0 ?? "";
                if (TW(s, size, bold) <= maxW) return s;
                while (s.Length > 0 && TW(s + "…", size, bold) > maxW) s = s.Substring(0, s.Length - 1);
                return s + "…";
            }
            // Tag de auditoria pra exibição. Resolve o fallback (`a|b`) pra UMA tag — a que
            // existe no XML desta nota (via data.TagInfo). Mantém o " ou " SÓ quando há
            // divergência: nenhuma candidata presente ou mais de uma.
            string FmtTag(string? s0)
            {
                var s = s0 ?? "";
                if (!s.Contains('|')) return s;
                var info = data.TagInfo;
                if (info == null) return s.Replace("|", " ou ");
                var lastParent = "";
                var cands = s.Split('|').Select(t => t.Trim()).Select(t =>
                {
                    var gt = t.LastIndexOf('>');
                    if (gt >= 0) { lastParent = t.Substring(0, gt); return t; }
                    return lastParent != "" ? lastParent + ">" + t : t; // herda o pai do shorthand
                }).ToList();
                bool Has(string t) => t.Contains('>') ? info.Pairs.Contains(t) : info.Leaves.Contains(t);
                var hit = cands.Where(Has).ToList();
                if (hit.Count == 1) return hit[0];                 // caso normal: 1 campo = 1 tag
                if (hit.Count > 1) return string.Join(" ou ", hit); // várias: anomalia → mostra as presentes
                return string.Join(" ou ", cands);                  // nenhuma: mostra as esperadas
            }
            string D(string? v) => string.IsNullOrEmpty(v) ? "-" : v!; // "-" em vazios, igual ao oficial
            string Pc(string? v) => !string.IsNullOrEmpty(v) ? v + "%" : "";  // percentual (anexa "%")
            string Pcs(params string?[] vs) => string.Join(" / ",
                vs.Where(v => !string.IsNullOrEmpty(v)).Select(v => v + "%")); // "% / %"
            string Slash(params string?[] vs) => string.Join(" / ",
                vs.Where(v => !string.IsNullOrEmpty(v)));           // concatena com " / "
            List<string> Wrap(string? s0, int max, int lines)
            {
                var s = Regex.Replace(s0 ?? "", @"\s+", " ").Trim();
                var outL = new List<string>(); var cur = "";
                foreach (var word in s.Split(' '))
                {
                    if ((cur + " " + word).Trim().Length > max) { if (cur != "") outL.Add(cur); cur = word; }
                    else cur = (cur + " " + word).Trim();
                    if (outL.Count >= lines) break;
                }
                if (cur != "" && outL.Count < lines) outL.Add(cur);
                return outL.Take(lines).ToList();
            }

            // ── helpers de células/linhas ──
            Cell C(double f, string l, string v, Opt? o = null) => new Cell { F = f, L = l, V = v, O = o ?? new Opt() };

            // Célula: fundo cinza opcional + SÓ linhas horizontais (topo e base). O oficial não
            // tem divisórias verticais internas.
            /*void Box(double x, double yTop, double w, double h, bool shaded)
            {
                if (shaded) FillRect(x, yTop, w, h, GRAY);
                HLine(x, x + w, yTop);
                HLine(x, x + w, yTop - h);
            }*/
            // Fundo cinza opcional de uma célula, SEM linhas (usado nos títulos de grupo).
            void Fill(double x, double yTop, double w, double h, XColor? col = null) =>
                FillRect(x, yTop, w, h, col ?? GRAY);
            // Separador de GRUPO: única linha horizontal (ponta a ponta L→R) no topo do grupo.
            void Sep(double yTop) => HLine(L, R, yTop);

            // Campo: rótulo (6pt negrito) em cima, valor (7pt normal) embaixo. Vazio = "-".
            // Modo auditoria (opt.Tag): rótulo em cima, valor no meio e a tag XML em VERMELHO
            // na linha de baixo. Sem valor, mostra só a tag (não desenha o "-").
            void Field(double x, double yTop, double w, double h, string lab, string? val, Opt opt)
            {
                if (opt.Shaded) Fill(x, yTop, w, h);           // só fundo; sem linhas
                if (opt.Hl) Fill(x, yTop, w, h, GRAY_DESTAQUE);        // destaque amarelo (retenção)
                if (!string.IsNullOrEmpty(lab))
                    T(Clip(lab, (int)Math.Floor(w / 2.85)), x + 2, yTop - 7, opt.LabSize ?? 6, true, K);
                var vSize = opt.ValSize ?? 7;
                var vBold = opt.ValBold;
                var rawStr = val ?? "";
                var hasVal = rawStr != "";
                if (audit && !string.IsNullOrEmpty(opt.Tag))
                {
                    if (hasVal) T(ClipW(rawStr, vSize, vBold, w - 4), x + 2, yTop - 15, vSize, vBold, K);
                    T(Clip(FmtTag(opt.Tag), (int)Math.Floor((w - 4) / 2.4)), x + 2, yTop - h + 3, 5, false, RED);
                    return;
                }
                var v = opt.Raw ? rawStr : D(val);
                if (v != "") T(ClipW(v, vSize, vBold, w - 4), x + 2, yTop - h + 3, vSize, vBold, K);
            }
            // Linha de campos: cells = [{F: fração da largura, L, V, O}]
            double Row(double yTop, double h, Cell[] cells, double? x0 = null, double? totalW = null)
            {
                var x = x0 ?? L; var tw = totalW ?? W;
                foreach (var c in cells) { var w = tw * c.F; Field(x, yTop, w, h, c.L, c.V, c.O); x += w; }
                return yTop - h;
            }
            // Título de bloco (faixa cinza, 7pt negrito caixa alta)
            double Title(double yTop, string text)
            {
                var h = Cm(0.42);
                Fill(L, yTop, W, h);   // faixa cinza (sem linhas)
                Sep(yTop);             // separador de grupo no topo
                T(text.ToUpperInvariant(), L + 3, yTop - h + (h - 7) / 2 + 1, 7, true, K);
                return yTop - h;
            }
            // Linha que ABRE uma seção no padrão do oficial: 1ª célula = TÍTULO (cinza, 0,25 da
            // largura) + os primeiros campos preenchendo o resto da grade de 4 colunas.
            // noSep=true: célula-título cinza SEM separador (2º título dentro do mesmo grupo).
            double TituloRow(double yTop, string text, Cell[] cells, bool noSep = false)
            {
                var h = RH; var tw = W * 0.25;
                Fill(L, yTop, tw, h);   // 1ª célula cinza (título), sem linhas
                if (!noSep) Sep(yTop);  // separador de grupo no topo (ponta a ponta)
                T(text, L + 3, yTop - h + (h - 7) / 2 + 1, 7, true, K);
                var x = L + tw;
                foreach (var c in cells) { var w = W * c.F; Field(x, yTop, w, h, c.L, c.V, c.O); x += w; }
                return yTop - h;
            }
            // Quadro de texto longo (rótulo + N linhas)
            double Textbox(double yTop, string lab, string? val, int maxLines, int minLines, string? tag)
            {
                var showTag = audit && !string.IsNullOrEmpty(tag);
                var ls = Wrap(val, 118, maxLines);
                var n = Math.Max(minLines, ls.Count);
                const double lineH = 9;
                // Sem rótulo, o valor começa no topo (firstY 10) — mas se há tag de auditoria,
                // reserva a linha do topo pra ela (firstY 15.5) pra não sobrepor o valor.
                var firstY = (!string.IsNullOrEmpty(lab) || showTag) ? 15.5 : 10.0;
                var h = firstY + (n - 1) * lineH + 4;
                if (!string.IsNullOrEmpty(lab)) T(lab, L + 2, yTop - 7, 6, true, K);
                if (showTag) T(FmtTag(tag), L + 2 + (!string.IsNullOrEmpty(lab) ? TW(lab, 6, true) + 6 : 0),
                               yTop - 7, 5, false, RED);
                for (int i = 0; i < ls.Count; i++) T(ls[i], L + 2, yTop - firstY - i * lineH, 7, false, K);
                return yTop - h;
            }
            // Quadro das Informações Complementares no modo AUDITORIA: UMA info por bloco — o
            // texto (preto) e a sua tag (vermelho) na linha logo abaixo. Texto vazio mostra só
            // a tag.
            double InfoboxAudit(double yTop, List<(string Txt, string Tag)> items)
            {
                const double lineH = 9;
                var rows = new List<(string T, bool Red)>();
                foreach (var it in items)
                {
                    if (it.Txt != "") foreach (var ln in Wrap(it.Txt, 118, 4)) rows.Add((ln, false));
                    rows.Add((FmtTag(it.Tag), true));
                }
                if (rows.Count == 0) rows.Add(("", false));
                var h = 10 + (rows.Count - 1) * lineH + 4;
                for (int i = 0; i < rows.Count; i++)
                    T(rows[i].T, L + 2, yTop - 10 - i * lineH, rows[i].Red ? 5 : 7, false, rows[i].Red ? RED : K);
                return yTop - h;
            }
            // Quadro das Infos no modo NORMAL: cada CAMPO numa LINHA própria; vazio é pulado;
            // um campo longo ainda quebra em até 4 linhas.
            double InfoboxNormal(double yTop, List<(string Txt, string Tag)> items)
            {
                const double lineH = 9;
                var lines = new List<string>();
                foreach (var it in items) if (it.Txt != "") foreach (var ln in Wrap(it.Txt, 118, 4)) lines.Add(ln);
                if (lines.Count == 0) lines.Add("");
                var h = 10 + (lines.Count - 1) * lineH + 4;
                for (int i = 0; i < lines.Count; i++) T(lines[i], L + 2, yTop - 10 - i * lineH, 7, false, K);
                return yTop - h;
            }
            // Linha centralizada (ex.: "INTERMEDIÁRIO ... NÃO IDENTIFICADO")
            double LinhaCentral(double yTop, string text, double? h0 = null)
            {
                var h = h0 ?? Cm(0.42);
                Sep(yTop);
                var tw = TW(text, 7, bold: true);
                T(text, L + (W - tw) / 2, yTop - h + (h - 7) / 2 + 1, 7, true, K);
                return yTop - h;
            }

            // ── Quebra de página entre GRUPOS (nunca no meio de um) ──
            double canhotoReserva = audit ? 0 : Cm(1.0) + Cm(0.3); // altura do canhoto + folga (só no normal)
            double yBottomLimit = mg + pad + Cm(0.12) + canhotoReserva;
            void NovaPagina() { NovaPaginaCore(); y = contentTop; }
            void Brk(double need) { if (y - need < yBottomLimit) NovaPagina(); } // quebra se não couber
            double TbH(string? val, int maxLines, int minLines, bool labOrTag)
            {
                var n = Math.Max(minLines, Wrap(val, 118, maxLines).Count);
                return (labOrTag ? 15.5 : 10) + (n - 1) * 9 + 4; // espelha Textbox()
            }

            // — Bloco de pessoa v2.0 (PRESTADOR/FORNECEDOR, TOMADOR/ADQUIRENTE…): título cinza
            //   na 1ª célula + CNPJ/Indicador Municipal/Telefone; depois Nome | Município/UF |
            //   Código IBGE/CEP; depois *Endereço | E-mail. tags = origem de cada campo p/ a
            //   auditoria. Destinatário (semIM) não tem Indicador Municipal → célula vazia. —
            double BlocoPessoaV2(double yTop, string titulo, Pessoa p, PTags tags, bool semIM = false)
            {
                var ibge = Slash(p.CMun, FmtCep(p.Cep));
                yTop = TituloRow(yTop, titulo, new[]
                {
                    C(0.25, "CNPJ / CPF / NIF", FmtDoc(p.CnpjCpf), new Opt { Tag = tags.Cnpj }),
                    semIM
                        ? C(0.25, "", "", new Opt { Raw = true })
                        : C(0.25, "Indicador Municipal (Inscrição)", p.Im, new Opt { Tag = tags.Im }),
                    C(0.25, "Telefone", FmtFone(p.Fone), new Opt { Tag = tags.Fone }),
                });
                yTop = Row(yTop, RH, new[]
                {
                    C(0.50, "Nome / Nome Empresarial", p.Nome, new Opt { Tag = tags.Nome }),
                    C(0.25, "Município / Sigla UF", p.MunicipioUF, new Opt { Raw = true, Tag = tags.Mun }),
                    C(0.25, "Código IBGE / CEP", ibge, new Opt { Raw = true, Tag = tags.IbgeCep }),
                });
                yTop = Row(yTop, RH, new[]
                {
                    C(0.75, "*Endereço", p.Endereco, new Opt { Tag = tags.End }),
                    C(0.25, "E-mail", p.Email, new Opt { Tag = tags.Email }),
                });
                return yTop;
            }

            // ══════════════════════════ Cabeçalho ══════════════════════════
            double qrColX = Cm(15.62); // dados/cabeçalho param à esquerda do QR
            double rx = qrColX + 2;
            // Palavras pra quebra do município: "Município:" + nome, com " / UF" GRUDADO na
            // última palavra (evita o "/ UF" pendurado sozinho no fim/início de linha).
            var munWords = Regex.Split(("Município: " + (data.MunicipioEmit ?? "")).Trim(), @"\s+").ToList();
            if (!string.IsNullOrEmpty(data.UfEmit)) munWords[munWords.Count - 1] += " / " + data.UfEmit;
            var ambGerTxt = data.AmbGer switch { "1" => "Sistema Próprio do Município", "2" => "Sefin Nacional NFS-e", _ => "" };
            var tpAmbTxt = data.TpAmb switch { "1" => "Produção", "2" => "Homologação", _ => "" };
            // Município quebra por LARGURA REAL da fonte; máx 2 linhas (modelo oficial).
            double munAvail = R - rx;
            List<string> MunWrap(List<string> words)
            {
                var outL = new List<string>(); var cur = "";
                foreach (var wd in words)
                {
                    var test = cur != "" ? cur + " " + wd : wd;
                    if (cur == "" || TW(test, 8) <= munAvail) cur = test;
                    else { outL.Add(cur); cur = wd; }
                }
                if (cur != "") outL.Add(cur);
                return outL.Take(2).ToList();
            }
            var munLs = MunWrap(munWords);
            // A faixa do cabeçalho CRESCE quando o município ocupa 2 linhas — segue o MODELO
            // oficial (município em 2 linhas + QR com folga embaixo).
            double headH = munLs.Count >= 2 ? Cm(1.55) : Cm(1.16);
            Fill(L, y, W, headH); // cabeçalho com sombreamento cinza 5% (norma 2.2.3) — ANTES do logo
            if (deps.LogoPng is { Length: > 0 })
            {
                try
                {
                    var ms = new MemoryStream(deps.LogoPng);
                    var logo = XImage.FromStream(ms);
                    disposables.Add(logo); disposables.Add(ms);
                    double lw = Cm(4.0), lh = lw * logo.PixelHeight / (double)logo.PixelWidth;
                    double drawnH = Math.Min(lh, headH - 6);
                    double bottom = y - (headH + lh) / 2;   // mesma conta do JS (y do pdf-lib = base da imagem)
                    gfx.DrawImage(logo, L + 4, A4H - (bottom + drawnH), lw, drawnH);
                }
                catch { /* logo inválido: segue sem */ }
            }
            // centro (versão v2.0)
            const string tV = "DANFSe v2.0", tD = "Documento Auxiliar da NFS-e";
            T(tV, L + (W - TW(tV, 9, true)) / 2, y - 13, 9, true, K);
            T(tD, L + (W - TW(tD, 9, true)) / 2, y - 25, 9, true, K);
            if (data.TpAmb == "2") T("NFS-e SEM VALIDADE JURÍDICA", L + Cm(5.9), y - 37, 8, true, RED);
            // direita: Município (8pt, 1-2 linhas) + Ambiente Gerador + Tipo de Ambiente (6pt)
            double hy = y - 9;
            foreach (var ln in munLs) { T(ln, rx, hy, 8, false, K); hy -= 10; }
            hy += 1.5;
            T("Ambiente Gerador: " + (ambGerTxt != "" ? ambGerTxt : "-"), rx, hy, 6, false, K); hy -= 7.5;
            T("Tipo de Ambiente: " + (tpAmbTxt != "" ? tpAmbTxt : "-"), rx, hy, 6, false, K);
            y -= headH;
            double bandTopY = y; // topo da faixa "chave/dados (esq.) + QR (dir.)"
            Sep(bandTopY);       // única linha do cabeçalho: separa a faixa cinza da chave/dados

            // ── QR Code (norma 2.4.3): dimensão mín. 1,52×1,52cm, endereço de consulta +
            //    chave de acesso. Centralizado na coluna direita, com folga (modelo oficial). ──
            try
            {
                var url = "https://www.nfse.gov.br/ConsultaPublica/?tpc=1&chave=" + data.Chave;
                using var gen = new QRCodeGenerator();
                using var qd = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
                // O ModuleMatrix do QRCoder inclui zona quieta de 4 módulos em cada lado —
                // descartamos pra desenhar só o símbolo em exatamente 1,52cm (igual ao JS).
                var mm = qd.ModuleMatrix;
                const int quiet = 4;
                int n = mm.Count - 2 * quiet;
                bool IsDark(int r, int c) => mm[r + quiet][c + quiet];
                double qsz = Cm(1.52);
                double rightColW = R - qrColX;
                const string cap = "A autenticidade desta NFS-e pode ser verificada pela leitura deste código QR ou pela consulta da chave de acesso no portal nacional da NFS-e";
                var capLs = Wrap(cap, 48, 3); // 3 linhas (norma 2.4.3)
                double qyTop = bandTopY - Cm(0.22);              // folga abaixo da linha do cabeçalho
                double qx = qrColX + (rightColW - qsz) / 2;      // centralizado na coluna direita
                double m = qsz / n;
                for (int r = 0; r < n; r++)
                    for (int c = 0; c < n; c++)
                        if (IsDark(r, c))
                            gfx.DrawRectangle(brushK, qx + c * m, A4H - qyTop + r * m - 0.4, m + 0.4, m + 0.4);
                double capCx = qx + qsz / 2;                     // legenda centralizada SOB o QR
                for (int i = 0; i < capLs.Count; i++)
                {
                    var lw2 = TW(capLs[i], 6);
                    T(capLs[i], capCx - lw2 / 2, qyTop - qsz - 7 - i * 7, 6, false, K); // legenda 6pt
                }
            }
            catch { /* QR indisponível: segue sem */ }

            // ── Chave de acesso ──
            y = Row(y, RH, new[]
            {
                C(1, "CHAVE DE ACESSO DA NFS-e", data.Chave, new Opt { ValSize = 7, Raw = true, Tag = "infNFSe@Id" }),
            }, L, qrColX - L);

            // ── Dados da NFS-e — na MESMA grade de 4 colunas do resto do documento; 3 campos
            //    ocupam col 1-3; a col 4 é a área do QR. ──
            y = Row(y, RH, new[]
            {
                C(0.25, "NÚMERO DA NFS-e", data.NNFSe, new Opt { Tag = "infNFSe>nNFSe" }),
                C(0.25, "COMPETÊNCIA DA NFS-e", FmtData(data.Competencia), new Opt { Tag = "infDPS>dCompet" }),
                C(0.25, "DATA E HORA DA EMISSÃO DA NFS-e", FmtDataHora(data.DhEmiNFSe), new Opt { Tag = "infNFSe>dhProc" }),
            });
            y = Row(y, RH, new[]
            {
                C(0.25, "NÚMERO DA DPS", data.NDPS, new Opt { Tag = "infDPS>nDPS" }),
                C(0.25, "SÉRIE DA DPS", data.SerieDPS, new Opt { Tag = "infDPS>serie" }),
                C(0.25, "DATA E HORA DA EMISSÃO DA DPS", FmtDataHora(data.DhEmiDPS), new Opt { Tag = "infDPS>dhEmi" }),
            });

            // ── Emitente (v2.0): EMITENTE DA NFS-e (+Situação/Finalidade) → PRESTADOR → … ──
            // "EMITENTE DA NFS-e" é CAMPO (não título): 1ª célula cinza, com rótulo + valor.
            // NÃO leva separador: faz parte do grupo DE CIMA (chave/dados).
            y = Row(y, RH, new[]
            {
                C(0.25, "EMITENTE DA NFS-e", TpEmitLabel(data.TpEmit), new Opt { Shaded = true, Raw = true, LabSize = 7, Tag = "infDPS>tpEmit" }),
                C(0.25, "SITUAÇÃO DA NFS-e", data.Situacao, new Opt { Raw = true, Tag = "infNFSe>cStat" }),
                C(0.25, "FINALIDADE", data.Finalidade, new Opt { Raw = true, Tag = "infDPS>finNFSe" }),
                C(0.25, "", "", new Opt { Raw = true }),
            });
            // PRESTADOR/FORNECEDOR — grupo próprio (linha acima, separando da linha EMITENTE).
            y = BlocoPessoaV2(y, "PRESTADOR / FORNECEDOR", data.Prest, new PTags
            {
                Cnpj = "emit>CNPJ|CPF|NIF", Im = "emit>IM", Fone = "emit>fone", Nome = "emit>xNome",
                Mun = "enderNac>cMun", IbgeCep = "enderNac>cMun + CEP",
                End = "enderNac>xLgr + nro + xCpl + xBairro", Email = "emit>email",
            });
            y = Row(y, RH, new[]
            {
                C(0.50, "Simples Nacional na Data de Competência", data.Prest.Simples, new Opt { Raw = true, Tag = "regTrib>opSimpNac" }),
                C(0.50, "Regime de Apuração Tributária pelo SN", RegApLabel(data.Prest.RegApSN), new Opt { Raw = true, Tag = "regTrib>regApTribSN" }),
            });

            // ── Tomador / Destinatário / Intermediário (v2.0) ──
            // Norma 2.3.1: bloco VAZIO é SUPRIMIDO pra UMA linha "... NÃO IDENTIFICADO NA
            // NFS-e" (modo NORMAL). No modo AUDITORIA expande SEMPRE (pra mostrar as tags).
            bool temToma = data.Toma != null && (data.Toma.CnpjCpf != "" || data.Toma.Nome != "");
            bool expToma = temToma || audit;
            Brk(expToma ? 3 * RH : Cm(0.42));
            if (expToma)
                y = BlocoPessoaV2(y, "TOMADOR / ADQUIRENTE", data.Toma ?? new Pessoa(), new PTags
                {
                    Cnpj = "toma>CNPJ|CPF|NIF", Im = "toma>IM", Fone = "toma>fone", Nome = "toma>xNome",
                    Mun = "endNac>cMun", IbgeCep = "endNac>cMun + CEP",
                    End = "end>xLgr + nro + xCpl + xBairro", Email = "toma>email",
                });
            else y = LinhaCentral(y, "TOMADOR / ADQUIRENTE NÃO IDENTIFICADO NA NFS-e");

            // ── Destinatário da Operação (v2.0, de IBSCBS/dest) — SEM Indicador Municipal ──
            bool temDest = data.Dest != null && (data.Dest.CnpjCpf != "" || data.Dest.Nome != "");
            bool expDest = temDest || audit;
            Brk(expDest ? 3 * RH : Cm(0.42));
            if (expDest)
                y = BlocoPessoaV2(y, "DESTINATÁRIO DA OPERAÇÃO", data.Dest ?? new Pessoa(), new PTags
                {
                    Cnpj = "dest>CNPJ|CPF|NIF", Fone = "dest>fone", Nome = "dest>xNome",
                    Mun = "endNac>cMun", IbgeCep = "endNac>cMun + CEP",
                    End = "end>xLgr + nro + xCpl + xBairro", Email = "dest>email",
                }, semIM: true);
            else y = LinhaCentral(y, "DESTINATÁRIO DA OPERAÇÃO NÃO IDENTIFICADO NA NFS-e");

            // ── Intermediário da Operação (v2.0, de infDPS/interm) ──
            bool temInterm = data.Interm != null && (data.Interm.CnpjCpf != "" || data.Interm.Nome != "");
            bool expInterm = temInterm || audit;
            Brk(expInterm ? 3 * RH : Cm(0.42));
            if (expInterm)
                y = BlocoPessoaV2(y, "INTERMEDIÁRIO DA OPERAÇÃO", data.Interm ?? new Pessoa(), new PTags
                {
                    Cnpj = "interm>CNPJ|CPF|NIF", Im = "interm>IM", Fone = "interm>fone", Nome = "interm>xNome",
                    Mun = "endNac>cMun", IbgeCep = "endNac>cMun + CEP",
                    End = "end>xLgr + nro + xCpl + xBairro", Email = "interm>email",
                });
            else y = LinhaCentral(y, "INTERMEDIÁRIO DA OPERAÇÃO NÃO IDENTIFICADO NA NFS-e");

            // ── Serviço Prestado (v2.0): título embutido + 3 campos; depois Descrição do
            //    Código (SEM título; municipal se houver, senão nacional) e Descrição do Serviço. ──
            var descCod = DanfseParser.FirstNonEmpty(data.Serv.XTribMun, data.Serv.XTribNac);
            Brk(RH + TbH(descCod, 2, 1, audit) + TbH(data.Serv.XDescServ, 4, 1, true));
            y = TituloRow(y, "SERVIÇO PRESTADO", new[]
            {
                C(0.25, "Código de Tributação Nacional / Municipal",
                    Slash(FmtCTribNac(data.Serv.CTribNac), data.Serv.CTribMun),
                    new Opt { Raw = true, Tag = "cServ>cTribNac + cTribMun" }),
                C(0.25, "Código da NBS", FmtNBS(data.Serv.CNBS), new Opt { Tag = "cServ>cNBS" }),
                C(0.25, "Local da Prestação / Sigla UF / País",
                    Slash(data.Serv.LocalPrest, data.Serv.UfLocPrest, data.Serv.PaisPrest),
                    new Opt { Raw = true, Tag = "infNFSe>xLocPrestacao + locPrest>cPaisPrestacao" }),
            });
            // Descrição do Código de Tributação Nacional/Municipal — SEM rótulo no DANFSe.
            y = Textbox(y, "", descCod, 2, 1, "cServ>xTribMun ou infNFSe>xTribNac");
            y = Textbox(y, "Descrição do Serviço", data.Serv.XDescServ, 4, 1, "cServ>xDescServ");

            // ── Tributação Municipal (ISSQN) v2.0 — grade 4×4. Norma 2.3.1: operação NÃO
            //    SUJEITA ao ISSQN (Imunidade/Exportação/Não Incidência/Imune) suprime pra UMA
            //    linha no modo NORMAL; auditoria expande sempre. Tributável(1) e Suspensa(6)
            //    ficam expandidos. ──
            bool issqnNaoSujeita = data.Issqn.TipoTrib == "2" || data.Issqn.TipoTrib == "3"
                                || data.Issqn.TipoTrib == "4" || data.Issqn.TipoTrib == "5";
            bool expISSQN = !issqnNaoSujeita || audit;
            Brk(expISSQN ? 4 * RH : Cm(0.42));
            if (expISSQN)
            {
                y = TituloRow(y, "TRIBUTAÇÃO MUNICIPAL (ISSQN)", new[]
                {
                    C(0.25, "Tipo de Tributação do ISSQN", TipoTribLabel(data.Issqn.TipoTrib), new Opt { Raw = true, Tag = "tribMun>tribISSQN" }),
                    C(0.50, "Município / Sigla UF / País de Incidência do ISSQN",
                        Slash(data.Issqn.MunicipioIncid, data.Issqn.UfIncid, data.Issqn.PaisResult),
                        new Opt { Raw = true, Tag = "infNFSe>xLocIncid + tribMun>cPaisResult" }),
                });
                y = Row(y, RH, new[]
                {
                    C(0.25, "Regime Especial de Tributação do ISSQN", RegEspLabel(data.Issqn.RegimeEsp), new Opt { Raw = true, Tag = "tribMun>tpRegEsp|regTrib>regEspTrib" }),
                    C(0.25, "Tipo de Imunidade do ISSQN", data.Issqn.TipoImunidade, new Opt { Tag = "tribMun>tpImunidade" }),
                    C(0.25, "Suspensão da Exigibilidade do ISSQN", SuspLabel(data.Issqn.Suspensao), new Opt { Tag = "tribMun>tpSusp" }),
                    C(0.25, "Número Processo Suspensão", data.Issqn.NProcSusp, new Opt { Tag = "tribMun>nProcesso" }),
                });
                y = Row(y, RH, new[]
                {
                    C(0.25, "Benefício Municipal", data.Issqn.BeneficioMun, new Opt { Tag = "tribMun>tpBM|cBenef" }),
                    C(0.25, "Cálculo do BM", data.Issqn.CalcBM, new Opt { Tag = "tribMun>vCalcBM|vRedBCBM" }),
                    C(0.25, "Total Deduções/Reduções", FmtMoeda(data.Issqn.Deducoes), new Opt { Tag = "valores>vDR|vDedRed" }),
                    C(0.25, "Desconto Incondicionado", FmtMoeda(data.Issqn.DescIncond), new Opt { Tag = "valores>vDescIncond" }),
                });
                y = Row(y, RH, new[]
                {
                    C(0.25, "BC ISSQN", FmtMoeda(data.Issqn.Bc), new Opt { Tag = "valores>vBC" }),
                    C(0.25, "Alíquota Aplicada", data.Issqn.Aliq != "" ? data.Issqn.Aliq + "%" : "", new Opt { Tag = "valores>pAliqAplic" }),
                    C(0.25, "Retenção do ISSQN", RetISSLabel(data.Issqn.Retencao), new Opt { Tag = "tribMun>tpRetISSQN", Hl = hlRet && issRetido }),
                    C(0.25, "ISSQN Apurado", FmtMoeda(data.Issqn.Apurado), new Opt { Tag = "valores>vISSQN", Hl = hlRet && issRetido }),
                });
            }
            else y = LinhaCentral(y, "TRIBUTAÇÃO MUNICIPAL (ISSQN) - OPERAÇÃO NÃO SUJEITA AO ISSQN");

            // ── Tributação Federal (Exceto CBS) — título na 1ª célula + grade 4 colunas ──
            Brk(2 * RH);
            y = TituloRow(y, "TRIBUTAÇÃO FEDERAL (EXCETO CBS)", new[]
            {
                C(0.25, "IRRF", FmtMoeda(data.Federal.Irrf), new Opt { Tag = "valores>vRetIRRF", Hl = hlRet && TemVal(data.Federal.Irrf) }),
                C(0.25, "Contribuição Previdenciária - Retida", FmtMoeda(data.Federal.Cp), new Opt { Tag = "valores>vRetCP", Hl = hlRet && TemVal(data.Federal.Cp) }),
                C(0.25, "Contribuições Sociais - Retidas", FmtMoeda(data.Federal.Csll), new Opt { Tag = "valores>vRetCSLL", Hl = hlRet && TemVal(data.Federal.Csll) }),
            });
            y = Row(y, RH, new[]
            {
                C(0.25, "PIS - Débito Apuração Própria", FmtMoeda(data.Federal.Pis), new Opt { Tag = "vPis" }),
                C(0.25, "COFINS - Débito Apuração Própria", FmtMoeda(data.Federal.Cofins), new Opt { Tag = "vCofins" }),
                C(0.25, "Descrição Contrib. Sociais - Retidas", RetPisCofinsLabel(data.Federal.TpRetPisCofins), new Opt { Tag = "piscofins>tpRetPisCofins", Hl = hlRet && pisCofRetido }),
                C(0.25, "", "", new Opt { Raw = true }),
            });

            // ── Tributação IBS / CBS (v2.0, reforma) — grade 4 col × 4 linhas. Vazio nas
            //    notas atuais (v1.01 não tem IBSCBS). ──
            var ic = data.IbsCbs;
            Brk(4 * RH);
            y = TituloRow(y, "TRIBUTAÇÃO IBS / CBS", new[]
            {
                C(0.25, "CST / cClassTrib", Slash(ic.Cst, ic.CClassTrib), new Opt { Raw = true, Tag = "gIBSCBS>CST + cClassTrib" }),
                C(0.50, "Indicador de Operação / Código IBGE Incidência / Município Incidência / Sigla UF",
                    Slash(ic.CIndOp, ic.CLocIncid, ic.XLocIncid, ic.UfIncid),
                    new Opt { Raw = true, Tag = "IBSCBS>cIndOp + cLocalidadeIncid + xLocalidadeIncid" }),
            });
            y = Row(y, RH, new[]
            {
                C(0.25, "Exclusões e Reduções da Base de Cálculo", "", new Opt { Raw = true, Tag = "(somatório)" }),
                C(0.25, "Base de Cálculo Após Exclusões e Reduções", FmtMoeda(ic.VBC), new Opt { Tag = "IBSCBS>valores>vBC" }),
                C(0.25, "Red. Alíquota IBS / Red. Alíquota CBS", Pcs(ic.PRedAliqUF, ic.PRedAliqMun, ic.PRedAliqCBS), new Opt { Raw = true, Tag = "pRedAliqUF + pRedAliqMun + pRedAliqCBS" }),
                C(0.25, "Alíquota – IBS UF / IBS Mun", Pcs(ic.PIBSUF, ic.PIBSMun), new Opt { Raw = true, Tag = "pIBSUF + pIBSMun" }),
            });
            y = Row(y, RH, new[]
            {
                C(0.25, "Alíq. Efetiva Municipal – IBS", Pc(ic.PAliqEfetMun), new Opt { Raw = true, Tag = "pAliqEfetMun" }),
                C(0.25, "Valor Apurado Municipal – IBS", FmtMoeda(ic.VIBSMun), new Opt { Tag = "gIBSMunTot>vIBSMun" }),
                C(0.25, "Alíq. Efetiva Estadual – IBS", Pc(ic.PAliqEfetUF), new Opt { Raw = true, Tag = "pAliqEfetUF" }),
                C(0.25, "Valor Apurado Estadual – IBS", FmtMoeda(ic.VIBSUF), new Opt { Tag = "gIBSUFTot>vIBSUF" }),
            });
            y = Row(y, RH, new[]
            {
                C(0.25, "Valor Total Apurado – IBS", FmtMoeda(ic.VIBSTot), new Opt { Tag = "gIBS>vIBSTot" }),
                C(0.25, "Alíquota - CBS", Pc(ic.PCBS), new Opt { Raw = true, Tag = "pCBS" }),
                C(0.25, "Alíquota Efetiva – CBS", Pc(ic.PAliqEfetCBS), new Opt { Raw = true, Tag = "pAliqEfetCBS" }),
                C(0.25, "Valor Total Apurado – CBS", FmtMoeda(ic.VCBS), new Opt { Tag = "gCBS>vCBS" }),
            });

            // ── Valor Total da NFS-e (v2.0): título embutido + IBS/CBS no total ──
            double totIbsCbs = NumOr0(ic.VIBSTot) + NumOr0(ic.VCBS);
            Brk(2 * RH);
            y = TituloRow(y, "VALOR TOTAL DA NFS-E", new[]
            {
                C(0.25, "VALOR DA OPERAÇÃO / SERVIÇO", FmtMoeda(data.Totais.VServ), new Opt { Tag = "vServPrest>vServ" }),
                C(0.25, "Desconto Incondicionado", FmtMoeda(data.Totais.DescIncond), new Opt { Tag = "valores>vDescIncond" }),
                C(0.25, "Desconto Condicionado", FmtMoeda(data.Totais.DescCond), new Opt { Tag = "valores>vDescCond" }),
            });
            y = Row(y, RH, new[]
            {
                C(0.25, "Total das Retenções (ISSQN / Federais)", FmtMoeda(data.Totais.TotalRet), new Opt { Tag = "valores>vTotalRet", Hl = hlRet && TemVal(data.Totais.TotalRet) }),
                C(0.25, "VALOR LÍQUIDO DA NFS-e", FmtMoeda(data.Totais.VLiq), new Opt { ValBold = true, ValSize = 8, Tag = "valores>vLiq" }),
                C(0.25, "Total do IBS/CBS", (ic.VIBSTot != "" || ic.VCBS != "") ? FmtMoeda(totIbsCbs) : "", new Opt { Tag = "vIBSTot + vCBS" }),
                C(0.25, "VALOR LÍQUIDO DA NFS-e + IBS/CBS", FmtMoeda(ic.VTotNF), new Opt { Shaded = true, ValBold = true, ValSize = 8, Tag = "totCIBS>vTotNF" }),
            });

            // ── Informações Complementares (v2.0) ──
            // A seção separada "Totais Aproximados dos Tributos" (v1.0) NÃO existe no v2.0 — a
            // transparência da Lei 12.741 vira texto AQUI. No PDF normal, uma info por linha;
            // na auditoria, uma info por linha COM a tag em vermelho embaixo.
            var ta = data.TotaisAprox;
            string Or(string a, string b) => a != "" ? a : b;
            var taTxt =
                (ta.Fed != "" || ta.Est != "" || ta.Mun != "")
                    ? $"Totais Aproximados dos Tributos cfe. Lei nº 12.741/2012: Federais: {Or(FmtMoeda(ta.Fed), "R$ 0,00")}; Estaduais: {Or(FmtMoeda(ta.Est), "R$ 0,00")}; Municipais: {Or(FmtMoeda(ta.Mun), "R$ 0,00")};"
                : (ta.PFed != "" || ta.PEst != "" || ta.PMun != "")
                    ? $"Totais Aproximados dos Tributos cfe. Lei nº 12.741/2012: Federais: {Or(ta.PFed, "0,00")}%; Estaduais: {Or(ta.PEst, "0,00")}%; Municipais: {Or(ta.PMun, "0,00")}%;"
                : ta.PSN != ""
                    ? $"Totais Aproximados dos Tributos cfe. Lei nº 12.741/2012: {ta.PSN}% sobre o valor do serviço (Simples Nacional);"
                : "";
            // NBS (código + descrição) — HÍBRIDO: usa o <xNBS> do XML se vier; senão busca na
            // TABELA oficial pelo cNBS. Resolve o caso (comum) de a nota só ter <cNBS>.
            var nbsDescr = Or(data.Serv.XNBS, NbsDesc(data.Serv.CNBS));
            var nbsTxt = (data.Serv.CNBS != "" || nbsDescr != "")
                ? "NBS: " + Or(FmtNBS(data.Serv.CNBS), data.Serv.CNBS) + (nbsDescr != "" ? " - " + nbsDescr : "")
                : "";
            // Auditoria: marca a ORIGEM da descrição (XML vs tabela).
            var nbsTag = data.Serv.XNBS != "" ? "cServ>cNBS + infNFSe>xNBS" : "cServ>cNBS + (tabela NBS)";
            var icp = data.InfoCompl;
            string Lbl(string rot, string v) => v != "" ? rot + ": " + v : ""; // '' se vazio → pulado no normal
            var infoItems = new List<(string Txt, string Tag)>
            {
                (data.Serv.XInfComp ?? "", "serv>infoCompl>xInfComp"),
                (nbsTxt, nbsTag),
                (Lbl("NFS-e Substituída", icp.ChSubstda), "subst>chSubstda"),
                (Lbl("Documento de Referência", icp.DocRef), "infoCompl>docRef"),
                (Lbl("Código da Obra (CNO)", icp.CObra), "obra>cObra"),
                (Lbl("Inscrição Imobiliária Fiscal", icp.InscImobFisc), "imovel>inscImobFisc"),
                (Lbl("Atividade do Evento", icp.IdAtvEvt), "atvEvento>idAtvEvt"),
                (Lbl("Documento Técnico", icp.IdDocTec), "infoCompl>idDocTec"),
                (Lbl("Pedido", icp.XPed), "infoCompl>xPed"),
                (Lbl("Item do Pedido", icp.XItemPed), "gItemPed>xItemPed"),
                (icp.XOutInf ?? "", "infNFSe>xOutInf"),
                (taTxt, "totTrib>vTotTribFed/Est/Mun ou pTotTribFed/Est/Mun ou pTotTribSN"),
            };
            // Altura: normal conta só as linhas com texto; auditoria soma +1 linha de tag/campo.
            int infoLines = 0, infoRows = 0;
            foreach (var it in infoItems)
            {
                var w = it.Txt != "" ? Wrap(it.Txt, 118, 4).Count : 0;
                infoLines += w; infoRows += w + 1;
            }
            double infoH = 10 + ((audit ? Math.Max(1, infoRows) : Math.Max(1, infoLines)) - 1) * 9 + 4;
            Brk(Cm(0.42) + infoH);
            y = Title(y, "Informações Complementares");
            y = audit ? InfoboxAudit(y, infoItems) : InfoboxNormal(y, infoItems);

            // ── CANHOTO (stub destacável no rodapé): DATA CIENTIFICAÇÃO | IDENTIFICAÇÃO E
            //    ASSINATURA | Nº NFS-e / CHAVE NFS-e. Fixo no fundo da página. Não desenhado
            //    em auditoria (o conteúdo desce até o rodapé, sobrepondo o canhoto fixo). ──
            if (!audit)
            {
                double canhotoH = Cm(1.0), canhotoTop = mg + pad + canhotoH;
                // Caixa FECHADA com divisórias verticais (ÚNICA parte do documento com verticais).
                gfx.DrawRectangle(new XPen(K, LINE), L, A4H - canhotoTop, W, canhotoH);
                foreach (var f in new[] { 0.25, 0.50 })
                    VLine(L + f * W, canhotoTop, canhotoTop - canhotoH);
                Row(canhotoTop, canhotoH, new[]
                {
                    C(0.25, "**** DATA CIENTIFICAÇÃO:", "", new Opt { Raw = true }),
                    C(0.25, "IDENTIFICAÇÃO E ASSINATURA", "", new Opt { Raw = true }),
                    C(0.50, "Nº NFS-e / CHAVE NFS-e", Slash(data.NNFSe, data.Chave), new Opt { Raw = true }),
                });
            }

            // ── Moldura + marca d'água + nº da folha: desenhadas em CADA página. SÓ UMA
            //    borda: a moldura externa (igual ao oficial). ──
            var wmTxt = string.IsNullOrEmpty(data.MarcaDagua) ? "" : data.MarcaDagua.ToUpperInvariant();
            const double wmSize = 70;
            double wmTw = wmTxt != "" ? gfx.MeasureString(wmTxt, F(false, wmSize)).Width : 0;
            double ang = Math.PI / 4, cs = Math.Cos(ang), sn = Math.Sin(ang);
            var brushK35 = new XSolidBrush(K35);
            for (int i = 0; i < pages.Count; i++)
            {
                var g = pages[i].G;
                g.DrawRectangle(new XPen(K, 1.0), mg, mg, A4W - 2 * mg, A4H - 2 * mg);
                // Marca d'água (NT-008, 2.5): diagonal, regular, >=50pt, K35 — CANCELADA/SUBSTITUÍDA.
                if (wmTxt != "")
                {
                    double wx = A4W / 2 - (wmTw / 2) * cs + (wmSize * 0.35) * sn;
                    double wy = A4H / 2 - (wmTw / 2) * sn - (wmSize * 0.35) * cs;
                    var st = g.Save();
                    g.TranslateTransform(wx, A4H - wy);
                    g.RotateTransform(-45); // -45 no PDFsharp (y pra baixo) = 45° anti-horário visual
                    g.DrawString(wmTxt, F(false, wmSize), brushK35, new XPoint(0, 0), baseline);
                    g.Restore(st);
                }
                // "Folha X/Y" no rodapé DENTRO da moldura, só quando há mais de uma página.
                if (pages.Count > 1)
                {
                    var ft = "Folha " + (i + 1) + "/" + pages.Count;
                    var ftW = g.MeasureString(ft, F(false, 6)).Width;
                    g.DrawString(ft, F(false, 6), brushK35, new XPoint(R - ftW, A4H - (mg + 3)), baseline);
                }
            }

            foreach (var (_, g) in pages) g.Dispose();
            using var outMs = new MemoryStream();
            pdf.Save(outMs, closeStream: false);
            foreach (var d in disposables) d.Dispose();
            return outMs.ToArray();
        }
    }

    /// <summary>
    /// Resolver de fontes que serve os bytes da Arimo (Regular/Bold) ao PDFsharp e delega o
    /// resto ao resolver de plataforma. Registrado uma única vez (GlobalFontSettings).
    /// </summary>
    internal sealed class DanfseFontResolver : IFontResolver
    {
        public const string FamilyName = "Arimo";
        private static byte[]? _regular, _bold;
        private static readonly object Lock = new object();

        public static void Register(byte[] regular, byte[] bold)
        {
            lock (Lock)
            {
                _regular = regular; _bold = bold;
                GlobalFontSettings.FontResolver ??= new DanfseFontResolver();
            }
        }

        public byte[]? GetFont(string faceName) => faceName switch
        {
            "Arimo#Regular" => _regular,
            "Arimo#Bold" => _bold,
            _ => null,
        };

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            if (familyName.Equals(FamilyName, StringComparison.OrdinalIgnoreCase) && _regular != null)
                return new FontResolverInfo(bold && _bold != null ? "Arimo#Bold" : "Arimo#Regular");
            return PlatformFontResolver.ResolveTypeface(familyName, bold, italic);
        }
    }

    /// <summary>Fachada — equivalente ao `gerarDanfse(xmlString, deps)` do JS.</summary>
    public static class DanfseGenerator
    {
        /// <summary>Parseia o XML da NFS-e e gera o PDF do DANFSe.</summary>
        /// <exception cref="InvalidOperationException">XML inválido para DANFSe.</exception>
        public static byte[] Gerar(string xmlString, DanfseDeps deps)
        {
            var data = DanfseParser.ParseDanfse(xmlString, deps.isCancelada)
                       ?? throw new InvalidOperationException("XML inválido para DANFSe");
            return DanfsePdfBuilder.Build(data, deps);
        }
    }
}
