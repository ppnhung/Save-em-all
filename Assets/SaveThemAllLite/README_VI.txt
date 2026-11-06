SAVE THEM ALL LITE 1.1 — PREFAB + JSON MAP IMPORT
Ngày: 06/11/2026. Dành cho Unity 6.

1. CÀI BỘ MỚI
Trong Unity: Assets > Import Package > Custom Package...
Chọn SaveThemAll_Lite_v1_0.unitypackage > Import toàn bộ.
Tên file gói được giữ để cập nhật bản cũ; giao diện sau import hiển thị 1.1 • JSON Import.
Đã có Lite 1.0: import đè package này, giữ Assets/STA_Content và các item của bạn.
Tool nằm ở Assets/SaveThemAllLite. Không cần kit AutoTile hoặc các bản Patch cũ.
Giữ các model, texture và material item bạn đã nhập vào project.
Mở Tools > Save Them All Lite. Có 2 mục: Create Prefabs và Map Editor.

2. TẠO PREFAB
Mở Tools > Save Them All Lite > 1. Create Prefabs.
Kéo THƯ MỤC chứa item từ cửa sổ Project vào Thư mục nguồn.
Bấm Quét thư mục. Có thể bỏ chọn item không muốn dùng.
Bật Căn terrain về 1 ô nếu dùng bộ Grass-* của Save Them All.
Bấm Tạo prefab + cập nhật danh sách.
- Mỗi model có mesh tạo một prefab riêng, giữ liên kết với model gốc.
- Material, cấu trúc xương và Animator giữ như trong model nguồn.
- Không thêm Animator lên object cha mới; tránh đổi đường dẫn xương của clip.
- Prefab có sẵn được dùng trực tiếp. Prefab đã tạo được dùng lại, không ghi đè chỉnh sửa.
- Tên file có GUID để phân biệt các model trùng tên. Tên hiển thị trong tool vẫn gọn.
- Output: Assets/STA_Content/Prefabs. Danh sách chung: Assets/STA_Content/Palette.asset.
- Có thêm item ở thư mục khác: quét và tạo tiếp, danh sách cũ được giữ.
- Tool giữ material hiện có; không tự sửa material hồng/thiếu texture từ lúc import model.
- Căn 1 ô chỉ dùng cho model mới có tên Grass-*, không có Background trong tên. Không thay mesh/UV.

3. DỰNG LEVEL TỪ FILE MAP JSON
Mở Tools > Save Them All Lite > 2. Map Editor.
Mở phần DỰNG TỰ ĐỘNG TỪ FILE MAP JSON.
1) Gán Palette.asset tạo ở bước 2 (thường tool tự lấy).
2) Bấm Mở file map… và chọn .terrainmap.json / .json xuất từ Penguin Terrain Studio.
   Hoặc kéo file JSON đã có trong Project vào ô JSON trong Project.
3) Xem tên map, số hàng/cột, số terrain và object. Tool tự nối prefab theo ID, tên và loại.
4) Nếu còn dòng trống, kéo prefab đúng từ Project vào dòng đó. Nếu có nhiều prefab cùng loại,
   tool chờ bạn chọn. Có thể sửa Loại bên phải; lựa chọn được nhớ trong Palette sau khi dựng.
5) Bấm Dựng level từ file. Tool tự chọn các khối địa hình và đặt toàn bộ object.
6) Chọn object trong Scene/Hierarchy để chỉnh ở bảng bên phải. Lưu level prefab khi xong.

- Đọc đúng định dạng TerrainMap schemaVersion 1, origin bottom-left; không đọc JSON bất kỳ.
- File .js được nhận nếu chứa JSON thuần. Không chạy JavaScript/const/export default.
- Giữ width/height/cellSize, tọa độ, offset, góc xoay, count, pairId và overrideModel.
- ID prefab cũ còn khớp sẽ tự nối. Nếu đã xóa prefab cũ hoặc đổi GUID, gán lại lần đầu.
- Với prefab tùy chỉnh, nếu không khớp ID/đường dẫn/tên, tool để trống để bạn chọn đúng model.
- Thiếu prefab hoặc thiếu mã terrain: chưa dựng; hiện rõ các mục cần bổ sung.
- Object mặc định Z = -0.65 ô để nằm trước terrain. Chỉnh về 0 nếu muốn cùng mặt phẳng như importer cũ.
- Giữ transform của model gốc, không tự xoay sửa quạt hoặc kéo giãn object theo số ô.
  Sau import có thể dùng Vừa vùng ô / Chạm sàn và chỉnh transform từng món.
