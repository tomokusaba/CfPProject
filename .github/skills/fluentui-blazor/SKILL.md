---
name: fluentui-blazor
description: "Fluent UI Blazor v5 を第一候補として Blazor UI を構築し、v5 package/API、providers、styles、component とアクセシビリティを確認する。"
license: MIT
---

# Fluent UI Blazor v5

Blazor WebAssembly UI component が必要な場合の第一候補として Fluent UI Blazor v5 を使うための Skill です。既存 project の別 UI framework、ユーザー指定、要件・互換性が優先される場合は無理に導入・置換しません。

## バージョンを先に確認する

- `*.csproj` と `Directory.Packages.props` の `Microsoft.FluentUI.AspNetCore.Components` / Icons package version を確認します。
- この Skill は v5 向けです。API の名前・parameter・provider・static asset の構成を、v4 の例や記憶から推測しません。
- 現在の v5 installation / component docs と実 project version が一致するか確認します。既存 version を依頼なく更新せず、preview / prerelease package もユーザー要件なしに導入しません。
- 新規採用で v5 が対象 framework / TFM と互換でない場合は、理由を示して既存の適切な選択肢を使います。

## Fluent UI Blazor MCP を使う

Fluent UI Blazor MCP server が利用可能な環境では、version-specific な component API、icon、migration を調べるために必要に応じて利用します。Agent frontmatter の `tools` に `Fluent-UI-Blazor-5/*` を指定して呼び出しを許可しています。

1. `get_version_info` を呼び、MCP が対象とする package version を確認します。
2. 対象 project の `.csproj` または `Directory.Packages.props` から実際の package version を確認し、`check_project_version` で照合します。
3. version が一致するときは `search_components` / `get_component_details` / `search_documentation` / `get_documentation_topic` を component の実装に応じて使います。v4 からの移行時は migration tools、icon 選定時は icon search / usage tools を使います。
4. version が一致しない場合は、MCP が返す API details をその project の確定仕様として使いません。project が参照する package version の公式 repository / package docs を優先します。

この repository に MCP endpoint や credential を追加する必要はありません。MCP が利用できない場合は下記の公式 documentation と project に導入済みの package docs を使います。MCP を使うのは version-sensitive な質問や component / icon / migration の調査が必要な時に限ります。
Microsoft の underlying Blazor / .NET API の調査には `.github/skills/microsoft-learn/SKILL.md` を併用します。Fluent UI component 固有の仕様は Fluent UI docs を使い、Microsoft Learn の記述から推測しません。

## v5 の導入と構成

1. 公式 [Installation guide](https://www.fluentui-blazor.net/installation) と現在の package version を確認します。
2. `Microsoft.FluentUI.AspNetCore.Components` package を追加し、icons が必要な時だけ `Microsoft.FluentUI.AspNetCore.Components.Icons` を追加します。既存の central package 管理ルールに従います。
3. 必要な namespace / icon alias を既存の `_Imports.razor` に加えます。
4. `Program.cs` の Blazor host に、公式 v5 setup が要求する `builder.Services.AddFluentUIComponents()` を登録します。
5. v5 docs に従い、root layout に `<FluentProviders />` を一度配置します。provider の有無や配置をサービス系 UI の動作と合わせて確認します。
6. v5 installation guide が求める component stylesheet を、アプリの実際の hosting / entry document に一度だけ含めます。旧 v4 の static asset や setup と混在させず、既存 asset の重複を避けます。
7. interactive component が Blazor WebAssembly の構成で実際に interactive であることを確認します。Blazor Web App の `@rendermode InteractiveServer` 例を WASM project に貼り付けません。

## Component を使うとき

- 採用前に v5 の公式 component docs を確認します。特に Button、TextField、Select、Dialog、DataGrid 等で version-specific な parameter / keyboard operation / rendering を調べます。
- Action は button、navigation は link を使い、icon-only button には v5 docs に合う accessible name を付けます。tooltip / `Title` だけを必須の名前や説明にしません。
- Form は visible label、validation、error association、keyboard focus を保ちます。
- Dialog、menu、toast 等は provider、loading/error state、keyboard exit、focus restore、announcement を確認します。
- DataGrid は実際の DataGrid docs、利用する display mode、sort / selection / virtualization / custom template を照合し、標準 table / grid behavior が崩れていないかテストします。
- Fluent theme / token を優先し、既定の focus indicator・forced-colors・reduced motion を壊す global CSS override を避けます。
- `Microsoft.FluentUI.AspNetCore.Components` が存在することだけで WCAG 適合とみなしません。UI 変更では `.github/skills/accessibility/SKILL.md` を併用します。

## アクセシビリティ

- WCAG の判定には W3C WAI の [WCAG 日本語ページ](https://www.w3.org/WAI/standards-guidelines/wcag/ja) を一次情報として参照します。
- Fluent component の仕様は v5 docs、適合の実態は生成 DOM / accessibility tree、keyboard 操作、画面状態で確認します。
- 詳しい実装・検証の方針は `.github/skills/accessibility/SKILL.md` を参照します。

## 公式参照

- [Fluent UI Blazor v5 installation](https://www.fluentui-blazor.net/installation)
- [Fluent UI Blazor documentation and component demos](https://www.fluentui-blazor.net/)
- [Microsoft Fluent UI Blazor repository](https://github.com/microsoft/fluentui-blazor)
- [Microsoft Learn: Blazor WebAssembly security](https://learn.microsoft.com/aspnet/core/blazor/security/webassembly/?view=aspnetcore-10.0)
- [Microsoft Learn: Blazor dependency injection](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/dependency-injection?view=aspnetcore-10.0)
- [W3C WAI WCAG 日本語ページ](https://www.w3.org/WAI/standards-guidelines/wcag/ja)
