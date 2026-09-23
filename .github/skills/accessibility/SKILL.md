---
name: accessibility
description: "Blazor WebAssembly UI の設計・実装・レビューで、WCAG に基づき semantic HTML、keyboard、focus、form、動的通知、視覚的アクセシビリティを確認する。"
license: MIT
---

# Blazor WebAssembly アクセシビリティ

Blazor WebAssembly UI のアクセシビリティ要件を実装・レビューする Skill です。特定の framework が WCAG 適合を自動的に保証すると考えず、実際の画面、DOM、操作、状態を確認します。

## 一次情報と適合範囲

- WCAG の達成基準・レベル・用語を判断する際は、[W3C WAI WCAG 日本語ページ](https://www.w3.org/WAI/standards-guidelines/wcag/ja) を必ず確認します。翻訳と最新勧告に差が疑われる場合は英語原文も参照します。
- WCAG 2.2 AA を通常の設計・レビュー目安とします。ユーザー要件や既存の適合目標がある場合はそちらを優先します。
- Microsoft Learn は `.github/skills/microsoft-learn/SKILL.md` に従い、Blazor の hosting、rendering、form validation など framework の挙動確認に使います。WCAG 適合基準の根拠は W3C WAI を一次情報とします。
- Fluent UI Blazor v5 使用時は `.github/skills/fluentui-blazor/SKILL.md` を併用し、実際の package version の component docs と rendered output を確認します。
- Fluent UI Blazor MCP が使える場合は、MCP の docs version と project package version が一致することを確認したうえで component の accessible name / keyboard behavior を調べます。MCP component docs は WCAG 適合の根拠にはしません。
- axe / Lighthouse 等の自動検査は補助です。自動検査の pass を完全な適合証明として扱いません。

## まず確認すること

- 対象ページ、利用者、主要 task、既存 WCAG / accessibility 要件。
- Blazor hosting model、`TargetFramework`、Fluent UI package version、変更された component と CSS。
- 初期・通常・keyboard focus・入力 error・loading・empty・success・dialog open の各状態。
- browser / screen reader / zoom 等の検証環境と、実際に行った検査。

## 実装・レビューの観点

### Semantics と名前

- native semantic HTML を優先し、不要な ARIA で native behavior を上書きしません。
- button は action、link は navigation とし、click handler だけの `div` / `span` を操作要素にしません。
- heading、landmark、list、table を目的に沿って使い、ページ構造を階層化します。
- 各 control の accessible name は目的を表し、visible label と一致または近い表現にします。icon-only control に名前を付けます。
- `title` / tooltip のみを必須情報や control の唯一の名前にしません。

### Keyboard と focus

- 主要 task を keyboard only で完了でき、Tab 順が論理的で focus indicator が明瞭であることを確認します。
- 標準の Enter / Space / Escape / 矢印キーの操作を壊さないようにします。
- Dialog / popover / menu を開いた時の focus、keyboard での退出、閉じた後の focus restore を確認します。
- focus outline を削除する場合は、視認性・contrast を保つ代替表示を用意します。

### Forms と動的状態

- label、hint、required 状態、入力例、validation error を関連付け、error の原因と修正方法を明示します。
- validation 後も入力を保持し、summary や最初の error への移動が必要か確認します。
- loading、成功、失敗、route change など非同期状態が screen reader に適切に伝わり、重要な error は画面内にも残るようにします。
- client-side validation は security validation の代わりにしません。

### 視覚、motion、日本語 UX

- 色だけで error / success / selected を伝えず、文字・形状・icon 等を併用します。
- text / control / focus の contrast と、zoom / narrow viewport / reflow による切れ・重なりを確認します。
- `prefers-reduced-motion` と forced-colors 等の利用者設定を壊さないようにします。
- 日本語 `lang`、長いラベル・エラー、全角半角、日本語入力、氏名・住所など実際の locale 条件を確認します。

## Fluent UI Blazor v5

- component 固有の label、ARIA parameter、keyboard pattern は v5 の公式 component docs で確認します。バージョンをまたいで property 名を推測しません。
- Fluent UI component が accessible name / keyboard support を備えていても、画面全体や組み合わせた動的状態の適合を保証するわけではありません。
- icon-only button、custom template の DataGrid cell、Dialog、Toast、Menu、Tooltip は実際の accessible tree と keyboard behavior を重点確認します。
- tooltip を唯一の説明にせず、Dialog では focus 移動・Esc・close 後の focus、DataGrid では v5 の documented keyboard behavior と実際の列設定を確認します。

## 検証のすすめ方

1. automated accessibility scan で明確な markup 問題を探します。
2. keyboard only で主要 task を操作し、focus 順と focus restore を確認します。
3. 重要な操作を NVDA / Narrator / VoiceOver など対象環境の screen reader で確認します。
4. zoom / reflow、contrast / forced-colors、reduced motion、日本語入力を必要に応じて確認します。
5. 実施した検査、環境、残る未確認事項を分けて報告します。

## Microsoft Learn 参照

- [ASP.NET Core Blazor documentation](https://learn.microsoft.com/aspnet/core/blazor/?view=aspnetcore-10.0) — Blazor の component / rendering / hosting guidance。
- [Blazor forms and validation](https://learn.microsoft.com/aspnet/core/blazor/forms/validation?view=aspnetcore-10.0) — form validation の framework behavior。
- [Blazor WebAssembly security](https://learn.microsoft.com/aspnet/core/blazor/security/webassembly/?view=aspnetcore-10.0) — client-side WebAssembly の security boundary。
- [Fluent UI Blazor v5 documentation](https://www.fluentui-blazor.net/) — v5 component 固有の実装・操作。
- [W3C WAI WCAG 日本語ページ](https://www.w3.org/WAI/standards-guidelines/wcag/ja) — WCAG の必須一次情報。