- Những ô có overrideModel giữ khối đã chọn riêng khi cập nhật terrain. Xóa rồi tô lại ô đó để về tự nối.
- Import lại tạo root level mới và ẩn bản trước; không ghi đè chỉnh sửa tay. Ctrl+Z hoàn tác cả lần import.
- Import chỉ dựng dữ liệu/visual. Camera, ánh sáng và gameplay dùng thiết lập hiện có của project.
- Có file thử: Assets/SaveThemAllLite/Examples/Level_01.terrainmap.json, 59 ô + 12 object.
- Giới hạn: 2 MB, 128 × 128 ô, tối đa 4.096 object.

3B. TẠO LEVEL TRỐNG (TÙY CHỌN)
Mở tab 2 Dựng map hoặc Tools > Save Them All Lite > 2. Map Editor.
Nhập tên level, số cột × số hàng, bấm Tạo level. Mặc định 9 × 16.
Bấm Canh góc nhìn để nhìn lưới XY chính diện trong Scene view.
Mỗi ô mặc định 1 Unity unit. Gốc ô (0,0) ở góc dưới trái, tọa độ ô là tâm ô.
Muốn đổi kích thước mỗi ô: mở Tên level / lưới / collider terrain, chỉnh trước khi đặt nội dung.
Đổi số hàng/cột vẫn giữ map. Không cho thu nhỏ nếu terrain bị nằm ngoài vùng mới.
Object có thể đặt lệch ngoài lưới bằng chỉnh transform; tool không tự xóa chúng khi đổi lưới.

4. VẼ TERRAIN
Bấm Tô terrain, kéo chuột trái trong Scene view.
Bấm Xóa terrain hoặc giữ Shift khi tô để xóa.
Tool tự chọn cạnh/góc theo bộ tên Grass-*, gồm seam, dùng 256 trường hợp láng giềng / 68 biến thể tự động.
Chọn một asset Terrain trong danh sách cũng bật cọ tự nối; không phải cọ đặt cố định riêng khối đó.
Thiếu prefab cần thiết: tool hiện tên còn thiếu và giữ terrain trước thao tác đó.
Để xử lý: tạo thêm prefab từ thư mục terrain, hoặc mở Chỉnh asset đang chọn để gán đúng mã Grass-*.
Nếu hai prefab cùng nhận một mã terrain, đổi mã/loại của mục dư để loại bỏ liên kết mơ hồ.
Nếu thiếu UV hoặc model nguồn có lỗi hình học, cần sửa source; tool chỉ chọn và căn khối.

5. ĐẶT VÀ CHỈNH OBJECT
Tìm tên asset, bấm vào item rồi click trong Scene để đặt.
Esc hoặc nút Chọn để trở về chọn đối tượng bình thường.
Chọn object trong Scene/Hierarchy. Bảng bên phải và Inspector cho chỉnh:
- Tên, vị trí X/Y/Z theo ô, góc xoay X/Y/Z, tỉ lệ X/Y/Z.
- Vùng chiếm ô để xem khung và căn kích thước.
- Loại object; loại được đoán từ tên, bạn có thể sửa lại.
- Cửa vào: số penguin, thời gian giữa mỗi con, chờ con đầu, hướng đi, điểm ra.
- Lồng / cổng ra: số chim / mục tiêu.
- Quạt: lực, hướng X/Y, tầm tác động.
- Dịch chuyển: mã cặp và điểm ra.
- Đá: số lần tác động. Cầu thang: hướng đi lên.
- Ghi chú, collider, nhân bản hoặc xóa.
Thông số trên từng object độc lập; không tự đổi các bản khác cùng prefab.
Thay Loại trong phần Chỉnh asset chỉ tác động các lần đặt mới.
Vị trí và góc xoay tác động object cha, giữ hierarchy của model/rig con.

