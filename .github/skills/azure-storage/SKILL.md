---
name: azure-storage
description: "Azure Storage の Blob、Table、Queue、Files を用途に合わせて選び、C# SDK、access、resilience、test を実装・レビューする。"
license: MIT
---

# Azure Storage

Azure Storage は複数の data service を含みます。実装前に `.github/skills/azure-data-persistence/SKILL.md` を読み、具体的に Blob / Table / Queue / Files のどの service が必要かを決めます。曖昧なら database を想定して進めず、必要な data shape と access pattern を確認します。

## Service の使い分け

- **Blob Storage**: unstructured object、file、image、backup 等。container / blob naming、metadata、content type、size、versioning / lifecycle、同時更新の扱いを確認します。Blob metadata は一般的な relational query の代わりになりません。
- **Table Storage**: key-attribute entity。`PartitionKey` / `RowKey` を実際の read / write / scale pattern から設計します。joins や任意 field 上の relational query が必要なら Azure SQL、柔軟な document query / scale が必要なら Cosmos DB の適合性も比較します。
- **Queue Storage**: asynchronous work handoff。message body に認証情報・巨大 payload・正本 record を置かず、必要に応じ Blob / database の record ID を渡します。redelivery / retry を考慮した idempotent consumer と poison message の処理を設計し、Functions trigger を使う時は trigger 固有の retry semantics を Learn で確認します。
- **Azure Files**: SMB / NFS 等の shared file semantics が必要な workload。protocol、mount、identity、network、concurrent file access を確認します。単純な web upload/download だけなら Blob の適合性も比較します。

## C# SDK と data access

- `.NET` SDK / package version と target framework を project に合わせ、必要な client package の API は Microsoft Learn で検証します。service 間で SDK / credential behavior を取り違えません。
- Azure SDK client の lifetime、DI 登録、retry、timeout、diagnostics は利用する SDK の公式 guidance に従います。request ごとに client を無条件で生成する実装を避けます。
- Blob の upload / download は stream を使い、対象サイズ、memory 使用量、cancellation、partial failure を確認します。large object を一度に memory に読み込まないようにします。
- ETag / conditional request 等の optimistic concurrency を使い、競合を last-write-wins で隠すか conflict として返すかを業務要件に合わせます。
- Table entity の query は partition / row key で絞ります。広い scan を通常の一覧 query として導入せず、paging と continuation token を扱います。
- Queue message の consumer は重複 delivery が起きても安全な operation にし、visibility timeout、poison message、最大 retry / dead-letter 相当の運用を明示します。Queue Storage の機能を Service Bus と同一視しません。

## Security と resilience

- account key / connection string を code、tracked config、Blazor WASM、URL、log に出しません。可能なら managed identity / Microsoft Entra ID と service に合う最小権限 role を使います。
- Blob の browser 直送が明示要件の場合、trusted server が user / object / operation を認可してから短命・最小権限の user delegation SAS を発行する方式を検討します。SAS は bearer credential として扱い、対象の HTTPS request にだけ使い、完全な SAS URL を log / telemetry / analytics に記録したり persistent browser storage に保存したりしません。account key から署名する SAS との違いを Learn で確認します。Storage 側の CORS も必要な UI origin / method / header に限定し、CORS を認可の代わりにしません。通常の CRUD で長期 SAS や storage key を client に配布しません。
- Blob public access、shared key、network firewall、private endpoint、TLS 等は必要要件から構成し、デフォルト設定の安全性を推測しません。
- retry は transient fault と SDK の default behavior を確認して設定します。non-idempotent write を無制限に retry せず、ETag / deterministic identifier 等で重複・競合を扱います。
- user authorization は application/API layer で行います。Storage account の認証に成功することを、end-user がその object を読んでよい認可の証明にしません。

## Tests

- unit test は既存の test framework / fake-client pattern に従い、cloud call を isolation します。
- Azurite 等の emulator を使う場合は対象 operation と未対応機能を確認します。Azure 上の identity / RBAC、network policy、lifecycle、redundancy、scale の証明として扱いません。
- 実サービス integration test が必要なら隔離した non-production account、test data cleanup、費用・権限を明確にし、secret をログや成果物に含めません。

## Microsoft Learn 参照

- [Introduction to Azure Storage](https://learn.microsoft.com/azure/storage/common/storage-introduction)
- [Blob Storage introduction](https://learn.microsoft.com/azure/storage/blobs/storage-blobs-introduction)
- [Azure Table Storage overview](https://learn.microsoft.com/azure/storage/tables/table-storage-overview)
- [Azure Queue Storage introduction](https://learn.microsoft.com/azure/storage/queues/storage-queues-introduction)
- [Azure Files introduction](https://learn.microsoft.com/azure/storage/files/storage-files-introduction)
- [Authorize access to data in Azure Storage](https://learn.microsoft.com/azure/storage/common/authorize-data-access)
- [Create a user delegation SAS for a blob with .NET](https://learn.microsoft.com/azure/storage/blobs/storage-blob-user-delegation-sas-create-dotnet)

API / package / authorization behavior は `.github/skills/microsoft-learn/SKILL.md` の手順で、project の version と照らして確認します。
