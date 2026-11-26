# ADR-005: DDD/SOLID Client Architecture, Declarative Vector-Rig Engine (.cvg.json), and RPG UI Upgrade

- **Trạng thái**: Accepted
- **Ngày**: 2026-11-26
- **Phạm vi**: `resources/art/`, `contracts/`, `server/`, `desktop/`

## 1. Bối cảnh (Context)

Người dùng yêu cầu chuẩn hóa kiến trúc **DDD / SRP / SOLID** ở phía client desktop, thay thế việc vẽ tay ảnh `.png` bằng hệ thống **khai báo bản vẽ vector (`*.cvg.json`) nạp trực tiếp lên GPU qua `GeometryBatch`**, đồng thời nâng cấp toàn diện **UI nhân vật, quái vật và vật phẩm**:

1. Không dùng file ảnh raster (`.png`), tránh phụ thuộc pixel vỡ hạt khi phóng to ở chân dung HUD hoặc màn chọn nhân vật; toàn bộ hình dạng, bảng màu, xương chuyển động (`bone`) và animation nằm trong file riêng `resources/art/*.cvg.json` để dễ chỉnh sửa trực tiếp.
2. `IClientWorldSession`, `NalixMultiplayerSession`, `WorldSceneRenderer` và `HudOverlayRenderer` trước đây ôm quá nhiều trách nhiệm (vi phạm ISP và SRP).
3. Thanh máu quái trước đây dễ chìm, thiếu hiệu ứng trễ sát thương (ghost damage trail) và khung mục tiêu riêng; thanh hotbar & thông tin vật phẩm cần hiển thị đẹp, rõ cấp độ công cụ/vũ khí; thao tác chặt cây/đào đá cần animation và VFX phân tách rõ ràng.

## 2. Quyết định (Decision)

### D1. Định dạng Bản vẽ Vector Carbon (`resources/art/*.cvg.json`)
- Lưu toàn bộ định nghĩa hình học vector 2D (ellipse, rectangle, capsule, polygon, triangle, line, ring) gắn theo xương (`root`, `cape`, `tail`, `legLeft`, `legRight`, `body`, `armBack`, `head`, `headgear`, `armFront`, `aura`) và track keyframe (`idle`, `walk`, `slash`, `chop`, `mine`, `attack`) trong thư mục `resources/art/`:
  - `catalog.cvg.json`: ánh xạ preset nhân vật (`0..3`), archetype quái (`101..104`), archetype NPC (`201..203`), và item (`1..12`).
  - `characters/*.cvg.json`, `monsters/*.cvg.json`, `npcs/*.cvg.json`, `items/*.cvg.json`.
- Nạp bằng `System.Text.Json` Source Generation (`CvgJsonContext`, 100% Native AOT) thành dữ liệu đã tiền biên dịch `CompiledVectorRig` và `CompiledItemVectorArt`; `VectorRigRenderer` vẽ không cấp phát bộ nhớ trên hot-path (`stackalloc`).

### D2. Chuẩn hóa DDD / ISP / SRP / DIP
- **ISP (`Carbon.Desktop.Abstractions`)**: Tách `IClientWorldSession` thành 4 port theo vai trò: `IClientAuthService`, `IClientCharacterService`, `IClientWorldReadModel` (chỉ đọc cho Renderer/HUD) và `IClientGameplayController`.
- **SRP (`Carbon.Desktop.Networking`)**: Tách `NalixMultiplayerSession` thành 4 partial file gọn sạch (`NalixMultiplayerSession.cs`, `.Auth.cs`, `.Actions.cs`, `.Packets.cs`).
- **DIP & SRP (`Carbon.Desktop.Rendering`)**:
  - `IVectorArtRepository` tách nguồn đọc dữ liệu `.cvg.json` khỏi lớp vẽ.
  - `CombatFeedbackTracker` quản lý rung prop, hạt va chạm, số sát thương, ghost-HP trail và khóa mục tiêu giao chiến.
  - `WorldEntityOverlayRenderer` vẽ screen-space nameplate gọn, thanh máu quái có ghost trail & rank pip, độ bền prop.
  - HUD được tách thành các widget độc lập dưới `Hud/`: `HeroVitalsWidget`, `MonsterTargetWidget`, `HotbarAndItemWidget` (+ tooltip thẻ vật phẩm), `CraftingWorkbenchWidget`, `MinimapRadarWidget`.

### D3. Giao diện Nhân vật, Quái vật & Vật phẩm Nâng cao
- **Hero Unit Frame (`HeroVitalsWidget`)**: Khung chân dung nhân vật vẽ trực tiếp từ `.cvg.json`, huy hiệu class, thanh máu ghost-trail, thanh sẵn sàng hành động.
- **Monster Plates & Target Frame (`WorldEntityOverlayRenderer` + `MonsterTargetWidget`)**: Thanh máu quái tỷ lệ chuẩn `68–76×9px` có damage ghost-trail, khung khóa mục tiêu trên đỉnh màn hình hiển thị chân dung vector quái/NPC + thông số máu chi tiết.
- **Hotbar & Item Tooltip (`HotbarAndItemWidget`)**: Ô hotbar 9 ngăn viền phân cấp vật phẩm, icon vector đa lớp, độ bền đổi màu, và thẻ tooltip RPG chi tiết khi rà chuột qua vật phẩm.

## 3. Hệ quả (Consequences)
- Mọi bản vẽ nhân vật, quái, NPC và vật phẩm nằm hoàn toàn ở `resources/art/*.cvg.json` — mở rộng hoặc đổi giao diện tức thì không cần ảnh `.png` hay biên dịch lại.
- Pass 3 Y-sort vẽ trực tiếp toàn bộ thực thể + vũ khí đang vung trong cùng một `GeometryBatch`, giảm chuyển state GPU và giữ che khuất 2.5D chính xác tuyệt đối.
