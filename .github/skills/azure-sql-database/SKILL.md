---
name: azure-sql-database
description: "Azure SQL Database の relational schema、C# access、EF Core / SqlClient、authentication、migration、query performance、tests を実装・レビューする。"
license: MIT
---

# Azure SQL Database

Azure SQL Database は relational model、integrity constraints、joins、SQL transaction が必要な場合の候補です。`.github/skills/azure-data-persistence/SKILL.md` で store の適合性を確認し、Azure SQL Database と SQL Server / Azure SQL Managed Instance を混同しません。

## Schema、query、transaction

- schema、primary / foreign keys、unique constraints、nullability、delete behavior を業務 invariant から設計します。application validation だけで database integrity を代替しません。
- query、pagination、sorting、index は実際の access pattern と cardinality をもとにします。table scan や N+1 query 等の懸念は execution plan / telemetry 等の証拠を確認し、測定なしに index や cache を増やしません。
- transaction boundary を短く保ち、multi-step operation の atomicity、isolation、optimistic concurrency、retry 時の idempotency を明示します。
- T-SQL は parameterized query を使い、user input を SQL fragment に連結しません。EF Core / SqlClient 等は project が選択した data access strategy と version を踏襲します。

## C# access、migration、運用

- `Microsoft.Data.SqlClient`、`Microsoft.EntityFrameworkCore.SqlServer` 等の package は target TFM / existing package graph と照合し、具体的な API と version を Microsoft Learn で確認します。
- DI lifetime、connection pooling、`DbContext` lifetime、async query、cancellation を hosting model と既存構成に合わせます。`DbContext` を singleton に保持したり、同時利用可能と仮定したりしません。
- schema migration は既存の EF Core migrations / deployment process に従います。production startup から無条件に destructive migration を実行せず、backward-compatible rollout、backup / restore、rollback / forward-fix 方針を確認します。
- DB size、compute tier、serverless / provisioned、elastic pool、backup retention、geo-replication、failover は RPO / RTO と利用 profile から選びます。Always-on や high availability を設定名だけで保証しません。

## Security と resilience

- SQL username / password、connection string を source、client bundle、log、issue、CI output に含めません。可能なら managed identity / Microsoft Entra authentication と least-privilege database user / role を採用します。
- public network access、firewall rule、private endpoint、DNS を hosting topology に合わせて制限し、Functions 側からの実際の接続経路を検証します。
- transient fault handling / retry は SqlClient / EF Core / hosting provider の推奨と合わせます。commit 結果が不明な transaction を無条件に再実行し、duplicate write を作らないようにします。
- SQL error details、connection string、個人情報を利用者応答や構造化 log に出しません。authorization は DB authentication だけでなく application/API の user / tenant access control でも確認します。

## Tests

- unit tests は業務ルールに集中させ、SQL translation / transaction / constraints が重要なら SQL Server または Azure SQL 互換の integration environment で検証します。
- SQLite / in-memory provider の test は SQL Server query translation、locking、collation、T-SQL、execution plan の同等性を保証しません。provider 差を明示します。
- integration test は隔離された database、secret-free test configuration、migration / cleanup strategy を使います。production database を破壊的 test に使いません。

## Microsoft Learn 参照

- [What is Azure SQL Database?](https://learn.microsoft.com/azure/azure-sql/database/sql-database-paas-overview?view=azuresql)
- [Microsoft Entra authentication with Azure SQL](https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-overview?view=azuresql)
- [Azure SQL Database connectivity architecture](https://learn.microsoft.com/azure/azure-sql/database/connectivity-architecture?view=azuresql)
- [EF Core database providers: SQL Server and Azure SQL](https://learn.microsoft.com/ef/core/providers/sql-server/)
- [Choose an Azure data store](https://learn.microsoft.com/azure/architecture/guide/technology-choices/data-store-overview)

対象の SQL engine、TFM、package version と deployment option に合わせ、`.github/skills/microsoft-learn/SKILL.md` の手順で詳細を確認します。
