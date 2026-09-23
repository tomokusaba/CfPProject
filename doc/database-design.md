# Cosmos DB データモデル設計

- 状態: 技術検証向け初期設計
- 更新日: 2026-09-23
- 対象: Azure Cosmos DB for NoSQL、データベース `cfp`
- 関連: [全体アーキテクチャ](architecture.md)

## 1. 設計前提

- Cosmos DB を業務データの正本とし、データアクセスは Azure Functions のみから行う。Blazor WebAssembly に Cosmos DB のキーやデータプレーン資格情報を含めない。
- `/api/v1` の Blazor–Functions 間 HTTP payload には MemoryPack を使うが、Cosmos DB item は query 可能な JSON document として保存する。SNS share endpoint の HTML、Event Grid の JSON event、Azure Queue の JSON message はそれぞれ専用契約とし、API DTO と永続化／event／queue schema を共有しない。
- 技術検証では Japan East の単一リージョン、Free Tier、共有データベーススループット 1,000 RU/s を使う。Free Tier は serverless と併用できず、無料枠を超えるスループット・ストレージは課金される。
- 1 コンテナー内のトランザクション境界は同一のパーティションキー値に限定する。異なるコンテナー間の書き込みを一つの ACID transaction として扱わない。
- 会議単位の管理操作を効率化し、応募・監査・メール outbox を同一トランザクションで扱えるよう、会議データは `/conferenceId` に集約する。1 会議に書き込みが集中した場合のホットパーティションは、負荷試験で確認する。
- 初期設計は小規模な技術検証向けであり、Free Tier のまま本番規模に対応できることを保証しない。

## 2. 主要アクセスパターン

| 操作 | 読み書き方法 | 注意点 |
|---|---|---|
| 公開 URL から会議を取得 | `conferenceDirectory` を `slug` で point read し、得た `conferenceId` で `conferenceData` の会議アイテムを読む | 非公開・アーカイブ状態を API 側で検査する |
| 公開会議カタログ | `conferenceDirectory` の公開 projection を `state == active` でクロスパーティションクエリ | 継続トークンでページングし、公開項目だけを返す |
| 会議の募集・設定を読む | `conferenceData` の `conferenceId` を指定し、必要なら `type` で絞って query | 常にパーティションキーを指定する |
| 会議内の応募一覧・審査一覧 | `conferenceId` を指定し、`type`、`status`、`proposalTypeId` 等で絞って continuation token 付き query | 大きな一覧を一度にメモリへ読まない |
| 応募者自身の応募一覧 | `conferenceData` を `ownerUserId` でクロスパーティションクエリ | 初期は低件数前提。RU と遅延が増えたら `userActivity` read model を検討 |
| 審査担当者の割当一覧 | `conferenceData` を `reviewerUserId` でクロスパーティションクエリ | 会議内の審査はパーティションスコープにできる |
| 会議のタイムテーブル | `conferenceData` を `conferenceId` で読み、公開対象の `scheduleSlot` を開始時刻順に取得 | 同じ会議・部屋で時間重複をサーバー側で検査 |
| ログインユーザー解決 | issuer と subject から作った `identityKey` で `identityDirectory` を point read し、内部 `userId` で `userProfiles` を読む | メールアドレスをアカウント識別子にしない |
| メール通知 | `conferenceData` の recipient ごとの outbox change feed を処理し、Queue には `conferenceId` と outbox ID のみを渡す | partition key を含めて point read し、少なくとも一度の処理を前提にする |
| ACS 配信レポート | `emailDeliveryDirectory` を provider message ID で point read し、`conferenceId` / outbox ID を解決 | Event Grid の event ID で重複処理を抑止し、別 container 更新は再実行可能にする |
| 監査履歴 | `conferenceId` を指定し、時刻順に `auditEvent` を取得 | 監査本文に応募本文や認証情報を複製しない |

## 3. データベースとコンテナー

