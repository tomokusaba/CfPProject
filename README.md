# CfPProject

.NET 10 の Blazor WebAssembly、isolated Azure Functions、Azure Cosmos DB for NoSQL を使うカンファレンス CFP 運営アプリです。要件・スキーマ・設計の根拠は [`doc/`](doc/) を参照してください。

## 実装範囲

- **公開閲覧:** 公開カンファレンスの検索・ページング・詳細、公開 CFP とフォーム項目、応募責任者の同意と Organizer 個別公開がそろった採択プロポーザル、公開タイムテーブル、セッション／プロポーザル OGP 共有ページ。
- **認証・権限:** Blazor MSAL の Authorization Code + PKCE、Functions Easy Auth principal から issuer + subject を内部ユーザー ID に対応付け、conference-scoped `ConferenceOwner` / `Organizer` / `Reviewer` 権限を API で検証。ロールをクライアントから受け入れません。
- **CFP:** カンファレンス作成・編集・公開・アーカイブ、募集状態と showcase 設定、ETag 付きメンバー管理、フォーム版を不変にした募集種別設定、speaker のプロフィール／下書き／提出／編集／取り下げ、公開同意と撤回。
- **審査・日程:** reviewer 割当と利益相反申告、採点／コメント、採否、操作履歴、部屋・トラック・セッション配置、サーバー側の時間枠競合検証、ETag 付き revision 保存／公開。
- **通知:** 応募受付・採否通知と対象者向け一括運営連絡の transactional outbox、Cosmos change feed → Queue Storage → ACS Email、確認済み宛先・suppression・任意運営連絡 opt-in の送信直前確認、配信 Event Grid journal、プレビュー／監査理由付きメール管理画面。
- **UI:** 日本語、レスポンシブ、semantic HTML、可視フォーカス、入力エラー／loading／empty／success 通知。Fluent UI は初期候補でしたが、この実装では native controls を使っています。

## Solution layout

| Project | Responsibility |
| --- | --- |
| `Cfp.AppHost` | .NET Aspire local orchestration for the Web, Functions, Cosmos DB Emulator, and Azurite |
| `Cfp.Domain` | Conference、CFP、proposal、review、schedule、email 状態遷移 |
| `Cfp.Application` | Use cases、conference authorization、persistence / email ports |
| `Cfp.Contracts` | Versioned MemoryPack HTTP DTOs |
| `Cfp.Infrastructure` | Cosmos DB / managed identity / ACS Email |
| `Cfp.Functions` | Public/protected APIs と change-feed、queue、timer、Event Grid triggers |
| `Cfp.Web` | Public、speaker、organizer、reviewer Blazor WebAssembly pages |
| `Cfp.Tests` | Domain、Application、MemoryPack、auth、API client、email workflow tests |

## Build and tests

Install the .NET 10 SDK pinned in `global.json`, then run:

```powershell
dotnet restore CfpProject.sln
dotnet build CfpProject.sln --no-restore
dotnet test CfpProject.sln --no-build
```

No production event, user, credential, or secret data is included in the repository.

## Aspire でのローカルデバッグ

Docker Desktop（Linux containers）、.NET 10 SDK、Azure Functions Core Tools v4 をインストールし、次を実行します。Visual Studio では `Cfp.AppHost` をスタートアッププロジェクトにします。

```powershell
dotnet run --project src\Cfp.AppHost\Cfp.AppHost.csproj
```

AppHost は WebAssembly 開発サーバー（`http://localhost:5094`）、Functions（`http://localhost:7071`）、Cosmos DB Emulator、Azurite を起動します。Cosmos DB は `cfp` database と本番と同じ partition key の6 container を作成し、AppHost が emulator の接続情報を Functions に渡します。Azurite は Functions host storage／Queue trigger に使用します。AppHost Dashboard から各 resource の状態、ログ、Web UI を確認できます。`src\Cfp.Web\wwwroot\appsettings.Development.json` と Functions の launch profile はこれらの固定 localhost port と CORS origin を合わせています。

