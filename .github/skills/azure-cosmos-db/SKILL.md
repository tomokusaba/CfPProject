---
name: azure-cosmos-db
description: "Azure Cosmos DB の API 選定、NoSQL data modeling、partition、consistency、RU、.NET SDK、security、tests を設計・レビューする。"
license: MIT
---

# Azure Cosmos DB

Cosmos DB 実装の前に `.github/skills/azure-data-persistence/SKILL.md` と `.github/skills/microsoft-learn/SKILL.md` を参照します。最初に account の API / model を確認し、すべての Cosmos account が同じ SDK、query、transaction semantics を持つと仮定しません。

## API と data model

- API for NoSQL、MongoDB、Cassandra、Gremlin、Table 等の対象 API を確認し、その API に対応する SDK / driver、version、feature support を公式 docs で検証します。NoSQL SDK の code sample を他 API の account に流用しません。
- NoSQL API では container を access pattern に合わせて model 化します。読み書きで頻出する data を一つの item にまとめる、または別 container に分ける判断を、transaction scope、query、update frequency、document size と照らします。
- relational joins / cross-entity constraints / ad hoc report が主用途なら、Azure SQL Database 等の relational store と比較します。schema-less であることを data validation 不要と解釈しません。

## Partitioning、consistency、cost

- partition key は query predicate、tenant isolation、cardinality、read/write distribution、growth、transaction boundary をもとに設計します。hot partition / skew / cross-partition fan-out の可能性を負荷予測で検討します。
- partition key の変更は通常単純な in-place configuration change ではありません。new container への移行、backfill、cutover、rollback が必要かを Learn の現在の制約で確認します。
- transactional batch 等の atomicity は account API と partition boundary を確認し、異なる partition / container を一つの transaction として扱いません。
- consistency level はアカウント設定と request behavior を確認し、ユーザーに見える stale read / read-your-writes 等の要件から選びます。strong consistency 等を必要性の説明なしに指定しません。
- throughput / RU、serverless / provisioned / autoscale、query charge、indexing policy を workload と cost target に合わせます。全項目の自動 indexing や全件 scan のコストを測定可能と見なさず、実データ相当の query / diagnostics を使います。
- TTL、change feed、multi-region、backup / restore は feature requirement と retention / RPO / RTO に合わせて明示的に設計します。free tier や emulator の存在を production suitability の根拠にしません。

## C# SDK と操作

- API for NoSQL の .NET SDK を使う場合は `Microsoft.Azure.Cosmos` と対象 TFM / version の Learn docs を確認します。`CosmosClient` の lifetime、connection mode、retry、timeouts、diagnostics は SDK の推奨に従います。
- 同じ account に対する `CosmosClient` を無条件に function invocation ごとに作らず、host DI と worker lifecycle に合う再利用を検討します。異なる credential / endpoint の client 混用を避けます。
- SDK が処理する retry (例: throttling response) と application-level retry を重ねないようにします。cancellation と request diagnostics を伝播し、RU charge / status / activity ID を調査可能にします。ただし item payload や credential は log に出しません。
- point read、partition-scoped query、cross-partition query の違いを踏まえ、必要な partition key を repository / service 境界で明示します。

## Security と test

- master key を code、CI log、tracked file、WASM client に保存しません。可能なら managed identity / Microsoft Entra ID、Cosmos DB data-plane RBAC と最小権限を確認します。
- firewall / private endpoint を使う場合は Function App の outbound networking、DNS、local / CI integration test の接続経路を一緒に設計します。
- unit tests は data access seam を使って isolation し、serializer / query / partition semantics が重要な変更では target account API と実際の SDK version に対応した integration test を検討します。
- Cosmos DB emulator は local functional test 向けとして使い、production RU、global replication、identity、availability、latency、service quota の再現と見なしません。

## Microsoft Learn 参照

- [Azure Cosmos DB for NoSQL overview](https://learn.microsoft.com/azure/cosmos-db/nosql/overview)
- [Partitioning and horizontal scaling](https://learn.microsoft.com/azure/cosmos-db/partitioning)
- [Consistency levels](https://learn.microsoft.com/azure/cosmos-db/consistency-levels)
- [.NET SDK v3 for Azure Cosmos DB for NoSQL](https://learn.microsoft.com/azure/cosmos-db/nosql/sdk-dotnet-v3)
- [.NET SDK performance tips](https://learn.microsoft.com/azure/cosmos-db/nosql/performance-tips-dotnet-sdk-v3)
- [Choose an Azure data store](https://learn.microsoft.com/azure/architecture/guide/technology-choices/data-store-overview)

API / SDK version / limits / security options は implementation 時に `.github/skills/microsoft-learn/SKILL.md` で確認します。

## 参考にした構成

- [Awesome Copilot: Cosmos DB data modeling](https://github.com/github/awesome-copilot/blob/main/skills/cosmosdb-datamodeling/SKILL.md) — access pattern を起点とする model design の構成を参考にし、artifact の強制や未検証の version-specific rules は採用しません。