すべて `cfp` データベースに配置し、6 container で共有スループット 1,000 RU/s を使う。データプレーン RBAC はアプリの managed identity にデータベーススコープで付与し、アプリからのキー認証は無効にする。

| コンテナー | パーティションキー | 主なアイテム | 使い分け |
|---|---|---|---|
| `conferenceData` | `/conferenceId` | conference、proposal type/version、proposal、review、assignment、membership、room、track、schedule slot、email campaign/recipient outbox、audit event | 会議内の読み書きと同一パーティション内の transactional batch |
| `conferenceDirectory` | `/slug` | slug、会議 ID、公開カタログ用の最小 projection | 既知 slug の point read、重複予約、公開会議一覧の検索 |
| `userProfiles` | `/userId` | speaker profile、通知設定、会議運営メールの opt-in、確認済みメール宛先、現在の宛先の配信抑止状態 | 内部ユーザー ID を指定した point read/write |
| `identityDirectory` | `/identityKey` | 外部 ID から内部ユーザー ID への対応 | issuer + subject によるログインユーザー解決 |
| `emailDeliveryDirectory` | `/providerMessageId` | ACS provider message ID と outbox の lookup、および event ID ごとの受信・適用 journal | Event Grid delivery report から outbox を point read し、event を再実行する |
| `functionLeases` | `/id` | Cosmos DB change feed processor の lease | Functions trigger の内部状態。業務 API から公開しない |

`conferenceDirectory` は 1 つの slug に対して `id: "entry"` の 1 アイテムを使う。作成は条件付き create とし、競合時は既存 slug として扱う。公開 projection には公開状態、タイトル、開催日時等だけを複製し、非公開情報を置かない。公開後の slug 変更は初期リリースでは行わない。将来変更する場合は旧 slug を alias/redirect として保持し、複数コンテナー更新を再実行可能な手順にする。

`emailDeliveryDirectory` 内の lookup item は `id` と partition key の両方に provider message ID を使う。event journal は同じ partition に `id: "event:{eventId}"` と `providerMessageId` partition key で保存する。Event Grid event の provider message ID から point read で宛先 outbox を特定し、メールアドレスや本文を delivery lookup item に複製しない。event journal は `Received → Applied` とし、別 container の outbox 更新が失敗しても `Received` を再適用する。lookup がまだない event は一時失敗として retry し、最終 retry 失敗は dead-letter 監視から照合・再処理する。

各会議には有効な `ConferenceOwner` を最低1名維持する。最後の owner の削除・降格は、新 owner への移譲を同一操作で完了しない限り Functions が拒否する。同時に複数 owner を降格する競合を防ぐため、owner roster revision を持つ `conference` item の ETag 更新と membership 変更を同じ transactional batch に入れる。競合した処理は最新 roster を読み直して再検証する。

`identityKey` は、期待する issuer の完全一致を検査した上で、token の `iss` と `sub` の値を大文字小文字変換せず連結 (`iss + "\n" + sub`) した UTF-8 の SHA-256 とする。メールアドレスを使った自動アカウント統合は行わない。Google 等の IdP を追加・統合する際は、両方のアカウントで再認証した後にのみ同じ内部 `userId` へリンクする。競合時に別ユーザーの identity mapping を上書きしない。

## 4. 共通アイテム形式と識別子

`conferenceData` の全アイテムは以下を基本形とする。`conferenceId` は実際のパーティションキー値であり、`type` は query と API DTO 選別に使う。

```json
{
  "id": "proposal:01J...",
  "conferenceId": "conf_01J...",
  "type": "proposal",
  "schemaVersion": 1,
  "createdAtUtc": "2026-09-23T10:00:00Z",
  "updatedAtUtc": "2026-09-23T10:00:00Z",
  "createdBy": "user_01J..."
}
```

