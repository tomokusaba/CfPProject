---
name: blazor-webassembly
description: "Blazor WebAssembly の UI、DI、HTTP API 呼び出し、ブラウザー security boundary、テストを実装・レビューする。Blazor WASM の .razor / Program.cs / client project に関する作業で使う。"
license: MIT
---

# Blazor WebAssembly

Blazor WebAssembly の開発・レビュー向け手順です。最初に既存 client project、target framework、SDK、認証方式、hosting model を確認し、プロジェクト固有の規約を優先します。

`.cs`、Razor code-behind、service の C# 実装では `.github/skills/csharp-dotnet/SKILL.md` を併用します。

## UI component framework

- 新しい UI component を選ぶ場合の第一候補は Fluent UI Blazor v5 です。既存プロジェクトの別 framework、ユーザー指定、互換性要件が優先される場合は置き換えません。
- Fluent UI Blazor を採用・変更する場合は `.github/skills/fluentui-blazor/SKILL.md` に従い、既存 project の package version に適合する v5 の公式 API を確認します。
- UI の accessibility review には `.github/skills/accessibility/SKILL.md` を適用します。

## 基本手順

1. `*.csproj`、`Program.cs`、`wwwroot/appsettings*.json`、`HttpClient` の登録、routing、既存 tests を調査します。
2. 既存の UI / service / DTO 構成を再利用し、単純な機能に新しい layer や package を追加しません。
3. 必要な framework API、package version、security behavior を `.github/skills/microsoft-learn/SKILL.md` に従い、対象 TFM に合う Microsoft Learn で確認します。
4. UI が依存する service と user-visible states を実装し、変更箇所に応じた test と検証を行います。

## HTTP API 呼び出し

- `HttpClient` の DI 登録、base address、相対 URI の解決方法を確認します。相対 URI は slash の有無で destination が変わり得るため、実際の base address と合わせて検証します。
- API 呼び出しは component に直接散らばらせず、既存の service pattern に合わせます。DTO と JSON options は client / server の両方で互換性を確認します。
- network failure、non-success status、空結果、loading、cancel、retry を既存 UX に合わせて扱います。API の失敗を正常な空データとして黙って隠しません。
- server-side ASP.NET Core の `HttpClient` / `IHttpClientFactory` の推奨を client-side WebAssembly にそのまま当てはめず、対象実行環境の公式 docs を確認します。
- `appsettings.json` や `wwwroot` の値はブラウザーから取得可能です。API endpoint 等の公開設定値に限定し、key、secret、connection string を入れません。

## Browser security

- client-side validation は UX のためであり、信頼境界ではありません。入力の検証と authorization を API 側でも実施します。
- function key や confidential client secret を client bundle、`localStorage`、URL、ログへ保存・送信しません。WASM client はすべてのコード・アセットが利用者に解析可能です。
- database credential、storage account key、connection string を WASM に含めません。Blob の直接転送が明示要件の場合のみ、`.github/skills/azure-storage/SKILL.md` に従った server-issued の短命・最小権限 user delegation SAS を使います。
- 認証・認可の実装や token 保存方式を決める場合は、プロジェクトの既存方式と Blazor WebAssembly の Microsoft Learn security guidance を確認します。
- cross-origin API では Functions / hosting 側に必要な CORS を設定します。CORS は認証・認可ではありません。credentials を使う場合、wildcard origin を設定しません。

## UI 品質とテスト

- semantic HTML、明示的 label、validation message と入力の関連付け、keyboard 操作、visible focus を保ちます。
- API エラーは利用者が次に取る行動が分かる形で表示し、機密情報や内部例外を公開しません。
- service behavior を既存のテスト framework で検証し、URI、request / response、status code に影響する変更は適切な契約テストを追加します。
- 実ブラウザーや外部 Azure 環境がない検証では、その制約を明記します。

## Microsoft Learn 参照

- [Call a web API from an ASP.NET Core Blazor app](https://learn.microsoft.com/aspnet/core/blazor/call-web-api?view=aspnetcore-10.0) — API 呼び出し、`HttpClient`、server/client の差異。
- [Blazor WebAssembly security](https://learn.microsoft.com/aspnet/core/blazor/security/webassembly/?view=aspnetcore-10.0) — client-side security と認証シナリオ。
- [Blazor dependency injection](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/dependency-injection?view=aspnetcore-10.0) — Blazor の service 登録と DI。

リンクの version view がプロジェクトの TFM と異なる場合は、対応する Learn version を選び直してください。API の現在の package / support 要件は実装時に再確認します。
