---
name: csharp-dotnet
description: "C# / .NET の実装・レビューで、言語機能、nullability、async、例外、DI、リソース管理、テストを project の対象 framework に合わせて扱う。"
license: MIT
---

# C# / .NET

C# の実装・レビューでは、まず project の SDK、target framework、language version、nullable context、analyzers、既存コードと tests を確認します。Blazor WebAssembly と Azure Functions 固有の設計は、それぞれの Skill と併用します。

## Project の規約と対応バージョン

- `global.json`、`.editorconfig`、`Directory.Build.props` / `targets`、`Directory.Packages.props`、各 `.csproj`、CI の SDK / TFM / `LangVersion` を確認します。
- repo の規約と analyzer 設定を優先します。project がサポートする C# version より新しい構文や API を、最新という理由だけで導入しません。
- `Nullable`、warning level、analyzers、TreatWarningsAsErrors を既存設定から確認し、依頼のない一括変更や warning 抑制をしません。
- Microsoft Learn の versioned documentation を対象 TFM / SDK と照合します。version-dependent な API の存在・対応状況は実装時に再確認します。
- Microsoft documentation の検索、完全なページ取得、code sample の利用には `.github/skills/microsoft-learn/SKILL.md` を適用します。

## 言語と API 設計

- 既存の naming / formatting / file organization に従い、意図を明確にする最小の変更をします。すべての method にコメントを付けたり、既存規約にない XML docs を一律追加したりしません。
- nullable annotations で必須値と optional 値を正確に表します。`null` を適切に検査・伝播し、`!` で警告を隠すのは根拠のある境界に限ります。
- `record`、`struct`、`class`、`required`、pattern matching 等は意味と利用者の C# version に合う場合に選び、流行や短さだけを理由に既存 API を置き換えません。
- 公開 API、serialization DTO、route / JSON contract の変更は compatibility と consumer への影響を確認します。wire format は property naming、nullability、enum、serializer options を含めて扱います。
- 小さな処理に不要な abstraction、dependency、generic framework を加えず、性能問題は測定または具体的な根拠なしに複雑化して最適化しません。

## Async、cancellation、並行処理

- I/O を含む非同期処理では `Task` / `Task<T>` と `async` / `await` を用い、可能な範囲で `CancellationToken` を下位 API へ伝播します。
- `.Result` / `.Wait()` による同期 block、未観測の fire-and-forget task、非同期処理を隠す `async void` を避けます。UI event handler は framework が要求する戻り値に従います。
- `ValueTask` や `ConfigureAwait(false)` を機械的に導入しません。既存 application / library の文脈と根拠に合わせます。
- shared mutable state、thread-safety、並行 request 間の状態漏れを確認し、実行環境に合う同期・状態管理を選びます。

## 例外、DI、リソース

- 不正な入力は境界で明示的に扱い、予期できる業務状態と予期しない故障を区別します。広範囲な catch、空の catch、正常値に見せる fallback で失敗を隠しません。
- 例外を変換・回復する必要がある箇所だけ catch し、再送出時は stack trace と元の原因を保ちます。ログや応答に secret、token、PII、内部例外を含めません。
- 既存 DI と constructor injection を優先し、service lifetime は hosting model と利用する state に合わせます。singleton に request / user 固有の mutable state を保持しません。
- 所有する disposable resource を `using` / `await using` 等で解放し、既存の ownership pattern と async disposal に従います。

## テストと変更検証

- 変更した behavior、null / error / cancellation boundary、API contract に対して既存 framework・naming・fixture に沿うテストを追加または更新します。
- テストを deterministic に保ち、実 Azure、network、secret、wall clock への依存は repository の既存 integration-test strategy に従って明示します。
- repo が定める最小の build / test / analyzer command を実行し、未実行の検証は未実施と報告します。
- style の好みだけで指摘せず、既存規約違反が correctness、保守性、互換性にどう影響するかを具体化します。

## Blazor / Azure Functions との併用

- Blazor WebAssembly の service、component code-behind、browser boundary では `.github/skills/blazor-webassembly/SKILL.md` と本 Skill を併用します。WASM 内の code / config を secret boundary と見なしません。
- Functions の trigger、worker、binding、hosting 固有の実装では `.github/skills/azure-functions-dotnet-isolated/SKILL.md` を併用し、worker model と package version を取り違えません。
- client / server 間の DTO や JSON contract では `.github/skills/blazor-functions-integration/SKILL.md` も適用します。
- Azure data access の選定・実装は `.github/skills/azure-data-persistence/SKILL.md` と実際の service（Azure Storage / Cosmos DB / Azure SQL Database）の Skill に従います。

## Microsoft Learn 参照

- [Common C# code conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions) — repo の規約が未定義の場合の参考。
- [Nullable reference types](https://learn.microsoft.com/dotnet/csharp/nullable-references) — nullability annotations、flow analysis と警告。
- [Task-based asynchronous pattern (TAP)](https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap) — Task、cancellation と async API の設計。
- [Exception handling](https://learn.microsoft.com/dotnet/csharp/fundamentals/exceptions/) — C# の例外処理。
- [Implement a Dispose method](https://learn.microsoft.com/dotnet/standard/garbage-collection/implementing-dispose) — managed / unmanaged resource の ownership と disposal。

Learn の一般的な例は project の TFM / language version、既存規約、Blazor hosting model、Functions worker model を置き換えるものではありません。該当する framework 固有 Skill と合わせて判断します。
