# ADR-003: Kiến trúc Backend Native AOT thuần DB & Desktop Client phân tầng

- **Trạng thái:** Accepted
- **Ngày:** 2026-11-26
- **Phạm vi:** `contracts/`, `server/`, `desktop/`, `tests/`

## 1. Bối cảnh

Sau khi hoàn thiện khung `engine/` (9 modules ECS/Physics/Simulation/Maps/Combat/AI/Items/Prediction) và đại tu WorldGen v12 (`ADR-002`), dự án cần mở rộng sang hai phần còn trống hoặc đang ở dạng prototype:

1. **Dedicated Server (`server/`)** phải chạy `Native AOT` + trimming, không phụ thuộc Entity Framework Core hay ORM dựa trên reflection, đồng thời vẫn giữ phân tách tầng nghiêm ngặt (tham chiếu mô hình Clean Architecture của `AtmosOS`).
2. **Desktop Client (`desktop/`)** hiện đang dồn toàn bộ game loop, terrain/prop rendering, camera và thế giới trong một file tĩnh duy nhất (`Program.cs` > 800 dòng), chưa có Abstraction hay tầng mạng `Nalix.SDK` để vừa chạy offline loopback vừa kết nối Dedicated Server.

## 2. Quyết định kiến trúc

### 2.1. Đặt tên và cấu trúc 5 tầng trong `server/` (Không dùng `Carbon.Server`)

Không đặt tên project/namespace là `Carbon.Server`. Thay vào đó tổ chức `server/Carbon.Host.slnx` thành 5 assembly đúng trách nhiệm:

| Project | Vai trò | Ràng buộc phụ thuộc |
| :--- | :--- | :--- |
| `Carbon.Abstractions` | Interfaces DB (`IAccountStore`, `ICharacterStore`, `IChunkDeltaRepository`), records dữ liệu, `IAuthoritativeWorld`, `IWorldOutboundSink` | Chỉ phụ thuộc `Carbon.Contracts.*` và `Carbon.Engine` (`Maps`, `Simulation`, `Items`, `Combat`) |
| `Carbon.Application` | `AuthService`, `SpatialAoiTracker`, `AuthoritativeWorld` điều phối Fixed-Tick & ghi delta trễ | Chỉ phụ thuộc `Carbon.Abstractions` |
| `Carbon.Infrastructure` | Cài đặt DB thuần (`Postgres` & `Sqlite`), `Pbkdf2PasswordHasher`, schema migrator SQL nhúng | Phụ thuộc `Carbon.Abstractions`, `Npgsql`, `Microsoft.Data.Sqlite` |
| `Carbon.Handlers` | Stateless Nalix packet handlers (`IdentityHandlers`, `MapHandlers`, `EntityHandlers`, `ItemHandlers`) | Phụ thuộc `Carbon.Abstractions`, `Carbon.Application`, `Carbon.Contracts.*`, `Carbon.Maps`, `Nalix.Runtime` — **tuyệt đối không** phụ thuộc `Carbon.Infrastructure` |
| `Carbon.Host` | Composition root (`Program.cs`, `Startup.cs`), `PeriodicTimer` world tick runner, `NalixWorldOutboundSink` | Tham chiếu các tầng server + `Nalix.Hosting`, bật `PublishAot=true` |

Lý do không tạo thêm `Carbon.Domain`: toàn bộ luật miền gameplay, thành phần ECS, vật lý và bản đồ đã nằm trong `engine/*` để dùng chung cho server authoritative lẫn client prediction (`Carbon.Prediction`).

### 2.2. Dual Pure Native AOT Database (Zero EF Core)

`Carbon.Infrastructure` cung cấp hai bộ cài đặt thuần ADO.NET / `DbDataReader` không dùng reflection:

- **PostgreSQL (`CARBON_DB_PROVIDER=postgres`)**: Dùng `Npgsql` (`10.0.3`) với `NpgsqlSlimDataSourceBuilder`, `EnableArrays()`, `EnableTransportSecurity()`, SQL tham số hóa hằng số `const string`, `NpgsqlBatch` cho ghi theo lô (`ON CONFLICT DO UPDATE`) và đọc dữ liệu theo ordinal.
- **SQLite (`CARBON_DB_PROVIDER=sqlite`, mặc định cho local/test)**: Dùng `Microsoft.Data.Sqlite` (`10.0.13`) chế độ `WAL`, parameterized SQL và migrator riêng `schema_migrations` (`SqliteSchemaMigrator`).

### 2.3. Chia nhỏ `contracts/` theo domain & dải OpCode

Giữ `Carbon.Contracts.Common` (`0x00F0–0x00FF`) và `Carbon.Contracts.Maps` (`0x1100–0x11FF`), bổ sung 3 contract assemblies tách biệt để tái sử dụng cho cả server, desktop và WASM tương lai:

- `Carbon.Contracts.Identity` (`0x0100–0x01FF`): Đăng ký, đăng nhập, danh sách/tạo/chọn nhân vật, `CharacterEnterWorld`.
- `Carbon.Contracts.Entities` (`0x1200–0x12FF`): `EntityMoveInput`, `EntityAttackInput`, `EntitySpawn`, `EntityDespawn`, `EntityDeltaBatch`, `CombatHit`.
- `Carbon.Contracts.Items` (`0x4000–0x40FF`): Đồng bộ/thay đổi slot túi đồ, chọn hotbar, chế tạo vật phẩm.

Tất cả contract assembly đều bật `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` và tham chiếu trực tiếp `Nalix.Abstractions` + `Nalix.Codec` để Roslyn source generator sinh mã tại thời điểm biên dịch.

### 2.4. Tách kiến trúc `desktop/` thành 4 projects

- `Carbon.Desktop.Abstractions`: `DesktopClientOptions`, `RemoteEntitySnapshot`, `IClientWorldSession` (không phụ thuộc OpenGL/Silk.NET).
- `Carbon.Desktop.Networking`: `OfflineWorldSession` (chạy cục bộ không cần server) và `NalixMultiplayerSession` (`WebSocketSession` `Nalix.SDK`, subscribe push trước khi connect, dự đoán client + đồng bộ AoI).
- `Carbon.Desktop.Rendering`: Quản lý rendering OpenGL 2.5D (`ChunkTerrainRenderer`, `PointOfInterestRenderer`, `Atmosphere`, `WorldSceneRenderer`, `CameraRig2D`, `ScreenshotHelper`).
- `Carbon.Desktop`: Entry point tối giản (~65 dòng), `DesktopInputController`, và `WorldGameplayScene` kế thừa `Carbon.Graphics.Scenes.Base.GameScene`.

## 3. Hệ quả

- **Tích cực:** Biên dịch sạch AOT (`0` IL/Trim warnings), có thể chạy local/CI ngay lập tức với SQLite không cần Docker và chuyển sang PostgreSQL chỉ qua biến môi trường; logic phiên chơi Desktop test được không cần OpenGL context.
- **Chi phí kiểm soát:** Thêm 12 projects mới vào build matrix và CI, được bảo vệ bằng arch/contract guard tests (`ProtocolContractGuardTests`, `ServerLayerArchitectureTests`) và persistence/multiplayer integration tests.