日時は UTC の RFC 3339 文字列で保存する。会議の表示には `timeZoneId`（例: `Asia/Tokyo`）も保持する。ID はサーバーで生成し、同じ会議内で重複しないようにする。外部入力をそのまま `id`・パーティションキーに採用しない。

## 5. アイテム定義

### 5.1 `conferenceData`

| `type` | `id` の形式 | 主な属性 |
|---|---|---|
| `conference` | `conference` | `conferenceId`、title、description、`lifecycleState`、`visibility`、`publicShowcaseEnabled`、`ownerRosterRevision`、slug、`timeZoneId`、開催日時、主催者設定 |
| `cfp` | `cfp` | 保存状態 `Draft / Published / ManuallyClosed`、開始・締切 UTC、公開 CFP 設定 |
| `reviewCycle` | `reviewCycle:{cycleId}` | 審査状態 `NotStarted / InProgress / Completed`、対象フォーム版、開始・完了日時 |
| `schedulePublication` | `schedule-publication` | 日程状態 `Draft / Published`、公開 revision、公開日時 |
| `proposalType` | `proposalType:{proposalTypeId}` | 表示名、説明、受付状態、時間、上限、現在のフォームバージョン |
| `proposalTypeVersion` | `proposalTypeVersion:{proposalTypeId}:{version}` | 変更しないフォーム schema、入力項目 ID、型、必須条件、文字数・選択肢制約 |
| `proposal` | `proposal:{proposalId}` | `ownerUserId`、`proposalTypeId`、状態、フォームバージョン、回答、共同登壇者の最小 snapshot、提出日時、`publicationState: Hidden / Published`、応募責任者の同意・共同登壇者同意確認・Organizer 公開者と日時 |
| `conferenceMembership` | `membership:{userId}` | 内部 `userId`、会議単位の role、招待・有効状態 |
| `reviewerAssignment` | `assignment:{proposalId}:{reviewerUserId}` | `proposalId`、`reviewerUserId`、割当日時、利益相反状態 |
| `review` | `review:{proposalId}:{reviewerUserId}:{round}` | 採点、構造化評価、コメント、reviewer ID、更新状態 |
| `room` | `room:{roomId}` | 会場、部屋名、収容人数、表示順 |
| `track` | `track:{trackId}` | トラック名、色等の表示設定、表示順 |
| `scheduleSlot` | `slot:{slotId}` | `proposalId`（MVP の stable `sessionId`）、room/track ID、開始・終了 UTC、枠種別、公開状態 |
| `scheduleRevision` | `schedule-revision` | 会議内の日程更新 revision。`_etag` による競合検出に使う |
| `emailCampaign` | `emailCampaign:{campaignId}` | 対象 proposal status と recipient user ID の固定 snapshot、送信者アドレス、件名・プレーンテキスト本文、対象外人数、作成者、request hash、preview 有効期限、確認 operation ID／理由 |
| `emailOutbox` | `outbox:{eventId}:{recipientId}` | recipient user ID、`Transactional / ConferenceOperations`、campaign 送信者 snapshot、通知内容／template version、`Pending / Ready / Sending / Accepted / Delivered / Bounced / Suppressed / FilteredSpam / Quarantined / Failed / Unknown / Cancelled`、provider message ID、attempt ID・send lease、再試行情報、作成日時 |
| `auditEvent` | `audit:{eventId}` | actor、操作、対象 ID、日時、必要最小限の変更要約 |

会議状態は一つの `status` にまとめない。公開の可否は会議の `visibility`、募集の可否は `cfp`、審査進行は `reviewCycle`、公開日程は `schedulePublication` でそれぞれ判定する。CFP の表示状態 `Scheduled / Open / Closed` は `cfp` の保存状態と UTC の開始・締切から導出し、締切の経過だけで保存データを暗黙更新しない。手動停止・再開は actor と理由を監査記録に残す。

