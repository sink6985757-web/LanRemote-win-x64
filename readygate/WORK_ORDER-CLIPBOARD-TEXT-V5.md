# WORK_ORDER_CONFIRMED — LanRemote 純文字剪貼簿同步

- id: `WO-LANREMOTE-V5-TEXT-CLIPBOARD-20260823`
- confirmed_by: Yulin（`確認執行`）
- target: protocol v5 / package v4.2 local test candidate
- profile: general

## 目標

- 控制端在本機複製或剪下純文字後，可在被控端直接以 Windows 原生 `Ctrl+V` 貼上。
- Toolbar 使用短名稱「剪貼簿」，提供關閉、主控到被控單向與雙向三種模式；預設雙向。
- 被控端每次配對另行核准文字同步，斷線後授權失效。

## 已確認行為

- 只同步 Unicode 純文字；忽略圖片、HTML、RTF、檔案與資料夾。
- 檔案與資料夾維持既有 Toolbar、路徑選擇、拖放與檔案剪貼簿傳輸。
- 剪貼簿變更時自動同步；雙向採最後變更優先並抑制回送迴圈。
- UTF-8 內容上限 64 MB，以 256 KB 分塊並驗證 SHA-256。
- 關閉模式後，遠端電腦內部的 `Ctrl+C／X／V` 仍正常運作。
- 關閉開關、斷線或結束程式時清除記憶體同步快取，不清除 Windows 原生剪貼簿。

## 驗收

- TLS loopback 證明控制端到被控端、被控端到控制端、單向與關閉模式。
- 單元測試證明跨分塊 Unicode、順序、容量與雜湊驗證。
- GUI smoke 回讀「剪貼簿」控制項；兩臺實機仍由 Yulin 核准及驗證。
- Release build、完整測試、format、腳本解析、NuGet vulnerability audit 與 ZIP 回讀通過。

## 邊界

- 不新增圖片、格式化剪貼簿、剪貼簿歷史或檔案背景同步。
- 不自動操作 Windows Firewall、安全桌面或其他安全提示。
- 只修改本機專案、測試與本機測試包；不 commit、push、release 或公開發布。
- 使用者後續提出的「主程式獨立檔案傳輸、雙端皆可主動發起」不在本工作單內，需另開 ReadyGate。
