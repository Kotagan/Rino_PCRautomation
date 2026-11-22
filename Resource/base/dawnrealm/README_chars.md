# 黎明界角色头像优先度

## 目录

| 路径 | 用途 |
|------|------|
| `Resource/base/dawnrealm/char_tiers.json` | **手改评级/属性**（S/A/B/C + 光火） |
| `Resource/base/dawnrealm/char_priority.json` | 由脚本生成的匹配库（含 priority） |
| `Resource/base/image/dawnrealm/chars/u{id}_s{star}.png` | 官网头像模板（RGB；unitId 区分同名皮肤） |
| `Resource/base/image/dawnrealm/chars/bootcell_u{id}.png` | 开局招募整格实机裁剪（优先） |
| `Resource/base/image/dawnrealm/chars/boot_u{id}.png` / `bootface_*.png` | 开局脸部实机裁剪（回退） |
| `Resource/base/image/dawnrealm/chars/inbox/` | 自动裁图（未标注，勿直接当模板） |

## 生成

```bash
python tools/_build_dawn_tier_catalog.py
```

头像来源：`https://redive.estertion.win/icon/unit/{unitId}{star}1.webp`

## 选人规则

1. 筛选项：**光 → 火 → 全部**
2. 同页内按 `priority` 模板匹配（属性分 + 评级分 + 6星）
3. 美食殿堂开局优先实机模板：**优衣(1002) / 怜·剑圣(1003) / 日和·猫拳(1001)**（bootcell→boot→bootface）
4. 开局三人未齐时按评级库补满；满 3/3 只点「進行邀請」，**不再全表乱点**

改评级：编辑 `char_tiers.json` 后重新跑生成脚本。
