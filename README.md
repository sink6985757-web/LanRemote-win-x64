# LanRemote

LanRemote 是 Windows 私人區網遠端控制 MVP。兩臺電腦執行同一套程式，各自都能選擇「讓這臺電腦被控制」或「控制另一臺電腦」；結束目前 session 後交換角色，就能反向控制。

目前提供未簽章的本機測試候選，不是公開發布版本。v4.3.1 修正 v4.3 連線後停在概覽、導致遠端畫面幀被丟棄的回歸；協定 v6 已在本機 TLS loopback 實際完成純文字剪貼簿同步、雙端檔案傳輸及分階段連線逾時測試。Windows 10 22H2／Windows 11 25H2 雙機 GUI、剪貼簿、檔案傳輸、SAS 與實際 FPS 仍待使用者執行。

## MVP 已實作

- 手動輸入私人區網 `IPv4:port`，預設連接埠 `45873`。
- TLS 1.2/1.3 加密、session-only 自簽憑證、兩端六位數 SAS 配對碼，以及被控端每次明確核准。
- 同一個 WPF 視窗先顯示高對比連線介面；核准後控制端直接進入遠端桌面，被控端顯示明確受控狀態，不再顯示工作階段／遠端控制／檔案傳輸 Tab。
- TCP、TLS 與初始協定交握限制 15 秒；人工配對核准獨立保留 120 秒。錯誤位址、版本、拒絕或逾時會清理連線並跳出「未連線成功」。
- 視窗化、最大化與 F11 無邊框全螢幕；提供符合視窗、拉伸滿版（預設）與裁切滿版三種縮放及正確座標映射。
- `File`、`Connection`、`View`、`Quality` 選單與可隱藏工具列；頂端感應區可叫回工具列。
- 連線中熱切換三種 GDI/JPEG 設定：
  - 流暢：最高 1280×720、60 fps、JPEG quality 40。
  - 平衡：最高 1600×900、48 fps、JPEG quality 60。
  - 畫質：最高 1920×1080、30 fps、JPEG quality 82。
- 接收端使用單一 latest-frame buffer；解碼來不及時丟棄過期畫面，不累積無界佇列。
- Toolbar 狀態可選「關閉／簡易（預設）／詳細」；底部狀態列已移除。
- 主要螢幕與被控端原生游標；控制端另顯示縮小 20% 的青色箭頭與白色描邊，沒有光圈或文字標籤。
- 滑鼠移動／按鍵／滾輪、鍵盤、`Ctrl+Alt+0`、本機保存的自訂按鍵與立即斷線。
- Toolbar「剪貼簿」提供關閉、單向（主控到被控）與雙向三種純文字同步模式，預設雙向；控制端複製文字後可在被控端直接貼上。
- `Ctrl+Alt+Delete` 使用 ZIP 內可選擇安裝的固定用途 LocalSystem 服務；程式本身不自動安裝或變更 Windows 安全性原則。
- 發起端預先要求且接收端配對核准後，兩端皆可從 Toolbar「檔案」開啟主視窗內嵌面板，主動瀏覽對方路徑、傳送或接收。
- 沿用原有左右配置，傳送與接收各有獨立狀態列並可同時執行；返回遠端桌面後仍在背景繼續，Toolbar 保留進度與取消入口。
- 同名預設保留兩者並自動改名，也能在衝突提示明確選擇覆寫或略過。
- 協定 v6 具有 magic、版本、訊息類型、payload 長度上限、nonce 驗證、畫質套用、SAS 結果、純文字剪貼簿及對等檔案分塊訊息。
- 僅接受 loopback、RFC1918 或 IPv4 link-local 位址。

## 目前限制

- v6 不向下相容；兩臺都必須使用同一份新版測試包。
- 畫面目前使用 GDI/JPEG 相容模式；Windows Graphics Capture、Media Foundation H.264 與硬體編碼留待後續效能工作。
- 三種 fps 是設定上限，不是對所有硬體與網路保證的實測值。
- 新版尚無 Windows 10 22H2 雙機實測證據，不能宣稱跨版本完整相容或 1080p30／互動延遲 p95 小於 200 ms。
- Windows 10 22H2 不在 .NET 10 官方支援清單；self-contained 發布只能降低部署相依，不能把該系統變成官方支援平臺。

## 安全邊界

- 不支援無人值守、隱蔽監控、提權、UAC／安全桌面繞過或公網 relay。
- 不包含自動探索、多螢幕、音訊、圖片／格式化剪貼簿、剪貼簿歷史或持續資料夾同步。
- 檔案傳輸需要發起端要求與接收端核准，只對當次 session 生效；斷線後必須重新取得雙方同意。
- 單檔最大 2 GB、單批 10 GB；同名預設自動改名，只有衝突提示中明確選擇才會覆寫或略過，並拒絕 UNC、ADS、路徑逸出、symlink／junction／reparse point 與 Windows／程式／Startup 寫入。
- `SendInput` 受 Windows UIPI 限制，普通權限程式不能控制較高權限視窗；`Ctrl+Alt+Delete` 不走 `SendInput`。
- SAS 服務的 named pipe 只接受固定命令，沒有 shell、程序啟動、檔案或任意 IPC 執行能力。
- `install-sas-service.ps1` 只在使用者以系統管理員身分手動執行後安裝並自動啟動服務，不修改 Local Security Policy；移除使用 `uninstall-sas-service.ps1`。
- 程式不自動修改 Windows Firewall；若系統詢問，僅由使用者決定是否允許私人網路。
- 測試包尚未簽章；檔案來源或 SHA-256 不符時不要執行。

## 使用本機測試包

v4.3.1 測試包位於 `artifacts/LanRemote-v4.3.1-win-x64.zip`（協定 v6；`artifacts/` 不納入 Git）。解壓縮後必須保留整個資料夾，不能只複製 EXE。

