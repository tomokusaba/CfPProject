---
name: azure-data-persistence
description: "Azure Storage、Azure Cosmos DB、Azure SQL Database の永続化方式を access pattern、整合性、運用、security、cost に基づいて選定・設計する。"
license: MIT
---

# Azure data persistence

Blazor WebAssembly + Azure Functions などの workload で永続化方式を設計・レビューする共通 Skill です。利用する service は requirements と access pattern から選び、既存構成を優先します。service-specific な作業では対応する storage Skill を併用してください。

## まず要件と access pattern を確認する

- 主な read / write、検索条件、sort、pagination、transaction scope、data ownership、更新頻度、予測量と growth を確認します。
- latency、availability、regional distribution、consistency、RPO / RTO、retention、backup / restore、security / compliance、運用負荷と cost を明確にします。
- 具体的な access pattern を確定せずに「NoSQL が速い」「fully managed が安い」などの一般論で選びません。SKU、region、throughput、backup 等の料金・制限は現在の Microsoft Learn / pricing を確認します。
- 「Azure Storage」は単一 database 名ではありません。Blob、Table、Queue、Files のどれが必要かを特定します。
- Azure Cosmos DB は account API / data model を確認します。NoSQL API、MongoDB API、Table API 等の SDK、query、transaction の仕様を混同しません。
- Azure SQL Database は relational model、Azure SQL Managed Instance、SQL Server on VM と区別します。

## 粗い適合性を比較する

| 要件 / data shape | 候補 | 初期確認 |
|---|---|---|
| 画像、文書、media、backup 等の object | Azure Blob Storage | object name、metadata、versioning、lifecycle、ETag / concurrent update |
| partition / row key を中心とする単純な key-attribute entity | Azure Table Storage | PartitionKey / RowKey と query / scale pattern |
| 非同期処理を渡す durable message | Azure Queue Storage | duplicate-safe consumer、retry、visibility timeout、poison message 処理 |
| SMB / NFS 等の shared file system が必要 | Azure Files | protocol、identity、mount、concurrent access |
| 柔軟な document / key-value workload、partitioned scale、region distribution が必要 | Azure Cosmos DB | API、partition key、consistency、RU / throughput と query pattern |
| relational constraints、joins、multi-row transaction、SQL query / reporting が必要 | Azure SQL Database | schema、transaction、index、migration、compute / storage tier |

この表は選択の出発点であり、性能・cost・availability を保証しません。Queue は通常 query 可能な業務 record store の代わりではなく、Blob / Files は relational query layer の代わりではありません。

## 横断的な実装ルール

- 新規に三種類すべてを組み合わせることを前提にせず、各 store の責務と正本を明確にします。複数 store は独立した access pattern / operational requirement がある場合に限定します。
- `IRepository<T>` 等の共通 abstraction に SQL transaction、Cosmos partition semantics、Blob ETag、Queue delivery behavior を押し込みません。必要なら業務上有用な境界を設け、store 固有の capability を隠さない設計にします。
- database / business-data access は原則 server-side Azure Functions に置きます。Blazor WebAssembly に account key、SQL credential、Cosmos key、connection string を含めず、browser から database endpoint に直接接続させません。Blob の大容量転送を browser から行う要件がある場合だけ、server-side authorization 後に発行する短命・最小権限の user delegation SAS を検討します。
- 対応する場合は managed identity / Microsoft Entra ID と最小権限の data-plane role を優先します。必要な secret は Azure 側の app settings / Key Vault 等で管理し、local secret file を commit しません。
- network access は実際の threat model / hosting に基づいて制限します。private endpoint 等を追加する場合は DNS、Functions outbound networking、deployment/test path も確認し、設定だけで接続可能と断言しません。
- transient retry は SDK / service の動作を確認し、idempotent な操作に限定します。write の結果が不明な timeout で二重作成・二重課金しないよう、operation identity、ETag、transaction 等を access pattern に合わせます。
- schema・partition key・data format の変更には migration / backfill / rollback・互換性・maintenance を含む計画を立てます。production startup で破壊的 migration を自動適用しません。
- restore は backup 設定だけでなく RPO / RTO と実際の recovery test を確認します。emulator / local tests では Azure 固有の network、identity、consistency、performance、failover を検証できたと扱いません。
- 具体的な product API / version / limit / security option は `.github/skills/microsoft-learn/SKILL.md` で Microsoft Learn を調べます。provider の詳細は個別 Skill と合わせます。

## 関連 Skills

- Azure Storage: `.github/skills/azure-storage/SKILL.md`
- Azure Cosmos DB: `.github/skills/azure-cosmos-db/SKILL.md`
- Azure SQL Database: `.github/skills/azure-sql-database/SKILL.md`
- Microsoft Learn research: `.github/skills/microsoft-learn/SKILL.md`
- C# / .NET implementation: `.github/skills/csharp-dotnet/SKILL.md`

## 参考にした構成

- [Microsoft Learn: Introduction to Azure Storage](https://learn.microsoft.com/azure/storage/common/storage-introduction)
- [Microsoft Learn: Choose an Azure data store](https://learn.microsoft.com/azure/architecture/guide/technology-choices/data-store-overview)
- [Awesome Copilot: Azure service mappings](https://github.com/github/awesome-copilot/blob/main/skills/cloud-design-patterns/references/azure-service-mappings.md)
