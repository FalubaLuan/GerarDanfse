# DANFSe v2.0

Gerador de **DANFSe (Documento Auxiliar da NFS-e)** em **C#/.NET**.

Gera o DANFSe em PDF a partir do XML da **NFS-e do Leiaute Nacional**, seguindo a **Nota Técnica nº 008 (SE/CGNFS-e)** e o layout oficial do Portal Nacional.

---

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

Instale o pacote:

```bash
dotnet add package GerarDanfse --version 1.0.4
```

A biblioteca utiliza os seguintes pacotes:

- PDFsharp 6.x
- QRCoder

> **Obs.:** essas dependências são instaladas automaticamente pelo NuGet.

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
| --- | --- |
| `LogoPng` | Logo exibida no DANFSe (opcional) |
| `Cancelada` | Exibe a marca d'água **CANCELADA** |
| `Auditoria` | Exibe a tag XML correspondente a cada campo |
| `DestacarRet` | Destaca os campos de retenção em amarelo |

---

## Observações

- As fontes necessárias já acompanham a biblioteca, não sendo necessário fornecê-las manualmente.
- O logotipo é opcional.

