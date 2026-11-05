# Bounded Observed Set

Versão de desenvolvimento **0.1.0**. Conjunto observado-removido original, em C# e em memória, da [Lofrano Tecnologia](https://lofranotecnologia.com.br), para experiências pequenas com réplicas offline. [English](README.md).

Um elemento está visível quando pelo menos uma de suas tags de adição permanece ativa. A remoção afeta somente tags ativas observadas localmente. Uma adição concorrente desconhecida sobrevive. Não há relógios, transporte, descarte de histórico nem promessa de processamento exatamente uma vez.

## Instalação, testes e demonstração

Instale o SDK .NET **10.0.403** pelo [site oficial](https://dotnet.microsoft.com/download/dotnet/10.0). O `global.json` permite patches posteriores da mesma faixa de SDK. Na raiz do repositório:

```sh
sh scripts/check.sh restore
dotnet test --solution BoundedObservedSet.sln --no-restore --configuration Release
dotnet run --project demo/BoundedObservedSet.Demo.csproj --no-restore --configuration Release -- --self-test
```

O repositório inclui arquivos de lock do NuGet gerados por uma restauração real. A preparação baixa os pacotes exatos em `build/packages` e usa modo travado; divergências entre dependências e locks falham sem atualizar os arquivos. Testes e demonstrações usam depois os recursos locais sem restaurar pacotes. Para repetir: `sh scripts/check.sh offline`. Ao mudar dependências, mantenedores devem executar deliberadamente `dotnet restore BoundedObservedSet.sln --force-evaluate`, revisar os locks gerados e repetir as verificações travadas.

Biblioteca e demonstração não dependem de pacotes externos em execução. Os testes fixam xUnit v3 4.0.2, adaptador VSTest 4.0.1 e Microsoft.NET.Test.Sdk 18.11.0. O projeto de testes executável usa Microsoft.Testing.Platform nativo, selecionado em `global.json`, com o comando `dotnet test --solution` do .NET 10.

Saída da demonstração, sem mensagens de compilação do SDK:

```text
Bounded Observed Set - development 0.1.0
Observed blue/red were removed; unobserved A:3 survives.
Converged: A, B, C; visible: [blue]
{"version":"0.1.0","replicas":3,"converged":true,"visible":["blue"],"historicalDots":3}
```

As verificações sempre são executadas, mesmo sem `--self-test`. Acrescente `--json` para emitir somente a saída JSON da aplicação; após compilar, `--no-build` evita novo trabalho de compilação. Códigos de saída: 0 para concordância, 1 para falha de verificação ou erro de E/S na saída e 2 para argumento inválido. Os dados são sintéticos; a demonstração é determinística e offline.

## Exemplo pequeno

Referencie `src/BoundedObservedSet.csproj` em um projeto .NET 10:

```csharp
using Lofrano.BoundedObservedSet;

string[] membros = { "A", "B" };
var a = new ObservedSet("A", membros);
var b = new ObservedSet("B", membros);
a.Add("blue");                  // A:1
b.Merge(a);                     // B observou A:1
b.RemoveObserved("blue");       // remove apenas A:1
a.Add("blue");                  // A:2, desconhecida por B
a.Merge(b);                     // blue continua visível por A:2
bool visivel = a.Contains("blue");
byte[] snapshot = a.ExportSnapshot();
```

Verifique `OperationResult.Code` em toda operação mutável; o exemplo omite os desvios para facilitar a leitura. Toda adição bem-sucedida cria uma nova tag, inclusive para valores já visíveis. `GetVisibleElements()` devolve uma cópia ordenada ordinalmente. O snapshot é uma cópia em bytes UTF-8 canônicos. A identidade do escritor é escolhida explicitamente:

```csharp
var restaurado = new ObservedSet("A", new[] { "A", "B" });
OperationResult resultado = restaurado.ImportSnapshot(snapshot);
if (!resultado.IsSuccess)
    throw new InvalidOperationException(resultado.Code.ToString());
```

Importar substitui o estado; não equivale a mesclar e não protege contra rollback. Use A somente se o escritor autoritativo anterior tiver parado e o estado não tiver regredido. As cópias usadas em testes representam experiências alternativas, não clonagem segura de identidade.

## Resumo do contrato

- Membros fixos: 1–16 IDs distintos, cada um com 1–32 letras ou dígitos ASCII, `_` ou `-`, distinguindo maiúsculas de minúsculas.
- Contadores causais inteiros de 64 bits com sinal: 0 até `long.MaxValue`; tags usam valores positivos e incremento verificado.
- Strings Unicode bem formadas, inclusive vazias e com controles, até 128 bytes UTF-8. Sem normalização.
- Padrões e máximos: 4.096 tags históricas e 1.024 elementos distintos **históricos**. Podem ser configurados limites menores. Fatos removidos continuam ocupando espaço.
- Mesclar une fatos, combina remoção por OR e escolhe máximos dos contadores. Conflitos de tag, membros incompatíveis e esgotamento de limites rejeitam a operação inteira.
- Snapshot de esquema 1: sem identidade local, limites, horários ou credenciais. Entrada máxima de 4.194.304 bytes; campos desconhecidos, campos decodificados duplicados e contadores inválidos são recusados.
- Métodos usam lock interno. A mesclagem captura o estado da origem antes de bloquear o destino; não é uma transação distribuída.

O [contrato detalhado](docs/contract.md) inclui uma seção de semântica causal em português. Consulte também [projeto e referências](docs/design.md). Diagnósticos são códigos fixos, sem reproduzir entrada ou mensagens do parser.

## Motivos e limitações

Preservar evidências de remoção torna a causalidade inspecionável. Recusar uma mesclagem grande demais evita esquecer remoções silenciosamente e ressuscitar adições antigas. Bytes canônicos permitem verificar convergência de estados aceitos sem substituir evidências por hashes. As leis algébricas valem somente quando as mesclagens necessárias são aceitas dentro dos limites e as tags têm atribuição consistente.

O histórico acabará cheio. Esta versão não implementa compactação segura, membros dinâmicos, autenticação, confidencialidade, protocolo de rede, persistência ou proteção de identidade. Snapshot não prova autorização. É responsabilidade externa manter um único escritor autoritativo por ID. Contadores não corrigem clones nem rollback. Não há alegação ampla de conformidade com padrões de CRDT, canonicalização JSON ou segurança.

Os testes cobrem seis ordens de três históricos, leis de mesclagem aceita, falhas atômicas, escapes máximos, chamadas paralelas e um oráculo independente de eventos que enumera 4.096 históricos pequenos. São verificações de correção, não benchmarks nem afirmação de prontidão para produção.

[Contribuição](CONTRIBUTING.md) · [Segurança](SECURITY.md) · [Alterações](CHANGELOG.md) · [Licença MIT](LICENSE). Contato geral pelo [site oficial](https://lofranotecnologia.com.br). Sem telemetria ou dependência de modelo/API em execução.
