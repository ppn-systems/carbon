# ADR-001: Đồng bộ map bằng seed + chunk delta

**Status:** Proposed

## Context

`engine/Carbon.Maps` sinh thế giới vô hạn từ seed (`Generators/ProceduralWorldGenerator.cs`) và stream theo chunk 32×32 (`Streaming/ChunkWorld2D.cs`). Trước thay đổi này:

- Chunk bị evict được trả về pool và sinh lại từ seed khi nạp lại, nên mọi chỉnh sửa (`SetTileAtWorld`, cây bị chặt, quặng bị đào) đều mất.
- Generator dùng `MathF.Sin/Cos`, mà kết quả không được đảm bảo giống nhau giữa x64, ARM64 và WASM. Client và server sinh cùng seed có thể ra địa hình khác nhau.
- `contracts/` trống, chưa có wire protocol cho map.

Một chunk đầy đủ là 1.024 tile. Gửi nguyên chunk qua mạng tốn băng thông, trong khi phần lớn chunk không bao giờ bị chỉnh sửa.

## Decision

- **D1 — Seed là nguồn sự thật cho địa hình gốc.** Server gửi `MapWorldInfo` (seed, `GeneratorVersion`, tile size, frequency, spawn). Client tự sinh mọi chunk.
- **D2 — Chỉ đồng bộ delta.** `ChunkDelta` (`engine/Carbon.Maps/Streaming/ChunkDelta.cs`) ghi tile override và trạng thái prop theo local index, kèm `Revision` tăng đơn điệu. Delta được áp lại mỗi khi chunk được populate và là đơn vị dùng cho lưu trữ (persist) lẫn truyền mạng.
- **D3 — Generation phải tất định trên mọi nền tảng.** Chỉ dùng phép toán IEEE cơ bản: `DeterministicMath.Sin/Cos` thay cho `MathF`, `SpatialHash` thay cho hash tự chế. Test golden hash (`ChunkDeltaAndDeterminismTests.GenerateChunk_GoldenHash_IsStable`) khóa output; mọi thay đổi làm đổi output phải bump `ProceduralWorldGenerator.GeneratorVersion`.
- **D4 — Contracts theo mẫu AtmosOS.** Dự án `contracts/Carbon.Contracts.Common` (`OpStatus`, `MutationResult`, `SharedOpCode` 0x00F0–0x00FF) và `contracts/Carbon.Contracts.Maps` (`MapOpCode`, `C2S/`, `S2C/`). Module Map giữ band `0x1100–0x11FF`:
  - `0x1101–0x110F` C2S
  - `0x1110–0x111F` S2C response
  - `0x1120–0x112F` S2C stream (`MapChunkDelta`, có `IsTerminator`)
  - `0x1130–0x11FF` S2C push (AoI broadcast)
- **D5 — Server-authoritative cho mọi thao tác ghi.** Client gửi ý định (`MapPropHarvest`, `MapStructurePlace`, …), không gửi giá trị damage hay kết quả. Server trả `MutationResult` và broadcast push (`MapTileChanged`, `MapPropChanged`, `MapStructureChanged`) tới các client trong AoI.
- **D6 — Hot path không sinh chunk.** Va chạm và navigation dùng `ChunkWorld2D.PeekTileAtWorld`: đọc chunk đã nạp, nếu không thì đọc delta, nếu vẫn không có thì gọi `SampleTile`. Cách này không cấp phát bộ nhớ.

## Alternatives Considered

- **Gửi nguyên chunk từ server:** đơn giản nhưng tốn khoảng 1.024 tile mỗi chunk dù không có chỉnh sửa nào, và server phải giữ toàn bộ chunk. Bị loại.
- **Fixed-point cho toàn bộ generator:** tất định tuyệt đối nhưng phải viết lại noise và có rủi ro lớn. Phép toán float IEEE cơ bản đã tất định trên .NET (SSE2 / ARM NEON / WASM), nên chỉ cần thay các hàm siêu việt (sin, cos).
- **Gửi enum domain (`BiomeType`, `StructureType`) trực tiếp:** buộc `contracts/` phụ thuộc vào `engine/`, vi phạm layer boundary. Thay vào đó gửi dạng `byte` và ghi chú mapping trong XML doc.

## Consequences

- Chỉnh sửa map không còn mất khi chunk bị evict. Băng thông map tỉ lệ với số chỉnh sửa, không tỉ lệ với diện tích.
- Mọi thay đổi thuật toán generation đều là thay đổi tương thích: phải bump `GeneratorVersion` và cập nhật golden hash.
- Delta hiện nằm trong RAM (`ChunkWorld2D.Deltas`). Việc ghi xuống storage và handler phía server chưa được triển khai vì `server/` còn trống. Đây là bước tiếp theo.