応募アイテムには `proposalTypeVersion` を参照するバージョンを保存する。募集フォームを変更しても提出済み回答の意味を変えないため、既存 schema version は不変とし、新版を追加する。回答内容はサーバーで該当バージョンの schema に照らして検証する。

MVP では採択提案をそのまま公開セッションとして扱い、`sessionId` は安定した `proposalId` と同じ値にする。`scheduleSlot` は `proposalId` を参照し、`/sessions/{sessionId}` は会議 partition 内の該当 proposal と slot を解決する。CFP 提案とは別に独立編集・複数枠配置される session が必要になった時点で、専用の `session` item を導入する。

`proposal` は審査中の非公開情報と公開用情報を同じ item に保持してよいが、公開 API は明示的な public DTO に投影し、内部 item をそのまま返さない。共同登壇者 snapshot にメールアドレス等の不要な連絡先を複製しない。公開状態の初期値は `Hidden` とする。MVP では応募責任者が全共同登壇者からの公開同意を確認したことを明示的に表明し、その actor・時刻・同意文面 version を記録する。Organizer が採択状態・同意を確認して個別公開し、応募責任者が同意を撤回した場合は同一 partition の transaction で `Hidden` に戻す。`publicShowcaseEnabled` は公開提案一覧のみに適用する。公開タイムテーブルと session share route は、会議の日程公開に加え、対象提案が `Accepted`、同意済み、`Published` であることを確認し、条件を満たさないセッションは情報を返さない。

### 5.2 参照関係と整合性検査

Cosmos DB のアイテム間に relational foreign key はない。次の参照を Functions の業務ロジックで検証し、同一 `conferenceId` 内の参照先であることを確認する。

| アイテム | 参照先 | 検証 |
|---|---|---|
| `conferenceDirectory` | `conferenceData` の `conference` | slug の所有者と正本の会議 ID が一致し、会議が active/public である |
| `proposal` | `proposalType` と `proposalTypeVersion` | 同じ会議に属し、提出時点の schema version が存在し、受付可能な状態である |
| `reviewerAssignment` / `review` | `proposal` と `conferenceMembership` | 提案が同じ会議に属し、reviewer が現在その会議で有効な reviewer である |
| `scheduleSlot` | `proposal`、`room`、`track` | 提案が採択済みで、参照先が同じ会議に属し、時間・部屋の重複がない |
| `identityDirectory` | `userProfiles` | mapping 先の内部 `userId` が存在し、すでに別ユーザーに割り当てられていない |
| `emailOutbox` | 通知対象の会議・応募者 | 宛先が当該操作から確定し、送信権限と通知理由が検証済みである |
| `emailDeliveryDirectory` | `emailOutbox` | provider message ID が一つの outbox にだけ対応し、受信 event の topic/type を許可リストで検証する。event journal は `Received` から outbox 更新後に `Applied` へ進める |

アイテム ID の一意性はパーティション内で保証される。会議内で一意な ID を持たせ、グローバルな一意性や外部キー制約を Cosmos DB が保証すると仮定しない。

### 5.3 代表アイテム

公開 slug の対応アイテム（`conferenceDirectory`）:

```json
{
  "id": "entry",
  "slug": "example-conf-2026",
  "conferenceId": "conf_01J...",
  "state": "active",
  "title": "Example Conference",
  "startsAtUtc": "2026-11-01T00:00:00Z",
  "timeZoneId": "Asia/Tokyo",
  "cfpState": "Published",
  "cfpOpensAtUtc": "2026-08-01T00:00:00Z",
  "cfpClosesAtUtc": "2026-10-01T00:00:00Z"
}
```

`state: "active"` は会議が `Active` かつ `Public` であることを確認した後にのみ設定する。公開カタログではこの状態を公開 gate とし、画面表示に必要な開催日時・タイムゾーン・募集期間だけを directory projection に保持する。非公開化・アーカイブ時は先に `state` を無効化する。

