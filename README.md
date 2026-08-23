# LanRemote

LanRemote 是 Windows 私人區網遠端控制 MVP。兩臺電腦執行同一套程式，各自都能選擇「讓這臺電腦被控制」或「控制另一臺電腦」；結束目前 session 後交換角色，就能反向控制。

目前提供未簽章的本機測試候選，不是公開發布版本。先前 v2 已在 Windows 11 25H2 以兩個真實程式實例完成 TLS 配對、畫面傳送、介面與畫質切換驗證；本輪協定 v3、SAS 服務、快捷鍵、游標與新 FPS 的 Windows 10 22H2／Windows 11 25H2 雙機驗證仍待使用者執行。

## MVP 已實作

- 手動輸入私人區網 `IPv4:port`，預設連接埠 `45873`。
- TLS 1.2/1.3 加密、session-only 自簽憑證、兩端六位數 SAS 配對碼，以及被控端每次明確核准。
- 同一個 WPF 視窗先顯示連線介面；核准後切換成遠端桌面，斷線再回到原介面。
- 視窗化、最大化與 F11 無邊框全螢幕；提供符合視窗、拉伸滿版（預設）與裁切滿版三種縮放及正確座標映射。
- `File`、`Connection`、`View`、`Quality` 選單與可隱藏工具列；頂端感應區可叫回工具列。
- 連線中熱切換三種 GDI/JPEG 設定：
  - 流暢：最高 1280×720、60 fps、JPEG quality 40。
  - 平衡：最高 1600×900、48 fps、JPEG quality 60。
  - 畫質：最高 1920×1080、30 fps、JPEG quality 82。
- 接收端使用單一 latest-frame buffer；解碼來不及時丟棄過期畫面，不累積無界佇列，狀態列顯示實際呈現 FPS 與丟棄數。
- 主要螢幕與被控端原生游標；控制端另顯示青色箭頭、白色描邊與半透明光圈，沒有文字標籤。
- 滑鼠移動／按鍵／滾輪、鍵盤、`Ctrl+Alt+0`、本機保存的自訂按鍵與立即斷線。
- `Ctrl+Alt+Delete` 使用 ZIP 內可選擇安裝的固定用途 LocalSystem 服務；程式本身不自動安裝或變更 Windows 安全性原則。
- 協定 v3 具有 magic、版本、訊息類型、payload 長度上限、nonce 驗證、畫質套用與 SAS 結果回覆。
- 僅接受 loopback、RFC1918 或 IPv4 link-local 位址。

## 目前限制

- v3 不向下相容；兩臺都必須使用同一份新版測試包。
- 畫面目前使用 GDI/JPEG 相容模式；Windows Graphics Capture、Media Foundation H.264 與硬體編碼留待後續效能工作。
- 三種 fps 是設定上限，不是對所有硬體與網路保證的實測值。
- 新版尚無 Windows 10 22H2 雙機實測證據，不能宣稱跨版本完整相容或 1080p30／互動延遲 p95 小於 200 ms。
- Windows 10 22H2 不在 .NET 10 官方支援清單；self-contained 發布只能降低部署相依，不能把該系統變成官方支援平臺。

## 安全邊界

- 不支援無人值守、隱蔽監控、提權、UAC／安全桌面繞過或公網 relay。
- 不包含自動探索、多螢幕、音訊、剪貼簿或檔案傳輸。
- `SendInput` 受 Windows UIPI 限制，普通權限程式不能控制較高權限視窗；`Ctrl+Alt+Delete` 不走 `SendInput`。
- SAS 服務的 named pipe 只接受固定命令，沒有 shell、程序啟動、檔案或任意 IPC 執行能力。
- `install-sas-service.ps1` 只在使用者以系統管理員身分手動執行後安裝並自動啟動服務，不修改 Local Security Policy；移除使用 `uninstall-sas-service.ps1`。
- 程式不自動修改 Windows Firewall；若系統詢問，僅由使用者決定是否允許私人網路。
- 測試包尚未簽章；檔案來源或 SHA-256 不符時不要執行。

## 使用本機測試包

測試包位於 `artifacts/LanRemote-win-x64.zip`（`artifacts/` 不納入 Git）。解壓縮後必須保留整個資料夾，不能只複製 EXE。

1. 把同一份 ZIP 複製到 Windows 11 與 Windows 10，兩邊都完整解壓縮。
2. 兩邊執行 `LanRemote.App.exe`。
3. 被控制的電腦按「開始等候」，把畫面顯示的 `IPv4:45873` 告訴另一臺。
4. 控制端選好「流暢／平衡／畫質」，輸入位址並按「連線並核對」。
5. 確認兩端六位配對碼相同，再由被控端按「是」。
6. 連線後從頂部工具列切換視窗、縮放或畫質；F11 切換全螢幕，Esc 退出全螢幕。
7. 直接按工具列 `Ctrl+Alt+0`，或從「自訂按鍵」新增、送出及移除本機快捷鍵。
8. 若要使用 `Ctrl+Alt+Delete`，先在被控端手動以系統管理員身分執行 `install-sas-service.ps1`；Windows 原則若未允許 Services，依 README.txt 的路徑由本人設定。
9. 工具列隱藏後，把滑鼠移到視窗最頂端即可重新顯示。
10. 反向控制時先斷線，再讓兩臺交換 A／B 角色並重新配對；需使用 SAS 的兩端都要各自手動安裝服務。

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
| `src/LanRemote.Protocol` | v3 framing、JSON payload、畫面、輸入、畫質與 SAS 結果訊息 |
| `src/LanRemote.Core` | TLS session、配對、私人 IP、快捷鍵、latest-frame buffer 與縮放映射 |
| `src/LanRemote.Windows` | 主要螢幕／原生游標 JPEG 擷取、named-pipe SAS client 與受限 `SendInput` |
| `src/LanRemote.SasService` | 固定用途 Windows SAS 服務；不包含任意命令執行 |
| `src/LanRemote.App` | WPF 單視窗、選單／工具列、自訂按鍵、三種縮放與雙游標呈現 |
| `tests/LanRemote.Tests` | 協定／座標單元測試與真實 TLS loopback 整合測試 |
| `scripts` | self-contained 發包與雙機證據腳本 |
| `readygate` | 已確認工作單與交付閘門證據 |

## Agent／Tool 接續

1. 先讀 `AGENTS.md`、`handoff.md` 與 `.agents/project-lifecycle.json`。
2. 每次接續先執行 `startup`；工作結束使用 `shutdown`。
3. 不得把上層 `gogoYulin` 當成本專案 Git root。
4. 技術棧、配對模型、安全邊界或外部 delivery 若要改變，重新進 ReadyGate。

## Delivery 狀態

- Git：獨立本機 `main`，無 remote。
- GitHub：`LOCAL_ONLY/NOT_CONFIGURED`。
- ReadyGate（`WO-LANREMOTE-INPUT-FPS-CURSOR-V1`）：執行中；正式發布仍停止，等待 v3 本機 GUI／發包驗證及 Windows 10／11 雙向實機證據。
- 原始效能目標：WGC + Media Foundation H.264 與雙機效能證據仍是後續工作，不屬於本輪 GDI/JPEG UX 工作單。
