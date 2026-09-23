---
name: azure-functions-dotnet-isolated
description: "C# Azure Functions の isolated worker、HTTP trigger、DI、設定、テスト、ローカル開発を実装・レビューする。Functions API や trigger/binding の変更で使う。"
license: MIT
---

# Azure Functions for .NET — isolated worker

Azure Functions の C# backend を開発・レビューする手順です。既存の worker model、Functions runtime、target framework、Worker / extension package version を最初に確認します。ここで示す方針を version-specific なコード例の代わりにせず、Microsoft Learn で現在の API を確認します。

C# 言語機能、nullability、async、例外、DI、resource ownership の共通方針には `.github/skills/csharp-dotnet/SKILL.md` を併用します。Learn での API / version 確認には `.github/skills/microsoft-learn/SKILL.md` を適用します。

永続化が必要な Function では `.github/skills/azure-data-persistence/SKILL.md` と実際に使う Azure Storage / Cosmos DB / Azure SQL Database の provider Skill を併用します。account key、database credential、connection string を Blazor WebAssembly に配置しません。Blob の直接転送だけは明示要件がある場合に限り、storage Skill の短命・最小権限 user delegation SAS を検討します。

## Model と HTTP trigger

- 新しい C# Functions では isolated worker を基本候補として検討します。対象 .NET version、Functions runtime、hosting plan のサポート状況を公式ドキュメントで確認します。
- 既存の in-process project は、要求と migration plan がない限り isolated worker へ一括移行しません。両モデル固有の API / package を混ぜません。
- HTTP trigger はプロジェクトの既存方式を踏襲します。built-in HTTP model は `HttpRequestData` / `HttpResponseData`、ASP.NET Core integration は対応する ASP.NET Core 型を使います。integration package と `Program.cs` の builder 設定は Learn に照らします。
- HTTP trigger に anonymous access を設定する場合は、その endpoint が公開でよいことを確認します。Function key は共有アクセスキーであり、ユーザー認証・authorization の代替ではありません。
- HTTP input はサーバー側で検証し、expected client errors と unexpected server failures を分けて HTTP response に反映します。

## Startup、DI、設定

- `Program.cs`、existing host builder、options、service lifetimes に従います。新しい helper / abstraction を追加する前に必要性を確認します。
- 外部 I/O は async で行い、対応する cancellation token を下位呼び出しに渡します。`.Result` / `.Wait()` は使いません。
- secrets をコード・`host.json`・tracked config に書きません。`local.settings.json` はローカル専用で秘密を含む可能性があるため source control から除外します。
- deployed environment の secret / credential は既存の運用モデルに合わせて管理し、Azure service access では managed identity がサポートされるかを Microsoft Learn で確認します。
- ログは構造化し、access token、request body 中の PII、secret を記録しません。

## CORS と deployment

- Blazor WASM から cross-origin で呼び出す場合、実際の UI origin と Functions / hosting 環境の CORS 設定を確認します。local developer origin と production origin を区別します。
- CORS の設定は必要な origin に絞ります。CORS は API authorization ではありません。
- Hosting platform / deployment target は既存構成を優先し、ユーザー要求なしに Static Web Apps 等を導入しません。

## テストとローカル開発

- business logic を function entry point から分離できる場合は、既存 test project と framework で単体テストします。
- HTTP contract、binding、host-specific behavior に重要な変更がある場合は、Functions Core Tools または既存 integration-test harness による検証を検討します。
- `local.settings.json` / local storage emulator 等の前提がある場合は、手順を明記し secret を含まない sample config を用います。
- テスト・build で実行した範囲と未検証の Azure 環境依存要素を区別して報告します。

## Microsoft Learn 参照

- [Guide for running C# Azure Functions in the isolated worker model](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide) — project setup、HTTP trigger、DI、ASP.NET Core integration。
- [Azure Functions HTTP trigger](https://learn.microsoft.com/azure/azure-functions/functions-bindings-http-webhook-trigger) — HTTP trigger behavior と binding。
- [Code and test Azure Functions locally](https://learn.microsoft.com/azure/azure-functions/functions-develop-local) — Core Tools とローカル設定。
- [Azure Functions security concepts](https://learn.microsoft.com/azure/azure-functions/security-concepts) — endpoint security と運用上の考慮事項。
- [Work with access keys in Azure Functions](https://learn.microsoft.com/azure/azure-functions/function-keys-how-to) — access key の意味と制約。
- [Migrate .NET apps from the in-process model to the isolated worker model](https://learn.microsoft.com/azure/azure-functions/migrate-dotnet-to-isolated-model) — 明示的な移行作業がある場合。

## 参考にした構成パターン

- [Awesome Copilot: Azure Functions C# instructions](https://github.com/github/awesome-copilot/blob/main/instructions/azure-functions-csharp.instructions.md)
- [Awesome Copilot: Azure Static Web Apps skill](https://github.com/github/awesome-copilot/blob/main/skills/azure-static-web-apps/SKILL.md)

Awesome Copilot は checklist / 構成の参考として扱い、API、package、security、version の根拠には Microsoft Learn と実際のプロジェクト構成を優先します。
