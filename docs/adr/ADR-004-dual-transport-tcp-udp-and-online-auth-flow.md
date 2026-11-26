# ADR-004: Dual-Transport TCP + Nalix Authenticated UDP (NDP) & Luồng Đăng nhập Online

- **Trạng thái:** Accepted
- **Ngày:** 2026-11-26
- **Phạm vi:** `server/`, `desktop/`, `tests/desktop/`, `docs/adr/`

## 1. Bối cảnh

Sau `ADR-003`, backend `Carbon.Host` và client `Carbon.Desktop` chạy theo mô hình single-channel WebSocket kèm chế độ `OfflineWorldSession` nội bộ trên client. Khi vận hành thực tế theo hướng MMORPG online:

1. Dồn toàn bộ gói di chuyển 30Hz (`EntityMoveInput`) và snapshot (`EntityDeltaBatch`) lên một kết nối stream duy nhất gây **Head-of-Line Blocking** mỗi khi mất gói.
2. Client Desktop khởi động mặc định vào sandbox offline thay vì kết nối thẳng máy chủ, thiếu luồng UI đăng nhập (`AuthLogin` / `AuthRegister`) và chọn/tạo nhân vật (`CharacterSelect` / `CharacterCreate`).

## 2. Quyết định

### D1 — Song song hai kênh vận chuyển trên Server (`Carbon.Host`)
- Mở **`MapTcp<DefaultProtocol>`** (mặc định port `57207`, bật `NoDelay = true`) cho toàn bộ giao dịch tin cậy có thứ tự.
- Mở **`MapDatagram<DefaultProtocol>`** (mặc định port `57208`, giao thức `Nalix Datagram Protocol` / NDP trên UDP) cho luồng realtime độ trễ thấp.
- Giữ **`MapWebSocket<DefaultProtocol>`** (`WebSocketHost` mặc định `localhost`, `WebSocketPort = 57206`, tắt hoàn toàn khi cấu hình `CARBON_WS_PORT=0`) để phục vụ client `wasm/` trong trình duyệt.

### D2 — Trình tự bắt tay mật mã & cấp vé Authenticated UDP
1. Client mở `TcpSession` kết nối tới `ServerHost:ServerTcpPort` và thực hiện X25519 ECDH `HandshakeAsync` (hỗ trợ `ServerPublicKey` ghim cứng hoặc `AllowTofu` cho môi trường dev) để bật mã hóa AEAD trên kênh TCP.
2. Client gửi `AuthLogin` hoặc `AuthRegister` qua TCP; xác thực thành công sẽ nâng quyền phiên TCP lên `PermissionLevel.USER`.
3. Client truy vấn danh sách nhân vật (`CharacterListRequest`), tùy chọn tạo mới (`CharacterCreate`), rồi chọn nhân vật (`CharacterSelect` -> `CharacterEnterWorld`).
4. Ngay sau `CharacterEnterWorld`, client khởi tạo `DatagramSession(_tcpSession)`, xin single-use `DatagramTicket` qua kênh TCP đã xác thực và xác thực gói UDP đầu tiên bằng ChaCha20-Poly1305 AEAD + cửa sổ chống replay. Kết nối `DatagramConnection` trên server kế thừa nguyên vẹn `PermissionLevel.USER` và danh tính phiên stream tại thời điểm cấp vé. Nếu tường lửa chặn UDP, client tự động fallback chạy hoàn toàn trên TCP mà không làm gián đoạn gameplay.

### D3 — Phân kênh gói tin (Channel Multiplexing)
| Kênh truyền | Delivery | Gói tin C2S & S2C |
| :--- | :--- | :--- |
| **TCP (`TcpSession`)** | Reliable Ordered | `Auth*`, `Character*`, `Inventory*`, `CraftItem`, `SelectHotbarSlot`, `MapChunkRequest`, `MapChunkDelta`, `MapProp*`, `MapStructure*`, `EntitySpawn`, `EntityDespawn`, `InventorySlotChanged` |
| **UDP (`DatagramSession`)** | `UnreliableSequenced` (ch `1`) | `EntityMoveInput` (C2S), `EntityDeltaBatch` (S2C) |
| **UDP (`DatagramSession`)** | `ReliableUnordered` (ch `0`, `2`) | `EntityAttackInput` (C2S, ch `0`), `CombatHit` (S2C, ch `2`) |

### D4 — Ánh xạ phiên logic thống nhất (`ISessionTransportRegistry`)
- `Carbon.Abstractions` định nghĩa `ISessionTransportRegistry` để quy đổi mọi `IConnection` (`DatagramConnection` -> `StreamConnectionId`; TCP/WS -> `ConnectionId`) về một mã phiên logic duy nhất trước khi đẩy vào `AuthoritativeWorld` và `ConnectionAuthRegistry`.
- Đóng/logout phiên TCP sẽ tự động ngắt các `DatagramConnection` ghép đôi; ngược lại rớt kênh UDP đơn lẻ không tự ý kick nhân vật khỏi thế giới.

### D5 — Client Desktop thuần Online & Flow 3 Scene
- Xóa bỏ `OfflineWorldSession` khỏi `Carbon.Desktop.Networking`.
- `Program.cs` khởi tạo `NalixMultiplayerSession` và mở `GameWindow` trực tiếp vào luồng `LoginScene` -> `CharacterSelectScene` -> `WorldGameplayScene` (hỗ trợ menu `Esc` để quay lại Login/đổi nhân vật và cờ `CARBON_AUTO_LOGIN=1` cho kiểm thử/chụp ảnh tự động).

## 3. Phương án thay thế đã cân nhắc
- **Chỉ giữ WebSocket/TCP**: Tránh được phối hợp 2 socket nhưng giữ nguyên hiện tượng Head-of-Line blocking khi mạng yếu.
- **Tự viết UDP handshake thô**: Không cần thiết và dễ lỗi bảo mật replay/MITM vì `Nalix 14.4.0` đã cung cấp sẵn NDP (`DatagramListener` + `DatagramSession`) với AEAD và vé phiên 1 lần.

## 4. Hệ quả
- **Tích cực:** Di chuyển và combat truyền qua UDP có AEAD bảo vệ, vẫn giữ toàn bộ giao dịch quan trọng an toàn trên TCP; trải nghiệm người dùng đầy đủ màn hình Đăng nhập/Đăng ký/Chọn nhân vật.
- **Chi phí:** Cần quản lý ánh xạ hai chiều Stream <-> Datagram và kiểm thử tích hợp end-to-end với server `Carbon.Host` thật trong `Carbon.Desktop.Tests`.