- ZIP 大小：136,112,419 bytes
- ZIP SHA-256：`A9954D63939A6580DCE98E4589041D36D33367525F791D33138372B303F083F2`
- 封裝來源：本機工作樹（基準 HEAD `ca147365bf4a2e7dea951781ae23645399843539`；本輪依工作單不 commit）

1. 把同一份 ZIP 複製到 Windows 11 與 Windows 10，兩邊都完整解壓縮。
2. 兩邊執行 `LanRemote.App.exe`。
3. 被控制的電腦按「開始等候」，把畫面顯示的 `IPv4:45873` 告訴另一臺。
4. 控制端選好「流暢／平衡／畫質」，輸入位址並按「連線並核對」。
5. 要傳檔時，發起端先勾選「要求啟用雙向檔案傳輸」；確認兩端六位配對碼相同後，接收端再勾選允許並核准。
6. 連線後控制端直接顯示遠端桌面；不需要再選分頁。可從工具列切換視窗、縮放或畫質；F11 切換全螢幕，Esc 退出。
7. 直接按工具列 `Ctrl+Alt+0`，或從「自訂按鍵」新增、送出及移除本機快捷鍵。
8. 若要使用 `Ctrl+Alt+Delete`，先在被控端手動以系統管理員身分執行 `install-sas-service.ps1`；Windows 原則若未允許 Services，依 README.txt 的路徑由本人設定。
9. 從本機檔案總管把檔案／資料夾拖進遠端畫面；畫面會先顯示可靠解析的遠端桌面／目前檔案總管路徑，無法解析時改開路徑選擇視窗。
10. 被控端核准「文字剪貼簿」後，Toolbar「剪貼簿」預設為雙向；也可切成單向（主控到被控）或關閉。複製／剪下文字後，直接在另一端按 `Ctrl+V`。
11. 任一端都從 Toolbar「檔案」開啟檔案面板，選擇本機來源、瀏覽對方路徑並傳送／接收；兩個方向可同時執行，按「返回桌面」不會取消傳輸。
12. `View > 狀態資訊` 可選擇關閉、簡易（預設）與詳細；詳細模式會多一條薄診斷列。
13. 工具列隱藏後，把滑鼠移到視窗最頂端即可重新顯示。
14. 反向控制時先斷線，再讓兩臺交換 A／B 角色並重新配對；需使用 SAS 的兩端都要各自手動安裝服務。

ZIP 內 `README.txt` 有完整操作與雙機證據腳本說明。

## 建置

需求：Windows x64 與 .NET SDK `10.0.400`。

```powershell
dotnet build .\LanRemote.sln --configuration Debug
dotnet test .\LanRemote.sln --configuration Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Package.ps1
```

建置中間檔放在 Windows temp 的 `LanRemoteBuild`，避免 Google Drive 對 WPF apphost 的 memory-map 鎖定。發包腳本會先跑測試，再輸出包含 .NET runtime 的 `win-x64 self-contained` 資料夾與 ZIP。

## 專案結構

| 路徑 | 責任 |
|---|---|
| `src/LanRemote.Protocol` | v6 framing、JSON／binary payload、畫面、輸入、SAS、文字剪貼簿、對等檔案分塊與路徑訊息 |
| `src/LanRemote.Core` | TLS session、配對、私人 IP、快捷鍵、縮放映射與受限檔案 sender／receiver／browser |
| `src/LanRemote.Windows` | 主要螢幕／原生游標 JPEG 擷取、Explorer 拖放目的地解析、檔案剪貼簿、SAS client 與 `SendInput` |
| `src/LanRemote.SasService` | 固定用途 Windows SAS 服務；不包含任意命令執行 |
| `src/LanRemote.App` | WPF 單視窗遠端桌面、Toolbar 檔案面板、拖放、三段狀態、自訂按鍵、縮放與雙游標呈現 |
| `tests/LanRemote.Tests` | 協定／座標單元測試、真實 TLS loopback、連線逾時與 UI source-contract 回歸測試 |
| `scripts` | self-contained 發包與雙機證據腳本 |
| `readygate` | 已確認工作單與交付閘門證據 |

## Agent／Tool 接續

1. 先讀 `AGENTS.md`、`handoff.md` 與 `.agents/project-lifecycle.json`。
2. 每次接續先執行 `startup`；工作結束使用 `shutdown`。
3. 不得把上層 `gogoYulin` 當成本專案 Git root。
4. 技術棧、配對模型、安全邊界或外部 delivery 若要改變，重新進 ReadyGate。

## License

本專案原始碼依 [Apache License 2.0](LICENSE) 授權，SPDX 識別碼為 `Apache-2.0`。第三方相依套件仍適用各自的授權條款。

## Delivery 狀態

- Git：本機 `main` 追蹤 `origin/main`；原始碼 checkpoint `498c88cbb2fc5f17555121649906b5d33aca40fc` 已由 GitHub `main` 回讀。
- GitHub：公開 repository `sink6985757-web/LanRemote-win-x64`，預設分支 `main`；只包含原始碼與文件，不包含 ignored 測試 ZIP／build outputs。
- License：Apache License 2.0（`Apache-2.0`），GitHub 已正確偵測。
- ReadyGate（`WO-LANREMOTE-V431-REMOTE-DISPLAY-FIX-20260823`）：本機遠端畫面路由、無 Tab 介面、分階段逾時與 61 項 Debug 測試已驗證；正式發布仍停止，等待 v4.3.1 Windows 10／11 的 schema v6 雙機實測證據。
- 原始效能目標：WGC + Media Foundation H.264 與雙機效能證據仍是後續工作，不屬於本輪 GDI/JPEG UX 工作單。
