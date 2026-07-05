# DANFSe v2.0

Gerador de **DANFSe (Documento Auxiliar da NFS-e)** em **C#/.NET**.

Gera o DANFSe em PDF a partir do XML da **NFS-e do Leiaute Nacional**, seguindo a **Nota Técnica nº 008 (SE/CGNFS-e)** e o layout oficial do Portal Nacional.

## Recursos

- Geração de DANFSe em PDF
- Compatível com o XML da NFS-e do Leiaute Nacional
- Layout conforme o modelo oficial do Portal Nacional
- Geração automática do QR Code
- Marca d'água para notas canceladas
- Modo **Auditoria**, exibindo a tag XML correspondente a cada campo
- Destaque visual para retenções
- Canhoto destacável
- Quebra automática de página entre grupos
- Suporte aos blocos **IBS/CBS** da Reforma Tributária

---

## Instalação

Instale as dependências necessárias:

```bash
dotnet add package PDFsharp
dotnet add package QRCoder
```

| Pacote | Finalidade |
| --- | --- |
| PDFsharp 6.x | Geração do PDF |
| QRCoder | Geração do QR Code |

---

## Uso

### Geração básica

Para a maioria dos casos, basta informar o XML.

```csharp
var xml = File.ReadAllText("nfse.xml");

byte[] pdf = DanfseGenerator.Gerar(xml, new DanfseDeps());

File.WriteAllBytes("danfse.pdf", pdf);
```

A biblioteca já inclui as fontes necessárias internamente. O logotipo é opcional.

---

### Carregando as tabelas auxiliares

As tabelas de municípios e NBS são utilizadas para exibir nomes e descrições em vez dos respectivos códigos.

```csharp
DanfseTables.IbgeMunicipios["3106200"] = "Belo Horizonte";
DanfseTables.IbgeUf["31"] = "MG";

// DanfseTables.NbsDesc["114013900"] = "...";
```

Na prática, recomenda-se carregá-las a partir de um JSON ou recurso embarcado.

---

### Configurando opções

```csharp
var deps = new DanfseDeps
{
    LogoPng = File.ReadAllBytes("logo.png"), // opcional

    Cancelada = true,

    Auditoria = false,
    DestacarRet = false
};

byte[] pdf = DanfseGenerator.Gerar(xml, deps);
```

| Propriedade | Descrição |
| --- | --- |
| `LogoPng` | Logo exibida no DANFSe (opcional) |
| `Cancelada` | Exibe a marca d'água **CANCELADA** |
| `Auditoria` | Exibe a tag XML correspondente a cada campo |
| `DestacarRet` | Destaca os campos de retenção em amarelo |

---

## Observações

- As fontes necessárias já acompanham a biblioteca, não sendo necessário fornecê-las manualmente.
- O logotipo é opcional.
- As tabelas auxiliares de municípios e NBS são opcionais, porém recomendadas para exibição de descrições amigáveis em vez de códigos.