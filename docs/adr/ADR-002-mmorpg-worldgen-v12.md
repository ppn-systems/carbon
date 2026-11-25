# ADR-002: Nâng cấp Thuật toán Sinh Bản Đồ MMORPG v12

**Status:** Accepted

## Context

Thuật toán `ProceduralWorldGenerator` phiên bản `v11` bộc lộ các hạn chế cả về độ đúng đắn kỹ thuật, hiệu năng server lẫn thiết kế thế giới MMORPG:

1. Lỗi `SimplexNoise2D.FastFloor` trên số nguyên âm và chuẩn hóa $L_1$ làm hẹp phương sai khí hậu.
2. `TopographyHelper.IsPotentialRampSlot` chặn nhầm prop trên cả đất phẳng (cạo trọc ~59.5% diện tích thế giới), trong khi các cao nguyên nhỏ lại bị cô lập hoàn toàn không có dốc lên và `CliffAutotiler.CanStepAcross` cho phép bước ngược lên vách đá.
3. `RiverNetwork` cấp phát ~6.5 MB LOH mỗi vùng `512×512` (~0.3s/vùng), cấp phát `96 B` closure trên mỗi hit cache, và có sai số ở ranh giới vùng do `JoinBonus`.
4. `RoadNetwork` dùng đường cong Bezier mù địa hình: cắt xuyên đại dương, đâm thẳng vào giếng/nhà dân, không có cầu qua sông, không nối được tới mỏ ở tầng cao hơn và không cho tốc độ di chuyển.
5. `MapProp` khóa cứng toàn bộ quặng cao cấp ở `HarvestTier = 1`, không có tái sinh tài nguyên và thiếu các công trình khám phá/PvE ngoài đồng bằng nội địa.

## Decision

Nâng cấp `ProceduralWorldGenerator.GeneratorVersion` lên **`12`** với 4 trụ cột:

- **D1 — Địa chất Đa đường cong & Khí hậu Tự nhiên:** Kết hợp `DomainWarp2D` (`Continentalness`), `Erosion`, và gập đỉnh núi Musgrave thực thụ (`EvaluateRidged`) trong `ElevationStage`, kết hợp `WorldProgression.GetStarterWeight` để vùng tâm thuận lợi cho người chơi mới. Giảm chu kỳ vĩ độ, bổ sung độ ẩm ven biển thực và lược bỏ dải ruy-băng sai lệch trong `WhittakerBiomeResolver`. Biến `VolcanicAshland` thành đất đi bộ được xen khe dung nham đặc.
- **D2 — Thủy văn Không Rác LOH (`ArrayPool`), Không Đứt Biên & Sông Sâu:** Dùng lưới routing `Cell = 16`, cache tĩnh chia sẻ có giới hạn (không cấp phát closure khi cache hit), `ArrayPool<T>.Shared` cho toàn bộ mảng tạm (0 B LOH), hướng dốc thuần túy từ mặt nước `filled`, và phân tách lõi sông sâu (`DeepOcean`, `Elevation = 0.25f`) với bến cạn tự nhiên (`ShallowWater`).
- **D3 — Đường Bộ A* Theo Địa Hình, Cầu Gỗ, Đèo Núi & POI Mở Rộng:** Dùng thuật toán A* lưới `4×4` tile nối từ cổng các POI (tránh biển sâu, tự bắc cầu qua sông `Chunk2D.IsBridge`, mở dốc đường qua chênh bậc `±1` để nối cả Làng `H=0` với Mỏ `H=1`), đồng thời cấp `WalkableFast` (`1.20x` tốc độ). Mở rộng POI thêm `AncientRuins`, `BanditCamp`, `Watchtower`, `BossArena` và sàn cầu cảng đi bộ được.
- **D4 — Phân Tầng Tiến Trình MMORPG, `HarvestTier` & Tái Sinh Tài Nguyên:** Dùng `WorldProgression.GetZoneTier` (khoảng cách biến dạng nhiễu) điều chỉnh mật độ mỏ và vị trí POI; gán `HarvestTier` `0..3` và máu theo độ hiếm (`Copper` T1, `Iron/Silver` T2, `Gold/Crystal` T3, `AncientShrine` bất tử); bổ sung `ProcessPropRespawns` dựa trên tick trong `ChunkDelta` và xóa bỏ lỗi lưới dốc cạo trọc rừng.

## Consequences

- Mọi world `GeneratorVersion = 12` khác layout với `v11`; cần cập nhật Golden Hash (`GoldenChunkHash`, `GoldenWorldFingerprint`) và bảo toàn `ChunkDelta` tương thích khi migrate `GeneratorVersion`.
- Đường và cầu đồng bộ nhất quán giữa `PopulateChunk`, `PeekTileAtWorld`, `TileWalkabilityGrid`, `WorldCollision` và `ChunkTerrainRenderer`.
