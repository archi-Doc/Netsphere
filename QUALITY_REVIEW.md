# 品質監査と修正記録（2026-10-03）

基準コミット `5f3c573` に対して、Core（接続・Transmission・パケット・ソケット・ストリーム）を直接精査し、Time／Crypto／Misc、Stats／Relay／Service、NetsphereGenerator と生成コード、Runner／DataServer／Version／Shared を並行して監査した。候補はすべて再現テストまたは生成コードの実出力で確認してから修正した。

## 修正内容

| 対象 | 確認した問題 | 修正内容 |
| --- | --- | --- |
| 接続の Close | `CloseInternal` が送信 Transmission だけを破棄するため、応答待ちの RPC（`SendAndReceive`／生成フロントエンド）、コールバック、ストリーム読み取りが接続を閉じても完了せず、TransmissionTimeout（既定 4 秒）まで待って `Timeout` を返す | 受信 Transmission も破棄して `Closed` で完了させる。再送 Gene への ACK のため、Transmission 自体は Disposed リストに移して清掃まで保持 |
| TrustSource | 容量到達後に `QueueChain.Dequeue()` した項目の所有者参照が残り、再追加が無視される。ウィンドウが縮み、カウンタが増え続ける（NAT ポート・端点の観測が無制限に蓄積） | `TryPeek` + `Goshujin = null` で切り離してから再登録 |
| Relay 端点キャッシュ | 送信側は宛先ノードの IPv4+IPv6 両方を含む `NetAddress` でキャッシュを登録するが、応答は片方のファミリーで届き別のキーで検索されるため、最外 Relay がデュアルスタック宛先からの応答を未知として破棄する。Relay ID の違いでも同様 | 解決に使うファミリーに正規化し Relay ID を除いたキーで登録・検索 |
| Relay キャッシュ汚染 | 未知の送信元からのパケットが上限付きキャッシュに項目を追加し、正規の Unrestricted 項目を追い出せる | 最外 Relay の検索を `Lookup`（追加しない）に変更 |
| Relay 制限間隔 | 未知ノード受け入れ間隔のタイムスタンプが Agent 全体で 1 つ | Exchange ごとに保持 |
| SetupRelay | 解放済み Exchange の ID を保持した接続が、同じ ID を再利用した別の Exchange の外側端点と鍵を上書きできる | Exchange の所有接続を照合。解放時に接続の `InnerRelayId` を消去 |
| RelayPoint | Exchange を 0 ポイントで公開した後にポイントを付与するため、最初のパケットで解放されうる。失敗時も接続の保持期間を変更していた | 初期ポイントをロック内で設定して公開。成功時のみ保持期間を変更 |
| NodeControl | 受信した ActiveNode の `LastConnectedMics` を検証せず、未来の値で既存ノードの公開鍵を置き換え、一覧の先頭に固定できる | 現在時刻 + 1 分を超える値を無視 |
| NTP 補正 | 永続化した補正値を「補正済み」として 1 時間扱うが、`Time` に設定されないため補正時刻が未補正のまま | 逆シリアル化時に補正値があれば設定 |
| IdFileLogger | ストリーム数の上限超過時に作成順で削除するため、活発な ID のログが消える | 書き込みを受けたストリームを末尾へ移動して LRU で削除 |
| StreamToSendStream | 元ストリームの例外をすべて `Canceled` として報告 | キャンセルのみ `Canceled`。それ以外は相手へ Cancel を送った上で例外を伝播 |
| Alias | 長さしか検証せず、`IsValid` が拒否する空文字や区切り文字を登録できる | `IsValid` で検証して `ArgumentException` |
| 生成コード（ストリーム + ブロック引数） | ストリームがブロック引数の前に完了すると `Completed` を失敗扱いせず、`default`（null）を handler に渡す | `Success` 以外を拒否。null 引数も拒否 |
| 生成コード（ResponseChannel） | 非 null 参照型引数に nil が届くと null のまま handler を呼ぶ | 通常経路と同じ null 検査を生成 |
| 生成器の診断 | ジェネリックメソッド、Task メソッドの ref/in/out 引数、非 void の ResponseChannel 形式、private コンストラクター、基底インターフェイスのプロパティ、他名前空間のプロパティ型でコンパイル不能なコードを診断なしに生成 | NSG003／NSG008／新設 NSG017 で拒否。アクセス可能なコンストラクターのみ使用。完全修飾型と `@` 名でプロパティを生成。基底インターフェイスのプロパティも生成 |
| 生成器（継承） | 基底クラスがサービスを実装する NetObject は何も生成されず、実行時に `NoNetService` | 基底クラスのインターフェイスも収集。サービスがなければ新設 NSG018 で警告 |
| 生成器（フィルター） | 基底インターフェイスのメソッドを明示実装した場合、メソッド単位のフィルターが黙って無視される | 宣言インターフェイス名でも検索 |
| Netsphere.Version | `server` が Ctrl+C で停止しない。バージョンの保存が非同期で競合し、途中終了で破損する。`GetVersionPacket` をワイヤ配置で読む | 実行ルートの `TryDelay` で待機。検査と更新と保存をロック内で行い一時ファイル経由で書き込む。逆シリアル化で読む。`restart` にトークンを伝播 |
| Netsphere.Runner | コンテナにネットワークがないと `NullReferenceException`、アドレスがないと遅延なしで状態遷移を繰り返す。起動回数が生涯 11 回で監視を終了する。Docker クライアントを解放しない | アドレスを安全に取得し、取得できない場合は待機。稼働確認で回数を初期化。失敗時に解放 |
| Netsphere.DataServer | `Put` が受信前に既存ファイルを切り詰め、フラッシュ前に成功を返す。`Get` が全失敗を `NotFound` にする。リモート公開鍵の未設定を通知しない | 一時ファイルに受信して成功時のみ移動。失敗理由を伝播。エラーを記録 |
| その他 | 空の `Send` がストリームの窓とパケットを消費する。`ReceiveBlock` が長さ一致時に再レントする。IP 文字列の `"\\r\\n"` 置換が無効。未使用のループバック判定、重複分岐、二重ロック | 早期 return、`>=`、`Trim()`、削除・統合 |