会議作成・公開状態の変更は `conferenceData` と `conferenceDirectory` をまたぐため単一トランザクションにはならない。会議の公開情報を保存した後に directory entry を active にする。非公開化・アーカイブ時は先に directory entry を無効化して公開から除き、その後に正本の状態を更新する。各段階は同じ operation ID で再実行できるようにする。

提案アイテム（`conferenceData`）:

```json
{
  "id": "proposal:01J...",
  "conferenceId": "conf_01J...",
  "type": "proposal",
  "schemaVersion": 1,
  "proposalTypeId": "talk-30",
  "formVersion": 2,
  "ownerUserId": "user_01J...",
  "status": "Submitted",
  "answers": {
    "title": "Example title",
    "abstract": "Example abstract"
  },
  "submittedAtUtc": "2026-09-23T10:00:00Z",
  "createdAtUtc": "2026-09-23T09:55:00Z",
  "updatedAtUtc": "2026-09-23T10:00:00Z"
}
```

個別に公開する採択提案には、応募責任者の明示同意と共同登壇者全員の同意確認、Organizer の公開操作を記録する。提案の `Accepted` 状態だけでは公開しない。同意撤回時は proposal と audit event を同一 partition の transaction で更新し、公開状態を `Hidden` に戻す。公開提案一覧のみ、さらに会議の `publicShowcaseEnabled` を要求する。

配信 lookup item（`emailDeliveryDirectory`）:

```json
{
  "id": "acs-message_01J...",
  "providerMessageId": "acs-message_01J...",
  "conferenceId": "conf_01J...",
  "outboxId": "outbox:campaign_01J:user_01J...",
  "deliveryStatus": "Accepted",
  "updatedAtUtc": "2026-09-23T10:00:00Z"
}
```

identity mapping（`identityDirectory`）は `identityKey` をパーティションキーにし、アイテムには内部 `userId` のみを対応付ける。OAuth/OIDC access token、refresh token、client secret はいずれのコンテナーにも保存しない。

## 6. 書き込み・整合性

### 6.1 transactional batch

応募提出・採否更新など、一つの会議に属する操作は `conferenceData` の同一 `conferenceId` で処理する。たとえば応募提出では、同じ transactional batch に以下を含める。

1. `proposal` の作成または更新
2. `auditEvent` の作成
3. 必要な通知の `emailOutbox` 作成

この方式により、業務更新だけ成功して outbox/audit が欠落する状態を避ける。複数 conference、`userProfiles`、`identityDirectory`、`conferenceDirectory` にまたがる更新は同じ batch に入れられない。会議作成やアカウントリンクは冪等な段階処理とし、途中失敗から再開できるようにする。

タイムテーブルを更新するときは、`scheduleRevision` の `_etag` を条件にした置換と、変更対象の `scheduleSlot`・`auditEvent` を同じ `conferenceId` の transactional batch に含める。revision 競合時は変更全体を `412` とし、再読込後に時間重複を再検証する。変更量が transactional batch の制約を超える一括更新は、別途段階公開方式を設計する。

### 6.2 競合と重複

