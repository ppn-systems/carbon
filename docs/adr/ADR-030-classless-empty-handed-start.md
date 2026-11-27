# ADR-030: Khởi đầu không nghề, tay trắng

- **Date**: 2026-11-27
- **Status**: Accepted
- **Scope**: `contracts/Carbon.Contracts.Identity` (`CharacterCreate`, `HeroArchetypeClass`), `contracts/Carbon.Contracts.Common`
  (`CarbonProtocol`), `server/Carbon.Application` (Identity, World persistence), `server/Carbon.Handlers` (Identity),
  `desktop/Carbon.Desktop` (character creator), `resources/localization`
- **Amends**: [ADR-008](ADR-008-standardized-nine-slice-ui-ranged-combat-and-station-forging.md) mục 2 (`CharacterCreate.StartingClass`).
- **Depends on**: survival bootstrap (tay không đấm được cây bậc 1, đá/cành/sợi rời quanh điểm xuất phát, `SpawnClearingStage`).

## Context

Yêu cầu của chủ dự án: "Lúc tạo nhân vật không cho người chơi chọn gì cả, không có class kiếm hay pháp sư. Họ bắt đầu
với hai bàn tay trắng và phải tự chặt cây, tự chế đồ."

Hiện trạng trước thay đổi:

- `CharacterCreate` mang `[SerializeOrder(2)] StartingClass` (ADR-008). Creator trên desktop
  (`desktop/Carbon.Desktop/Scenes/CharacterSelectScene.cs`) có ba thẻ chọn nghề Vanguard / Ranger / Arcanist.
- `AuthService.CreateCharacterAsync` kiểm tra `startingClass`, rồi gọi `StarterGameCatalog.GrantStarterLoadout(inv, class)`:
  rìu, cuốc đá, vũ khí theo nghề, thảo dược, bộ tường gỗ, 18 khúc gỗ và bộ giáp da mặc sẵn.
- Nghề chỉ ảnh hưởng hai chỗ:
  1. bộ đồ khởi đầu ở trên;
  2. cổng kỹ năng `SkillCatalog.CanCharacterAccessSkill` (`engine/Carbon.Items/Catalog/SkillCatalog.cs`): một kỹ năng mở
     khi nó đã có trong mask đã học, **hoặc** khi nhân vật có cờ `CharacterTraits.ClassAdvanced` và `RequiredClass`
     trùng nghề. Không có code nào bật `ClassAdvanced`, và mask chỉ được ghi khi đã qua cổng này. Thực tế không nhân vật
     mới nào mở được kỹ năng nghề.
- Chỉ số không phụ thuộc nghề: `CharacterStatCalculator` và `CharacterProgressionPolicy` không đọc `Class`.
- Với túi rỗng, đường tiến lên bị khóa: tay không là bậc 0, cây sồi và tảng đá bậc 1, nên không có gỗ, đá cho rìu đá.
  Thay đổi survival bootstrap (PR riêng) gỡ khóa này trước.

## Decision

- **D1 — Gói tin.** Bỏ `StartingClass` khỏi `CharacterCreate`. Gói chỉ còn `CharacterName` (0) và `AppearancePreset` (1).
  Đây là thay đổi wire, nên `CarbonProtocol.Version` tăng từ 1 lên 2. `AuthService.LoginAsync` và `RegisterAsync` đã từ
  chối mọi phiên bản khác (`AuthStatus.ProtocolMismatch`), nên client cũ không đăng nhập được để gửi gói cũ. Nếu một gói
  cũ còn byte nghề ở cuối, `CharacterCreate.Deserialize` trả về `null` chứ không đọc sai trường.
- **D2 — Không nghề.** Thêm `HeroArchetypeClass.None = 255`. Mọi nhân vật mới được lưu `Class = None`. Cột `class_id`
  (SQLite `INTEGER`, PostgreSQL `SMALLINT`) đã chứa được 255, nên không cần migration.
- **D3 — Túi rỗng.** Nhân vật mới có túi, hotbar và mọi ô trang bị đều trống, vàng bằng 0. `CharacterPersistence.LoadInventory`
  khi gặp blob hỏng chỉ cấp bộ đồ nghề cho nhân vật có nghề, nhân vật `None` giữ túi rỗng. Các tùy chọn Debug
  (`CARBON_DEBUG_STARTER_ITEMS`, `CARBON_DEBUG_FORGE_KIT`, `CARBON_DEBUG_WORN_GEAR`) vẫn chỉ có ở host bản Debug và giữ nguyên.
- **D4 — Nhân vật cũ giữ nguyên.** Không migration phá dữ liệu: nhân vật đã có `Class` 0..2 giữ nghề, túi đồ và cổng kỹ
  năng như cũ. Roster, HUD và bảng nhân vật hiển thị nghề cũ, còn nhân vật `None` hiển thị "SURVIVOR".
- **D5 — Kỹ năng.** Cổng kỹ năng giữ nguyên. Với nhân vật `None`, nhánh theo nghề không bao giờ đúng, nên kỹ năng chỉ mở
  qua mask đã học. Khởi đầu tay trắng không cần kỹ năng, nên ADR này không thêm cách mở mới (theo cấp, theo vật phẩm...).
  Cơ chế đó sẽ có ADR riêng khi cần.
- **D6 — Creator.** Bỏ ba thẻ chọn nghề. Cột trái hiện thẻ "FRESH START" giải thích khởi đầu tay trắng. Mô hình xem
  trước không cầm vũ khí, không mặc giáp. Chuỗi hiển thị nằm trong section mới `[character_creator]` của `en.ini` và
  `vi.ini`. Các key không còn dùng (`character.starting_class`, `character.class_hint.*`) đã bị xóa.

## Alternatives Considered

- **Giữ `StartingClass` trên wire và để server bỏ qua.** Không cần tăng protocol, nhưng gói vẫn mang một trường chết, và
  client cũ vẫn tưởng mình chọn được nghề. Bị loại.
- **Gán nhân vật mới `Class = Vanguard` (0).** Không cần giá trị enum mới, nhưng nhân vật mới trông như kiếm sĩ, và
  `LoadInventory` sẽ cấp bộ đồ kiếm sĩ khi blob hỏng. Bị loại.
- **Migration đổi mọi nhân vật cũ sang `None`.** Đơn giản hơn về mô hình, nhưng phá dữ liệu người chơi, trái yêu cầu. Bị loại.
- **Thay cổng kỹ năng ngay bằng mở theo cấp.** Ngoài phạm vi: khởi đầu tay trắng không cần kỹ năng, và đây là quyết định
  cân bằng riêng.

## Consequences

- Client protocol 1 phải cập nhật mới đăng nhập được.
- Người chơi mới bắt đầu bằng thu nhặt bằng tay (đá rời, cành cây, bụi khô quanh điểm xuất phát, đấm cây), chế dây thừng
  rồi rìu đá, sau đó tiến lên bình thường (bàn thợ, cuốc đá...).
- Hai kiểu nhân vật cùng tồn tại: có nghề (cũ) và `None` (mới). Code đọc `Class` phải xử lý `None`, ví dụ
  `GameText.ClassName` và bản xem trước của creator.
- Kỹ năng nghề hiện chỉ nhân vật cũ có `ClassAdvanced` mới dùng được. Cách nhân vật mới mở kỹ năng là việc còn để ngỏ.