`RemoteDataHelper.SendLog` は結果を返すように変更した（戻り値の型が `Task` から `Task<NetResult>` になる）。`RelayAgent.AddExchange` には省略可能な `relayPoint` 引数を追加した。

## 検証

- 追加テスト：`xUnitTest/Tests/QualityAuditTest.cs`、`xUnitTest/Services/IQualityAuditService.cs`（接続 Close による応答待ちの完了、ストリーム完了後のブロック引数、ResponseChannel の nil 引数、元ストリーム例外の伝播、Relay キャッシュのキー正規化と Lookup、SetupRelay の所有権、未来の ActiveNode、TrustSource の窓）。各テストは修正前のコードで失敗することを確認した。
- Debug／Release ビルド：警告 0、エラー 0。全 357 件のテストが両構成で成功。`git diff --check` 問題なし。`tools/CoreBenchmarks` もビルド可能。
- 生成コードは `EmitCompilerGeneratedFiles` で再出力して確認した。`obj` 配下の既存出力は古い。

## 残っている懸念点

- `Netsphere/Obsolete/*`、`Misc/RobustConnection.cs`、`Netsphere.Shared/Obsolete`、`IVersionService.cs` は全体がコメントアウトされ参照もないが、これまでの方針に従い残した。`TimeCorrection.AddCorrection` は呼び出し元がなく、NTP なしの補正は機能しない。
- Relay の端点キャッシュは `NetStats.IsIpv6Supported` が変化した直後にだけ応答を一度取りこぼす。Relay 間（双方が Relay 越し）の経路は実ネットワークで検証していない。
- 生成器の診断（NSG017／NSG018 など）は xUnitTest では検証できないため、ビルド結果と Playground／NetsphereTest の既存サービスで確認した。
- `CertificateToken<T>.MaxStringLength` の固定値、`SeedKey.New` と `Clear` の競合、トリム後の `read` 位置、`RestartMachine` のコンテナ名部分一致、シェル文字列の組み立て、`Console.ReadKey` のリダイレクト時の例外は未修正。
- 長時間の実ネットワーク負荷、パケット損失・順序逆転の組み合わせ、旧バージョンとの相互運用は試験していない。