- 更新には Cosmos DB の `_etag` と `If-Match` を使い、古い版の上書きを 409/412 として扱う。
- 提出・採否・メール送信など再試行されうる操作は operation ID を持たせる。同じ operation ID と同じ request hash の再送は既存結果を返し、同じ ID で異なる payload の場合は競合として拒否する。
- 応募 ID と `auditEvent` / `emailOutbox` ID は操作 ID から決定的に作成できるようにし、transactional batch 再試行で二重作成しない。
- Functions Queue/Change Feed は再配信されるため、outbox ID を処理キーにする。ただし外部メール送信が成功した直後に worker が停止する可能性があるため、メールの exactly-once 配信は保証しない。運営画面では `Accepted` と実配信確認を区別する。
- Queue message には `{ conferenceId, outboxId }` のみを含める。outbox がある `conferenceData` の point read には partition key の `conferenceId` が必要であり、メールアドレス・本文などの PII は載せない。
- 一括 campaign はプレビュー時に応募状態、recipient user ID、設定済み送信者アドレスを固定し、最大50人までとする。送信確認では preview ETag と operation ID を検証し、campaign の `Previewed → Ready`、recipient ごとの `Ready` な `emailOutbox`（deterministic `outbox:{campaignId}:{recipientId}` ID）、監査イベントを同一 `conferenceData` transactional batch に含める。campaign ID は preview operation ID から導出する。同一 operation の再試行は既存 campaign を返し、異なる内容の再利用や古い ETag は拒否する。上限内では全件が atomic に commit されるため部分 fan-out は発生しない。50人を超える配信を導入する場合は、段階 fan-out と進捗復旧を別途設計する。
- Event Grid delivery report は provider message ID lookup から outbox を特定する。Event Grid event ID を event journal に記録し、outbox 更新完了後に `Applied` とする。duplicate delivery や途中停止は `Received` の再実行で同じ最終状態に収束させる。ACS 受付後に結果が不明な場合は自動で再送せず `Unknown` として照合作業に回す。

### 6.3 状態遷移

応募状態は `Draft → Submitted → UnderReview → Accepted/Rejected` を基本とし、締切前の取り下げは `Withdrawn` とする。許可する遷移は API service で検証し、現在状態だけでなく CFP state と deadline も同時に確認する。公開状態の変更は別の publication state として管理し、状態を変更した時は `auditEvent` を同一 batch で追加する。

メール状態は dispatch と provider delivery の進行を一つの状態列で表す。`Pending → Ready → Sending → Accepted → Delivered / Bounced / Suppressed / FilteredSpam / Quarantined` が基本である。現行 campaign は最大50人のため、送信確認 transaction で全 recipient outbox を直接 `Ready` とし、部分 fan-out の `Pending` item は作らない。Change Feed は `type == emailOutbox && status == Ready` の item だけを Queue に発行する。送信前の一時障害は ACS 未受付を確認できる場合に限り backoff 後 `Ready` へ戻し、確定的な失敗は `Failed`、ACS が受け付けた可能性を否定できない障害は `Unknown` とする。送信 worker は ETag 条件付きで `Ready → Sending` を取得し、attempt ID と lease 時刻を保存してから ACS を呼ぶ。期限切れの `Sending` は安全側に倒して `Unknown` とし、自動再送しない。`Unknown` は運営者が provider 状態を照合し、理由を記録した場合だけ明示的に再送できる。送信開始前のみ `Cancelled` にできる。`Suppressed` には opt-out、既知 hard bounce 等の理由を別フィールドで記録する。`Transactional` は受付・採否等の運用上必須の連絡に限り、`ConferenceOperations` は既定 opt-out とし、送信時にも最新の同意・suppression を再確認する。

`Accepted` は terminal state ではない。worker の受付結果更新と Event Grid の配信結果更新は競合するため、いずれも ETag 条件付きで処理し、`Delivered / Bounced / Suppressed / FilteredSpam / Quarantined / Failed` などの配信結果を `Accepted` で上書きしない。異なる terminal event が届いた場合は既存状態を維持して anomaly として記録・監視する。

## 7. クエリ・インデックス・ページング

- `conferenceData` を検索するときは原則 `conferenceId` を必須にし、`type`、`status`、`proposalTypeId`、時刻で絞る。API から任意の Cosmos SQL を受け取らない。
- 会議内一覧は continuation token を用いる。OFFSET/LIMIT による深いページングは避け、token を API client に不透明な値として渡す。
- 会議をまたぐ応募者自身の提出一覧、審査担当者一覧、公開会議カタログはクロスパーティションクエリになる。初期は低件数に限って実装し、RU charge、結果件数、応答時間を実測する。高頻度化した場合は `userActivity` / public catalog の read model を追加し、正本データとの整合性を change feed で保つ。
- 技術検証では Cosmos DB の既定 indexing policy を維持し、クエリの RU と書き込みコストを測定する。回答本文・審査コメント等を検索対象にしないことが確定してから除外 index を設け、必要なクエリで再計測する。
- JSON の大きな添付ファイルや画像は item に埋め込まない。将来必要になった場合は Blob Storage に分離し、Cosmos DB には metadata と object key のみを保持する。

