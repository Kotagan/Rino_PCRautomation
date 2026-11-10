<p align="center">
  <img alt="LOGO" src="https://s2.loli.net/2024/10/06/XSPUyb2VKuA9DQT.png" width="225" height="225" />
</p>

<div align="center">

# Rino 台服版

公主連結（Princess Connect! Re:Dive）**台灣繁中服**圖像識別自動化助手

</div>

> [!IMPORTANT]
> 本倉庫是基於已歸檔上游 [miaoyu2233/Rino_PCRautomation](https://github.com/miaoyu2233/Rino_PCRautomation) 的 **台服專用 Fork**，由 [Kotagan](https://github.com/Kotagan) 繼續開發維護。  
> 目標只服務 **台灣繁體字服**，不適配國服 / 日服 / 簡中界面。

PCR 自動化小助手，沿用 Rino / MFAWPF / MaaFramework 技術路線。  
原專案作者聲明：禁止用於任何營利行為；本 Fork 同樣遵守。

## 開發方向

- 只維護台服繁中 UI / 任務管線
- 優先修台服活動、日常、商店等實際可用功能
- 默認界面語言：繁體中文
- 軟件更新源指向本倉庫：https://github.com/Kotagan/Rino_PCRautomation

## 運行注意

- 模擬器解析度必須為 **1280x720（DPI 240）**
- 建議遊戲 60 幀及以上；不要開後台掛機保活、動態調幀
- 任務起點與終點默認都是**遊戲主頁**
- 推薦 MuMu 12 / 雷電；不推薦藍疊

## 功能概覽

行程表、商店買藥、競技場、郵件/任務獎勵、探險、角色碎片商店、劇情、活動掃蕩、露娜塔、裝備強化與部分戰鬥小工具等。  
具體任務列表以 `interface.json` 為準，後續會按台服實際需求增刪。

## 黎明界肉鸽（開發中）

任務名：**黎明界肉鸽**（完整肉鸽，不是只 SKIP）。

可配置項：

- **策略模式**：穩定通關 / 凹分
- **同行公會**：美食殿堂、破曉之星、咲戀育幼院、王宮騎士團、拉比林斯，以及伊麗莎白牧場、墨丘利財團、暮光流星群等
- **目標難度**：1–5
- **通行證用量**：用完為止 / 固定 1 次 / 固定 3 次

決策偏好（首版寫死在決策表，後續可再做成選項）：

- 遺物優先：
  - **難度 1–4**：**弱體**（沖 4 / 9 / 15 層）
  - **難度 ≥ 5**：**守護 → 會心**
- 角色屬性優先：**光 → 火**（其餘其次）；職階優先坦 / 奶

注意：

- 必須 **橫屏 1280×720（DPI 240）**，從遊戲**主頁**啟動任務
- 需已解鎖對應難度；完整肉鸽單局較久，首版目標是「多數局能打完」而非 100% 難度 5
- Custom Action 位於 `Resource/custom/DawnRealm*.cs`，管線為 `Resource/base/pipeline/dawnrealm_task.json`

## 致謝

- 上游：[miaoyu2233/Rino_PCRautomation](https://github.com/miaoyu2233/Rino_PCRautomation)（已 Archived）
- 框架：[MaaFramework](https://github.com/MaaXYZ/MaaFramework) / MFAWPF

## 許可

沿用上游開源協議（見 `LICENSE` / `LICENSE-GPL-3.0`）。
