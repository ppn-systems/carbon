# Carbon Declarative Vector Art (`resources/art/`)

Thư mục này chứa toàn bộ bản vẽ vector 2D khai báo bằng JSON (`.cvg.json`) cho **nhân vật, quái vật, NPC, vật phẩm, cây cối/tài nguyên, quần xã (biome) và công trình** — **0% ảnh raster (`.png`/`.jpg`)**, biên dịch trực tiếp thành bảng tra `O(1)` khi khởi động và bake tĩnh vào terrain/vẽ qua `GeometryBatch`.

## Cấu trúc thư mục
- `catalog.cvg.json` (`schemaVersion: 2`): Bảng ánh xạ ID toàn cục:
  - `characters` (`preset 0..3`), `monsters` (`101..107`), `npcs` (`201..204`), `items` (`1..32`)
  - `props` (`MapPropType 1..22`)
  - `biomes` (`BiomeType 0..13`)
  - `structures` (`StructureType 1..8`)
- `characters/base_hero.cvg.json`: Rig cơ sở duy nhất cho người chơi (`supportsModularHuman`, tag `role`).
- `monsters/*.cvg.json`: 7 quái vật kèm keyframe animation `idle`, `walk`, `attack`.
- `npcs/*.cvg.json`: 4 NPC kèm animation.
- `items/*.cvg.json`: 32 vật phẩm, công cụ, vũ khí, tài nguyên và trang bị.
- `props/*.cvg.json`: 24 cây cối, bụi rậm (kể cả bụi khô sa mạc `dry_bush` và cỏ đụn cát `dune_grass`), xương rồng, nấm (kèm biến thể phát quang hiếm), mỏ khoáng sản, tinh thể và đền cổ (`groundShapes`, `canopyShapes`, `remnantShapes`, `paletteVariants`, `light`). Gốc tọa độ `(0, 0)` tại chân prop.
- `biomes/*.cvg.json`: 14 biome chuẩn hóa `displayName` tiếng Anh sạch đẹp, bảng màu mặt đất/minimap/vách đá 4 sắc độ (`cliffBase`, `cliffStrata`, `cliffHighlight`, `cliffShadow`) và 3 biến thể chi tiết bề mặt `detailVariants` trong không gian tile `[0..32]`. Khối `surface` mô tả trường màu mặt đất liên tục (`pattern`: `patches`, `dunes`, `strata`, `drifts`, `peat`, `rock`, `water`; `colors`, `accent`, `variation`, `grain`) và danh sách `decals` (lớp trang trí rải ngẫu nhiên trong tile: `chance`, `maxCount`, `placement` = `any`/`dry`/`shore`, `jitter`, `variants`); tất cả được bake tĩnh vào mesh chunk.
- `structures/*.cvg.json`: 8 công trình xây dựng (`WoodWall`, `StoneWall`, `WoodDoor`, `StoneDoor`, `WoodFloor`, `StoneFloor`, `RoofPillar`, `ForgeAnvil`) trong không gian tile `[0..32]`, hỗ trợ riêng `openShapes` cho trạng thái cửa mở.

## Quy ước & Kiểm tra
1. Mọi `polygon` phải **lồi (convex)** và có từ **3 đến 12 đỉnh**.
2. Toàn bộ DTO JSON được sinh mã nguồn Native-AOT thông qua `CvgJsonContext`.
3. Chạy `dotnet test tests/desktop/Carbon.Desktop.Tests/Carbon.Desktop.Tests.csproj` để xác thực đủ 100% file và đảm bảo `RasterImagesFound == 0`.