6. CĂN OBJECT
Vừa vùng ô: co/phóng đồng đều để mesh vừa khung rộng/cao đã nhập. Không ép méo hình.
Chạm sàn: tìm mặt terrain ngay bên dưới toàn bộ bề ngang mesh rồi hạ đáy mesh xuống mặt đó.
Đưa object lên phía trên sàn trước khi dùng Chạm sàn; nút này không đoán đường thoát khi đang xuyên đất.
Các nút căn dùng mesh bounds lúc chỉnh sửa; với nhân vật có animation, vẫn cần kiểm tra pose thực tế sau này.
Chọn root object rồi dùng W/E/R nếu muốn chỉnh bằng gizmo Unity.

7. COLLIDER
Terrain mặc định có BoxCollider 3D cho từng ô, độc lập với mesh visual.
Trong thiết lập level có thể chọn Không thêm / Box3D / Trigger3D / Box2D / Trigger2D.
Đổi loại, đỉnh hoặc độ dày xong bấm Cập nhật terrain / collider sau khi đổi thiết lập.
Các collider ở visual terrain được tắt; collider mới theo ô đảm nhận vùng va chạm.
Object: chọn loại collider > Tạo / cập nhật collider theo mesh.
Sau đó có thể chỉnh tâm/kích thước collider trực tiếp trong bảng object.
Collider có sẵn trong prefab object được giữ. Tránh thêm chồng collider khi prefab đã có.
Không tự thêm Rigidbody. Hệ gameplay sau này quyết định vật lý và tương tác.

8. LƯU LEVEL
Ctrl+S lưu scene cùng các thay đổi và level đang ẩn.
Bấm Lưu level prefab để lưu từng map riêng. Nên giữ trong Assets/STA_Content/Levels để hiện ở danh sách Level đã lưu.
Mở level đã lưu: chọn trong danh sách > Mở. Tool tạo bản sửa trong scene, giữ liên kết các prefab item con.
Sửa xong bấm Lưu level prefab và chọn lại file đó để cập nhật; Ctrl+S không tự ghi đè file prefab level.
Mở/tạo level mới sẽ ẩn root level trước đó, không xóa. Chọn lại root cũ trong Hierarchy và bấm Chuyển sang level này để hiện lại.
Để nhân bản level thành bản mới: đổi Tên level và lưu sang tên file khác.
Level prefab tham chiếu palette, prefab item và model/material gốc; giữ các asset phụ thuộc này trong project.

9. UNDO VÀ PHẠM VI
Ctrl+Z hoàn tác thao tác vẽ/đặt/chỉnh/xóa trong scene; một lượt kéo cọ được gộp lại.
Tạo file prefab mới và ghi file level là thao tác lưu asset, không được hoàn tác như scene; chạy tạo prefab lần nữa sẽ dùng lại file đã có.
Tool tập trung tạo prefab, đọc JSON từ web và chỉnh map trong Unity. Không cần cài kit importer cũ.
Chỉnh xong lưu scene / level prefab; bản này chưa xuất ngược các chỉnh sửa ra JSON.
Chưa có spawn, tự đi, chết, giải cứu, quạt thổi, teleport, UI hay tự gán clip animation.
Count/lực/mã cặp… được lưu trong STAObject để script gameplay đọc về sau; việc chỉnh chúng chưa kích hoạt tương tác.

10. KIỂM TRA ĐÃ THỰC HIỆN
Lõi C# ghép khối đã biên dịch và chạy: 256 mask, 1.200 lần thêm/xóa ngẫu nhiên, kiểm tra biên lưới, không sửa input, từ chối khi thiếu prefab; tổng 43.161 assert đạt.
Đã chạy thêm 54 kiểm tra C# cho dữ liệu import: file JSON mẫu cũ, tọa độ/offset, thông số object, liên kết custom ID, kiểm tra dữ liệu sai và bảo toàn override khi sửa terrain.
Fixture test dùng System.Text.Json để đưa dữ liệu vào DTO; JsonUtility thực tế vẫn cần kiểm tra trong Unity.
Đã kiểm tra cú pháp script, tham chiếu assembly và cấu trúc gói import.
Không dùng GetInstanceID, FindObjectsSortMode hoặc các hàm tìm object toàn scene đã bị loại bỏ.
Chưa chạy Unity Editor tại môi trường tạo gói. Phần JsonUtility, dựng prefab thật, giao diện, Undo và lưu/mở level cần kiểm tra trong project của bạn.
