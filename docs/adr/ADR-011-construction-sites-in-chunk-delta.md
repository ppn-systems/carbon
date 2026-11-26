# ADR-011: Construction sites và quyền sở hữu công trình trong chunk delta

**Status:** Proposed

## Context

Xây dựng kiểu RimWorld (`docs/building.md`) thêm các trạng thái mà `ChunkDelta` format v2 (`engine/Carbon.Maps/Streaming/ChunkDelta.cs`) và `MapChunkDelta` chưa lưu được:

- Blueprint/frame đang xây dở: loại công trình, rotation, owner, stage, số vật liệu đã giao, work đã làm.
- Owner của công trình đã hoàn thành. Thiếu owner thì không kiểm tra được quyền deconstruct phía server.
- Công trình nhiều ô (workbench 2×1, giường 1×2): mỗi ô cần biết anchor và rotation để gỡ cả footprint.
- Sàn nằm dưới đồ đạc (bếp, giường cần sàn): grid chỉ có một lớp, nên sàn phải được nhớ lại để khôi phục khi gỡ đồ.

ADR-001 D2 quy định `ChunkDelta` là đơn vị duy nhất dùng cho cả persist lẫn sync, nên các trạng thái này phải nằm trong đó.

## Decision

- **D1 — `ChunkDelta` format v3.** Sau section `Structures` thêm hai section, đều sắp xếp theo local index để output tất định:
  - `Sites`: key theo anchor tile, gồm type, rotation, stage, width, height, owner (`ulong`), work done, và số lượng đã giao của từng cost entry.
  - `StructureRecords`: key theo từng ô của công trình, gồm owner, offset tới anchor, rotation, loại sàn bên dưới.
  
  Bộ đọc vẫn nhận v1 và v2. Bộ ghi luôn ghi v3.
- **D2 — `MapChunkDelta` thêm các mảng song song** ở `SerializeOrder` 18–29: `Site*` (index, type, rotation, stage, size đóng gói nibble, work done, owner) và `Record*` (index, owner, anchor offset đóng gói nibble, rotation, sàn bên dưới). Số lượng vật liệu đã giao không gửi xuống client.
- **D3 — `ConstructionLedger`** (`engine/Carbon.Maps/Building/ConstructionLedger.cs`) giữ sites và records trong RAM. `ChunkWorld2D` ghi mọi thay đổi vào delta qua callback, giống cách `BuildingGrid` đang làm với `Structures`, và khi `ImportDelta` thì nạp lại bằng `ApplyChunk`.
- **D4 — Enum giữ dạng `byte` trên wire** (như ADR-001). Thêm `StructureType` 9–15 và `StructureCategory.Furniture = 6`. Giá trị cũ không đổi.

## Alternatives Considered

- **Nhét trạng thái xây dựng vào `StructureTile`:** sẽ đổi struct có mặt khắp engine và desktop, và mọi structure đều phải mang thêm dữ liệu không dùng tới. Bị loại.
- **Store riêng cho sites ngoài chunk delta:** phải có thêm một kênh persist và sync, trái với ADR-001 D2. Bị loại.
- **Thêm lớp sàn riêng vào `BuildingGrid`:** đúng mô hình hơn, nhưng phải sửa collision, renderer và format v2. `FloorBeneath` trong record đủ cho nhu cầu hiện tại.

## Consequences

- Blueprint, frame và owner còn nguyên khi chunk bị evict hoặc server restart.
- `MapChunkDelta` có layout mới. Client và server phải nâng cấp cùng lúc; client cũ không đọc được packet mới.
- Mỗi tick xây (work tick) tăng `Revision` của chunk.
- Tiến độ deconstruct chưa được persist, sẽ reset khi restart.
