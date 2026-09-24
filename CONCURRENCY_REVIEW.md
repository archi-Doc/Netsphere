# 並行処理と生成コードのレビュー記録（2026-09-24）

基準コミット `91e41c0` に対して、Socket／パケット処理、Transmission、Relay、並列処理時の共有状態、そして NetsphereGenerator が出力するコードを精査した。

## 修正内容

| 対象 | 確認した問題 | 修正内容 |
| --- | --- | --- |
| 接続の清掃 | `ConnectionTerminal.Clean` がロック外で Open→Closed／Closed→Disposed を判定し、Close フレーム送信や Transmission の破棄を先に行っていた。同時に `Connect` が同じ接続を再利用（再オープン）すると、返却直後の接続が閉じられる。 | 候補の選定のみロック外で行い、状態遷移と副作用はロック内で時刻を再評価してから実行する。 |
| サーバー接続の再オープン | 接続 ID が一致するだけの未認証パケットで Closed のサーバー接続を Open に戻していた。ヘッダーは平文のため、第三者が接続を保持し続けられる。 | 復号に成功した Close 以外のフレームを受信したときだけ、ロック内で再オープンする。 |
| 輻輳制御の巡回 | `UnorderedLinkedList.Remove` はノードのリンクを切るため、破棄された接続の制御を取り除いた直後に `Next` が null になり、その回の残りの制御が処理されない。 | 削除前に次ノードを取得する。 |
| 優先フレームの経路 | サーバー接続の Close／Knock／即時 ACK が常に送信側回路で暗号化されていた。データと遅延 ACK は `CorrespondingRelayKey`（受信側回路）を使うため経路が食い違う。 | サーバー接続では受信側回路を使う。 |
| キャンセル | `ClientConnection.Send` が待機中にトークンを無視する。Transmission 枠待ちのキャンセルが例外として漏れる。`PacketTerminal.SendAndReceive` がキャンセルを Timeout として報告する。 | 待機にトークンを渡し、枠待ちのキャンセルと応答待ちのキャンセルを `NetResult.Canceled` として返す。 |
| ストリーム応答 | ストリームを開かずに結果だけを返した RPC で、空の応答バッファーを解放せず、破棄済み Transmission 上の `ReceiveStream` を返していた。 | ブロック応答（借用バッファー付き）なら解放して null を返す。長さ 0 のストリームは従来どおり有効。 |
| 生成コード（バックエンド） | 逆シリアル化失敗や null 引数で早期 return する際に要求バッファーを保持したままにするため、`InvokeRPC` が要求バイト列を応答本文として送り返していた。ReceiveStream を返すメソッドでストリームを開かなかった場合も要求を送り返す。 | 早期 return と ReceiveStream 経路で要求バッファーを解放する。 |
| 生成コード（フロントエンド） | `Task` を返すメソッドで常に空の応答本文を `NetResult` として逆シリアル化していた。 | 逆シリアル化を生成しない。 |
| 生成器 | 結果を参照しない `ConfigureRelation`、未使用の `GeneratorState`／`GenerateInitializer`／`ServiceFilterSet.AddRange`／`Merge`、常に真の条件、未使用のローカル `owner` 生成などを削除。 | 生成結果は変えずに実行コードを削減。 |
| パケット待機 | 応答待ちの終了時に保留項目全体を走査していた。 | 送信時の項目参照を保持し、リンク中なら直接削除する。 |
| ソケット | IPv6 を持たないホストで IPv6 ソケットの作成に失敗すると起動を中止していた。 | `Socket.OSSupportsIPv6` が偽なら IPv4 のみで起動し警告を記録する。 |

## 検証

- 追加テスト：`xUnitTest/Tests/LifecycleReviewTest.cs`（清掃と再利用の競合、未認証パケットによる再オープン拒否、輻輳制御の巡回、キャンセル結果、生成バックエンドのバッファー解放、ストリームなし応答）。
- `CoreReliabilityTest.CanceledPacketWaitRemovesPendingBuffer` の期待値を `Canceled` に更新。
- Debug／Release ビルド：警告 0、エラー 0。全 297 件のテストが両構成で成功。
- 検証環境は IPv6 非対応の Linux コンテナで、IPv4 のみで実施した。IPv6 経路と受信側リレー経路の実ネットワーク試験は行っていない。