Cosmos DB Emulator は TLS を使います。接続時に証明書エラーが出た場合は [Microsoft の手順](https://learn.microsoft.com/azure/cosmos-db/how-to-develop-emulator#import-the-emulators-tlsssl-certificate)で emulator の証明書を信頼済みストアに登録してください。SDK の TLS 検証を無効化しないでください。

Functions のローカル実行では Easy Auth が提供されません。公開 API／画面と Cosmos DB を使う基本フローはデバッグできますが、保護された運営・speaker・reviewer API の認証は Entra External ID と Easy Auth を構成した環境で確認してください。ACS Email は emulator 化していないため、一括メールを試す場合は `Communication__Endpoint` と検証済み `Communication__SenderAddress`、および開発者 ID の ACS Email Sender 権限も必要です。

## Aspire を使わない Functions のローカル設定

Aspire を使わず `func start` で Functions 単体を起動する場合は、Azure Functions Core Tools v4 と Azurite を用意し、ignored file `src\Cfp.Functions\local.settings.json` に環境に応じた値を設定します。以下は Azure Cosmos DB と ACS を使う例です。Aspire から起動する場合、Cosmos／host storage の設定は AppHost が注入します。

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "COSMOS_DATABASE_NAME": "cfp",
    "Cosmos__Endpoint": "https://<account>.documents.azure.com:443/",
    "CosmosConnection__accountEndpoint": "https://<account>.documents.azure.com:443/",
    "Communication__Endpoint": "https://<communication-service>.communication.azure.com/",
    "Communication__SenderAddress": "no-reply@<verified-email-domain>"
  }
}
```

Run `az login` and assign the developer identity Cosmos DB data-plane access and, when testing sends, the ACS Email Sender role. Standalone Functions use `DefaultAzureCredential` for Azure Cosmos DB; do **not** put Cosmos keys, ACS connection strings, or other credentials in the browser project. The sender address must be verified by ACS or campaign preview returns a configuration error.

All values in `wwwroot/appsettings.json` are public. Configure:

```json
{
  "ApiBaseUrl": "https://<function-app>.azurewebsites.net/",
  "Authentication": {
    "Authority": "https://<external-id-tenant-authority>/",
    "ClientId": "<spa-application-client-id>",
    "ApiScope": "<api-application-scope>"
  }
}
```

Register the SPA callback paths `/authentication/login-callback` and `/authentication/logout-callback`, the API scope/audience, and Google/Microsoft identity providers in Entra External ID. Bicep parameters `externalIdApiClientId` and `externalIdMetadataUrl` configure Easy Auth for the **API app**; these are separate from the public SPA client ID. The app uses only a verified email claim for delivery; when External ID does not supply one, email is suppressed rather than sent to an unverified address.

## Azure deployment

`infra/main.bicep` provisions the Japan East Functions/Cosmos/Storage resources, East Asia Static Web App, ACS Email resources, six Cosmos containers, managed identity permissions, ACS Email Sender role, and an exact-origin CORS allowlist. Preview changes first:

```powershell
az deployment group what-if `
  --resource-group <resource-group> `
  --template-file infra/main.bicep `
  --parameters externalIdApiClientId=<api-client-id> `
               externalIdMetadataUrl=<metadata-url> `
               additionalCorsOrigins=[] `
               emailSenderAddress=''
```

After the Azure-managed email domain is provisioned, set `emailSenderAddress` to its verified MailFrom address. `infra/email-events.bicep` is a second-stage deployment: publish the Functions code first so `EmailDeliveryReportFunction` is indexed, then create the ACS delivery-report Event Grid subscription:

```powershell
az deployment group what-if `
  --resource-group <resource-group> `
  --template-file infra/email-events.bicep `
  --parameters communicationServiceName=<communication-service-name> `
               functionAppName=<function-app-name>
```

The outbox queue contains only `{ conferenceId, outboxId }`. `Accepted` means ACS accepted the request, not delivery. Expired sends become `Unknown` and are never automatically resent. Event Grid event IDs are journaled; hard bounce/suppression reports suppress the verified profile address. Organizers can target proposals by status, preview the fixed recipient snapshot, configured sender, and plain-text message, then confirm with an audit reason. Each campaign is limited to 50 eligible recipients and expires after 30 minutes; confirmation uses ETag and idempotency checks and atomically creates all recipient outbox items with the previewed sender. Larger campaigns, arbitrary per-recipient selection, and manual resend controls are not implemented.

## Validation boundary

The complete solution build, unit tests, Functions binding metadata generation, and both Bicep templates are locally testable. This environment has no Entra External ID tenant, deployed Cosmos account, verified ACS sender domain, or Event Grid subscription. Live Easy Auth claim mapping, tenant redirect configuration, data-plane RBAC, CORS preflight, ACS delivery, and Azure deployment still require environment validation; a successful local build does not claim those have been verified.

Favorites, exports, calendar integrations, automated social posting, and the optional blind-review setting remain out of scope.