## 8. プライバシー・保持・運用

- Email、speaker profile、非公開 proposal、review comment は個人情報を含む可能性がある。アクセス制御は API で行い、公開 read DTO から除外する。
- Profile の識別子には内部 `userId` を使い、email 変更で所有権が変わらないようにする。外部 IdP は `identityDirectory` の mapping を通す。
- `auditEvent` は actor、対象、操作時刻、最低限の変更要約を記録し、応募全文や秘密情報を複製しない。改ざんを避けるため通常の業務 API から更新・削除しない。
- `emailOutbox` の `Delivered` は受信者側 MTA への引き渡し結果であり、開封や実 inbox 表示の証明にしない。配信 event には重複／遅延がある前提で最新の有効状態を保つ。
- `emailDeliveryDirectory` の event journal は event ID ごとに `Received / Applied` を持つ。`emailOutbox` と lookup/journal の cross-container 更新は原子ではないため、`Received` の再処理と定期的な未解決 item 照合を行う。
- 初期は container TTL を設定しない。retention / 削除・匿名化方針を確定してから、outbox・identity mapping・応募データ・監査データごとに保持期限と削除方法を定義する。
- Cosmos DB の RU charge、429、論理パーティション別の使用量、クロスパーティションクエリ、インデックスサイズを確認できるようにする。ログに item 本文、メール、token、PII を出さない。

## 9. スケール時の見直し条件

`conferenceData` の `/conferenceId` は会議内 transactional batch と管理 query を単純にする一方、特定の大規模会議に書き込みが集中するとホットパーティションになる。会議数・応募数・ピーク同時投稿を含む負荷試験で、partition key ごとの RU・429・ストレージを確認する。

以下が発生したら、Free Tier の上限だけでなくモデル自体を再評価する。

- 1 会議のアイテム量・処理量が偏り、論理パーティションの上限または処理性能に近づく。
- speaker/reviewer の cross-partition query が頻繁になり、RU または応答時間の予算を超える。
- 監査・メール履歴の保持によりデータ容量が増え続ける。
- 別リージョンの低遅延・高可用性、バックアップ目標が必要になる。

その場合は `proposalData` の分割や `userActivity` / public catalog read model の追加を検討する。`conferenceId` の変更は単純な設定変更ではなく、コピー、整合性検証、dual-write または停止時間を含む移行・切戻し計画を伴う。Free Tier の 1,000 RU/s・25 GB を本番の容量設計の根拠にしない。

## 10. 参照資料

- [Azure Cosmos DB for NoSQL overview](https://learn.microsoft.com/azure/cosmos-db/nosql/overview)
- [Partitioning and horizontal scaling in Azure Cosmos DB](https://learn.microsoft.com/azure/cosmos-db/partitioning)
- [Transactional batch operations in Azure Cosmos DB](https://learn.microsoft.com/azure/cosmos-db/nosql/transactional-batch)
- [Lifetime Free Tier - Azure Cosmos DB](https://learn.microsoft.com/azure/cosmos-db/free-tier)
- [Connect to Azure Cosmos DB for NoSQL using role-based access control and Microsoft Entra ID](https://learn.microsoft.com/azure/cosmos-db/nosql/how-to-connect-role-based-access-control)
- [Use Azure Cosmos DB change feed with Azure Functions](https://learn.microsoft.com/azure/cosmos-db/change-feed-functions)
