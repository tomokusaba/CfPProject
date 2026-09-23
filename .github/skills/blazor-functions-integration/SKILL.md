---
name: blazor-functions-integration
description: "Blazor WebAssembly と Azure Functions 間の HTTP API 契約、CORS、環境別 endpoint、認証境界、連携テストを設計・実装する。両 project をまたぐ変更で使う。"
license: MIT
---

# Blazor WebAssembly + Azure Functions integration

Blazor WASM client と Azure Functions backend が HTTP で連携する場合のチェックリストです。まず API が同一 origin、cross-origin、または hosting platform proxy のどの経路を通るか、既存コードと deployment 設定から確認します。どの hosting / auth model も既定と決めつけません。

DTO、service、serialization を C# で変更する場合は `.github/skills/csharp-dotnet/SKILL.md` も併用します。
Microsoft / Azure の API や hosting-specific な制約は `.github/skills/microsoft-learn/SKILL.md` に従って検証します。
Functions に永続化を追加する場合は `.github/skills/azure-data-persistence/SKILL.md` と選択した provider Skill を使います。Blazor WebAssembly から SQL / Cosmos DB や Storage account に直接認証しません。Blob の直接 upload / download が明示要件の場合のみ、server-side authorization 後に発行する短命・最小権限の user delegation SAS と、Storage 側の限定的な CORS 設定を使います。

## API 契約

- 連携機能に新しい Blazor UI component が必要な場合は Fluent UI Blazor v5 を第一候補とします。既存 framework や明示要件が優先される場合は変更せず、UI 差分には `.github/skills/accessibility/SKILL.md` を適用します。
- route、HTTP method、query / headers、request body、response DTO、JSON naming / serializer settings、status code、error format を両 project で照合します。
- URI 組み立ては `HttpClient.BaseAddress` と相対 path の実際の解決を検証し、環境ごとの endpoint 設定を一貫させます。
- API versioning や shared DTO project は既存方式に従います。共有型を理由に client project が Functions implementation project に依存しないようにします。
- Functions 側で request を validation し、unauthorized / forbidden / invalid request / not found / server failure を必要に応じて分けます。UI は error を成功形の空データとして隠しません。
- JSON の互換性を変える場合は UI と API の双方に対して後方互換性、deployment 順序、test を確認します。

## Origin、CORS、認証

- ブラウザー origin（scheme、host、port）と Functions API origin を特定します。local と deployed environment の origin を混同しません。
- Cross-origin 要求に必要な CORS 設定は API をホストする側に限定的に設けます。wildcard origin は credentials と併用せず、必要な method / header だけを許可します。
- CORS は browser が応答を利用できるかを制御するもので、authentication / authorization や request validation ではありません。API は直接呼び出し可能な前提で server-side authorization を実施します。
- Azure Functions key は shared secret です。static client / WASM bundle は公開物と見なし、function key、client secret、connection string を埋め込みません。
- ユーザーを認証する必要がある場合、既存の OIDC / Microsoft Entra ID 等の方式と token validation / authorization を Learn で確認します。Function key をユーザー identity token として扱いません。
- same-origin proxy / Azure Static Web Apps を利用する場合、proxy 経由の auth、route、CORS の挙動を選定した hosting の公式 docs で確認します。新規導入は明示要件がある場合に限ります。

## 環境と秘密情報

- API base address や public feature flags は公開設定として扱い、`wwwroot` config / browser bundle から読めることを前提にします。
- `local.settings.json` は Functions のローカル実行用であり commit しません。deploy 時の app settings / Key Vault / managed identity の設定は client 設定と分離します。
- endpoint を環境別にする場合、production が誤って localhost を参照しないことを build / deploy 設定で検証します。

## 検証

- UI service の request URI、method、serialization と Functions response の組み合わせを test します。
- cross-origin 構成は CORS preflight と実 origin を含めて検証します。unit test だけで production CORS 設定が正しいと断言しません。
- error status、timeout / cancellation、offline、expired credential の UI behavior を確認します。
- 未実施の Azure deployment、browser E2E、auth provider test は未検証として報告します。

## Microsoft Learn 参照

- [Call a web API from an ASP.NET Core Blazor app](https://learn.microsoft.com/aspnet/core/blazor/call-web-api?view=aspnetcore-10.0)
- [Blazor WebAssembly security](https://learn.microsoft.com/aspnet/core/blazor/security/webassembly/?view=aspnetcore-10.0)
- [Azure Functions HTTP trigger](https://learn.microsoft.com/azure/azure-functions/functions-bindings-http-webhook-trigger)
- [Azure Functions security concepts](https://learn.microsoft.com/azure/azure-functions/security-concepts)
- [Work with access keys in Azure Functions](https://learn.microsoft.com/azure/azure-functions/function-keys-how-to)

Microsoft / Azure の具体的な API、CORS、authentication、hosting 制約を実装するときは、上記の overview に留まらず、対象 feature の最新 Learn page と project version に合う guidance を確認してください。
