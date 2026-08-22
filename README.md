# LanRemote

LanRemote 是一個 Windows 私人區網遠端控制 MVP。兩臺電腦執行同一套程式，各自都能選擇「讓這臺電腦被控制」或「控制另一臺電腦」；結束目前 session 後交換角色，就能反向控制。

目前提供本機測試包，不是公開發布版本。已在 Windows 11 25H2 完成本機建置、加密 loopback 整合與 UI 冒煙測試；Windows 10 22H2 實機相容性與雙向五分鐘效能測試仍待完成。

## MVP 已實作

- 手動輸入私人區網 `IPv4:port`，預設連接埠 `45873`。
- TLS 1.2/1.3 加密，程式每次啟動產生新的 session-only 自簽憑證。
- 兩端六位數 SAS 配對碼，被控端每次必須明確按「是」才開始傳送畫面。
- 主要螢幕、滑鼠移動／按鍵／滾輪、鍵盤與立即斷線。
- 同一個 EXE 具備被控端與控制端兩個角色；單一 session 僅允許一個控制方向。
- 協定具有 magic、版本、訊息類型、payload 長度上限與 nonce 驗證。
- 僅接受 loopback、RFC1918 或 IPv4 link-local 位址。

## 尚未達成

- 畫面目前使用 GDI/JPEG 相容模式，不是工作單指定的 Windows Graphics Capture + Media Foundation H.264。
- 尚無 Windows 10 22H2 雙機實測證據，因此不能宣稱達到 1080p30、五分鐘互動延遲 p95 小於 200 ms。
- Windows 10 22H2 不在 .NET 10 官方支援清單；self-contained 發布只能降低部署相依，不能把該系統變成官方支援平臺。

## 安全邊界

- 不支援無人值守、隱蔽監控、提權、UAC／安全桌面繞過或公網 relay。
- 不包含自動探索、多螢幕、音訊、剪貼簿或檔案傳輸。
- `SendInput` 受 Windows UIPI 限制，普通權限程式不能控制較高權限視窗。
- 程式不自動修改 Windows Firewall；若系統詢問，僅由使用者決定是否允許私人網路。
- 測試包尚未簽章；檔案來源或 SHA-256 不符時不要執行。

## 使用本機測試包

測試包位於 `artifacts/LanRemote-win-x64.zip`（`artifacts/` 不納入 Git）。解壓縮後必須保留整個資料夾，不能只複製 EXE。

1. 把 ZIP 複製到 Windows 11 與 Windows 10，兩邊都完整解壓縮。
2. 兩邊執行 `LanRemote.App.exe`。
3. 被控制的電腦按「開始等候」，把畫面顯示的 `IPv4:45873` 告訴另一臺。
4. 控制端輸入位址並按「連線並核對」。
5. 確認兩端六位配對碼相同，再由被控端按「是」。
6. 在遠端畫面內按一下後測試滑鼠與鍵盤。
7. 反向控制時先斷線，再讓兩臺交換 A／B 角色並重新配對。

完整的人類操作與證據腳本說明也包含在 ZIP 的 `README.txt`。

## 建置

需求：Windows x64 與 .NET SDK `10.0.400`。

```powershell
dotnet build .\LanRemote.sln --configuration Debug
dotnet test .\LanRemote.sln --configuration Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Package.ps1
```

建置中間檔會放在 Windows temp 的 `LanRemoteBuild`，避免 Google Drive 對 WPF apphost 的 memory-map 鎖定。發包腳本會先跑測試，再輸出包含 .NET runtime 的 `win-x64 self-contained` 資料夾與 ZIP。

## 專案結構

| 路徑 | 責任 |
|---|---|
| `src/LanRemote.Protocol` | 二進位 framing、JSON payload、畫面與輸入訊息 |
| `src/LanRemote.Core` | TLS session、SAS 配對、私人 IP 政策、host/controller |
| `src/LanRemote.Windows` | 主要螢幕 JPEG 擷取與受限 `SendInput` 注入 |
| `src/LanRemote.App` | WPF 雙角色 UI、配對核准、遠端畫面與輸入轉換 |
| `tests/LanRemote.Tests` | 協定單元測試與真實 TLS loopback 整合測試 |
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
- ReadyGate：等待 Windows 10／11 雙向實機證據；H.264 效能基線尚未完成。
