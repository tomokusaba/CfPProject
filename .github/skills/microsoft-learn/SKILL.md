---
name: microsoft-learn
description: "Microsoft / Azure の仕様・API・version・コード例を Microsoft Learn で調査し、対象 project に照らして根拠付きで実装・回答する。"
license: MIT
---

# Microsoft Learn research

Microsoft、.NET、Azure の API、設定、version、support policy、security behavior を調べる場合の共通 Skill です。Microsoft Learn を一次情報として確認し、Awesome Copilot や一般的な web search の例を API 仕様の根拠にしません。

## 調査手順

1. **実 project の前提を確定する。** `TargetFramework` / SDK / `LangVersion`、NuGet package version、Azure Functions runtime / worker model、hosting・認証方式など、質問の答えに関係する条件を調べます。未確認の条件は推測で固定しません。
2. **具体的な検索を行う。** Microsoft Learn MCP が利用可能なら `microsoft_docs_search` を使います。query には製品・API 名、目的、language、該当 version を含めます（例: `Azure Functions isolated worker HTTP trigger C# .NET 10`）。
3. **根拠を必要な範囲で取得する。** search excerpt が不足・切り詰められている、または複数の overload / config / migration detail が関係する場合は、該当ページを `microsoft_docs_fetch` で読みます。簡単な照合のためだけに無関係な長い記事全体を取得しません。
4. **Microsoft / Azure のコードを生成する前に確認する。** 利用可能なら `microsoft_code_sample_search` を適切な language と task で使い、sample の version・hosting model・前提条件を project と照合します。sample は API 形状の参考であり、project にそのまま貼れる保証ではありません。
5. **実際の問いに照らす。** 検索結果の見出しや要約だけで結論を断定せず、対象 version の API reference / guide を優先します。Learn の現行例が project の古い TFM / package と異なる場合は、互換性を分けて説明します。
6. **回答に根拠を示す。** version-sensitive な仕様や推奨には確認した Microsoft Learn URL と、適用する version / 前提を添えます。docs が曖昧・未確認なら、その制約と追加で必要な情報を明示します。

## ソースの使い分け

- Learn は Microsoft 製品の概念、tutorial、API、configuration、support / security guidance の標準的な一次情報です。`learn.microsoft.com` に情報があることを前提にせず、Microsoft 製でない library の仕様まで Learn から推定しません。
- Fluent UI Blazor component の詳細は `.github/skills/fluentui-blazor/SKILL.md` に従い、利用可能な Fluent UI MCP docs と project package version を照合します。WCAG 適合基準は W3C WAI、GitHub 固有仕様は GitHub 公式資料など、その分野の適切な一次情報を使います。
- Awesome Copilot は agent / skill 構成の参考として扱い、そこにあるコード例・version の記述を Microsoft API の根拠として引用しません。
- Learn MCP が利用できない場合は `web` tool で Microsoft Learn の公式 URL を検索・取得します。`mslearn` CLI は既に利用可能な環境でのみ fallback として使い、依頼なしに package を install しません。サードパーティの要約は一次根拠の代わりにしません。

## Tool access

この Skill を使う Agent の frontmatter で、必要な MCP tools を許可します:

- `microsoft-learn/microsoft_docs_search`
- `microsoft-learn/microsoft_docs_fetch`
- `microsoft-learn/microsoft_code_sample_search`

MCP が環境から提供されている場合に使い、endpoint や credential を repository に埋め込みません。tool が利用できない時は上記 fallback に従います。

## 参考

- [Microsoft Learn](https://learn.microsoft.com/)
- [Awesome Copilot: Microsoft Docs skill](https://github.com/github/awesome-copilot/blob/main/skills/microsoft-docs/SKILL.md)
- [Awesome Copilot: Microsoft Code Reference skill](https://github.com/github/awesome-copilot/blob/main/skills/microsoft-code-reference/SKILL.md)
